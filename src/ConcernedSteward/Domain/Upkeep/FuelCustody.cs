using System;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>Where a unit of withdrawn fuel is. Every unit that left the depot
/// is in exactly one of these, always.</summary>
internal enum FuelPlace
{
    /// <summary>Not a place. Zero, so an unset value never reads as one.
    /// </summary>
    Unspecified = 0,

    /// <summary>In the Steward's own hands.</summary>
    Carried = 1,

    /// <summary>Burned: accepted by a fire, through the game's own path.
    /// Legitimately gone from the world.</summary>
    Burned = 2,

    /// <summary>Put back in the depot.</summary>
    Returned = 3,

    /// <summary>Left the depot and cannot be shown to be anywhere.
    ///
    /// <b>This is a real category and never an accounting convenience.</b> A
    /// unit lands here when a measurement proved it left and could not prove
    /// where it went — a fire that took the item and discarded the fuel, a
    /// crash between a withdrawal and its receipt, a body that died holding it.
    /// Nothing ever moves a unit <i>out</i> of here, because the only way to do
    /// that would be to decide, without evidence, that it had been somewhere
    /// after all.</summary>
    Unaccounted = 4,
}

/// <summary>The conservation ledger for one run of fire tending.
///
/// <b>The one invariant.</b>
/// <code>
/// Withdrawn == Carried + Burned + Returned + Unaccounted
/// </code>
/// It holds after every operation, and it is checked rather than asserted in a
/// comment: every mutator re-establishes it and <see cref="Balances"/> is what
/// the tests read. A ledger that cannot state where each unit went is not a
/// ledger, it is a total.
///
/// <b>The invariant is Concerned NPC's, not this file's</b> (#376). This type
/// used to keep four counters of its own. Every unit now lives in the
/// library's <see cref="NpcCustodyLedger"/>, the same ledger Concerned
/// Teamster's collection account keeps Gunnar's material in, so "acquired
/// equals the sum over every place" is one rule for every role. What is left
/// here is only what is the Steward's: which of her words is which library
/// place, the refusal to clamp, the reload restatement, and the one
/// acknowledgement a person may give. Each place is a distinct library place:
///
/// <list type="bullet">
/// <item><see cref="FuelPlace.Carried"/> is <see cref="NpcCustodyPlace.Carried"/>,
/// her pack.</item>
/// <item><see cref="FuelPlace.Burned"/> is <see cref="NpcCustodyPlace.Delivered"/>
/// at the fire: where the job was taking it, terminal and credited once.</item>
/// <item><see cref="FuelPlace.Returned"/> is <see cref="NpcCustodyPlace.Handed"/>
/// at the depot: back in the player's keeping and <b>terminal</b>, so a unit put
/// back is never again the job's. A later withdrawal from that chest is a new
/// acquisition, measured on its own; were the depot a place the job still held,
/// the same wood could be counted once in the chest and again in her hands. A
/// lead default pending owner confirmation
/// (docs/mods/concerned-npc/ARCHITECTURE.md section 10).</item>
/// <item><see cref="FuelPlace.Unaccounted"/> is <see cref="NpcCustodyPlace.Lost"/>.
/// The library never moves material out of it, which is the rule this type has
/// always stated: nothing leaves unaccounted except by a person's
/// acknowledgement, and that moves nothing (see <see cref="AcknowledgeLoss"/>).
/// </item>
/// </list>
///
/// <b>Why counts rather than identified items.</b> Wood is fungible and vanilla
/// merges it into stacks the instant it lands, so an identity per unit would be
/// a fiction maintained by us and contradicted by the game. Counts are what can
/// actually be measured on both sides of a move, and the measurement is the
/// evidence. For the same reason the library is told one material, the run's
/// fuel, whatever item that is: this ledger never knew which item it counted.
///
/// <b>What this does not do.</b> It never decides that something happened. Each
/// mutator is called by the loop <i>after</i> a measured engine change, with
/// the measured number, and records it as a library acquisition or a measured
/// move. Nothing here predicts, retries or compensates.
///
/// <b>Nothing here is written to disk, apart from the loss</b>, which the
/// runtime saves in the Steward's own record (see <see cref="RestoreLoss"/>).
/// Every place is named in the library's unknown load, because nothing in this
/// domain type knows which world it is in. <b>It is bounded by one world load
/// all the same</b> (#376, third slice): when her world goes down the run is
/// closed with it (<see cref="CloseForWorld"/>) and the next world starts a
/// fresh ledger. Before that slice the ledger lived as long as the runtime, so
/// fuel she carried in one world became a loss in the next.
/// </summary>
internal sealed class FuelCustody
{
    /// <summary>The one job this ledger keeps custody for. A constant, because
    /// a fresh run is a fresh library ledger (<see cref="TryReset"/>) and
    /// nothing else writes to it.</summary>
    internal const string Job = "steward-fuel-run";

