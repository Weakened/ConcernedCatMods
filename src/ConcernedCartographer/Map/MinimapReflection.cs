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
