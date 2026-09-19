using System;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>How much of a container's contents this job may actually plan on.
///
/// <b>Why a planner may not just count what is in the chest.</b> Chest C holds
/// a hundred wood. Job A plans a hundred-wood wall against it; a tick later job
/// B plans a sixty-wood fence against the same observation, because the
/// observation still says a hundred. Both plans are perfectly formed and
/// between them they plan a hundred and sixty wood out of a chest holding a
/// hundred. Custody's measured deltas stop the second one <i>minting</i>
/// anything, so the cost is a stall and a wasted walk rather than duplicated
/// material - but a stall on every tick, on a job the player can see is doable,
/// is exactly what an NPC gets no credit for surviving.
///
/// So a planner asks this instead: what is in there, less what somebody else has
/// already set aside.
///
/// <b>The asking job is baked in, not passed.</b> A selector that carried a job
/// id could pass the wrong one, and the wrong one is not a crash - it is a plan
/// that quietly counts another job's reserved units as free. The implementation
/// knows whose it is, and the selector cannot get it wrong because it never
/// holds it.
///
/// <b>Null is the honest default, not a safe one.</b> A pipeline built without
/// an availability is a pipeline that plans against raw counts, which is the
/// behaviour above. It is allowed because the first roles will land before their
/// reservation books do, and it is the caller's decision rather than a silent
/// one.
///
/// <b>Two requirements on the role, and they are requirements rather than
/// advice.</b> This seam joins two pairs of vocabularies that nothing in this
/// library mints, and it compares both ordinally:
///
/// <i>A container's name.</i> <c>INpcContainer.Key</c>, which is what a
/// planner's <see cref="SourceStock"/> is keyed by, must be the same string as
/// the <c>NpcCustodyLocation.Key</c> a reservation over that container is
/// recorded under. Keying one by a prefab and a place and the other by a ZDOID's
/// text means no container ever matches.
///
/// <i>An item's name.</i> <see cref="StockLine.Item"/> must be the same string
/// as the <c>NpcMaterial.ItemName</c> a reservation's stacks carry. "Wood" in
/// one and "wood" in the other means no item ever matches.
///
/// <b>What a mismatch costs, and why it is worse than having no seam.</b>
/// Nothing matches, nothing is subtracted, and every call answers the raw
/// observed count: the exact pre-seam behaviour, in the unsafe direction, with a
/// seam in place saying it is handled. No exception is thrown and no plan looks
/// wrong. So the shipped implementation counts what it saw and did not match,
/// and <see cref="NpcReservedSourceAvailability.Agreement"/> is a value a role's
/// own tests can assert on - see
/// <see cref="NpcSourceVocabularies"/>.</summary>
internal interface INpcSourceAvailability
{
    /// <summary>How many units of <paramref name="item"/> in the container named
    /// <paramref name="containerKey"/> this job may count on, given that
    /// <paramref name="observed"/> were seen in it.
    ///
    /// Never more than <paramref name="observed"/> and never below zero. An
    /// implementation that cannot tell answers <paramref name="observed"/> -
    /// failing towards a plan that may stall rather than towards one that
    /// under-provisions, because a stall is visible and an under-provisioned
    /// round is an NPC that walks away from half a wall.</summary>
    int AvailableIn(string containerKey, string item, int observed);
}

/// <summary>Whether the two role-owned vocabularies this seam joins were seen to
/// line up.
///
/// <b>Every value is a fact about what was compared, not a judgement.</b> A
/// mismatch cannot be told apart from an ordinary absence by one observation -
/// "no reservation names any chest I asked about" is what a wrongly-keyed role
/// looks like and also what a settlement with nothing reserved in those
/// particular chests looks like. So this reports what happened and a role's own
/// test, which knows what it reserved, is what turns it into a verdict.</summary>
internal enum NpcSourceVocabularies
{
    /// <summary>Nobody asked, or the book held nothing to compare against.
    /// </summary>
    NothingToCompare = 0,

    /// <summary>At least one container name matched, and at least one item name
    /// inside a matched container matched. The seam is joined.</summary>
    Agree = 1,

    /// <summary>Reservations were held and <b>not one</b> of them named a
    /// container that was asked about. Either the role keys containers and
    /// custody locations differently - in which case nothing is ever subtracted
    /// and every answer is the raw count - or this job's chests genuinely have
    /// nothing reserved in them.</summary>
    NoContainerNameMatched = 2,

