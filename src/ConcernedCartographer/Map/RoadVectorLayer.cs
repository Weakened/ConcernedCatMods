using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>High-precision large-map road layer (DEF-v1.0-006): batched
/// vector quads baked once in zoom-independent map space and placed under
/// the vanilla large-map image, above Jötunn's overlay (its first child)
/// and below pins and the player marker. The RoadAtlas stays the source of
/// truth and the 2048-texel texture overlay keeps rendering (minimap +
/// fallback). Pan/zoom is a per-frame container-transform update that
/// reproduces vanilla's ((m − uvMin)/uvSize)·rectSize exactly
/// (RoadVectorMath) — no magic offsets; geometry rebakes only on road-data
/// changes, zoom-step changes, or a slow parity timer. Unexplored segments
/// are skipped at bake (the layer draws above fog compositing). Fails
/// soft: any error disables the layer for the session and texture
/// rendering continues untouched.</summary>
internal sealed class RoadVectorLayer
{
    private const string ContainerName = "CcRoadVectorLayer";

    /// <summary>Target on-screen ink width. Constant by design: the layer
    /// exists for sub-texel *positioning*; width is cosmetic.</summary>
    private const float TargetScreenWidthPixels = 3f;

    // 16-bit UI mesh indices allow 65 535 vertices; 4 per quad.
    private const int MaxQuads = 16_000;

    private const float RebuildDebounceSeconds = 0.5f;

    /// <summary>Rebake when the uv window width drifts one zoom step from
    /// the baked width (positions stay exact through the transform; only
    /// the on-screen ink width drifts between rebakes).</summary>
    private const float ZoomStepRatio = 1.25f;

    /// <summary>Slow parity rebake: picks up newly explored fog cells and
    /// resolution changes even when road data is quiet.</summary>
    private const float PeriodicRebuildSeconds = 30f;

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;

    private RectTransform? _container;
    private RoadVectorGraphic? _dirtGraphic;
    private RoadVectorGraphic? _pavedGraphic;
    private bool _dirtVisible = true;
    private bool _pavedVisible = true;
    private bool _dataDirty = true;
    private float _debounceElapsed;
    private float _periodicElapsed;
    private float _bakedUvWidth = -1f;
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
        _dataDirty = true;
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

    /// <summary>Per-frame drive. Cheap when the large map is closed or the
    /// feature is off; the container transform update is a handful of
    /// float ops per open-map frame.</summary>
    public void Tick(float unscaledDeltaTime, RoadAtlas atlas, Color32 dirtColor, Color32 pavedColor)
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

            _debounceElapsed += unscaledDeltaTime;
            _periodicElapsed += unscaledDeltaTime;

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

            bool zoomStepChanged = _bakedUvWidth > 0f &&
                (uvRect.width > _bakedUvWidth * ZoomStepRatio || uvRect.width < _bakedUvWidth / ZoomStepRatio);
            bool colorsChanged = !_bakedDirtColor.Equals(dirtColor) || !_bakedPavedColor.Equals(pavedColor);
            bool debouncedData = _dataDirty && _debounceElapsed >= RebuildDebounceSeconds;
            bool periodic = _periodicElapsed >= PeriodicRebuildSeconds;

            if (_bakedUvWidth < 0f || debouncedData || zoomStepChanged || colorsChanged || periodic)
            {
                Rebake(atlas, image, uvRect, rect, dirtColor, pavedColor);
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
        _bakedUvWidth = -1f;
        _dataDirty = true;
    }

    private static RoadVectorGraphic CreateGraphic(string name, RectTransform parent)
    {
        var graphicObject = new GameObject(name, typeof(RectTransform));
        var rectTransform = (RectTransform)graphicObject.transform;
        rectTransform.SetParent(parent, worldPositionStays: false);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;

        var graphic = graphicObject.AddComponent<RoadVectorGraphic>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private void Rebake(
        RoadAtlas atlas, RawImage image, Rect uvRect, Rect rect, Color32 dirtColor, Color32 pavedColor)
    {
        _dataDirty = false;
        _debounceElapsed = 0f;
        _periodicElapsed = 0f;
        _bakedUvWidth = uvRect.width;
        _bakedDirtColor = dirtColor;
        _bakedPavedColor = pavedColor;

        // Width target in the image's GUI units, so BakedHalfWidth (which
        // works in rect units) hits TargetScreenWidthPixels on screen.
        float widthGuiUnits = TargetScreenWidthPixels;
        if (MapScreenMath.TryGetPixelsPerGuiUnit(image, out float pixelsPerGuiUnit) && pixelsPerGuiUnit > 0f)
        {
            widthGuiUnits = TargetScreenWidthPixels / pixelsPerGuiUnit;
        }

        float halfWidth = RoadVectorMath.BakedHalfWidth(widthGuiUnits, uvRect.width, rect.width);
        if (halfWidth <= 0f)
        {
            return;
        }

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
    }

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
        private Color32 _inkColor;
        private bool _building;

        public void BeginQuads(Color32 inkColor)
        {
            _inkColor = inkColor;
            _quadCorners.Clear();
            _building = true;
        }

        public void AddSegmentQuad(float startX, float startY, float endX, float endY, float halfWidth)
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
            vertex.color = _inkColor;

            for (int index = 0; index + 3 < _quadCorners.Count; index += 4)
            {
                int baseIndex = vh.currentVertCount;
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
