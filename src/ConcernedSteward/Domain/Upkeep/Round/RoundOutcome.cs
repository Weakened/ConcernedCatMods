using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>What one light turned out to be when it was looked at again, just
/// before walking to it.
///
/// <b>Seven answers where a naive loop has two</b>, and the values are
/// deliberately the seven the shared route runtime already names, in the same
/// order, so that adopting its revalidation seam is a mapping and not a
/// redesign. Collapsing them is how an NPC declares a round finished because a
/// zone had not streamed in, or walks to a hearth a player filled two minutes
/// ago.</summary>
internal enum LightVerdict
{
    /// <summary>She could not tell. <b>Never a yes and never a no</b>: the light
    /// is left for the next round rather than counted as done or dropped as
    /// gone. Zero, so an unanswered look is never a reason to act.</summary>
    Unreadable = 0,

    /// <summary>Still worth going to.</summary>
    Actionable = 1,

    /// <summary>Somebody else already dealt with it — the player filled it, or
    /// it has climbed back over the threshold. <b>Skip it and keep the
    /// fuel</b>: putting it back the moment one stop disappears is a walk to a
    /// chest for nothing.</summary>
    AlreadyDone = 2,

    /// <summary>It is not there any more. Skipped; what was fetched for it stays
    /// carried, and the end of the round decides where that goes.</summary>
    Gone = 3,

    /// <summary>It is still there and it is somewhere else. Skipped this round
    /// and offered again next round, at the place it is now — acting on the old
    /// place is acting on whatever is standing there instead.</summary>
    Moved = 4,

    /// <summary>It exists, it is where it was, and she could not get to it. Not
    /// produced by looking: this is the walk's answer, kept in the vocabulary so
    /// the two halves speak one language.</summary>
    Unreachable = 5,

    /// <summary>Something refuses it that is neither the world nor the distance:
    /// a ward, an owner that is not this session, a settlement that no longer
    /// contains it. Named apart because the sentence a player reads has to tell
    /// them what to change.</summary>
    Refused = 6,
}

/// <summary>Asking, immediately before each stop, whether it is still worth
/// walking to.
///
/// <b>Asked before every stop and not once at the start.</b> A round is long
/// enough for the player to fill the hearth themselves, for a troll to knock the
/// torch over, and for a ward to go up over both. Asking once and trusting the
/// answer for the rest of the round is the defect this exists to prevent, and it
/// is the one that costs a player material: fuel withdrawn for a light that is
/// gone.</summary>
internal static class LightRevalidation
{
    /// <summary>How far a light may have moved and still be the same stop.
    ///
    /// A piece does not move in vanilla, so any measurable movement means the
    /// key now names something else — but float noise across a save and a load
    /// is real, so the tolerance is the one this repository already uses for the
    /// same question about a different world object.</summary>
    internal const float SamePlaceMetres = 0.35f;

