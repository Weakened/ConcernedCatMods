using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedSteward.Domain.Scope;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>Where one trip stands. <see cref="Unspecified"/> is zero and is
/// never a state the loop is in.</summary>
internal enum UpkeepPhase
{
    Unspecified = 0,

    /// <summary>Nothing to do, or nothing allowed. Scans on an interval.
    /// </summary>
    Idle = 1,

    /// <summary>Walking to the supply depot to fetch wood.</summary>
    ToDepot = 2,

    /// <summary>At the depot, taking wood out of it.</summary>
    Withdrawing = 3,

    /// <summary>Walking to the reserved fire, carrying wood.</summary>
    ToTarget = 4,

    /// <summary>At the fire, putting wood in one unit at a time.</summary>
    Feeding = 5,

    /// <summary>Walking back to the depot with whatever was not burned.
    /// </summary>
    Returning = 6,

    /// <summary>At the depot, putting the remainder back.</summary>
    Depositing = 7,

    /// <summary>Stopped, and a person needs to look. Nothing further happens
    /// until it is acknowledged.</summary>
    NeedsAttention = 8,
}

/// <summary>Everything one tick of the loop is allowed to see.
///
/// Passed in rather than held, so the loop has no way to reach the game, no
/// cached port that could outlive a world, and no state a test cannot
/// construct. Every awkward combination — no authority, an empty depot, a fire
/// that changed hands mid-walk — is one struct literal away.</summary>
internal readonly struct UpkeepTick
{
    public UpkeepTick(
        float now,
        bool tendingEnabled,
        WorkAuthorityVerdict authority,
        ScopeSnapshot scope,
        IFuelTargetPort fires,
        IItemStorePort depot,
        IItemStorePort carrier,
        IStewardMotionPort motion)
    {
        Now = now;
        TendingEnabled = tendingEnabled;
        Authority = authority;
        Scope = scope;
        Fires = fires;
        Depot = depot;
        Carrier = carrier;
        Motion = motion;
    }

    public float Now { get; }

    /// <summary>The fire-tending switch, read live so turning it off stops him
    /// on the next tick rather than at the next restart.</summary>
    public bool TendingEnabled { get; }

    public WorkAuthorityVerdict Authority { get; }

    public ScopeSnapshot Scope { get; }

    public IFuelTargetPort Fires { get; }

    public IItemStorePort Depot { get; }

    /// <summary>The Steward's own inventory.</summary>
    public IItemStorePort Carrier { get; }

    public IStewardMotionPort Motion { get; }
}

/// <summary>The Steward's one job: keep the settlement's fires alight.
///
/// <b>The shape of a trip</b>, and nothing is allowed to skip a step:
///
/// <list type="number">
/// <item><b>Discover.</b> A bounded, deterministic scan of the fires inside the
/// marked settlement (<see cref="FuelTargetSelector"/>).</item>
/// <item><b>Reserve.</b> One fire, held for the life of the trip.</item>
/// <item><b>Fetch.</b> Walk to the marked depot and withdraw real wood, with
/// both inventories measured before and after.</item>
/// <item><b>Walk.</b> To the fire, on the game's own motor.</item>
/// <item><b>Revalidate.</b> Everything the scan established could have changed
/// during the walk, and the walk is the longest part of the trip.</item>
/// <item><b>Mutate.</b> One unit, through vanilla's own
/// <c>Fireplace.UseItem</c>.</item>
/// <item><b>Measure.</b> The fire's fuel and his hands, before and after that
/// one call. <b>Never</b> the call's return value: the audit's first finding is
/// that vanilla answers <c>true</c> for a refusal.</item>
/// <item><b>Receipt.</b> What actually moved, written down.</item>
/// <item><b>Return.</b> Carry the remainder back to the depot, then idle.</item>
/// </list>
///
/// <b>What it refuses to do.</b> It never conjures fuel; every unit that
/// reaches a fire came out of the marked chest and that was measured, not
/// assumed. It never takes from any other container. It never touches a fire or
/// chest this process does not own — vanilla destroys the item on an unowned
/// fire, which is the audit's section 3. It never replays a step after an
/// uncertain one, and it never compensates: an unaccountable unit is recorded
/// as unaccounted and the Steward stops until somebody says they have seen it.
///
/// <b>One step per tick.</b> The loop advances at most one phase, and feeds at
/// most one unit, per call. That is what makes the work visible in game
/// instead of instantaneous, and it is also what bounds the cost of a tick to
/// something a frame can afford.</summary>
internal sealed class UpkeepLoop
{
    private const float ArrivalToleranceMetres = 2.5f;

    private readonly UpkeepLimits _limits;
    private readonly IUpkeepJournal _journal;
    private readonly FuelCustody _custody = new FuelCustody();
    private readonly UpkeepReservation _reservation = new UpkeepReservation();
    private readonly AttentionThrottle _throttle;
    private readonly BoundedRetry _retry;
    private readonly Action<string>? _report;

    private UpkeepPhase _phase = UpkeepPhase.Idle;
    private string _explanation = "Waiting.";
    private float _nextScanAt;
    private OrderId _job;
    private int _step;
    private string _fuelItemName = string.Empty;
    private SitePoint _targetPoint;
    private int _scopeRevision = -1;
    private PhaseDeadline _deadline;
    private int _tripsCompleted;
    private int _unitsBurnedTotal;

