using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>The two questions the Steward settles before he walks anywhere: is
/// the goal in loaded ground, and is it dangerous.
///
/// <b>Both fail closed.</b> A check that cannot be faithfully made becomes a
/// refusal rather than an assumption, and that bites hardest here because both
/// questions are about world state that may simply not be there. A missing
/// <c>ZoneSystem</c> is answered "not loaded", not "probably fine". Ground that
/// cannot be measured is answered "hazardous", not "flat".
///
/// The base class refuses everything on purpose: a Steward whose runtime forgot
/// to hand him a real policy stands still, which is visible and harmless,
/// rather than walking into unchecked terrain, which is neither.
///
/// <b>Fire is the interesting one for this character.</b> He works around
/// hearths for a living, so "there is a fire here" must not read as a hazard —
/// <c>EffectArea.Type.Burning</c> is the damaging kind, while <c>Heat</c> and
/// <c>WarmCozyArea</c> are a hearth doing its job. Treating the latter as a
/// hazard would make the Steward refuse to approach the only thing he is for.
/// </summary>
/// <remarks>The shape of these checks is Concerned Foreman's, arrived at from
/// the same decompile. It is copied rather than shared: shared source may not
/// contain game types, and products never reference each other
/// (docs/settlement/cart-and-collection/DECISIONS.md D2).</remarks>
internal class StewardSitePolicy
{
    /// <summary>Refuses everything. The default, and the fallback whenever the
    /// world is not in a state that can be asked.</summary>
    internal static readonly StewardSitePolicy Refusing = new();

    /// <summary>How deep water has to be before he refuses to stand in it. A
    /// humanoid starts swimming somewhere above waist height; a metre and a
    /// half is comfortably below that, so he refuses before he would begin to
    /// swim rather than after.</summary>
    internal const float MaxStandingWaterDepth = 1.5f;

    internal virtual bool IsInLoadedGround(Vector3 point) => false;

    internal virtual bool IsHazardous(Vector3 point) => true;
}

/// <summary>The real policy, against the installed game. Each check names the
/// API it uses, because the point of this class is that the checks are real.
/// </summary>
internal sealed class WorldStewardSitePolicy : StewardSitePolicy
{
    internal override bool IsInLoadedGround(Vector3 point)
    {
        ZoneSystem zones = ZoneSystem.instance;

        // No zone system means no world, which is not the same as "the goal is
        // fine".
        return zones != null && zones.IsZoneLoaded(point);
    }

    internal override bool IsHazardous(Vector3 point)
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null)
        {
            return true;
        }

        // Burning is the damaging kind. A lit hearth's Heat and WarmCozyArea
        // are not hazards, and must not be: standing next to a fire is the job.
        if (EffectArea.IsPointInsideArea(point, EffectArea.Type.Burning) != null)
        {
            return true;
        }

        // Depth is the liquid level less the solid ground. Ground we cannot
        // measure is a refusal, because "how deep is this" has no safe default.
        if (!zones.GetSolidHeight(point, out float ground))
        {
            return true;
        }

        return Floating.GetLiquidLevel(point) - ground > MaxStandingWaterDepth;
    }
}
