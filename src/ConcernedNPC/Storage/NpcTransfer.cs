using System;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>How much of a wanted amount may actually move, and where the rest
/// stays.
///
/// <b>The whole point is the second half.</b> <see cref="Shortfall"/> and
/// <see cref="Units"/> always add up to what was wanted, and the shortfall is
/// named "stays where it is" rather than "left over", because the tempting
/// third option - the one that must never exist - is for it to go nowhere at
/// all. A deposit of forty nails into a chest with room for thirty moves thirty
/// and keeps ten; it does not move forty and lose ten, and it does not drop ten
/// on the floor for a despawn timer to collect.
///
/// There is no path from this type to deletion, because this type does not move
/// anything. It arithmetic only, on counts a caller measured, and a caller that
/// cannot fit everything is told so before it starts rather than after.</summary>
internal readonly struct NpcTransferPlan
{
    private NpcTransferPlan(int wanted, int units)
    {
        Wanted = wanted;
        Units = units;
    }

    /// <summary>How many units the job asked to move.</summary>
    internal int Wanted { get; }

    /// <summary>How many may move: never more than are there, never more than
    /// will fit, never fewer than none.</summary>
    internal int Units { get; }

    /// <summary>How many stay where they are. <see cref="Units"/> plus this is
    /// always <see cref="Wanted"/> - the conservation law of a plan, before any
    /// item has moved.</summary>
    internal int Shortfall => Wanted - Units;

    /// <summary>Nothing can move. Not a failure: a chest that is full is an
    /// ordinary Tuesday, and the caller keeps what it is carrying.</summary>
    internal bool IsEmpty => Units <= 0;

    /// <summary>Whether everything asked for can move.</summary>
    internal bool IsWhole => Wanted > 0 && Units == Wanted;

    /// <summary>Plans one leg of a transfer from three measured counts.
    ///
    /// <paramref name="availableAtSource"/> and
    /// <paramref name="roomAtDestination"/> are what a caller measured just now,
    /// on both sides. Negative or nonsensical counts plan nothing rather than
    /// being repaired into something plausible.</summary>
    internal static NpcTransferPlan For(int wanted, int availableAtSource, int roomAtDestination)
    {
        if (wanted < 1 || availableAtSource < 1 || roomAtDestination < 1)
        {
            return new NpcTransferPlan(Math.Max(0, wanted), 0);
        }

        int units = Math.Min(wanted, Math.Min(availableAtSource, roomAtDestination));
        return new NpcTransferPlan(wanted, Math.Max(0, units));
    }
}

/// <summary>What one leg of a transfer turned out to have done.</summary>
internal enum ContainerMoveOutcome
{
    /// <summary>Nobody moved anything and nobody said so. Never a success.
    /// </summary>
    Unspecified = 0,

    /// <summary>Exactly what was planned left one side and arrived at the
    /// other.</summary>
    Completed = 1,

    /// <summary>Less than planned moved, and the same amount left as
    /// arrived. Everything is accounted for; the job asks again for the
    /// rest.</summary>
    Partial = 2,

    /// <summary>Nothing moved, and nothing was lost. The ordinary result of a
    /// chest that filled up between the plan and the act.</summary>
    Nothing = 3,

    /// <summary>The two sides do not agree. Something may or may not have
    /// happened, and <b>nothing is done about it here</b>: no second attempt, no
    /// compensating write, no adjusting a number until it balances. A
    /// compensation that guesses wrong either mints a player's material or
    /// deletes it, and both look exactly like this. It is recorded, it needs a
    /// person, and the ledger that decides what to do about it is the custody
    /// leaf's.</summary>
    Uncertain = 4,

    /// <summary>It was never authorised, or the authorisation had already been
    /// used, or it belonged to a world that is no longer loaded. Nothing was
    /// recorded.</summary>
    Refused = 5,
}