    /// <summary>Builds the loop.
    ///
    /// <b>It no longer takes an actor-mode owner, and that is the Concerned NPC
    /// adoption rather than a simplification.</b> Until #382 this loop entered
    /// <c>Working</c> when a trip began and released on every exit, against an
    /// <c>ActorModeOwner</c> compiled into this assembly out of shared source.
    /// Nothing ever read it: Foreman and Teamster each compiled their own copy
    /// of that type, so "one mode owner per identity" was three separate truths
    /// in three assemblies and none of them could see the others. The Steward's
    /// identity is now held where every product can see it - through the
    /// library's arbiter, by <c>StewardNpcAdoption</c>, for the life of the body
    /// rather than the life of a trip. The validator forbids this product
    /// constructing a mode owner at all
    /// (<c>check_library_consumers_do_not_bypass_the_arbiter</c>).
    ///
    /// What is NOT lost: whether a trip is running is still this loop's own
    /// answer, and <see cref="IsWorking"/>, <see cref="Reservation"/> and the
    /// trip's <c>OrderId</c> are where it always actually lived.</summary>
    public UpkeepLoop(
        UpkeepLimits limits, IUpkeepJournal journal, Action<string>? report = null)
    {
        _limits = limits;
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _report = report;
        _throttle = new AttentionThrottle(limits.ScanIntervalSeconds * 2f);
        _retry = new BoundedRetry(limits.MaxFailuresPerPhase, limits.ScanIntervalSeconds, limits.ScanIntervalSeconds * 4f);
    }

    public UpkeepPhase Phase => _phase;

    /// <summary>What he is doing, or the reason he is not. Always a sentence a
    /// player can act on: a Steward standing still without an explanation and a
    /// broken one look identical.</summary>
    public string Explanation => _explanation;

    public FuelCustody Custody => _custody;

    public UpkeepReservation Reservation => _reservation;

    /// <summary>The fire being tended, or the default while idle.</summary>
    public FuelTargetKey CurrentTarget => _reservation.Target;

    /// <summary>The last scan, for the status command. Never used to decide
    /// anything — a decision is made from the scan taken in the same tick.
    /// </summary>
    public FuelTargetScan LastScan { get; private set; } = FuelTargetScan.Empty;

    public int TripsCompleted => _tripsCompleted;

    public int UnitsBurnedTotal => _unitsBurnedTotal;

    public bool IsWorking => _phase != UpkeepPhase.Idle && _phase != UpkeepPhase.NeedsAttention;

    /// <summary>Advances the trip by at most one step.</summary>
    public void Tick(in UpkeepTick tick)
    {
        if (_phase == UpkeepPhase.NeedsAttention)
        {
            return;
        }

        string? refusal = WhyNotNow(tick);
        if (refusal != null)
        {
            // Stopping mid-trip is not abandoning it: he keeps what he carries
            // and the ledger keeps saying so. The next tick that is allowed
            // resumes from Idle, which deposits before doing anything else.
            StopAndIdle(tick, refusal);
            return;
        }

        switch (_phase)
        {
            case UpkeepPhase.Idle: TickIdle(tick); break;
            case UpkeepPhase.ToDepot: TickWalk(tick, tick.Scope.DepotPoint, UpkeepPhase.Withdrawing); break;
            case UpkeepPhase.Withdrawing: TickWithdraw(tick); break;
            case UpkeepPhase.ToTarget: TickWalk(tick, _targetPoint, UpkeepPhase.Feeding); break;
            case UpkeepPhase.Feeding: TickFeed(tick); break;
            case UpkeepPhase.Returning: TickWalk(tick, tick.Scope.DepotPoint, UpkeepPhase.Depositing); break;
            case UpkeepPhase.Depositing: TickDeposit(tick); break;
            default: StopAndIdle(tick, "The Steward was in no recognised state, so she stopped."); break;
        }
    }

    // ------------------------------------------------------------------
    // Gates
    // ------------------------------------------------------------------

