using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Answers the two questions a worker must settle before it will walk
/// anywhere: is the goal in loaded ground, and is it dangerous.
///
/// <b>Every answer fails closed.</b> The authority ADR's rule is that a check
/// which cannot be faithfully made becomes a refusal rather than an assumption,
/// and that rule bites hardest here, because both questions are asked about
/// world state that may simply not be there yet. A missing <c>ZoneSystem</c> is
/// answered "not loaded", not "probably fine". Ground that cannot be measured is
/// answered "hazardous", not "flat".
///
/// The base class is the refusing one on purpose: a worker whose runtime forgot
/// to hand it a real policy stands still, which is visible and harmless, rather
/// than walking into unchecked terrain, which is neither.</summary>
internal class WorkerSitePolicy
{
    /// <summary>Refuses everything. The default, and the fallback whenever the
    /// world is not in a state that can be asked.</summary>
    internal static readonly WorkerSitePolicy Refusing = new();

    /// <summary>How deep water has to be before a worker refuses to stand in
    /// it. A humanoid starts swimming somewhere above waist height; a metre and
    /// a half is comfortably below that, so the worker refuses before it would
    /// begin to swim rather than after.</summary>
    internal const float MaxStandingWaterDepth = 1.5f;

    /// <summary>True when the goal is inside a loaded zone. The first proof does
    /// no offscreen work, and reports that limit rather than hiding it.</summary>
    internal virtual bool IsInLoadedGround(Vector3 point) => false;

    /// <summary>True when the goal is somewhere a worker must not be sent.</summary>
    internal virtual bool IsHazardous(Vector3 point) => true;
}

/// <summary>The real policy, against the installed game.
///
/// Each check below names the exact API it uses, because the point of this class
/// is that the checks are real. Anything it cannot establish is a refusal.</summary>
internal sealed class WorldSitePolicy : WorkerSitePolicy
{
    internal override bool IsInLoadedGround(Vector3 point)
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null)
        {
            // No zone system means no world, which is not the same as "the goal
            // is fine". Refuse.
            return false;
        }

        return zones.IsZoneLoaded(point);
    }

    internal override bool IsHazardous(Vector3 point)
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null)
        {
            return true;
        }

        // Fire. EffectArea.IsPointInsideArea returns the area, or null when the
        // point is outside every one of them. Burning is the damaging kind;
        // Heat and WarmCozyArea are a hearth doing its job and are not hazards.
        if (EffectArea.IsPointInsideArea(point, EffectArea.Type.Burning) != null)
        {
            return true;
        }

        // Water. Liquid level comes from Floating.GetLiquidLevel and the ground
        // from ZoneSystem.GetSolidHeight; the depth is the difference. Ground we
        // cannot measure is a refusal, because "how deep is this" has no safe
        // default.
        if (!zones.GetSolidHeight(point, out float ground))
        {
            return true;
        }

        float liquid = Floating.GetLiquidLevel(point);
        return liquid - ground > MaxStandingWaterDepth;
    }
}
