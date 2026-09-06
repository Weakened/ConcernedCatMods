namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>The one set of text colors every Teamster wood panel uses
/// (CT-033) — a single definition instead of six copies, so a contrast fix
/// applies everywhere at once and <see cref="ContrastRatio"/> has one thing
/// to audit. Channels are sRGB in [0,1]; adapters convert to their UI
/// framework's color type at the point of use.</summary>
public static class PanelPalette
{
    /// <summary>Panel titles and totals — always rendered with a black
    /// outline (see each panel's <c>CreateText</c> call), which is a strong
    /// contrast aid independent of the background underneath it.</summary>
    public static readonly (float R, float G, float B) Header = (0.9f, 0.8f, 0.6f);

    /// <summary>Body rows (mass, grade, manifest lines, etc.).</summary>
    public static readonly (float R, float G, float B) Body = (0.85f, 0.85f, 0.82f);

    /// <summary>The optional HUD warning hint under the Cart button.</summary>
    public static readonly (float R, float G, float B) HudHint = (1f, 0.85f, 0.5f);

    /// <summary>A documented approximation of Valheim's wood-panel UI
    /// background, for contrast auditing only. The actual texture cannot be
    /// read exactly without a live game session (it is art, not a flat
    /// color Teamster's source can query), so this is a deliberately
    /// mid-range estimate rather than one tuned to make the audit pass —
    /// see docs/mods/concerned-teamster/ACCESSIBILITY.md for the exact
    /// reasoning, the sensitivity check against a lighter estimate, and the
    /// pending in-game color-pick that would replace this assumption with a
    /// measured value.</summary>
    public static readonly (float R, float G, float B) ApproximateWoodBackground = (0.30f, 0.24f, 0.17f);
}