    /// <summary>Why nothing may happen this tick, or null.
    ///
    /// Asked <b>before every step</b>, not once when a trip starts. A player
    /// can turn the setting off, un-mark their settlement, or have somebody
    /// join their game in the middle of a walk, and each of those has to stop
    /// the next mutation rather than the next trip.</summary>
    private string? WhyNotNow(in UpkeepTick tick)
    {
        if (!tick.TendingEnabled)
        {
            return "Fire tending is off in this mod's settings (Steward/TendFiresEnabled).";
        }

        if (tick.Authority != WorkAuthorityVerdict.Granted)
        {
            return WorkAuthorityPolicy.Describe(tick.Authority);
        }

        if (!tick.Scope.IsReady)
        {
            return tick.Scope.Describe();
        }

        if (!_journal.IsWritable)
        {
            return "The Steward's record cannot be written just now, so she is not moving anything.";
        }

        if (tick.Motion == null || !tick.Motion.IsPresent)
        {
            return "The Steward is not here. Recruit her, or wait for her part of the world to load.";
        }

        // A trip that started against one settlement must not finish against
        // another. Outside a trip this is simply the current revision.
        if (_phase != UpkeepPhase.Idle && _scopeRevision != tick.Scope.Revision)
        {
            return "The settlement or the supply chest changed while the Steward was working, " +
                "so she stopped. She is still carrying whatever she picked up.";
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Idle: deposit first, then look for work
    // ------------------------------------------------------------------

    private void TickIdle(in UpkeepTick tick)
    {
        // Anything in his hands goes back before anything else is considered.
        // This is also how a trip interrupted by a reload ends: the ledger says
        // he is carrying, so the first thing he does is put it back.
        if (_custody.Carried > 0)
        {
            // The same throttle the idle scan uses, and for a sharper reason: a
            // chest with no room refuses the deposit, which returns him here
            // still carrying, which would send him straight back. Three ticks
            // per lap, a walk each time, for as long as the chest stays full.
            // Waiting out the scan interval turns that into one attempt every
            // fifteen seconds, which recovers by itself the moment somebody
            // makes room and costs nothing while nobody does.
            if (tick.Now < _nextScanAt)
            {
                return;
            }

            _nextScanAt = tick.Now + _limits.ScanIntervalSeconds;
            BeginJobIfNeeded();
            Enter(UpkeepPhase.Returning, tick.Now,
                "Carrying " + _custody.Carried.ToString(CultureInfo.InvariantCulture) +
                " back to the supply chest.");
            return;
        }

        if (_custody.HasLoss)
        {
            Halt("Some of the wood the Steward took cannot be accounted for (" +
                _custody.Describe() + "). She has stopped. Run \"cs_steward resolve\" once you " +
                "have looked, and she will start again.");
            return;
        }

        CloseJob();

        if (tick.Now < _nextScanAt)
        {
            return;
        }

        _nextScanAt = tick.Now + _limits.ScanIntervalSeconds;

        // The chest is asked about before the fires are, and that ordering is
        // about the sentence rather than the logic. A chest that is empty or
        // open makes every fire ineligible, so a scan run first would report
        // the fires -- "they burn something the chest does not stock" -- which
        // is technically true of an empty chest and reads like a different
        // problem entirely.
        if (!tick.Depot.IsAvailable)
        {
            Say(tick, "depot-unavailable",
                "The marked supply chest is not reachable right now, so the Steward is waiting. " +
                "It may be open, or its part of the world may not be loaded.");
            return;
        }

        IReadOnlyList<FuelTargetObservation> offered;
        IReadOnlyCollection<string> stocked;
        try
        {
            offered = tick.Fires.Survey(tick.Scope.SettlementCentre, tick.Scope.SettlementRadius)
                ?? (IReadOnlyList<FuelTargetObservation>)Array.Empty<FuelTargetObservation>();
            stocked = tick.Depot.IsAvailable
                ? tick.Depot.ItemNames ?? (IReadOnlyCollection<string>)Array.Empty<string>()
                : Array.Empty<string>();
        }
        catch (Exception exception)
        {
            Say(tick, "scan-failed",
                "The Steward could not look at the settlement's fires (" + Brief(exception) + ").");
            return;
        }

        LastScan = FuelTargetSelector.Scan(
            offered, tick.Scope.Settlement, tick.Scope.DepotPoint, stocked, tick.Scope.Epoch, _limits);

        FuelTargetObservation? chosen = LastScan.Next;
        if (chosen == null)
        {
            if (stocked.Count == 0)
            {
                Say(tick, "depot-empty",
                    "The marked supply chest is empty, so the Steward has nothing to tend the " +
                    "fires with.");
                return;
            }

            _explanation = DescribeNothingToDo(LastScan);
            return;
        }

        FuelTargetObservation target = chosen.Value;
        int wanted = FuelMath.UnitsToFill(target.Fuel, target.MaxFuel, _limits.MaxUnitsPerTrip);
        int stock = SafeCount(tick.Depot, target.FuelItemName);
        if (stock <= 0)
        {
            Say(tick, "depot-empty",
                "There is no " + target.FuelItemName + " in the marked supply chest, so the " +
                "Steward has nothing to tend the fires with.");
            return;
        }

        int planned = Math.Min(wanted, stock);
        if (planned < 1)
        {
            // UnitsToFill already answered zero, which means the fire is full.
            // The scan should have caught that; if it did not, refusing here
            // costs nothing and withdrawing would cost a player's wood.
            _explanation = "The fires are all as full as they can get.";
            return;
        }

        if (_reservation.Take(target.Key, StewardRole.UpkeepJobId, planned) != ReservationOutcome.Taken)
        {
            _explanation = "The Steward is already committed to a fire.";
            return;
        }

        if (!_custody.TryReset())
        {
            _reservation.Release(StewardRole.UpkeepJobId);
            Halt("The Steward's record still has wood unaccounted for, so she did not start " +
                "another trip. " + _custody.Describe() + ".");
            return;
        }

        BeginJobIfNeeded();
        _fuelItemName = target.FuelItemName;
        _targetPoint = target.Position;
        _scopeRevision = tick.Scope.Revision;
        _retry.Reset();
        Enter(UpkeepPhase.ToDepot, tick.Now,
            "Fetching " + planned.ToString(CultureInfo.InvariantCulture) + " " + _fuelItemName +
            " for a fire at " + FuelMath.Describe(target.Fuel, target.MaxFuel) + ".");
    }

    private string DescribeNothingToDo(FuelTargetScan scan)
    {
        if (scan.Examined == 0)
        {
            return "There are no fires inside the marked settlement area.";
        }

        int fuelled = 0;
        int unowned = 0;
        int wrongFuel = 0;
        foreach (FuelTargetVerdict verdict in scan.Considered)
        {
            switch (verdict.Status)
            {
                case FuelTargetStatus.AlreadyFuelled: fuelled++; break;
                case FuelTargetStatus.NotOwnedHere: unowned++; break;
                case FuelTargetStatus.WrongFuel: wrongFuel++; break;
            }
        }

        if (fuelled == scan.Examined)
        {
            return "Every fire in the settlement is full. Nothing to do.";
        }

        var text = "Nothing to tend: " +
            fuelled.ToString(CultureInfo.InvariantCulture) + " of " +
            scan.Examined.ToString(CultureInfo.InvariantCulture) + " fires are full";
        if (wrongFuel > 0)
        {
            text += ", " + wrongFuel.ToString(CultureInfo.InvariantCulture) +
                " burn something the marked chest does not stock";
        }

        if (unowned > 0)
        {
            text += ", " + unowned.ToString(CultureInfo.InvariantCulture) +
                " are not this session's to touch";
        }

        if (scan.Truncated)
        {
            text += ". She looks at " + _limits.MaxTargetsScanned.ToString(CultureInfo.InvariantCulture) +
                " fires at a time and the settlement holds " +
                scan.Offered.ToString(CultureInfo.InvariantCulture);
        }

        return text + ".";
    }

    // ------------------------------------------------------------------
    // Walking
    // ------------------------------------------------------------------

    private void TickWalk(in UpkeepTick tick, SitePoint destination, UpkeepPhase onArrival)
    {
        if (_deadline.IsExpired(tick.Now))
        {
            GiveUpOnThisLeg(tick, "she could not get there in time");
            return;
        }

        tick.Motion.WalkTo(destination, ArrivalToleranceMetres);

        switch (tick.Motion.WalkStatus)
        {
            case WorkerWalkStatus.Arrived:
                tick.Motion.Stop();
                Enter(onArrival, tick.Now, _explanation);
                break;

            case WorkerWalkStatus.Deferred:
                GiveUpOnThisLeg(tick, DescribeDeferral(tick.Motion.DeferredReason));
                break;

            case WorkerWalkStatus.Idle:
                // The motion port dropped the goal underneath us — a body that
                // went away, or a runtime that cleared it. Re-stating it next
                // tick is fine; the deadline still bounds the attempt.
                break;
        }
    }

    private static string DescribeDeferral(WorkerDeferralReason reason)
    {
        switch (reason)
        {
            case WorkerDeferralReason.Unreachable: return "she could not find a way there";
            case WorkerDeferralReason.TooFar: return "it is further than she will walk in one go";
            case WorkerDeferralReason.Hazardous: return "the ground there is dangerous";
            case WorkerDeferralReason.NoAuthority: return "she is not allowed to act there";
            case WorkerDeferralReason.OutsideLoadedGround: return "that part of the world is not loaded";
            default: return "she stopped walking";
        }
    }

    /// <summary>A leg of the trip failed. What happens next depends only on
    /// whether he is holding a player's wood: if he is, the trip becomes a
    /// return trip, because putting it back is the one thing still worth
    /// doing.</summary>
    private void GiveUpOnThisLeg(in UpkeepTick tick, string because)
    {
        tick.Motion.Stop();
        RetryDecision decision = _retry.RecordFailure(tick.Now);

        if (_custody.Carried > 0 && _phase != UpkeepPhase.Returning)
        {
            Enter(UpkeepPhase.Returning, tick.Now,
                "The Steward stopped because " + because + ", so she is taking the " +
                _custody.Carried.ToString(CultureInfo.InvariantCulture) +
                " she is carrying back to the chest.");
            return;
        }

        _reservation.Release(StewardRole.UpkeepJobId);
        string tail = decision.GiveUp
            ? " She has stopped trying for now."
            : string.Empty;
        StopAndIdle(tick, "The Steward stopped because " + because + "." + tail);
    }

    // ------------------------------------------------------------------
    // Withdrawing: depot -> his hands
    // ------------------------------------------------------------------

    private void TickWithdraw(in UpkeepTick tick)
    {
        int planned = _reservation.PlannedUnits;
        if (planned < 1 || !_reservation.IsHeldBy(StewardRole.UpkeepJobId))
        {
            StopAndIdle(tick, "The Steward lost track of which fire she was fetching for.");
            return;
        }

        if (!tick.Depot.IsAvailable || !tick.Carrier.IsAvailable)
        {
            GiveUpOnThisLeg(tick, "the supply chest or her own pack could not be used");
            return;
        }

        int available = SafeCount(tick.Depot, _fuelItemName);
        if (available <= 0)
        {
            _reservation.Release(StewardRole.UpkeepJobId);
            StopAndIdle(tick,
                "The supply chest has no " + _fuelItemName + " left, so the Steward took nothing.");
            return;
        }

        int asked = Math.Min(planned, available);
        var intent = new UpkeepIntent(NextRequest(), UpkeepStep.Withdraw, _fuelItemName, asked);

        TransferMeasurement measurement = Transfer(intent, tick.Depot, tick.Carrier, asked);
        if (measurement.Blocked != null)
        {
            _reservation.Release(StewardRole.UpkeepJobId);
            StopAndIdle(tick, measurement.Blocked!);
            return;
        }

        switch (measurement.Outcome)
        {
            case UpkeepOutcome.Completed:
            case UpkeepOutcome.Partial:
                _custody.RecordWithdrawn(measurement.Moved);
                _retry.Reset();
                Enter(UpkeepPhase.ToTarget, tick.Now,
                    "Carrying " + measurement.Moved.ToString(CultureInfo.InvariantCulture) + " " +
                    _fuelItemName + " to a fire.");
                break;

            case UpkeepOutcome.Declined:
                _reservation.Release(StewardRole.UpkeepJobId);
                StopAndIdle(tick,
                    "The Steward could not take any " + _fuelItemName + " from the chest. " +
                    measurement.Evidence);
                break;

            default:
                // Uncertain. Whatever is in his hands is measured by the
                // reload path and by the deposit that follows; nothing is
                // retried and nothing is put back by guess.
                Halt("A withdrawal from the supply chest could not be accounted for, so the " +
                    "Steward stopped: " + measurement.Evidence +
                    ". Nothing was retried. Check the chest and her pack, then run " +
                    "\"cs_steward resolve\".");
                break;
        }
    }

    // ------------------------------------------------------------------
    // Feeding: his hands -> one fire, one unit
    // ------------------------------------------------------------------

    private void TickFeed(in UpkeepTick tick)
    {
        if (_custody.Carried < 1)
        {
            FinishTrip(tick, "The fire is as full as the Steward could make it.");
            return;
        }

        FuelTargetKey key = _reservation.Target;

        // Revalidate. Everything the scan established could have changed during
        // the walk, and the walk is the longest part of the trip.
        bool seen;
        FuelTargetObservation target;
        try
        {
            seen = tick.Fires.TryObserve(key, out target);
        }
        catch (Exception exception)
        {
            Enter(UpkeepPhase.Returning, tick.Now,
                "The fire could not be looked at (" + Brief(exception) + "), so the Steward is " +
                "taking the wood back.");
            return;
        }

        if (!seen)
        {
            Enter(UpkeepPhase.Returning, tick.Now,
                "That fire is gone, so the Steward is taking the wood back to the chest.");
            return;
        }

        IReadOnlyCollection<string> stocked = new[] { _fuelItemName };
        FuelTargetStatus status = FuelTargetSelector.Classify(
            target, tick.Scope.Settlement, stocked, tick.Scope.Epoch);
        if (status != FuelTargetStatus.Eligible)
        {
            Enter(UpkeepPhase.Returning, tick.Now,
                "The Steward left that fire alone (" + DescribeStatus(status) +
                ") and is taking the wood back.");
            return;
        }

        var intent = new UpkeepIntent(NextRequest(), UpkeepStep.Feed, key.Value, 1);
        if (!_journal.TryRecordIntent(intent))
        {
            Enter(UpkeepPhase.Returning, tick.Now,
                "The Steward could not write down what she was about to do, so she did nothing " +
                "and is taking the wood back.");
            return;
        }

        FeedMeasurement measurement;
        try
        {
            measurement = tick.Fires.FeedOneUnit(key, tick.Carrier);
        }
        catch (Exception exception)
        {
            measurement = new FeedMeasurement(
                FeedOutcome.Uncertain, -1, -1, target.Fuel, target.Fuel,
                "adding fuel threw " + Brief(exception));
        }

        UpkeepOutcome outcome;
        int moved;
        switch (measurement.Outcome)
        {
            case FeedOutcome.Accepted:
                outcome = UpkeepOutcome.Completed;
                moved = 1;
                break;

            case FeedOutcome.Declined:
                outcome = UpkeepOutcome.Declined;
                moved = 0;
                break;

            default:
                outcome = UpkeepOutcome.Uncertain;
                moved = 0;
                break;
        }

        bool receipted = _journal.TryRecordReceipt(
            new UpkeepReceipt(intent.Request, UpkeepStep.Feed, outcome, moved, measurement.Evidence));

        switch (measurement.Outcome)
        {
            case FeedOutcome.Accepted:
                _custody.RecordBurned(1);
                _unitsBurnedTotal++;
                _explanation = "Feeding the fire: " +
                    FuelMath.Describe(measurement.FuelAfter, target.MaxFuel) + ", " +
                    _custody.Carried.ToString(CultureInfo.InvariantCulture) + " left to carry.";
                if (!receipted)
                {
                    Halt("The Steward put wood on the fire but could not write down that she " +
                        "had, so she stopped rather than risk doing it twice. " +
                        measurement.Evidence);
                }

                break;

            case FeedOutcome.Declined:
                // Nothing left his hands and the fire gained nothing, so there
                // is nothing to reconcile — the fire is full, or it stopped
                // being one he may touch between the revalidation and the call.
                // The adapter's evidence says which; repeating it here in our
                // own words would sometimes contradict it.
                FinishTrip(tick, measurement.Evidence);
                break;

            case FeedOutcome.Lost:
                // The audit's section 3, reached anyway. The unit is gone and
                // nothing recreates it.
                _custody.RecordLost(1);
                Halt("A piece of " + _fuelItemName + " left the Steward's hands and did not " +
                    "reach the fire. It has not been replaced. " + measurement.Evidence +
                    ". Run \"cs_steward resolve\" once you have looked.");
                break;

            default:
                Halt("The Steward could not tell whether the fire took the wood, so she " +
                    "stopped. Nothing was tried again. " + measurement.Evidence +
                    ". Run \"cs_steward resolve\" once you have looked.");
                break;
        }
    }

    private static string DescribeStatus(FuelTargetStatus status)
    {
        switch (status)
        {
            case FuelTargetStatus.AlreadyFuelled: return "somebody already filled it";
            case FuelTargetStatus.NotOwnedHere: return "it is not this session's to touch";
            case FuelTargetStatus.CannotRefill: return "it takes no fuel";
            case FuelTargetStatus.InfiniteFuel: return "it never runs out";
            case FuelTargetStatus.OutsideSettlement: return "it is outside the marked settlement";
            case FuelTargetStatus.AccessDenied: return "a ward covers it";
            case FuelTargetStatus.StaleIdentity: return "it is no longer the fire she set out for";
            case FuelTargetStatus.WrongFuel: return "it burns something else";
            default: return "it could not be checked";
        }
    }

    // ------------------------------------------------------------------
    // Depositing: his hands -> depot
    // ------------------------------------------------------------------

    private void TickDeposit(in UpkeepTick tick)
    {
        int carried = _custody.Carried;
        if (carried < 1)
        {
            FinishTrip(tick, "Nothing left to put back.");
            return;
        }

        if (!tick.Depot.IsAvailable || !tick.Carrier.IsAvailable)
        {
            GiveUpOnThisLeg(tick, "the supply chest could not be used");
            return;
        }

        var intent = new UpkeepIntent(NextRequest(), UpkeepStep.Deposit, _fuelItemName, carried);
        TransferMeasurement measurement = Transfer(intent, tick.Carrier, tick.Depot, carried);
        if (measurement.Blocked != null)
        {
            StopAndIdle(tick, measurement.Blocked!);
            return;
        }

        switch (measurement.Outcome)
        {
            case UpkeepOutcome.Completed:
            case UpkeepOutcome.Partial:
                _custody.RecordReturned(measurement.Moved);
                if (_custody.Carried > 0)
                {
                    _nextScanAt = tick.Now + _limits.ScanIntervalSeconds;
                    StopAndIdle(tick,
                        "The supply chest only had room for " +
                        measurement.Moved.ToString(CultureInfo.InvariantCulture) + ". The Steward " +
                        "is still holding " + _custody.Carried.ToString(CultureInfo.InvariantCulture) +
                        "; make room and she will put it back.");
                    return;
                }

                FinishTrip(tick, "Everything is back in the chest.");
                break;

            case UpkeepOutcome.Declined:
                _nextScanAt = tick.Now + _limits.ScanIntervalSeconds;
                StopAndIdle(tick,
                    "There is no room in the supply chest, so the Steward is still holding " +
                    carried.ToString(CultureInfo.InvariantCulture) + " " + _fuelItemName +
                    ". She will try again when there is room.");
                break;

            default:
                Halt("Putting wood back in the chest could not be accounted for, so the " +
                    "Steward stopped: " + measurement.Evidence + ". Nothing was tried again.");
                break;
        }
    }

    // ------------------------------------------------------------------
    // The measured transfer both inventory moves share
    // ------------------------------------------------------------------

    private readonly struct TransferMeasurement
    {
        internal TransferMeasurement(UpkeepOutcome outcome, int moved, string evidence, string? blocked)
        {
            Outcome = outcome;
            Moved = moved;
            Evidence = evidence;
            Blocked = blocked;
        }

        public UpkeepOutcome Outcome { get; }

        public int Moved { get; }

        public string Evidence { get; }

        /// <summary>Set when nothing was attempted at all, with the sentence
        /// saying why. Distinct from an outcome because no mutation was made
        /// and no receipt exists.</summary>
        public string? Blocked { get; }
    }

    /// <summary>One measured move between two inventories: intent written
    /// first, engine move second, both sides counted before and after, receipt
    /// written last.
    ///
    /// The ordering is the settlement custody executor's, and so is the
    /// classification: equal deltas within what was asked are a real move,
    /// anything else is uncertain, and an uncertain move is never compensated.
    /// </summary>
    private TransferMeasurement Transfer(
        in UpkeepIntent intent, IItemStorePort from, IItemStorePort to, int asked)
    {
        int fromBefore = SafeCount(from, _fuelItemName);
        int toBefore = SafeCount(to, _fuelItemName);
        if (fromBefore < 0 || toBefore < 0)
        {
            return new TransferMeasurement(
                UpkeepOutcome.Refused, 0, string.Empty,
                "The Steward could not count what was in " +
                (fromBefore < 0 ? Describe(from) : Describe(to)) + ", so nothing was moved.");
        }

        int room;
        try
        {
            room = to.RoomFor(_fuelItemName, asked);
        }
        catch (Exception exception)
        {
            return new TransferMeasurement(
                UpkeepOutcome.Refused, 0, string.Empty,
                "The Steward could not tell whether " + Describe(to) + " had room (" +
                Brief(exception) + "), so nothing was moved.");
        }

        if (room < 1)
        {
            // Nothing moved and nothing was supposed to: no intent is written,
            // because an intent with no mutation behind it is a row that a
            // later reload would have to interpret.
            return new TransferMeasurement(
                UpkeepOutcome.Declined, 0,
                "there was no room in " + Describe(to), null);
        }

        if (!_journal.TryRecordIntent(intent))
        {
            return new TransferMeasurement(
                UpkeepOutcome.Refused, 0, string.Empty,
                "The Steward could not write down what she was about to do, so she did nothing.");
        }

        int requested = Math.Min(asked, room);
        Exception? fault = null;
        try
        {
            from.MoveTo(to, _fuelItemName, requested);
        }
        catch (Exception exception)
        {
            fault = exception;
        }

        int fromAfter = SafeCount(from, _fuelItemName);
        int toAfter = SafeCount(to, _fuelItemName);

        string evidence = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}: {2} -> {3}; {4}: {5} -> {6}; asked {7}{8}",
            _fuelItemName, Describe(from), fromBefore, Text(fromAfter),
            Describe(to), toBefore, Text(toAfter), requested,
            fault == null ? string.Empty : "; fault: " + Brief(fault));

        UpkeepOutcome outcome;
        int moved = 0;
        if (fault == null && fromAfter >= 0 && toAfter >= 0)
        {
            int gained = toAfter - toBefore;
            int lost = fromBefore - fromAfter;
            if (gained == lost && gained >= 0 && gained <= requested)
            {
                moved = gained;
                outcome = gained == 0
                    ? UpkeepOutcome.Declined
                    : gained == asked ? UpkeepOutcome.Completed : UpkeepOutcome.Partial;
            }
            else
            {
                outcome = UpkeepOutcome.Uncertain;
            }
        }
        else
        {
            outcome = UpkeepOutcome.Uncertain;
        }

        if (!_journal.TryRecordReceipt(
            new UpkeepReceipt(intent.Request, intent.Step, outcome, moved, evidence)))
        {
            return new TransferMeasurement(
                UpkeepOutcome.Uncertain, 0,
                evidence + "; and the result could not be written down", null);
        }

        return new TransferMeasurement(outcome, moved, evidence, null);
    }