    /// <summary>Containers matched and held stacks, and <b>not one</b> of those
    /// stacks named an item that was asked about. Either the role writes item
    /// names differently on the two sides, or the reservations in those chests
    /// are all of materials this job does not want.</summary>
    NoItemNameMatchedInAMatchedContainer = 3,
}

/// <summary>What one availability saw while it was being asked.
///
/// <b>Why counting is the instrument rather than a throw or a refusal.</b> The
/// seam's own contract says an implementation that cannot tell answers what was
/// observed, so failing closed here would stall every job that legitimately has
/// nothing reserved. And a mismatch is a role defect that produces a plausible
/// number rather than an error. What is left is to make it <i>visible</i>: a
/// role's test reserves a known amount in a known chest, plans, and asserts that
/// the names met.</summary>
internal readonly struct NpcSourceAvailabilityAgreement
{
    internal NpcSourceAvailabilityAgreement(
        int asks,
        int heldRowsSeen,
        int containerNameMatches,
        int stacksInMatchedContainers,
        int itemNameMatches,
        int unitsSubtracted)
    {
        Asks = asks;
        HeldRowsSeen = heldRowsSeen;
        ContainerNameMatches = containerNameMatches;
        StacksInMatchedContainers = stacksInMatchedContainers;
        ItemNameMatches = itemNameMatches;
        UnitsSubtracted = unitsSubtracted;
    }

    /// <summary>How many times a planner asked and the book was actually walked.
    /// </summary>
    internal int Asks { get; }

    /// <summary>How many held, non-excluded reservations were looked at, summed
    /// over the asks. A row the book holds across three asks counts three
    /// times: this is work done, not rows in the book.</summary>
    internal int HeldRowsSeen { get; }

    /// <summary>How many of those named a container that was being asked about.
    /// <b>Nought with <see cref="HeldRowsSeen"/> above nought is the container
    /// half of the mismatch.</b></summary>
    internal int ContainerNameMatches { get; }

    /// <summary>How many material stacks were looked at inside matched
    /// containers.</summary>
    internal int StacksInMatchedContainers { get; }

    /// <summary>How many of those named the item being asked about.
    /// <b>Nought with <see cref="StacksInMatchedContainers"/> above nought is
    /// the item half of the mismatch</b> - the one the reviewer named, because
    /// a container can match while every item name inside it misses.</summary>
    internal int ItemNameMatches { get; }

    /// <summary>How many units were taken off an observed count in total. Nought
    /// across a whole plan, with a book that holds something, is the symptom a
    /// role sees first.</summary>
    internal int UnitsSubtracted { get; }

    /// <summary>What the counts add up to.</summary>
    internal NpcSourceVocabularies Vocabularies
    {
        get
        {
            if (Asks == 0 || HeldRowsSeen == 0)
            {
                return NpcSourceVocabularies.NothingToCompare;
            }

            if (ContainerNameMatches == 0)
            {
                return NpcSourceVocabularies.NoContainerNameMatched;
            }

            return StacksInMatchedContainers > 0 && ItemNameMatches == 0
                ? NpcSourceVocabularies.NoItemNameMatchedInAMatchedContainer
                : NpcSourceVocabularies.Agree;
        }
    }

    /// <summary>A line for a log or a test failure message.</summary>
    public override string ToString() =>
        Vocabularies + ": " + Asks + " asks, " + HeldRowsSeen + " held rows seen, " +
        ContainerNameMatches + " container names matched, " + ItemNameMatches + " of " +
        StacksInMatchedContainers + " item names matched, " + UnitsSubtracted + " units subtracted";
}