    internal static readonly NpcMaterial Fuel = NpcMaterial.Of("steward-fuel");
    private static readonly NpcCustodyLocation InHerPack = Place(NpcCustodyPlace.Carried, "pack");
    private static readonly NpcCustodyLocation BurnedInAFire = Place(NpcCustodyPlace.Delivered, "fire");
    private static readonly NpcCustodyLocation BackInTheDepot = Place(NpcCustodyPlace.Handed, "depot");
    private static readonly NpcCustodyLocation NotFound = Place(NpcCustodyPlace.Lost, "unaccounted");

    private NpcCustodyLedger _ledger = NewRun();
    private int _nextStep;

    /// <summary>Units the library still holds as lost that a person has
    /// acknowledged. They stay where the library put them - nothing in that
    /// ledger is ever taken away - and this run stops counting them in
    /// <see cref="Withdrawn"/> and <see cref="Unaccounted"/>. Only
    /// <see cref="AcknowledgeLoss"/> raises it and only a fresh run clears it.
    /// </summary>
    private int _acknowledged;

    /// <summary>Total units taken out of the depot during this run.</summary>
    public int Withdrawn => _ledger.Acquired(Job, Fuel) - _acknowledged;

    public int Carried => _ledger.HoldingAt(Job, InHerPack, Fuel);

    public int Burned => _ledger.HoldingAt(Job, BurnedInAFire, Fuel);

    public int Returned => _ledger.HoldingAt(Job, BackInTheDepot, Fuel);

    public int Unaccounted => _ledger.HoldingAt(Job, NotFound, Fuel) - _acknowledged;

    /// <summary>Increments on every change, so a caller can tell a view it read
    /// is stale. Same idea as <c>ActorModeOwner.Revision</c>.</summary>
    public int Revision { get; private set; }

    /// <summary>The library ledger this run's units are kept in. Read-only use:
    /// a test asks it whether the invariant holds in the library's own terms.
    /// </summary>
    internal NpcCustodyLedger Ledger => _ledger;

    /// <summary>True when the invariant holds, asked of the library and in the
    /// Steward's own words both. The tests read this after every operation.
    /// </summary>
    public bool Balances =>
        _ledger.IsConserved(Job, Fuel)
        && Withdrawn == Carried + Burned + Returned + Unaccounted;

    /// <summary>True when something left the depot and could not be accounted
    /// for. New work refuses while this is set: the Steward has already lost
    /// track of a player's materials once, and the answer to that is to stop
    /// and say so, not to carry on and hope.</summary>
    public bool HasLoss => Unaccounted > 0;

    public int CountAt(FuelPlace place)
    {
        switch (place)
        {
            case FuelPlace.Carried: return Carried;
            case FuelPlace.Burned: return Burned;
            case FuelPlace.Returned: return Returned;
            case FuelPlace.Unaccounted: return Unaccounted;
            default: return 0;
        }
    }

    /// <summary>Units measurably left the depot and are now in his hands.
    /// </summary>
    public void RecordWithdrawn(int units)
    {
        Require(units, nameof(units));
        Acquire(InHerPack, units);
        Revision++;
    }

    /// <summary>A fire accepted units, measured on both the fire and his hands.
    /// </summary>
    public void RecordBurned(int units)
    {
        Require(units, nameof(units));
        TakeFromHands(units, BurnedInAFire);
        Revision++;
    }

    /// <summary>Units measurably went back into the depot.</summary>
    public void RecordReturned(int units)
    {
        Require(units, nameof(units));
        TakeFromHands(units, BackInTheDepot);
        Revision++;
    }

    /// <summary>Units left his hands and could not be shown to have arrived
    /// anywhere. Terminal for those units.</summary>
    public void RecordLost(int units)
    {
        Require(units, nameof(units));
        TakeFromHands(units, NotFound);
        Revision++;
    }

