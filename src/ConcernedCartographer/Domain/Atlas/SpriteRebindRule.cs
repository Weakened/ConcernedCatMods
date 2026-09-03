using System;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC14 final-smoke fix 1: when must a pin rendering be rebuilt
/// so its icon shows the right sprite? The decision behind the
/// "custom markers become Dots after relog" report, extracted pure:
/// after a restart the reconcile claims saved vanilla renderings whose
/// applied-sprite record is empty, so every cc:* pin (wanted sprite, none
/// applied) must rebuild to regain its art — while vanilla-icon pins
/// (nothing wanted, nothing applied) must stay untouched, because
/// repainting genuine vanilla Dots would violate the adoption
/// contract. A recorded sprite that Unity destroyed across a scene
/// change also forces a rebuild: matching bookkeeping is only
/// trustworthy while the sprite is actually alive.</summary>
internal static class SpriteRebindRule
{
    /// <summary>True when the rendering must be rebuilt (remove + re-add)
    /// to show the wanted sprite. wanted/applied are custom-sprite icon
    /// ids, null meaning "no custom sprite"; appliedSpriteAlive reports
    /// whether the rendering's current icon object is still alive.</summary>
    public static bool MustRebuild(string? wantedIconId, string? appliedIconId, bool appliedSpriteAlive)
    {
        if (!string.Equals(wantedIconId, appliedIconId, StringComparison.Ordinal))
        {
            return true;
        }

        return wantedIconId is not null && !appliedSpriteAlive;
    }
}
