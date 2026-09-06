using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Clamped UI scale factor (CT-033), applied uniformly to a whole
/// panel via its root transform rather than to individual dimensions inside
/// it. Uniform scaling of a parent transform carries every child's relative
/// position and size along with it, so a panel that does not clip its own
/// content at one scale cannot newly clip it at another — the property is
/// structural, not something each panel has to re-prove.</summary>
public static class UiScaleOptions
{
    public const float DefaultScale = 1.0f;
    public const float MinScale = 0.8f;
    public const float MaxScale = 1.5f;

    /// <summary>NaN/Infinity fall back to the default (matches
    /// <c>WarningOptions.CreateClamped</c>'s pattern); everything else
    /// clamps into [MinScale, MaxScale].</summary>
    public static float Clamp(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return DefaultScale;
        }

        return Math.Min(MaxScale, Math.Max(MinScale, scale));
    }
}
