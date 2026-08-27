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
