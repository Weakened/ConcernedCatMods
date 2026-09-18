using System;
using System.Globalization;

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
/// <b>Why counts rather than identified items.</b> Wood is fungible and vanilla
/// merges it into stacks the instant it lands, so an identity per unit would be
/// a fiction maintained by us and contradicted by the game. Counts are what can
/// actually be measured on both sides of a move, and the measurement is the
/// evidence.
///
/// <b>What this does not do.</b> It never decides that something happened. Each
/// mutator is called by the loop <i>after</i> a measured engine change, with
/// the measured number. Nothing here predicts, retries or compensates.</summary>
internal sealed class FuelCustody
{
    private int _carried;
    private int _burned;
    private int _returned;
    private int _unaccounted;

    /// <summary>Total units taken out of the depot during this run.</summary>
    public int Withdrawn { get; private set; }

    public int Carried => _carried;

    public int Burned => _burned;

    public int Returned => _returned;

    public int Unaccounted => _unaccounted;

    /// <summary>Increments on every change, so a caller can tell a view it read
    /// is stale. Same idea as <c>ActorModeOwner.Revision</c>.</summary>
    public int Revision { get; private set; }

    /// <summary>True when the invariant holds. The tests read this after every
    /// operation; the loop reads it before recording a receipt.</summary>
    public bool Balances => Withdrawn == _carried + _burned + _returned + _unaccounted;

    /// <summary>True when something left the depot and could not be accounted
    /// for. New work refuses while this is set: the Steward has already lost
    /// track of a player's materials once, and the answer to that is to stop
    /// and say so, not to carry on and hope.</summary>
    public bool HasLoss => _unaccounted > 0;

    public int CountAt(FuelPlace place)
    {
        switch (place)
        {
            case FuelPlace.Carried: return _carried;
            case FuelPlace.Burned: return _burned;
            case FuelPlace.Returned: return _returned;
            case FuelPlace.Unaccounted: return _unaccounted;
            default: return 0;
        }
    }

    /// <summary>Units measurably left the depot and are now in his hands.
    /// </summary>
    public void RecordWithdrawn(int units)
    {
        Require(units, nameof(units));
        Withdrawn += units;
        _carried += units;
        Revision++;
    }

    /// <summary>A fire accepted units, measured on both the fire and his hands.
    /// </summary>
    public void RecordBurned(int units)
    {
        Require(units, nameof(units));
        TakeFromHands(units);
        _burned += units;
        Revision++;
    }

    /// <summary>Units measurably went back into the depot.</summary>
    public void RecordReturned(int units)
    {
        Require(units, nameof(units));
        TakeFromHands(units);
        _returned += units;
        Revision++;
    }

    /// <summary>Units left his hands and could not be shown to have arrived
    /// anywhere. Terminal for those units.</summary>
    public void RecordLost(int units)
    {
        Require(units, nameof(units));
        TakeFromHands(units);
        _unaccounted += units;
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

        int difference = actuallyCarried - _carried;
        if (difference == 0)
        {
            return 0;
        }

        if (difference > 0)
        {
            Withdrawn += difference;
            _carried = actuallyCarried;
        }
        else
        {
            _unaccounted += -difference;
            _carried = actuallyCarried;
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
    /// The withdrawn total is raised with it so the invariant still holds: those
    /// units really did leave the depot, and saying so is the honest shape of
    /// the record.</summary>
    internal void RestoreLoss(int units)
    {
        if (units < 1)
        {
            return;
        }

        Withdrawn += units;
        _unaccounted += units;
        Revision++;
    }

    /// <summary>Starts a fresh run. Refused while anything is still in his
    /// hands or unaccounted for, because a new run that inherited either would
    /// make the old units unattributable to the job that moved them.</summary>
    public bool TryReset()
    {
        if (_carried != 0 || _unaccounted != 0)
        {
            return false;
        }

        Withdrawn = 0;
        _burned = 0;
        _returned = 0;
        Revision++;
        return true;
    }

    /// <summary>Clears a recorded loss, once a person has been told about it.
    ///
    /// The only way out of <see cref="HasLoss"/>, and it is explicitly an
    /// acknowledgement rather than a correction: the units stay counted in
    /// <see cref="Withdrawn"/> so the run's history still adds up, and what
    /// changes is only that the Steward is allowed to work again. Nothing is
    /// recreated and nothing is written back to any chest.</summary>
    public void AcknowledgeLoss()
    {
        if (_unaccounted == 0)
        {
            return;
        }

        Withdrawn -= _unaccounted;
        _unaccounted = 0;
        Revision++;
    }

    /// <summary>One line a player can read, and a reviewer can check the
    /// invariant from.</summary>
    public string Describe()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "withdrew {0}; carrying {1}, burned {2}, returned {3}, unaccounted {4}",
            Withdrawn, _carried, _burned, _returned, _unaccounted);
    }

    public override string ToString() => Describe();

    private void TakeFromHands(int units)
    {
        if (units > _carried)
        {
            throw new InvalidOperationException(
                "The ledger was asked to move " + units.ToString(CultureInfo.InvariantCulture) +
                " units out of the Steward's hands, which hold " +
                _carried.ToString(CultureInfo.InvariantCulture) + ". " +
                "Every unit must come from somewhere: the caller measured a move that " +
                "the record cannot account for, and silently allowing it would make the " +
                "conservation invariant meaningless.");
        }

        _carried -= units;
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