/// <summary>The shipped <see cref="INpcSourceAvailability"/>: what a material
/// reservation book says is still free in a container.
///
/// <b>It matches on the item's name alone, and that is deliberate.</b> A
/// reservation is over an <see cref="NpcMaterial"/>, which carries a quality and
/// a variant; a planner's item token is a bare string the role chose, with no
/// way to say which quality it meant. Matching name-only therefore subtracts
/// <i>every</i> quality of that name, which can only ever report <b>less</b>
/// available than a quality-exact match would. That is the safe direction: the
/// worst case is a job that waits for material it could have had, and the
/// alternative's worst case is two jobs planning the same units, which is the
/// failure this type exists to prevent.
///
/// It is also why this does not call the book's own <c>AvailableIn</c>, which
/// takes a material and would need a quality this layer cannot supply. The
/// arithmetic is the same arithmetic; only the match is wider - and keeping it
/// here means the book's signature can change without a planner noticing, which
/// it has already done once.
///
/// <b>It excludes one reservation, never one job, and the difference is a
/// defect somebody already found.</b> The obvious rule - "ignore everything my
/// own job holds" - is wrong for an arithmetic of quantities: one job's two
/// steps each see forty free nails in a chest holding forty, and between them
/// set aside eighty. So the exclusion is a single name, for the one case that
/// needs it: a re-plan re-stating a claim it already owns. A planner building a
/// fresh plan excludes nothing and sees every held unit, its own job's
/// included, which is the conservative and correct answer.
///
/// <b>It counts what it matched, because a mismatch answers plausibly.</b> Both
/// comparisons below are between vocabularies a role owns on both sides, and
/// neither is checkable from here: if they disagree, nothing is subtracted, every
/// answer is the raw observed count, and a plan built on it looks perfectly
/// well formed. <see cref="Agreement"/> is what a role's own test asserts on -
/// it reserved a known amount in a known chest, so it knows what the names
/// should have met.</summary>
internal sealed class NpcReservedSourceAvailability : INpcSourceAvailability
{
    private readonly NpcMaterialReservationBook _book;
    private readonly ReservationId _exceptThis;
    private int _asks;
    private int _heldRowsSeen;
    private int _containerNameMatches;
    private int _stacksInMatchedContainers;
    private int _itemNameMatches;
    private int _unitsSubtracted;

    /// <summary>Reads a book.</summary>
    /// <param name="book">Where the claims live.</param>
    /// <param name="exceptThis">The one reservation to look past, for a re-plan
    /// re-stating a claim it already owns. Default excludes nothing, which is
    /// what a fresh plan wants.</param>
    internal NpcReservedSourceAvailability(NpcMaterialReservationBook book, ReservationId exceptThis = default)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _exceptThis = exceptThis;
    }

    /// <summary>What the two vocabularies looked like over every ask so far.
    /// <b>Read after planning, asserted on by a role's own tests.</b> See
    /// <see cref="NpcSourceAvailabilityAgreement"/> for why this is a count
    /// rather than a throw.</summary>
    internal NpcSourceAvailabilityAgreement Agreement =>
        new NpcSourceAvailabilityAgreement(
            _asks,
            _heldRowsSeen,
            _containerNameMatches,
            _stacksInMatchedContainers,
            _itemNameMatches,
            _unitsSubtracted);

    /// <inheritdoc />
    public int AvailableIn(string containerKey, string item, int observed)
    {
        if (observed <= 0 || string.IsNullOrEmpty(containerKey) || string.IsNullOrEmpty(item))
        {
            return observed < 0 ? 0 : observed;
        }

        _asks++;

        int reserved = 0;
        foreach (NpcMaterialReservation reservation in _book.Reservations)
        {
            if (reservation.State != NpcReservationState.Held || Excluded(reservation))
            {
                continue;
            }

            // Counted before the container name is compared, so "the book held
            // something and none of it named a chest I asked about" is a state
            // that can be told from "the book held nothing".
            _heldRowsSeen++;
            if (!string.Equals(reservation.Container.Key, containerKey, StringComparison.Ordinal))
            {
                continue;
            }

            _containerNameMatches++;
            foreach (NpcMaterialStack stack in reservation.Stacks)
            {
                _stacksInMatchedContainers++;
                if (string.Equals(stack.Material.ItemName, item, StringComparison.Ordinal))
                {
                    _itemNameMatches++;
                    reserved += stack.Count;
                }
            }
        }

        int free = observed - reserved;
        if (free < 0)
        {
            free = 0;
        }

        _unitsSubtracted += observed - free;
        return free;
    }

    private bool Excluded(NpcMaterialReservation reservation) =>
        !_exceptThis.IsEmpty
        && string.Equals(reservation.Id.Value, _exceptThis.Value, StringComparison.Ordinal);
}
