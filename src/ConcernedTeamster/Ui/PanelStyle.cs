using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>Shared UI-boundary helpers (CT-033): converts the pure
/// <see cref="PanelPalette"/> RGB tuples to Unity's Color type (Domain stays
/// Unity-free), and applies the uniform scale factor to a built panel's root
/// transform — the one place every panel's scale support goes through, so
/// scaling a panel is always "the whole subtree together," never individual
/// dimensions re-derived per file.</summary>
internal static class PanelStyle
{
    public static Color Header => ToColor(PanelPalette.Header);

    public static Color Body => ToColor(PanelPalette.Body);

    public static Color HudHint => ToColor(PanelPalette.HudHint);

    public static void ApplyScale(GameObject gameObject, float scale)
    {
        gameObject.transform.localScale = new Vector3(scale, scale, scale);
    }

    private static Color ToColor((float R, float G, float B) rgb) => new(rgb.R, rgb.G, rgb.B, 1f);
}