    // ------------------------------------------------------------------
    // Reload recovery
    // ------------------------------------------------------------------

    /// <summary>Reconciles the record against what the Steward is actually
    /// carrying, after a world load or a crash.
    ///
    /// <b>Nothing is replayed.</b> An intent with no receipt means a move may
    /// or may not have happened, and neither redoing it nor writing it off is
    /// supportable — one duplicates a player's wood and the other destroys it.
    /// What <i>is</i> knowable is what he is holding, so that is measured and
    /// the record is corrected to match, the difference is written down, and
    /// the trip is <b>abandoned rather than resumed</b>.
    ///
    /// A clean reload — no open intents, and his hands agreeing with the record
    /// — costs one walk back to the chest and nothing else.</summary>
    public void OnWorldLoaded(IItemStorePort? carrier, string? fuelItemName)
    {
        _reservation.Release(StewardRole.UpkeepJobId);
        _nextScanAt = 0f;
        _retry.Reset();
        LastScan = FuelTargetScan.Empty;

        if (!string.IsNullOrEmpty(fuelItemName))
        {
            _fuelItemName = fuelItemName!;
        }

        IReadOnlyList<UpkeepIntent> open = _journal.UnresolvedIntents;
        var unresolved = new List<UpkeepIntent>(open);

        int actuallyCarried = carrier == null || !carrier.IsAvailable || string.IsNullOrEmpty(_fuelItemName)
            ? -1
            : SafeCount(carrier, _fuelItemName);

        if (actuallyCarried < 0)
        {
            if (unresolved.Count == 0 && _custody.Carried == 0)
            {
                Enter(UpkeepPhase.Idle, 0f, "Waiting.");
                return;
            }

            Halt("The Steward's pack could not be counted after loading, and her record has " +
                unresolved.Count.ToString(CultureInfo.InvariantCulture) + " unfinished step(s). " +
                "Nothing has been tried again. Run \"cs_steward resolve\" once you have looked.");
            return;
        }

        int difference = _custody.RestateCarried(actuallyCarried);

        foreach (UpkeepIntent intent in unresolved)
        {
            // Every open intent is closed as uncertain. That is the honest
            // record: the step was started, and whether it landed is exactly
            // what nobody can say.
            _journal.TryRecordReceipt(new UpkeepReceipt(
                intent.Request, intent.Step, UpkeepOutcome.Uncertain, 0,
                "the game closed before this finished; the Steward's pack was counted at " +
                actuallyCarried.ToString(CultureInfo.InvariantCulture) +
                " on loading and the record was corrected to match, not replayed"));
        }

        if (_custody.HasLoss)
        {
            Halt("After loading, " + _custody.Unaccounted.ToString(CultureInfo.InvariantCulture) +
                " " + _fuelItemName + " that left the supply chest could not be found. It has " +
                "not been replaced. " + _custody.Describe() +
                ". Run \"cs_steward resolve\" once you have looked.");
            return;
        }

        if (unresolved.Count > 0 || difference != 0)
        {
            _explanation = "The Steward stopped mid-errand when the game closed. She is holding " +
                _custody.Carried.ToString(CultureInfo.InvariantCulture) +
                " and will put it back before starting anything new" +
                (difference == 0
                    ? "."
                    : "; her record was " + Math.Abs(difference).ToString(CultureInfo.InvariantCulture) +
                      " out and has been corrected to what she is actually carrying.");
        }
        else
        {
            _explanation = "Waiting.";
        }

        _phase = UpkeepPhase.Idle;
        _journal.Compact();
    }