    /// <summary>Looks at one stop again.</summary>
    /// <param name="planned">What was planned for it.</param>
    /// <param name="seen">What it looks like now, or null when the adapter could
    /// not find it at all.</param>
    /// <param name="settlement">The marked area as it stands now.</param>
    /// <param name="epoch">The current world load.</param>
    /// <param name="thresholds">The same thresholds the round was planned
    /// with.</param>
    internal static LightVerdict Look(
        in LightNeed planned,
        FuelTargetObservation? seen,
        Designation? settlement,
        string? epoch,
        in MaintenanceThresholds thresholds)
    {
        if (seen == null)
        {
            // The adapter could not resolve the key. In this product that means
            // the object is not there: a key is scoped to one world load, and
            // within a load a key that resolves to nothing named something that
            // has been destroyed.
            return LightVerdict.Gone;
        }

        FuelTargetObservation now = seen.Value;
        if (!now.Key.Equals(planned.Light.Key))
        {
            return LightVerdict.Gone;
        }

        if (planned.Light.Position.HorizontalDistanceTo(now.Position) > SamePlaceMetres)
        {
            return LightVerdict.Moved;
        }

        LightNeed fresh = LightNeeds.Assess(now, settlement, epoch, thresholds);
        switch (fresh.Status)
        {
            case FuelTargetStatus.Eligible:
                // Eligible with nothing left to give it is the same situation as
                // already fuelled, and LightNeeds never produces it - but a
                // planner that trusted "eligible" alone would walk there for
                // nothing, so it is answered explicitly.
                return fresh.Units > 0 ? LightVerdict.Actionable : LightVerdict.AlreadyDone;

            case FuelTargetStatus.AlreadyFuelled:
            case FuelTargetStatus.NotDue:
            case FuelTargetStatus.NeverConsumes:
            case FuelTargetStatus.NotLit:
            case FuelTargetStatus.InfiniteFuel:
            case FuelTargetStatus.CannotRefill:
                // Nothing to do here any more, for a reason that is nobody's
                // fault and that will not be fixed by coming back. The fuel
                // fetched for it stays carried.
                return LightVerdict.AlreadyDone;

            case FuelTargetStatus.StaleIdentity:
                return LightVerdict.Gone;

            case FuelTargetStatus.NotOwnedHere:
            case FuelTargetStatus.AccessDenied:
            case FuelTargetStatus.OutsideSettlement:
            case FuelTargetStatus.WrongFuel:
                return LightVerdict.Refused;

            default:
                return LightVerdict.Unreadable;
        }
    }

    /// <summary>Whether a verdict means "go".</summary>
    internal static bool MeansGo(LightVerdict verdict) => verdict == LightVerdict.Actionable;

    /// <summary>Whether a verdict means the stop is finished with for good, so
    /// it is not owed anything at the end of the round.
    ///
    /// <b>The distinction that decides whether a round is complete.</b> A light
    /// somebody else filled, or that is gone, is not owed; one she could not
    /// read, or could not reach, is. Folding them together makes an interrupted
    /// round look like a finished one.</summary>
    internal static bool IsSettled(LightVerdict verdict) =>
        verdict == LightVerdict.AlreadyDone || verdict == LightVerdict.Gone;

    internal static string Describe(LightVerdict verdict)
    {
        switch (verdict)
        {
            case LightVerdict.Actionable: return "it still wants fuel";
            case LightVerdict.AlreadyDone: return "somebody already saw to it";
            case LightVerdict.Gone: return "it is not there any more";
            case LightVerdict.Moved: return "it is not where it was";
            case LightVerdict.Unreachable: return "she could not get to it";
            case LightVerdict.Refused: return "she may not touch it";
            default: return "she could not tell";
        }
    }
}

/// <summary>What became of one stop.</summary>
internal enum StopOutcome
{
    /// <summary>Nobody said. Taken as never reached, which is the safe reading:
    /// a stop nobody reported on certainly was not serviced.</summary>
    Unspecified = 0,

    /// <summary>Fuelled.</summary>
    Serviced = 1,

    /// <summary>Looked at again and not worth doing. <b>The fuel stays
    /// carried.</b></summary>
    Skipped = 2,

    /// <summary>Reached and it did not work.</summary>
    Failed = 3,

    /// <summary>The round ended before she got there.</summary>
    NotReached = 4,
}

/// <summary>One stop's fate.</summary>
internal readonly struct StopResult
{
    internal StopResult(FuelTargetKey light, StopOutcome outcome, int unitsServiced)
    {
        Light = light;
        Outcome = outcome;
        UnitsServiced = unitsServiced < 0 ? 0 : unitsServiced;
    }

    public FuelTargetKey Light { get; }

    public StopOutcome Outcome { get; }

    /// <summary>Units actually put into it, measured. Never what was planned:
    /// the plan is an intention and the measurement is what happened.</summary>
    public int UnitsServiced { get; }
}