/// <summary>What one leg of a transfer did, measured on both sides.
///
/// <b>Conservation is checked, never assumed.</b> The only evidence that units
/// moved is that one inventory has that many fewer and the other has that many
/// more. A caller that reports "I moved thirty" is reporting an intention. So
/// this takes both measured deltas and compares them, and any disagreement -
/// in either direction - is <see cref="ContainerMoveOutcome.Uncertain"/>. Fewer
/// arrived than left is material lost; more arrived than left is material
/// minted, which is worse, and a retry loop that trusts its own intention is how
/// you get there.
///
/// <b>A permit is required, and it is spent.</b> Recording a transfer consumes
/// the permission that authorised it, so the same authorisation can never record
/// two transfers. An interrupted job that does not know whether its move
/// happened has to ask the container again, which re-reads it - the only honest
/// way to find out - rather than replaying a token it kept.
///
/// <b>What this is not.</b> Not a ledger. It does not remember, reconcile,
/// compensate or carry custody from one leg to the next; it is the receipt one
/// leg hands to whatever does. That belongs to the custody leaf, and half a
/// custody contract is worse than none.</summary>
internal readonly struct ContainerMoveResult
{
    private ContainerMoveResult(ContainerMoveOutcome outcome, int moved, int discrepancy, string evidence)
    {
        Outcome = outcome;
        Moved = moved;
        Discrepancy = discrepancy;
        Evidence = evidence;
    }

    internal ContainerMoveOutcome Outcome { get; }

    /// <summary>How many units are known to have moved. Zero for
    /// <see cref="ContainerMoveOutcome.Uncertain"/> and
    /// <see cref="ContainerMoveOutcome.Refused"/>: an amount nobody can prove is
    /// not an amount, and crediting one is how a job reports work it did not
    /// do.</summary>
    internal int Moved { get; }

    /// <summary>How far the two sides disagree - what left, minus what arrived.
    /// Zero whenever they agree. Positive means material left and did not
    /// arrive; negative means material arrived that did not leave.</summary>
    internal int Discrepancy { get; }

    /// <summary>What was observed, for a person and for a reviewer.</summary>
    internal string Evidence { get; }

    /// <summary>Whether this leg is finished and accounted for.</summary>
    internal bool IsSettled => Outcome == ContainerMoveOutcome.Completed
        || Outcome == ContainerMoveOutcome.Partial
        || Outcome == ContainerMoveOutcome.Nothing;

    /// <summary>Records one leg.
    ///
    /// <paramref name="leftTheSource"/> and <paramref name="arrivedAtTheTarget"/>
    /// are measured deltas, both counted as positive numbers. Which side is the
    /// player's chest depends on the direction the permit authorises; the
    /// arithmetic does not, because conservation reads the same either way.
    /// </summary>
    internal static ContainerMoveResult Record(
        NpcContainerPermit? permit,
        NpcWorldEpoch world,
        NpcTransferPlan plan,
        int leftTheSource,
        int arrivedAtTheTarget)
    {
        if (permit == null)
        {
            return new ContainerMoveResult(
                ContainerMoveOutcome.Refused, 0, 0, "nothing authorised this transfer, so nothing is recorded");
        }

        if (!world.Matches(permit.Epoch))
        {
            return new ContainerMoveResult(
                ContainerMoveOutcome.Refused,
                0,
                0,
                "this permission belongs to a world that is no longer loaded, and the container it names is " +
                    "not the container that name now finds");
        }

        if (!permit.TryConsume())
        {
            return new ContainerMoveResult(
                ContainerMoveOutcome.Refused,
                0,
                0,
                "this permission was already used; an interrupted transfer asks the container again rather " +
                    "than replaying what it was given");
        }

        if (leftTheSource < 0 || arrivedAtTheTarget < 0)
        {
            return new ContainerMoveResult(
                ContainerMoveOutcome.Uncertain,
                0,
                leftTheSource - arrivedAtTheTarget,
                "the counts on one side moved the wrong way, so what happened is not known: " +
                    Counts(leftTheSource, arrivedAtTheTarget));
        }

        if (leftTheSource != arrivedAtTheTarget)
        {
            return new ContainerMoveResult(
                ContainerMoveOutcome.Uncertain,
                0,
                leftTheSource - arrivedAtTheTarget,
                "the two sides do not agree, so nothing is credited and nothing is put right automatically: " +
                    Counts(leftTheSource, arrivedAtTheTarget));
        }

        int moved = leftTheSource;
        if (moved > plan.Units)
        {
            return new ContainerMoveResult(
                ContainerMoveOutcome.Uncertain,
                0,
                0,
                "more moved than was planned, so something else was writing to these inventories at the same " +
                    "time: " + Counts(leftTheSource, arrivedAtTheTarget));
        }

        if (moved == 0)
        {
            return new ContainerMoveResult(ContainerMoveOutcome.Nothing, 0, 0, "nothing moved, and nothing was lost");
        }

        return moved == plan.Units
            ? new ContainerMoveResult(
                ContainerMoveOutcome.Completed, moved, 0, "moved " + Number(moved) + ", as planned")
            : new ContainerMoveResult(
                ContainerMoveOutcome.Partial,
                moved,
                0,
                "moved " + Number(moved) + " of " + Number(plan.Units) + "; the rest stays where it was");
    }

    private static string Counts(int left, int arrived) =>
        Number(left) + " left, " + Number(arrived) + " arrived";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