    /// <summary>A person has looked at a recorded loss. Lets him work again;
    /// recreates nothing.</summary>
    public string Acknowledge()
    {
        if (_phase != UpkeepPhase.NeedsAttention && !_custody.HasLoss)
        {
            return "Nothing needed acknowledging.";
        }

        string had = _custody.Describe();
        _custody.AcknowledgeLoss();
        _phase = UpkeepPhase.Idle;
        _explanation = "Waiting.";
        _nextScanAt = 0f;
        _retry.Reset();
        return "Noted (" + had + "). Nothing was recreated; the Steward will work again.";
    }

    /// <summary>Stops the current trip from outside — the player turned tending
    /// off, or the world is going away. He keeps what he is carrying, and the
    /// ledger keeps saying so.</summary>
    public void Stop(string reason)
    {
        _reservation.Release(StewardRole.UpkeepJobId);
        if (_phase != UpkeepPhase.NeedsAttention)
        {
            _phase = UpkeepPhase.Idle;
        }

        _explanation = reason ?? "Stopped.";
    }

    // ------------------------------------------------------------------
    // Bookkeeping
    // ------------------------------------------------------------------

    private void BeginJobIfNeeded()
    {
        if (!_job.IsEmpty)
        {
            return;
        }

        _tripsCompleted++;
        _job = new OrderId("steward-trip-" + _tripsCompleted.ToString(CultureInfo.InvariantCulture));
        _step = 0;
    }