/// <summary>The books, at the end of a round.
///
/// <b>The number that matters most here is the one that is not about this
/// round's stops at all.</b> <see cref="LeftForAnotherRound"/> counts the lights
/// the plan never reached — not stops she failed to get to, but lights that were
/// never written into a step, because the whole job needed more trips than one
/// plan covers or more chests than one provisioning phase opens. A round can
/// execute perfectly and still not be a finished job, and an NPC that reported
/// otherwise would be the most expensive kind of wrong: the player stops
/// checking.</summary>
internal readonly struct RoundOutcome
{
    internal RoundOutcome(
        int planned,
        int serviced,
        int skipped,
        int failed,
        int notReached,
        int leftForAnotherRound,
        RoundManifest outstanding,
        RoundManifest surplus)
    {
        Planned = planned;
        Serviced = serviced;
        Skipped = skipped;
        Failed = failed;
        NotReached = notReached;
        LeftForAnotherRound = leftForAnotherRound < 0 ? 0 : leftForAnotherRound;
        Outstanding = outstanding;
        Surplus = surplus;
    }

    public int Planned { get; }

    public int Serviced { get; }

    public int Skipped { get; }

    public int Failed { get; }

    public int NotReached { get; }

    /// <summary>Lights the plan never reached, because the plan was for part of
    /// the job. Handed in by whatever produced the plan; this type never guesses
    /// it, and zero is a claim rather than a default.</summary>
    public int LeftForAnotherRound { get; }

    /// <summary>What the lights that were not serviced still need.</summary>
    public RoundManifest Outstanding { get; }

    /// <summary>What she fetched and did not use. Still in her hands, and the
    /// reason there is somewhere for it to go at the end.</summary>
    public RoundManifest Surplus { get; }

    /// <summary>Whether every light <b>the round was for</b> was seen to. Two
    /// questions, not one: did every stop of this plan come off, and did this
    /// plan cover the whole round.</summary>
    public bool IsComplete =>
        Planned > 0 && Failed == 0 && NotReached == 0 && Outstanding.IsEmpty && LeftForAnotherRound == 0;

    /// <summary>Whether there is more to do, whatever the stop counts say.
    /// </summary>
    public bool NeedsAnotherRound =>
        !Outstanding.IsEmpty || Failed > 0 || NotReached > 0 || LeftForAnotherRound > 0;

    /// <summary>How long to wait before looking again.
    ///
    /// <b>This is what reads <see cref="LeftForAnotherRound"/> and acts on
    /// it.</b> A round that covered everything waits out the ordinary interval,
    /// because nothing is going to change in the next few seconds. A round that
    /// covered part of the settlement does not wait at all: the lights it never
    /// reached are still burning down, and making a player watch her stand at
    /// the chest for fifteen seconds between halves of one job is the whole
    /// benefit of batching thrown away at the last step.</summary>
    public float NextRoundDelaySeconds(float ordinaryInterval) =>
        NeedsAnotherRound ? 0f : ordinaryInterval;

    public string Describe()
    {
        var text = new System.Text.StringBuilder();
        text.Append("She tended ");
        text.Append(Serviced.ToString(CultureInfo.InvariantCulture));
        text.Append(" of ");
        text.Append(Planned.ToString(CultureInfo.InvariantCulture));
        text.Append(" light(s)");

        if (Skipped > 0)
        {
            text.Append("; ");
            text.Append(Skipped.ToString(CultureInfo.InvariantCulture));
            text.Append(" needed nothing by the time she got there");
        }

        if (Failed > 0)
        {
            text.Append("; ");
            text.Append(Failed.ToString(CultureInfo.InvariantCulture));
            text.Append(" would not take it");
        }

        if (NotReached > 0)
        {
            text.Append("; ");
            text.Append(NotReached.ToString(CultureInfo.InvariantCulture));
            text.Append(" she did not reach");
        }

        if (LeftForAnotherRound > 0)
        {
            text.Append(". ");
            text.Append(LeftForAnotherRound.ToString(CultureInfo.InvariantCulture));
            text.Append(" more light(s) than this round covered are still waiting; she is going " +
                "straight back out");
        }

        if (!Surplus.IsEmpty)
        {
            text.Append(". She is carrying ");
            text.Append(Surplus.Describe());
            text.Append(" she did not need");
        }

        return text.Append('.').ToString();
    }
}

