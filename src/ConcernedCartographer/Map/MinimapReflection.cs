using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Skip-visibility access to the private Minimap members the mod
/// needs. Direct publicized calls throw MethodAccessException at JIT
/// (DEF-v0.2-001), so everything here goes through Harmony invokers and
/// fails soft.</summary>
internal static class MinimapReflection
{
    private static readonly MethodInfo? ScreenToWorldMethod =
        AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });

    private static readonly FastInvokeHandler? ScreenToWorldInvoker =
        ScreenToWorldMethod is null ? null : MethodInvoker.GetHandler(ScreenToWorldMethod);

    private static readonly AccessTools.FieldRef<Minimap, float>? LargeZoomField = BuildLargeZoomRef();

    private static readonly MethodInfo? WorldToPixelMethod = AccessTools.Method(
        typeof(Minimap), "WorldToPixel",
        new[] { typeof(Vector3), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() });

    private static readonly FastInvokeHandler? WorldToPixelInvoker =
        WorldToPixelMethod is null ? null : MethodInvoker.GetHandler(WorldToPixelMethod);

    private static readonly MethodInfo? WorldToMapPointMethod = AccessTools.Method(
        typeof(Minimap), "WorldToMapPoint",
        new[] { typeof(Vector3), typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });

    private static readonly FastInvokeHandler? WorldToMapPointInvoker =
        WorldToMapPointMethod is null ? null : MethodInvoker.GetHandler(WorldToMapPointMethod);

    private static readonly AccessTools.FieldRef<Minimap, int>? TextureSizeField =
        BuildFieldRef<int>("m_textureSize");

    private static readonly AccessTools.FieldRef<Minimap, float>? PixelSizeField =
        BuildFieldRef<float>("m_pixelSize");

    /// <summary>The game's own explored-texture pixel for a world position
    /// (the projection native pins and fog use). Diagnostics only.</summary>
    public static bool TryWorldToPixel(Vector3 world, out int pixelX, out int pixelY)
    {
        pixelX = 0;
        pixelY = 0;
        if (WorldToPixelInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            // Harmony's invoker writes by-ref results back into the array.
            object[] arguments = { world, 0, 0 };
            WorldToPixelInvoker(Minimap.instance, arguments);
            pixelX = (int)arguments[1];
            pixelY = (int)arguments[2];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The game's normalized map UV for a world position (the
    /// projection Jötunn's WorldToOverlayCoords delegates to).</summary>
    public static bool TryWorldToMapPoint(Vector3 world, out float mapX, out float mapY)
    {
        mapX = 0f;
        mapY = 0f;
        if (WorldToMapPointInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            object[] arguments = { world, 0f, 0f };
            WorldToMapPointInvoker(Minimap.instance, arguments);
            mapX = (float)arguments[1];
            mapY = (float)arguments[2];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The live map texture size in pixels.</summary>
    public static bool TryGetTextureSize(out int textureSize)
    {
        textureSize = 0;
        if (TextureSizeField is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            textureSize = TextureSizeField(Minimap.instance);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>World meters covered by one map texel.</summary>
    public static bool TryGetPixelSize(out float metersPerPixel)
    {
        metersPerPixel = 0f;
        if (PixelSizeField is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            metersPerPixel = PixelSizeField(Minimap.instance);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Current large-map zoom (fraction of the map texture shown;
    /// small = close). Returns false when unavailable.</summary>
    public static bool TryGetLargeZoom(out float zoom)
    {
        zoom = 0f;
        if (LargeZoomField is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            zoom = LargeZoomField(Minimap.instance);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static readonly AccessTools.FieldRef<Minimap, Minimap.PinData>? NamePinField =
        BuildFieldRef<Minimap.PinData>("m_namePin");

    private static readonly MethodInfo? SelectIconMethod =
        AccessTools.Method(typeof(Minimap), "SelectIcon", new[] { typeof(Minimap.PinType) });

    private static readonly FastInvokeHandler? SelectIconInvoker =
        SelectIconMethod is null ? null : MethodInvoker.GetHandler(SelectIconMethod);

    /// <summary>The pin currently going through the vanilla naming flow
    /// (double-click → name input), or null when the input is closed. The
    /// Enhanced Pin Palette watches this to claim palette-born pins.</summary>
    public static bool TryGetNamePin(out Minimap.PinData? namePin)
    {
        namePin = null;
        if (NamePinField is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            namePin = NamePinField(Minimap.instance);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Selects the vanilla placement icon through the game's own
    /// SelectIcon, so vanilla double-click placement uses the palette's
    /// chosen type and the (possibly hidden) vanilla highlights stay
    /// consistent.</summary>
    public static bool TrySelectIcon(int vanillaType)
    {
        if (SelectIconInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            SelectIconInvoker(Minimap.instance, new object[] { (Minimap.PinType)vanillaType });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The five vanilla player-placeable icon buttons on the large
    /// map (the parents of the public m_selectedIcon0..4 highlight images).
    /// Death/boss filter buttons are deliberately NOT included — only the
    /// pin-creation selectors may be hidden by the enhanced palette.</summary>
    public static System.Collections.Generic.List<GameObject> GetPlaceableIconButtons()
    {
        var buttons = new System.Collections.Generic.List<GameObject>();
        Minimap minimap = Minimap.instance;
        if (minimap == null)
        {
            return buttons;
        }

        try
        {
            foreach (UnityEngine.UI.Image image in new[]
            {
                minimap.m_selectedIcon0, minimap.m_selectedIcon1, minimap.m_selectedIcon2,
                minimap.m_selectedIcon3, minimap.m_selectedIcon4,
            })
            {
                if (image != null && image.transform.parent != null)
                {
                    buttons.Add(image.transform.parent.gameObject);
                }
            }
        }
        catch
        {
            // Fail soft: an unexpected hierarchy simply leaves vanilla visible.
        }

        return buttons;
    }

    private static readonly AccessTools.FieldRef<Minimap, bool[]>? VisibleIconTypesField =
        BuildFieldRef<bool[]>("m_visibleIconTypes");

    private static readonly MethodInfo? ToggleIconFilterMethod =
        AccessTools.Method(typeof(Minimap), "ToggleIconFilter", new[] { typeof(Minimap.PinType) });

    private static readonly FastInvokeHandler? ToggleIconFilterInvoker =
        ToggleIconFilterMethod is null ? null : MethodInvoker.GetHandler(ToggleIconFilterMethod);

    private static readonly AccessTools.FieldRef<Minimap, bool>? DragViewField =
        BuildFieldRef<bool>("m_dragView");

    /// <summary>Reads the vanilla per-pin-type visibility filter state.</summary>
    public static bool TryGetIconFilterVisible(int vanillaType, out bool visible)
    {
        visible = true;
        if (VisibleIconTypesField is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            bool[] types = VisibleIconTypesField(Minimap.instance);
            if (types is null || vanillaType < 0 || vanillaType >= types.Length)
            {
                return false;
            }

            visible = types[vanillaType];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Toggles a vanilla pin-type filter through the game's own
    /// ToggleIconFilter, so state and (possibly hidden) highlights stay
    /// canonical. Filtering never touches pin data.</summary>
    public static bool TryToggleIconFilter(int vanillaType)
    {
        if (ToggleIconFilterInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            ToggleIconFilterInvoker(Minimap.instance, new object[] { (Minimap.PinType)vanillaType });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Suppresses vanilla left-drag map panning for this frame
    /// (route drawing consumes the drag). Called every frame while an
    /// explicit CC map mode is active; vanilla behavior returns the moment
    /// the calls stop.</summary>
    public static bool TrySuppressMapDragThisFrame()
    {
        if (DragViewField is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            DragViewField(Minimap.instance) = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The death/boss filter buttons on the vanilla rail (parents
    /// of the public highlight images), for the full-rail replacement.</summary>
    public static System.Collections.Generic.List<GameObject> GetSystemFilterButtons()
    {
        var buttons = new System.Collections.Generic.List<GameObject>();
        Minimap minimap = Minimap.instance;
        if (minimap == null)
        {
            return buttons;
        }

        try
        {
            foreach (UnityEngine.UI.Image image in new[] { minimap.m_selectedIconDeath, minimap.m_selectedIconBoss })
            {
                if (image != null && image.transform.parent != null)
                {
                    buttons.Add(image.transform.parent.gameObject);
                }
            }
        }
        catch
        {
            // Fail soft: vanilla stays visible.
        }

        return buttons;
    }

    private static readonly MethodInfo? GetSpriteMethod =
        AccessTools.Method(typeof(Minimap), "GetSprite", new[] { typeof(Minimap.PinType) });

    private static readonly FastInvokeHandler? GetSpriteInvoker =
        GetSpriteMethod is null ? null : MethodInvoker.GetHandler(GetSpriteMethod);

    /// <summary>The sprite the game renders for a vanilla pin type, for
    /// icon-picker previews. Fails soft (no preview) when unavailable.</summary>
    public static bool TryGetPinSprite(int vanillaType, out Sprite? sprite)
    {
        sprite = null;
        if (GetSpriteInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            sprite = GetSpriteInvoker(Minimap.instance, new object[] { (Minimap.PinType)vanillaType }) as Sprite;
            return sprite != null;
        }
        catch
        {
            return false;
        }
    }

    private static readonly MethodInfo? IsExploredMethod =
        AccessTools.Method(typeof(Minimap), "IsExplored", new[] { typeof(Vector3) });

    private static readonly FastInvokeHandler? IsExploredInvoker =
        IsExploredMethod is null ? null : MethodInvoker.GetHandler(IsExploredMethod);

    /// <summary>Whether the player has explored (unfogged) a world position.
    /// The vector road layer draws above the map's fog compositing, so it
    /// filters unexplored geometry at bake time to keep fog parity with the
    /// texture overlay. Fails open (treated as explored) when unavailable.</summary>
    public static bool TryIsExplored(Vector3 world, out bool explored)
    {
        explored = true;
        if (IsExploredInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            explored = (bool)IsExploredInvoker(Minimap.instance, new object[] { world });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AccessTools.FieldRef<Minimap, float>? BuildLargeZoomRef()
    {
        try
        {
            return AccessTools.FieldRefAccess<Minimap, float>("m_largeZoom");
        }
        catch
        {
            return null;
        }
    }

    private static AccessTools.FieldRef<Minimap, TField>? BuildFieldRef<TField>(string fieldName)
    {
        try
        {
            return AccessTools.FieldRefAccess<Minimap, TField>(fieldName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>World position under a screen point on the open large map.
    /// Falls back to false when the API is unavailable.</summary>
    public static bool TryScreenToWorldPoint(Vector3 screenPosition, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (ScreenToWorldInvoker is null || Minimap.instance == null)
        {
            return false;
        }

        try
        {
            worldPosition = (Vector3)ScreenToWorldInvoker(Minimap.instance, new object[] { screenPosition });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