    private void CloseJob()
    {
        if (_job.IsEmpty)
        {
            return;
        }

        _job = default;
        _step = 0;
        _journal.Compact();
    }

    /// <summary>The next idempotence key for this trip. Deterministic from the
    /// trip and the step number, so the same step of the same trip produces the
    /// same id after a restart — which is what makes a replayed attempt
    /// recognisable as the same attempt.</summary>
    private RequestId NextRequest() => RequestId.For(_job, _step++);

    private void FinishTrip(in UpkeepTick tick, string what)
    {
        _reservation.Release(StewardRole.UpkeepJobId);
        tick.Motion.Stop();
        _retry.Reset();

        if (_custody.Carried > 0)
        {
            Enter(UpkeepPhase.Returning, tick.Now,
                what + " Taking " + _custody.Carried.ToString(CultureInfo.InvariantCulture) +
                " back to the chest.");
            return;
        }

        CloseJob();
        _phase = UpkeepPhase.Idle;
        _explanation = what;
        _nextScanAt = tick.Now + _limits.ScanIntervalSeconds;
    }

    private void Enter(UpkeepPhase phase, float now, string explanation)
    {
        _phase = phase;
        _explanation = explanation;
        _deadline = new PhaseDeadline(now, _limits.PhaseLimitSeconds);
    }

