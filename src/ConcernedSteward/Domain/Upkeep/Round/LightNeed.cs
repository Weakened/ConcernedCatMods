using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Designations;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>One light, judged for one maintenance round: whether it is a stop,
/// what it would take, and how badly it is wanted.
///
/// <b>Everything here is arithmetic over an observation.</b> Nothing in this
/// type reads the world, and nothing in it decides where anybody walks. That is
/// what lets every case a player could build — a hearth somebody just filled, a
/// brazier switched off, a torch under a ward, a fire that burns something the
/// chest does not stock — be one struct literal away in a test.</summary>
internal readonly struct LightNeed
{
    internal LightNeed(
        FuelTargetObservation light, FuelTargetStatus status, int units, int priority, float secondsLeft)
    {
        Light = light;
        Status = status;
        Units = units < 0 ? 0 : units;
        Priority = priority < 0 ? 0 : priority;
        SecondsLeft = secondsLeft;
    }

    public FuelTargetObservation Light { get; }

    /// <summary>Why this light is or is not a stop this round.</summary>
    public FuelTargetStatus Status { get; }

    /// <summary>How many units it would take to bring it up to the threshold —
    /// <b>not</b> to full. Zero for anything that is not a stop.</summary>
    public int Units { get; }

    /// <summary>Higher is serviced sooner, and it is a number of seconds: how
    /// far under the threshold this light's remaining burning time is. A light
    /// that has just gone out carries the whole threshold; one a minute under it
    /// carries sixty. Handed to the planner already decided, because how urgent
    /// a fire is is this product's business and nobody else's.</summary>
    public int Priority { get; }

    /// <summary>How long this light has left at its own burn rate, or infinity
    /// for one that never consumes.</summary>
    public float SecondsLeft { get; }

    /// <summary>A stop: eligible, and there is actually something to give it.
    /// Both halves are required, because a light that is eligible and needs zero
    /// units is a walk for nothing.</summary>
    public bool IsAStop => Status == FuelTargetStatus.Eligible && Units > 0;

    /// <summary>What this light burns.</summary>
    public string Item => Light.FuelItemName;

    public override string ToString() =>
        IsAStop
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} {2}, {3:0}s left, priority {4}",
                Light.Key, Units, Item, SecondsLeft, Priority)
            : Light.Key + ": " + Status;
}

/// <summary>Judging every light in one survey, once, before anybody walks
/// anywhere.
///
/// <b>Which pieces are serviced, and which are not, decided from the game's own
/// API rather than from a list of prefab names.</b> The audit behind this is
/// <c>docs/mods/concerned-steward/VALHEIM_FIRE_API_AUDIT.md</c>; what it comes to
/// is that in Valheim 1.0.14 exactly four component types carry a fuel API, and
/// only one of them is a light. So the survey looks for that one, and every
/// other bright thing in a settlement — a wisp light, a beacon, anything whose
/// glow is a light component and not a fire — is not offered here at all,
/// because it has no fuel to be low on. <b>No decorative light is ever
/// patched</b>, and the reason is structural rather than a filter that could be
/// got wrong: a piece with no fuel API produces no observation.
///
/// Of the pieces that do carry one, three are still refused, each for its own
/// reason and each with a verdict a player can read: one that never consumes,
/// one that is switched off, and one that simply is not due.</summary>
internal static class LightNeeds
{
    /// <summary>Judges one light.</summary>
    /// <param name="light">What the adapter read.</param>
    /// <param name="settlement">The marked area. Null means nothing is marked,
    /// and nothing marked means nothing is in scope — never "everywhere".</param>
    /// <param name="epoch">The current world load. A key from any other one
    /// resolves to nothing.</param>
    /// <param name="thresholds">When a light is due and how far it is filled.
    /// </param>
    /// <remarks><b>What is deliberately not a parameter: what the chests
    /// hold.</b> A light that burns something no approved chest stocks is still
    /// a light that needs fuel, and saying so is the whole of "no free fuel" —
    /// ten torches and no resin has to come out as "she needs forty resin", not
    /// as "nothing to tend". Availability is asked once, over the whole round's
    /// total, by <c>MaintenanceRound</c>.</remarks>
    internal static LightNeed Assess(
        in FuelTargetObservation light,
        Designation? settlement,
        string? epoch,
        in MaintenanceThresholds thresholds)
    {
        // The existing classifier first, unchanged, because every refusal in it
        // is a reason a fire may not be TOUCHED - a stale key, an unowned
        // object, a ward - and those outrank every question about whether it
        // needs anything. Re-deciding them here would be a second place for the
        // ownership rule to be got wrong.
        FuelTargetStatus status = FuelTargetSelector.Classify(
            light, settlement, null, epoch, requireStock: false);
        if (status != FuelTargetStatus.Eligible)
        {
            return new LightNeed(light, status, 0, 0, SecondsLeftOf(light));
        }

        if (!(light.SecondsPerUnit > 0f))
        {
            return new LightNeed(light, FuelTargetStatus.NeverConsumes, 0, 0, float.PositiveInfinity);
        }

        if (!light.IsLit)
        {
            return new LightNeed(light, FuelTargetStatus.NotLit, 0, 0, float.PositiveInfinity);
        }

        float secondsLeft = SecondsLeftOf(light);
        if (secondsLeft >= thresholds.RefuelBelowSeconds)
        {
            return new LightNeed(light, FuelTargetStatus.NotDue, 0, 0, secondsLeft);
        }

        int units = FuelMath.UnitsToReach(
            light.Fuel,
            light.MaxFuel,
            thresholds.TargetLevel(light.MaxFuel, light.SecondsPerUnit),
            thresholds.MostUnitsPerLight);

        if (units < 1)
        {
            // Under the threshold and vanilla will still not take a unit: a fire
            // at 9.5 of 10 whose burn rate makes 9.5 units less than the
            // threshold. Ceiling acceptance is vanilla's, not ours, and walking
            // there would be a walk for nothing.
            return new LightNeed(light, FuelTargetStatus.AlreadyFuelled, 0, 0, secondsLeft);
        }

        return new LightNeed(
            light, FuelTargetStatus.Eligible, units, PriorityFor(secondsLeft, thresholds), secondsLeft);
    }