/// <summary>Closing the books on a round.</summary>
internal static class RoundReconciler
{
    /// <summary>Works out what the round achieved.</summary>
    /// <param name="plan">The round as it was prepared.</param>
    /// <param name="results">What became of each stop. A stop with no result is
    /// taken as never reached rather than as serviced.</param>
    /// <param name="fetched">What was actually drawn out of the chests,
    /// measured. Never what the plan asked for.</param>
    /// <param name="leftForAnotherRound">Lights the plan never covered at all,
    /// as the planner reported it.</param>
    internal static RoundOutcome Close(
        in RoundPlan plan,
        IReadOnlyList<StopResult>? results,
        in RoundManifest fetched,
        int leftForAnotherRound)
    {
        var byLight = new Dictionary<FuelTargetKey, StopResult>();
        if (results != null)
        {
            foreach (StopResult result in results)
            {
                // Last word wins: a stop reported twice was visited twice, and
                // the later report is the one still true.
                byLight[result.Light] = result;
            }
        }

        int serviced = 0;
        int skipped = 0;
        int failed = 0;
        int notReached = 0;
        var owed = new List<LightNeed>();
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (LightNeed stop in plan.Stops)
        {
            if (!byLight.TryGetValue(stop.Light.Key, out StopResult result))
            {
                result = new StopResult(stop.Light.Key, StopOutcome.Unspecified, 0);
            }

            switch (result.Outcome)
            {
                case StopOutcome.Serviced:
                    serviced++;
                    if (result.UnitsServiced > 0 && stop.Item.Length != 0)
                    {
                        used.TryGetValue(stop.Item, out int running);
                        used[stop.Item] = running + result.UnitsServiced;
                    }

                    break;

                case StopOutcome.Skipped:
                    // Not owed. Somebody else saw to it, or it is gone. That
                    // distinction is the entire reason a skip and a failure are
                    // different outcomes.
                    skipped++;
                    break;

                case StopOutcome.Failed:
                    failed++;
                    owed.Add(stop);
                    break;

                default:
                    notReached++;
                    owed.Add(stop);
                    break;
            }
        }

        var usedLines = new List<RoundManifestLine>();
        foreach (KeyValuePair<string, int> entry in used)
        {
            usedLines.Add(new RoundManifestLine(entry.Key, entry.Value));
        }

        return new RoundOutcome(
            plan.Stops.Count,
            serviced,
            skipped,
            failed,
            notReached,
            leftForAnotherRound,
            RoundManifestArithmetic.Total(owed),
            RoundManifestArithmetic.Subtract(fetched, new RoundManifest(usedLines)));
    }

    /// <summary>Where the surplus goes.
    ///
    /// <b>Back into an approved DEPOSIT container, and into no other.</b> There
    /// is no nearest-chest fallback and there is no dropping it on the ground:
    /// a container the player did not approve for deposits is not a place a
    /// player's wood may be left, and a pile on the floor is a despawn timer.
    /// If nothing is approved, she keeps carrying it and says so — which is
    /// visible, reversible and costs nobody anything.
    ///
    /// Among approved containers, the nearest to where the round ended, with the
    /// key as a total tie-break so two equidistant chests do not swap between
    /// rounds.</summary>
    internal static bool TryChooseDeposit(
        IReadOnlyList<SupplySighting>? sources,
        SitePoint endedAt,
        out SupplySighting chosen)
    {
        chosen = default;
        bool found = false;
        float best = 0f;

        if (sources == null)
        {
            return false;
        }

        foreach (SupplySighting source in sources)
        {
            if (!source.CanDeposit)
            {
                continue;
            }

            float distance = endedAt.HorizontalDistanceTo(source.Position);
            if (!found
                || distance < best
                || (distance == best && string.CompareOrdinal(source.Key, chosen.Key) < 0))
            {
                chosen = source;
                best = distance;
                found = true;
            }
        }

        return found;
    }
}