    private void StopAndIdle(in UpkeepTick tick, string reason)
    {
        if (_phase != UpkeepPhase.Idle)
        {
            try
            {
                tick.Motion?.Stop();
            }
            catch (Exception)
            {
                // Stopping is best effort; the reason still gets reported.
            }
        }

        _phase = UpkeepPhase.Idle;
        _explanation = reason;
        Say(tick, "idle:" + reason, reason);
    }

    /// <summary>Stops everything until a person acknowledges it. The only way
    /// out is <see cref="Acknowledge"/>.</summary>
    private void Halt(string reason)
    {
        _reservation.Release(StewardRole.UpkeepJobId);
        _phase = UpkeepPhase.NeedsAttention;
        _explanation = reason;
        _report?.Invoke(reason);
    }

    /// <summary>At most one of any given message per cooldown, so a state that
    /// lasts minutes says so once rather than twenty times a second.</summary>
    private void Say(in UpkeepTick tick, string key, string message)
    {
        _explanation = message;
        if (_throttle.ShouldNotify(key, tick.Now))
        {
            _report?.Invoke(message);
        }
    }

    private static int SafeCount(IItemStorePort port, string fuelItemName)
    {
        try
        {
            return port == null || string.IsNullOrEmpty(fuelItemName) ? -1 : port.Count(fuelItemName);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static string Describe(IItemStorePort port)
    {
        try
        {
            return string.IsNullOrEmpty(port.Describe) ? "that container" : port.Describe;
        }
        catch (Exception)
        {
            return "that container";
        }
    }

    private static string Text(int count) =>
        count < 0 ? "?" : count.ToString(CultureInfo.InvariantCulture);

    private static string Brief(Exception exception) =>
        SafeFailure.Brief(exception);
}