    /// <summary>Judges a whole survey, in a deterministic order.
    ///
    /// <b>Ordered most urgent first, and the tie-break is total.</b> Two lights
    /// with the same time left are separated by their key, ordinally, because
    /// otherwise the order would depend on whatever order the engine happened to
    /// enumerate pieces in and "deterministic" would be true of the sort and
    /// false of the result. The planner re-orders by geography within a priority
    /// band; this decides the bands.</summary>
    internal static List<LightNeed> Assess(
        IReadOnlyList<FuelTargetObservation>? lights,
        Designation? settlement,
        string? epoch,
        in MaintenanceThresholds thresholds)
    {
        var judged = new List<LightNeed>(lights == null ? 0 : lights.Count);
        if (lights == null)
        {
            return judged;
        }

        foreach (FuelTargetObservation light in lights)
        {
            judged.Add(Assess(light, settlement, epoch, thresholds));
        }

        judged.Sort(Compare);
        return judged;
    }

    /// <summary>Only the stops, in order.</summary>
    internal static List<LightNeed> StopsIn(IReadOnlyList<LightNeed> judged)
    {
        var stops = new List<LightNeed>();
        foreach (LightNeed need in judged)
        {
            if (need.IsAStop)
            {
                stops.Add(need);
            }
        }

        return stops;
    }

    private static int PriorityFor(float secondsLeft, in MaintenanceThresholds thresholds)
    {
        float under = thresholds.RefuelBelowSeconds - secondsLeft;
        if (float.IsNaN(under) || under <= 0f)
        {
            return 0;
        }

        double rounded = Math.Ceiling(under);
        return rounded > int.MaxValue ? int.MaxValue : (int)rounded;
    }

    private static float SecondsLeftOf(in FuelTargetObservation light) =>
        MaintenanceThresholds.SecondsLeft(light.Fuel, light.SecondsPerUnit);

    private static int Compare(LightNeed left, LightNeed right)
    {
        // Stops before non-stops: a round's own list should read as the round.
        if (left.IsAStop != right.IsAStop)
        {
            return left.IsAStop ? -1 : 1;
        }

        int byUrgency = right.Priority.CompareTo(left.Priority);
        if (byUrgency != 0)
        {
            return byUrgency;
        }

        return string.CompareOrdinal(left.Light.Key.Value, right.Light.Key.Value);
    }
}
