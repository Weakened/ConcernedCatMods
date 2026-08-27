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