    /// <summary>Reconciles the ledger against what the Steward is <i>actually</i>
    /// carrying, and reports the discrepancy.
    ///
    /// <b>This is the whole of reload recovery, and it is deliberately not a
    /// replay.</b> After a crash an intent may have been written with no
    /// receipt, so the record cannot say whether the move happened. Replaying
    /// it would duplicate on one branch and losing it would destroy on the
    /// other. What <i>can</i> be established is what he is holding right now,
    /// so that is measured and the record is corrected to match it:
    ///
    /// <list type="bullet">
    /// <item>He holds <b>more</b> than the record knew: those units came out of
    /// the depot in a withdrawal whose receipt never landed. They are recorded
    /// as withdrawn and carried, which is the truth — the depot really is
    /// short by that much — and conservation holds again.</item>
    /// <item>He holds <b>less</b>: units the record thought he had are gone with
    /// no evidence of where. They become <see cref="FuelPlace.Unaccounted"/>,
    /// which is a fact and not a failure to balance.</item>
    /// </list>
    ///
    /// Either way the job that owned them is <b>abandoned rather than
    /// resumed</b>. Returns the signed difference, for the evidence line.
    /// </summary>
    public int RestateCarried(int actuallyCarried)
    {
        if (actuallyCarried < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actuallyCarried), actuallyCarried,
                "A measured count cannot be negative. Use a failed measurement's own path.");
        }

        int difference = actuallyCarried - Carried;
        if (difference == 0)
        {
            return 0;
        }

        if (difference > 0)
        {
            Acquire(InHerPack, difference);
        }
        else
        {
            TakeFromHands(-difference, NotFound);
        }

        Revision++;
        return difference;
    }

    /// <summary>Restores a loss a previous session recorded.
    ///
    /// <b>Why only the loss is persisted.</b> Everything else in this ledger can
    /// be re-established by measurement — what he carries is in his hands, and
    /// what burned or went back is history that changes nothing about what to do
    /// next. A loss cannot: the units are gone, so there is nothing left to
    /// measure, and without this the Steward would forget he had lost a player's
    /// wood the moment they reloaded and would carry on as though nothing had
    /// happened.
    ///
    /// <b>Reconciled, never replayed</b> (#376). The number on disk is the loss
    /// this ledger knew when the record was written, not a new one, so restoring
    /// it raises <see cref="Unaccounted"/> <i>to</i> that number and never adds
    /// it on top. It used to add: the loop lives as long as the runtime, so
    /// leaving a world and loading it again restored the same loss into a
    /// ledger that still held it, the next save wrote the doubled number, and
    /// every reload doubled it again. A loss already recorded here is never
    /// lowered by a smaller number on disk either: a record that could not be
    /// written after a loss is behind this ledger, not ahead of it. Since the
    /// ledger closes with its world (<see cref="CloseForWorld"/>), a restore is
    /// only ever its own world's.
    ///
    /// What is restored is acquired straight into the library's lost place, so
    /// the invariant still holds: those units really did leave the depot, and
    /// saying so is the honest shape of the record.</summary>
    internal void RestoreLoss(int units)
    {
        if (units < 1)
        {
            return;
        }

        int missing = units - Unaccounted;
        if (missing < 1)
        {
            return;
        }

        Acquire(NotFound, missing);
        Revision++;
    }

    /// <summary>Starts a fresh run. Refused while anything is still in his
    /// hands or unaccounted for, because a new run that inherited either would
    /// make the old units unattributable to the job that moved them. A fresh
    /// run is a fresh library ledger: the old one holds nothing that can still
    /// move, and nothing of it is carried over.</summary>
    public bool TryReset()
    {
        if (Carried != 0 || Unaccounted != 0)
        {
            return false;
        }

        _ledger = NewRun();
        _acknowledged = 0;
        Revision++;
        return true;
    }

    /// <summary>Her world went down (#376, third slice). Returns where the run
    /// stood and replaces the ledger with a fresh one in the same call,
    /// <b>unconditionally</b>: unlike <see cref="TryReset"/>, which refuses an
    /// unsettled run, this is not a new run beginning in the same world but the
    /// world the run described having gone. Nothing moves in any world. What
    /// was in her pack is still in her pack, in that world, and is counted
    /// again by measurement when it loads (<see cref="RestateCarried"/>); a
    /// loss nobody acknowledged is the runtime's to make sure of in that
    /// world's record before calling this, which the returned snapshot is for.
    /// Never carried into the next world's ledger, where it would be counted
    /// against a depot it never came out of.</summary>
    internal FuelRunAtWorldDown CloseForWorld()
    {
        var closed = new FuelRunAtWorldDown(Withdrawn, Carried, Burned, Returned, Unaccounted, Balances);
        _ledger = NewRun();
        _acknowledged = 0;
        Revision++;
        return closed;
    }

    /// <summary>Clears a recorded loss, once a person has been told about it.
    ///
    /// The only way out of <see cref="HasLoss"/>, and it is explicitly an
    /// acknowledgement rather than a correction. Nothing moves in the library
    /// ledger - the units stay lost there, which is where they went - and this
    /// run stops counting them in <see cref="Withdrawn"/> and
    /// <see cref="Unaccounted"/>, so the Steward is allowed to work again.
    /// Nothing is recreated and nothing is written back to any chest.</summary>
    public void AcknowledgeLoss()
    {
        int unaccounted = Unaccounted;
        if (unaccounted == 0)
        {
            return;
        }

        _acknowledged += unaccounted;
        Revision++;
    }

    /// <summary>One line a player can read, and a reviewer can check the
    /// invariant from.</summary>
    public string Describe()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "withdrew {0}; carrying {1}, burned {2}, returned {3}, unaccounted {4}",
            Withdrawn, Carried, Burned, Returned, Unaccounted);
    }

    public override string ToString() => Describe();

    private static NpcCustodyLedger NewRun()
    {
        var ledger = new NpcCustodyLedger();
        ledger.OpenJob(Job);
        return ledger;
    }

    private static NpcCustodyLocation Place(NpcCustodyPlace place, string key) =>
        new NpcCustodyLocation(place, key, NpcWorldEpoch.Unknown);

    private ReservationId Mint() => ReservationId.For(Job, _nextStep++);

    /// <summary>Units entered the run: measured out of the depot, or a loss a
    /// previous session recorded.</summary>
    private void Acquire(NpcCustodyLocation at, int units)
    {
        NpcCustodyOutcome acquired = _ledger.Acquire(Mint(), at, Fuel, units);
        if (acquired != NpcCustodyOutcome.Applied)
        {
            // The job is opened with the ledger and every name is fresh, so this
            // is the library refusing for a reason of its own. Nothing was
            // recorded, and a caller that measured a withdrawal must not be told
            // it was: this is as loud as a move the record cannot account for.
            throw new InvalidOperationException(
                "The conservation ledger refused to record " +
                units.ToString(CultureInfo.InvariantCulture) + " units entering the Steward's custody (" +
                acquired + "). Nothing was recorded.");
        }
    }

    private void TakeFromHands(int units, NpcCustodyLocation to)
    {
        int carried = Carried;
        if (units > carried)
        {
            throw new InvalidOperationException(
                "The ledger was asked to move " + units.ToString(CultureInfo.InvariantCulture) +
                " units out of the Steward's hands, which hold " +
                carried.ToString(CultureInfo.InvariantCulture) + ". " +
                "Every unit must come from somewhere: the caller measured a move that " +
                "the record cannot account for, and silently allowing it would make the " +
                "conservation invariant meaningless.");
        }

        NpcCustodyOutcome moved = _ledger.RecordMeasuredMove(Mint(), InHerPack, to, Fuel, units);
        if (moved != NpcCustodyOutcome.Applied)
        {
            // Unreachable after the check above: her pack is not a terminal
            // place, every destination is named in the same load, and no
            // transfer of this run ever waits on a person. If it is reached,
            // nothing moved, and saying so loudly is the only honest answer.
            // Should the library ever refuse after opening the transfer, that
            // open record would refuse every later move of this run too, so
            // she stops with it rather than carrying on half recorded.
            throw new InvalidOperationException(
                "The conservation ledger refused to record " +
                units.ToString(CultureInfo.InvariantCulture) + " units leaving the Steward's hands (" +
                moved + "). Nothing was recorded.");
        }
    }

    private static void Require(int units, string parameterName)
    {
        if (units < 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, units, "A custody movement is at least one unit.");
        }
    }
}

/// <summary>Where one run stood when her world went down, in her words.
/// </summary>
internal readonly struct FuelRunAtWorldDown
{
    public FuelRunAtWorldDown(int withdrawn, int carried, int burned, int returned, int unaccounted, bool balanced)
    {
        Withdrawn = withdrawn;
        Carried = carried;
        Burned = burned;
        Returned = returned;
        Unaccounted = unaccounted;
        Balanced = balanced;
    }

    public int Withdrawn { get; }

    /// <summary>Still in her pack, in the world that went down.</summary>
    public int Carried { get; }

    public int Burned { get; }

    public int Returned { get; }

    /// <summary>A loss nobody has acknowledged.</summary>
    public int Unaccounted { get; }

    public bool Balanced { get; }

    /// <summary>Nothing in her pack and nothing unaccounted: nothing of the run
    /// can still matter.</summary>
    public bool IsSettled => Carried == 0 && Unaccounted == 0;

    public string Describe() => string.Format(
        CultureInfo.InvariantCulture,
        "withdrew {0}; carrying {1}, burned {2}, returned {3}, unaccounted {4}",
        Withdrawn, Carried, Burned, Returned, Unaccounted);
}
