using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>High-precision large-map vector layer (DEF-v1.0-006; RC10
/// feedback 5/6 extends it to routes): batched vector quads baked once in
/// zoom-independent map space and placed under the vanilla large-map
/// image, above Jötunn's overlay (its first child) and below pins and the
/// player marker. The RoadAtlas and RouteStore stay the sources of truth
/// and the 2048-texel texture overlays keep rendering (minimap +
/// fallback). Pan/zoom is a per-frame container-transform update that
/// reproduces vanilla's ((m − uvMin)/uvSize)·rectSize exactly
/// (RoadVectorMath) — no magic offsets; geometry rebakes only on data
/// changes, zoom-step changes, or a slow parity timer. Roads and routes
/// share ONE screen-space width/style system: widths and dash/dot
/// cadences are defined in screen pixels and re-derived at every rebake,
/// so they stay readable at every zoom. Unexplored road segments are
/// skipped at bake (the layer draws above fog compositing); routes are
/// the player's own plans and render regardless of fog, matching the
/// texture path. Fails soft: any error disables the layer for the session
/// and texture rendering continues untouched.</summary>
internal sealed class RoadVectorLayer
{
    private const string ContainerName = "CcRoadVectorLayer";

    /// <summary>Target on-screen ink width for roads AND routes (RC10
    /// feedback 5: 2× the RC8 value of 3). Constant by design: the layer
    /// exists for sub-texel *positioning*; width is cosmetic.</summary>
    private const float TargetScreenWidthPixels = 6f;

    // Route dash/dot cadence in SCREEN pixels (RC10 feedback 6): geometric
    // distances walked along the polyline, re-derived from the live zoom at
    // every rebake, so the pattern reads the same at every zoom level.
    private const float DashOnScreenPixels = 12f;
    private const float DashOffScreenPixels = 8f;
    private const float DotSpacingScreenPixels = 9f;

    // 16-bit UI mesh indices allow 65 535 vertices; 4 per quad.
    private const int MaxQuads = 16_000;

    /// <summary>Separate stamp budget for routes: a dotted route at close
    /// zoom stamps many quads; a route that would exceed its share degrades
    /// to a solid line instead of killing the whole layer.</summary>
    private const int MaxRouteQuads = 20_000;
    private const int MaxQuadsPerRoute = 4_000;

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;

    // RC11 feedback 3: the rebake decision is a pure, sweep-tested state
    // machine so no zoom band or invalidation window can leave roads
    // undrawn.
    private readonly VectorBakeScheduler _scheduler = new();

    private RectTransform? _container;
    private RoadVectorGraphic? _dirtGraphic;
    private RoadVectorGraphic? _pavedGraphic;
    private RoadVectorGraphic? _routeGraphic;
    private bool _dirtVisible = true;
    private bool _pavedVisible = true;
    private bool _routesVisible = true;
    private bool _routeBudgetWarned;
    private bool _bakeIncompleteWarned;
    private Color32 _bakedDirtColor;
    private Color32 _bakedPavedColor;
    private bool _disabledForSession;

    public RoadVectorLayer(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _rateLimited = new RateLimitedLog(log, 5f);
    }

    /// <summary>Whether sub-texel road ink is actually being drawn right
    /// now (the C verdict of `cc_roads align live`).</summary>
    public bool IsActive =>
        !_disabledForSession &&
        _settings.HighPrecisionLargeMapRoads.Value &&
        _container != null &&
        _container.gameObject.activeInHierarchy;

    public void MarkDataDirty()
    {
        _scheduler.MarkDataDirty();
    }

    /// <summary>Mirrors the texture overlay's dirt/paved visibility (same
    /// switch the drawer, saved views, and Jötunn's toggle panel drive).</summary>
    public void SetKindEnabled(RoadKind kind, bool enabled)
    {
        if (kind == RoadKind.Dirt)
        {
            _dirtVisible = enabled;
        }
        else
        {
            _pavedVisible = enabled;
        }
    }

    /// <summary>Mirrors the route overlay's visibility (RC10 feedback 5/7):
    /// the same switch the Routes toggle and Jötunn's panel drive.</summary>
    public void SetRoutesEnabled(bool enabled)
    {
        _routesVisible = enabled;
    }

    /// <summary>Per-frame drive. Cheap when the large map is closed or the
    /// feature is off; the container transform update is a handful of
    /// float ops per open-map frame.</summary>
    public void Tick(
        float unscaledDeltaTime,
        RoadAtlas atlas,
        Atlas.RouteStore? routes,
        bool highContrast,
        Color32 dirtColor,
        Color32 pavedColor)
    {
        if (_disabledForSession)
        {
            return;
        }

        try
        {
            if (!_settings.Enabled.Value || !_settings.HighPrecisionLargeMapRoads.Value)
            {
                DestroyContainer();
                return;
            }

            Minimap? minimap = Minimap.instance;
            if (minimap == null)
            {
                // Scene teardown destroyed the map (and our objects with it).
                ReleaseDeadReferences();
                return;
            }

            RawImage image = minimap.m_mapImageLarge;
            if (image == null)
            {
                return;
            }

            _scheduler.Advance(unscaledDeltaTime);

            if (minimap.m_largeRoot == null || !minimap.m_largeRoot.activeInHierarchy)
            {
                // Closed map: our objects are inactive with it; nothing to do.
                return;
            }

            EnsureCreated(image);
            if (_container == null || _dirtGraphic == null || _pavedGraphic == null)
            {
                return;
            }

            // Exact vanilla placement for the current pan/zoom window.
            Rect uvRect = image.uvRect;
            Rect rect = image.rectTransform.rect;
            if (uvRect.width <= 0f || uvRect.height <= 0f || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            (float scaleX, float scaleY, float positionX, float positionY) = RoadVectorMath.ContentTransform(
                uvRect.xMin, uvRect.yMin, uvRect.width, uvRect.height, rect.width, rect.height);
            _container.anchoredPosition = new Vector2(positionX, positionY);
            _container.localScale = new Vector3(scaleX, scaleY, 1f);

            _dirtGraphic.enabled = _dirtVisible;
            _pavedGraphic.enabled = _pavedVisible;
            if (_routeGraphic != null)
            {
                _routeGraphic.enabled = _routesVisible;
            }

            bool colorsChanged = !_bakedDirtColor.Equals(dirtColor) || !_bakedPavedColor.Equals(pavedColor);
            if (_scheduler.ShouldRebake(uvRect.width, colorsChanged))
            {
                Rebake(atlas, routes, highContrast, image, uvRect, rect, dirtColor, pavedColor);
            }
        }
        catch (Exception exception)
        {
            // Fail soft, once: the texture overlay remains the road view.
            _disabledForSession = true;
            _log.LogError($"High-precision road layer disabled for this session: {exception}");
            DestroyContainer();
        }
    }

    private void EnsureCreated(RawImage image)
    {
        if (_container != null && _container.parent == image.rectTransform)
        {
            return;
        }

        DestroyContainer();

        var containerObject = new GameObject(ContainerName, typeof(RectTransform));
        _container = (RectTransform)containerObject.transform;
        _container.SetParent(image.rectTransform, worldPositionStays: false);
        // Above Jötunn's overlay (the image's FIRST child); pins and the
        // player marker live in later sibling subtrees and stay on top.
        _container.SetAsLastSibling();
        _container.anchorMin = Vector2.zero;
        _container.anchorMax = Vector2.zero;
        _container.pivot = Vector2.zero;
        _container.sizeDelta = Vector2.zero;
        _container.anchoredPosition = Vector2.zero;

        _dirtGraphic = CreateGraphic("CcRoadVectorDirt", _container);
        _pavedGraphic = CreateGraphic("CcRoadVectorPaved", _container);
        _routeGraphic = CreateGraphic("CcRouteVector", _container);
        _scheduler.Invalidate();
    }

    private static RoadVectorGraphic CreateGraphic(string name, RectTransform parent)
    {
        var graphicObject = new GameObject(name, typeof(RectTransform));
        var rectTransform = (RectTransform)graphicObject.transform;
        rectTransform.SetParent(parent, worldPositionStays: false);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        // RC11 feedback 3: a REAL rect covering the whole baked map extent
        // (±ReferenceSize/2 around the container origin). Mesh geometry
        // ignores the rect, but rect-based clippers (RectMask2D from
        // vanilla or any other mod) CULL a graphic whose rect misses the
        // clip area — with the old zero-size rect that depended on where
        // the container origin happened to sit at the current pan/zoom,
        // which read as roads vanishing in zoom bands.
        rectTransform.sizeDelta = new Vector2(RoadVectorMath.ReferenceSize, RoadVectorMath.ReferenceSize);
        rectTransform.anchoredPosition = Vector2.zero;

        var graphic = graphicObject.AddComponent<RoadVectorGraphic>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private int _bakeProjectionFailures;

    private void Rebake(
        RoadAtlas atlas, Atlas.RouteStore? routes, bool highContrast, RawImage image,
        Rect uvRect, Rect rect, Color32 dirtColor, Color32 pavedColor)
    {
        _bakeProjectionFailures = 0;
        _bakedDirtColor = dirtColor;
        _bakedPavedColor = pavedColor;

        // Width target in the image's GUI units, so BakedHalfWidth (which
        // works in rect units) hits TargetScreenWidthPixels on screen.
        float pixelToGuiUnits = 1f;
        if (MapScreenMath.TryGetPixelsPerGuiUnit(image, out float pixelsPerGuiUnit) && pixelsPerGuiUnit > 0f)
        {
            pixelToGuiUnits = 1f / pixelsPerGuiUnit;
        }

        float widthGuiUnits = TargetScreenWidthPixels * pixelToGuiUnits;
        float halfWidth = RoadVectorMath.BakedHalfWidth(widthGuiUnits, uvRect.width, rect.width);
        if (halfWidth <= 0f)
        {
            return;
        }

        // One screen pixel expressed in baked map units at the CURRENT
        // zoom: the shared conversion behind widths and dash/dot cadence.
        float bakedUnitsPerScreenPixel = RoadVectorMath.BakedHalfWidth(
            pixelToGuiUnits, uvRect.width, rect.width) * 2f;

        _dirtGraphic!.BeginQuads(dirtColor);
        _pavedGraphic!.BeginQuads(pavedColor);

        int quads = 0;
        bool exploredCheckAvailable = MinimapReflection.TryIsExplored(Vector3.zero, out _);
        foreach (RoadStroke stroke in atlas.Strokes)
        {
            if (stroke.Hidden || stroke.Points.Count == 0)
            {
                continue;
            }

            RoadVectorGraphic graphic = stroke.Kind == RoadKind.Dirt ? _dirtGraphic : _pavedGraphic;

            bool hasPrevious = false;
            float previousX = 0f, previousY = 0f;
            bool previousExplored = false;

            foreach (RoadPoint point in stroke.Points)
            {
                var world = new Vector3(point.X, point.Y, point.Z);
                if (!MinimapReflection.TryWorldToMapPoint(world, out float mapX, out float mapY))
                {
                    // No native projection, no vector layer: never guess.
                    // The scheduler keeps this bake uncommitted so it
                    // retries within a quarter second (RC11 feedback 3).
                    _bakeProjectionFailures++;
                    hasPrevious = false;
                    continue;
                }

                (float bakedX, float bakedY) = RoadVectorMath.Bake(mapX, mapY);
                bool explored = true;
                if (exploredCheckAvailable)
                {
                    MinimapReflection.TryIsExplored(world, out explored);
                }

                if (hasPrevious && (explored || previousExplored))
                {
                    if (quads >= MaxQuads)
                    {
                        DisableOverBudget();
                        return;
                    }

                    graphic.AddSegmentQuad(previousX, previousY, bakedX, bakedY, halfWidth);
                    quads++;
                }
                else if (!hasPrevious && stroke.Points.Count == 1 && explored)
                {
                    // Lone construction dabs render as dots on the texture
                    // path; mirror them.
                    if (quads >= MaxQuads)
                    {
                        DisableOverBudget();
                        return;
                    }

                    graphic.AddSegmentQuad(bakedX, bakedY, bakedX, bakedY, halfWidth);
                    quads++;
                }

                hasPrevious = true;
                previousX = bakedX;
                previousY = bakedY;
                previousExplored = explored;
            }
        }

        _dirtGraphic.CommitQuads();
        _pavedGraphic.CommitQuads();

        BakeRoutes(routes, highContrast, halfWidth, bakedUnitsPerScreenPixel);

        if (_bakeProjectionFailures > 0)
        {
            _scheduler.OnBakeIncomplete();
            if (!_bakeIncompleteWarned)
            {
                _bakeIncompleteWarned = true;
                _log.LogWarning(
                    $"Vector bake could not project {_bakeProjectionFailures} point(s); " +
                    "showing the partial result and retrying (logged once per session).");
            }
        }
        else
        {
            _scheduler.OnBakeCommitted(uvRect.width);
        }
    }

    /// <summary>Bakes route polylines into the shared vector container with
    /// the SAME width system as roads and screen-space dash/dot cadence
    /// (RC10 feedback 5/6). Routes are the player's own plans: no fog
    /// filtering, matching the texture presentation. A route whose styled
    /// stamps would blow the budget falls back to a solid line; it never
    /// takes the road layer down.</summary>
    private void BakeRoutes(
        Atlas.RouteStore? routes, bool highContrast, float halfWidth, float bakedUnitsPerScreenPixel)
    {
        if (_routeGraphic == null)
        {
            return;
        }

        _routeGraphic.BeginQuads(new Color32(255, 255, 255, 255));
        if (routes is null || bakedUnitsPerScreenPixel <= 0f)
        {
            _routeGraphic.CommitQuads();
            return;
        }

        float dashOn = DashOnScreenPixels * bakedUnitsPerScreenPixel;
        float dashOff = DashOffScreenPixels * bakedUnitsPerScreenPixel;
        float dotSpacing = DotSpacingScreenPixels * bakedUnitsPerScreenPixel;

        int totalQuads = 0;
        _routeBaked.Clear();
        foreach (Atlas.AtlasRoute route in routes.Living)
        {
            if (route.Archived || route.Points.Count == 0 || totalQuads >= MaxRouteQuads)
            {
                continue;
            }

            _routeBaked.Clear();
            float bakedLength = 0f;
            foreach (RoadPoint point in route.Points)
            {
                if (!MinimapReflection.TryWorldToMapPoint(new Vector3(point.X, point.Y, point.Z), out float mapX, out float mapY))
                {
                    _bakeProjectionFailures++;
                    continue;
                }

                (float bakedX, float bakedY) = RoadVectorMath.Bake(mapX, mapY);
                if (_routeBaked.Count > 0)
                {
                    (float previousX, float previousY) = _routeBaked[_routeBaked.Count - 1];
                    bakedLength += Mathf.Sqrt(
                        ((bakedX - previousX) * (bakedX - previousX)) +
                        ((bakedY - previousY) * (bakedY - previousY)));
                }

                _routeBaked.Add((bakedX, bakedY));
            }

            if (_routeBaked.Count == 0)
            {
                continue;
            }

            Color32 color = RouteInk.Resolve(route, highContrast);
            if (_routeBaked.Count == 1)
            {
                _routeGraphic.AddSegmentQuad(
                    _routeBaked[0].X, _routeBaked[0].Y, _routeBaked[0].X, _routeBaked[0].Y, halfWidth, color);
                totalQuads++;
                continue;
            }

            Atlas.RouteStyle style = route.Style;
            if (style != Atlas.RouteStyle.Solid)
            {
                float cadence = style == Atlas.RouteStyle.Dotted ? dotSpacing : dashOn + dashOff;
                int estimatedStamps = cadence > 0f ? (int)(bakedLength / cadence) + _routeBaked.Count : int.MaxValue;
                if (estimatedStamps > MaxQuadsPerRoute || totalQuads + estimatedStamps > MaxRouteQuads)
                {
                    style = Atlas.RouteStyle.Solid;
                    if (!_routeBudgetWarned)
                    {
                        _routeBudgetWarned = true;
                        _log.LogInfo(
                            "A styled route exceeds the vector stamp budget at this zoom and renders solid instead.");
                    }
                }
            }

            // Shared cadence math (RC10 feedback 5/6): both the vector and
            // texture presentations walk RoutePatternMath, so their
            // dash/dot geometry can never drift apart.
            int budget = Mathf.Min(MaxQuadsPerRoute, MaxRouteQuads - totalQuads);
            switch (style)
            {
                case Atlas.RouteStyle.Dashed:
                    totalQuads += Atlas.RoutePatternMath.WalkDashes(
                        _routeBaked, dashOn, dashOff, budget,
                        (fromX, fromY, toX, toY) =>
                            _routeGraphic.AddSegmentQuad(fromX, fromY, toX, toY, halfWidth, color));
                    break;
                case Atlas.RouteStyle.Dotted:
                    totalQuads += Atlas.RoutePatternMath.WalkDots(
                        _routeBaked, dotSpacing, budget,
                        (x, y) => _routeGraphic.AddSegmentQuad(x, y, x, y, halfWidth, color));
                    break;
                default:
                    for (int index = 1; index < _routeBaked.Count && totalQuads < MaxRouteQuads; index++)
                    {
                        _routeGraphic.AddSegmentQuad(
                            _routeBaked[index - 1].X, _routeBaked[index - 1].Y,
                            _routeBaked[index].X, _routeBaked[index].Y, halfWidth, color);
                        totalQuads++;
                    }

                    break;
            }
        }

        _routeGraphic.CommitQuads();
    }

    private readonly List<(float X, float Y)> _routeBaked = new();

    /// <summary>Budget overflow (RC8-1): a PARTIAL vector bake may not ship
    /// — the suppressed texture overlay would hide whatever fell over the
    /// budget. The layer disables itself for the session instead, so the
    /// complete texture presentation returns as the single road view.</summary>
    private void DisableOverBudget()
    {
        _disabledForSession = true;
        _log.LogWarning(
            $"High-precision road layer exceeded its {MaxQuads} segment budget and is disabled " +
            "for this session; the complete texture overlay remains the road view.");
        DestroyContainer();
    }

    /// <summary>External teardown (mod disabled mid-session): destroys the
    /// layer's objects without marking the session failed, so re-enabling
    /// rebuilds cleanly.</summary>
    public void ForceInactive()
    {
        DestroyContainer();
    }

    private void ReleaseDeadReferences()
    {
        _container = null;
        _dirtGraphic = null;
        _pavedGraphic = null;
        _routeGraphic = null;
    }

    private void DestroyContainer()
    {
        if (_container != null)
        {
            UnityEngine.Object.Destroy(_container.gameObject);
        }

        ReleaseDeadReferences();
    }

    /// <summary>The mesh half of the layer: renders the currently baked
    /// quad list in the container's baked-unit local space. A plain
    /// MaskableGraphic so any vanilla viewport mask still clips it.</summary>
    private sealed class RoadVectorGraphic : MaskableGraphic
    {
        private readonly List<Vector2> _quadCorners = new();
        private readonly List<Color32> _quadColors = new();
        private Color32 _inkColor;
        private bool _building;

        public void BeginQuads(Color32 inkColor)
        {
            _inkColor = inkColor;
            _quadCorners.Clear();
            _quadColors.Clear();
            _building = true;
        }

        public void AddSegmentQuad(float startX, float startY, float endX, float endY, float halfWidth)
        {
            AddSegmentQuad(startX, startY, endX, endY, halfWidth, _inkColor);
        }

        /// <summary>Per-quad-color variant for the route batch, where every
        /// route wears its own ink in one shared graphic.</summary>
        public void AddSegmentQuad(
            float startX, float startY, float endX, float endY, float halfWidth, Color32 color)
        {
            if (!_building)
            {
                return;
            }

            float directionX = endX - startX;
            float directionY = endY - startY;
            float length = Mathf.Sqrt((directionX * directionX) + (directionY * directionY));
            float normalX;
            float normalY;
            if (length <= 1e-6f)
            {
                // A dot: square stamp, axis-aligned.
                normalX = 0f;
                normalY = halfWidth;
                directionX = halfWidth;
                directionY = 0f;
            }
            else
            {
                normalX = -directionY / length * halfWidth;
                normalY = directionX / length * halfWidth;
                directionX = 0f;
                directionY = 0f;
            }

            _quadCorners.Add(new Vector2(startX - directionX + normalX, startY - directionY + normalY));
            _quadCorners.Add(new Vector2(endX + directionX + normalX, endY + directionY + normalY));
            _quadCorners.Add(new Vector2(endX + directionX - normalX, endY + directionY - normalY));
            _quadCorners.Add(new Vector2(startX - directionX - normalX, startY - directionY - normalY));
            _quadColors.Add(color);
        }

        public void CommitQuads()
        {
            _building = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            UIVertex vertex = UIVertex.simpleVert;

            for (int index = 0; index + 3 < _quadCorners.Count; index += 4)
            {
                int baseIndex = vh.currentVertCount;
                vertex.color = _quadColors[index / 4];
                for (int corner = 0; corner < 4; corner++)
                {
                    vertex.position = _quadCorners[index + corner];
                    vh.AddVert(vertex);
                }

                vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
                vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
            }
        }
    }
}
