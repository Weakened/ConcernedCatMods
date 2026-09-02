using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The single source of route ink colors (RC10, feedback 5): the
/// texture overlay (minimap/fallback) and the large-map vector layer both
/// resolve through here, so a route always wears the same color in every
/// presentation.</summary>
internal static class RouteInk
{
    public static Color32 Resolve(AtlasRoute route, bool highContrast)
    {
        if (route.ColorArgb is int argb)
        {
            return FromArgb(argb);
        }

        return route.Status switch
        {
            RouteStatus.Active => highContrast
                ? new Color32(255, 160, 0, 255)
                : new Color32(255, 200, 80, 255),
            RouteStatus.Done => highContrast
                ? new Color32(200, 200, 200, 255)
                : new Color32(135, 135, 135, 255),
            _ => highContrast
                ? new Color32(0, 220, 255, 255)
                : new Color32(210, 210, 235, 255),
        };
    }

    public static Color32 FromArgb(int argb)
    {
        return new Color32(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
    }
}
