using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>The durable record around the real <see cref="UpkeepLoop"/>.
///
/// This object never surveys, walks, withdraws, feeds, deposits, or constructs
/// an item. UpkeepLoop remains the only executor. The plan is its write-ahead
/// witness: it must accept an intent before the loop touches the world, and it
/// records the loop's measurements afterwards. On reload it can prepare a
/// return from what is actually in the pack, but it never resumes an old feed.
/// </summary>
internal sealed class StewardMaintenancePlan
{
    private static readonly ReservationId[] NoReservations = Array.Empty<ReservationId>();
    private static readonly NpcMaterialStack[] NoMaterial = Array.Empty<NpcMaterialStack>();

    private readonly StewardPlanFiles _files;
    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;
    private readonly INpcPlanCodec _codec;

    private NpcPlanJournal? _journal;
    private NpcPlanRun? _run;
    private NpcWorldEpoch _world;
    private string _fuelItemName = string.Empty;
    private string _blockedReason = string.Empty;
    private bool _unreadable;

    internal StewardMaintenancePlan(
        string root,
        NpcRoleRegistry registry,
        NpcIdentity identity,
        INpcPlanCodec? codec = null)
    {
        _files = new StewardPlanFiles(root);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _identity = identity.IsEmpty
            ? throw new ArgumentException("The Steward's identity is required.", nameof(identity))
            : identity;
        _codec = codec ?? new StewardPlanCodec(identity, StewardRole.UpkeepJobId);
    }

    internal bool IsBlocked => _blockedReason.Length != 0;

    internal string BlockedReason => _blockedReason;

    internal bool HasActivePlan => _run != null
        && _run.State.Phase != NpcPlanPhase.Settled
        && _run.State.Phase != NpcPlanPhase.Refunded
        && _run.State.Phase != NpcPlanPhase.NeedsAttention;

    internal string FuelItemName => _fuelItemName;

    internal int ExpectedCarried => _run?.State.CarriedUnits ?? 0;

    /// <summary>Loads, reconstructs and revalidates. No material moves here.
    /// A live old destination is never trusted: every loaded state is stale,
    /// and a resumable load is returned to Observing until the loop has a newly
    /// marked depot and asks for a return-only route.</summary>
    internal void OnWorldLoaded(
        SettlementScope scope,
        in NpcPlanEvidence evidence,
        IItemStorePort? carrier,
        string? fallbackFuelItemName)
    {
        _run = null;
        _journal = null;
        _world = evidence.World;
        _fuelItemName = fallbackFuelItemName ?? string.Empty;
        _blockedReason = string.Empty;
        _unreadable = false;

        string path = _files.ResolvePath(scope);
        if (!NpcPlanJournal.TryOpen(path, _codec, out _journal, out string openFailure))
        {
            Block("The Steward's maintenance plan could not be opened (" + openFailure
                + "), so she will not move material.");
            return;
        }

        NpcPlanLoad loaded = _journal!.Load();
        if (loaded.IsAbsent)
        {
            return;
        }

        if (loaded.IsUnreadable || loaded.Plan == null)
        {
            _unreadable = true;
            Block("The Steward's maintenance plan could not be read (" + loaded.Failure
                + "). It was left untouched; run \"cs_steward resolve\" only after looking at her pack and chest.");
            return;
        }

        NpcPlanState recovered = loaded.Plan;
        _run = NpcPlanRun.Resume(_journal, recovered);
        if (_run == null)
        {
            Block("The Steward's maintenance plan named no resumable work, so nothing was replayed.");
            return;
        }

        if (recovered.Carried.Count == 1)
        {
            _fuelItemName = recovered.Carried[0].Material.ItemName;
        }

        int actual = Count(carrier, _fuelItemName);
        if (recovered.CarriedUnits > 0 && actual < 0)
        {
            StopForAttention(
                "The Steward's pack could not be counted after loading. The saved plan says she carried "
                + recovered.CarriedUnits + "; nothing was replayed.");
            return;
        }

        if (actual >= 0 && actual != recovered.CarriedUnits)
        {
            StopForAttention(
                "The saved maintenance plan says the Steward carried " + recovered.CarriedUnits
                + " but her pack now contains " + actual + " " + DisplayFuel()
                + ". Nothing was replayed or replaced.");
            return;
        }

        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            _registry,
            recovered,
            NpcBodyKind.Worker,
            StewardRole.UpkeepJobId,
            evidence,
            NpcWorkInterruptionPolicy.Instance);

        if (decision.WasAlreadyOver)
        {
            if (decision.Next.Phase != recovered.Phase || decision.Next.Custody != recovered.Custody)
            {
                NpcPlanSave corrected = _run.Adopt(decision.Next, decision.Outcome);
                if (!corrected.IsSaved)
                {
                    Block(SaveFailure("correct its ending", corrected));
                    return;
                }
            }

            if (decision.Next.Phase == NpcPlanPhase.NeedsAttention)
            {
                Block(decision.Outcome.Reason);
            }

            return;
        }

        // A refund only releases material that has not moved. A role that
        // accepted it while the body still carried fuel would turn "refund"
        // into silent disposal, which the shared policy explicitly forbids.
        if (decision.Outcome.Response == InterruptionResponse.Refund && recovered.CarriedUnits > 0)
        {
            var needsAttention = new InterruptionOutcome(
                InterruptionResponse.NeedsAttention,
                decision.Outcome.Cause,
                0f,
                "The interruption cannot refund material already in the Steward's pack; somebody must look.");
            NpcPlanState stopped = recovered.WithPhase(NpcPlanPhase.NeedsAttention, needsAttention.Reason);
            NpcPlanSave adopted = _run.Adopt(stopped, needsAttention);
            Block(adopted.IsSaved ? needsAttention.Reason : SaveFailure("record the stop", adopted));
            return;
        }

        NpcPlanSave saved = _run.Adopt(decision.Next, decision.Outcome);
        if (!saved.IsSaved)
        {
            Block(SaveFailure("record the recovery decision", saved));
            return;
        }

        switch (decision.Outcome.Response)
        {
            case InterruptionResponse.Replan:
                if (recovered.CarriedUnits == 0)
                {
                    RefundEmpty("The old maintenance round held no material after reload.");
                }

                break;

            case InterruptionResponse.NeedsAttention:
                Block(decision.Outcome.Reason);
                break;
        }
    }

    internal bool TryBegin(
        NpcWorldEpoch world,
        string sourceKey,
        FuelTargetKey target,
        string fuelItemName,
        int plannedUnits,
        out string failure) =>
        TryBegin(world, sourceKey, target, fuelItemName, plannedUnits, 1, out failure);

    internal bool TryBegin(
        NpcWorldEpoch world,
        string sourceKey,
        FuelTargetKey target,
        string fuelItemName,
        int plannedUnits,
        int targetCount,
        out string failure)
    {
        failure = string.Empty;
        if (!CanWrite(out failure) || world.IsUnknown || target.IsEmpty
            || string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(fuelItemName)
            || plannedUnits < 1 || targetCount < 1)
        {
            if (failure.Length == 0)
            {
                failure = "The maintenance plan did not have a live world, depot, target, material and amount.";
            }

            Block(failure);
            return false;
        }

        if (HasActivePlan)
        {
            failure = "A previous maintenance plan is still active, so a second one was not started.";
            Block(failure);
            return false;
        }

        _world = world;
        _fuelItemName = fuelItemName;
        _run = NpcPlanRun.Begin(
            _journal,
            NpcPlanState.Opening(_identity, StewardRole.UpkeepJobId, world, targetCount),
            out NpcPlanSave opening);
        if (_run == null)
        {
            failure = SaveFailure("start the maintenance plan", opening);
            Block(failure);
            return false;
        }

        if (!Write(
                _run.Advance(
                    NpcPlanPhase.Planned,
                    targetCount == 1 ? "one fire was selected" : targetCount + " fires were selected"),
                "record the selected fire(s)",
                out failure)
            || !Write(_run.Advance(NpcPlanPhase.Manifested, "the trip was totalled"), "record the trip total", out failure))
        {
            return false;
        }

        var reservations = new ReservationId[targetCount];
        for (int index = 0; index < reservations.Length; index++)
        {
            reservations[index] = ReservationId.For(StewardRole.UpkeepJobId, index);
        }

        NpcPlanState reserved = _run.State
            .WithHoldings(reservations, NoMaterial)
            .WithRoute(sourceKey, target.Value, string.Empty)
            .WithPhase(NpcPlanPhase.Reserved, "the fire and depot were recorded before walking");
        return Write(_run.Record(reserved), "record the reservation", out failure);
    }

    internal bool TryIntend(string note, out string failure) =>
        RunWrite(run => run.Intend(note), "record what was about to move", out failure);

    internal bool TryConclude(
        bool established,
        string fuelItemName,
        int carried,
        bool targetDone,
        string note,
        out string failure)
    {
        IReadOnlyList<NpcMaterialStack> load = Load(fuelItemName, carried);
        return RunWrite(
            run => run.Conclude(
                established,
                load,
                targetDone
                    ? Math.Min(run.State.TargetsTotal, run.State.TargetsDone + 1)
                    : run.State.TargetsDone,
                note),
            "record what the movement did",
            out failure);
    }

    internal bool TryRouteToTarget(out string failure)
    {
        failure = string.Empty;
        if (!RunWrite(
                run => run.Advance(NpcPlanPhase.Provisioned, "the measured fuel is in the Steward's pack"),
                "record the provisioned pack",
                out failure))
        {
            return false;
        }

        return RunWrite(
            run => run.Advance(NpcPlanPhase.Routed, "the walk to the selected fire was ordered"),
            "record the route",
            out failure);
    }

    internal bool TryEnterExecuting(out string failure)
    {
        if (_run != null && _run.State.Phase == NpcPlanPhase.Executing)
        {
            failure = string.Empty;
            return true;
        }

        return RunWrite(
            run => run.Advance(NpcPlanPhase.Executing, "the selected fire was revalidated"),
            "record the start of fire tending",
            out failure);
    }

    /// <summary>Moves the record, not material, into return/reconciliation.
    /// A measured no-op intent supplies the write-ahead pair required for the
    /// material-moving phase without replaying a feed.</summary>
    internal bool TryBeginReconciliation(
        string fuelItemName, int carried, string note, out string failure)
    {
        failure = string.Empty;
        if (_run == null)
        {
            return true; // An upgraded legacy trip has no shared plan to finish.
        }

        if (_run.State.Phase == NpcPlanPhase.Reconciling)
        {
            return SameLoad(fuelItemName, carried, out failure);
        }

        if (_run.State.Phase == NpcPlanPhase.Provisioned
            && !RunWrite(
                run => run.Advance(NpcPlanPhase.Routed, "return route ordered"),
                "record the return route",
                out failure))
        {
            return false;
        }

        if (_run.State.Phase == NpcPlanPhase.Routed && !TryEnterExecuting(out failure))
        {
            return false;
        }

        if (_run.State.Phase != NpcPlanPhase.Executing)
        {
            failure = "The maintenance plan was not at a phase from which it could return material.";
            Block(failure);
            return false;
        }

        if (!TryIntend("about to close the round from the measured pack", out failure)
            || !TryConclude(true, fuelItemName, carried, false, note, out failure))
        {
            return false;
        }

        return RunWrite(
            run => run.Advance(NpcPlanPhase.Reconciling, note),
            "record the start of reconciliation",
            out failure);
    }

    /// <summary>Rebuilds only a return route after reload. The saved fire key is
    /// evidence and is never fed; a fresh depot key is required before the old
    /// carried load can move at all.</summary>
    internal bool TryPrepareReturn(
        NpcWorldEpoch world,
        string depotKey,
        string fuelItemName,
        int carried,
        out string failure)
    {
        failure = string.Empty;
        if (_run == null)
        {
            return true;
        }

        if (!SameLoad(fuelItemName, carried, out failure))
        {
            Block(failure);
            return false;
        }

        if (_run.State.Phase == NpcPlanPhase.Reconciling)
        {
            if (world.IsUnknown || string.IsNullOrEmpty(depotKey))
            {
                failure = "The recovered maintenance plan needs a newly validated depot before anything can move.";
                Block(failure);
                return false;
            }

            NpcPlanState currentReturn = _run.State.WithRoute(depotKey, depotKey, string.Empty);
            if (!Write(_run.Record(currentReturn), "record the validated return depot", out failure))
            {
                return false;
            }

            return _run.State.World.Matches(world)
                || Write(_run.Reattach(world, "the return depot was revalidated"), "re-attach the return", out failure);
        }

        if (_run.State.Phase == NpcPlanPhase.Provisioned
            || _run.State.Phase == NpcPlanPhase.Routed
            || _run.State.Phase == NpcPlanPhase.Executing)
        {
            if (!TryBeginReconciliation(
                    fuelItemName, carried, "the interrupted trip is returning only", out failure))
            {
                return false;
            }

            return TryPrepareReturn(world, depotKey, fuelItemName, carried, out failure);
        }

        if (_run.State.Phase != NpcPlanPhase.Observing || world.IsUnknown || string.IsNullOrEmpty(depotKey))
        {
            failure = "The recovered maintenance plan needs a newly validated depot before anything can move.";
            Block(failure);
            return false;
        }

        NpcPlanState returnOnly = _run.State
            .WithHoldings(NoReservations, _run.State.Carried)
            .WithRoute(depotKey, depotKey, string.Empty);
        if (!Write(_run.Record(returnOnly), "replace stale route keys", out failure)
            || !Write(_run.Reattach(world, "the current depot was revalidated"), "re-attach the plan", out failure)
            || !Write(_run.Advance(NpcPlanPhase.Planned, "return-only recovery"), "record the return plan", out failure)
            || !Write(_run.Advance(NpcPlanPhase.Manifested, "the carried load was counted"), "record the return manifest", out failure)
            || !Write(_run.Advance(NpcPlanPhase.Reserved, "no new material was reserved"), "record the empty reservation", out failure)
            || !TryIntend("about to restate the already measured pack", out failure)
            || !TryConclude(true, fuelItemName, carried, false, "the existing pack was measured, not refetched", out failure)
            || !TryRouteToTarget(out failure)
            || !TryEnterExecuting(out failure))
        {
            return false;
        }

        return TryBeginReconciliation(
            fuelItemName, carried, "the old destination was abandoned; returning only", out failure);
    }

    internal bool TryFinish(string fuelItemName, int carried, string note, out string failure)
    {
        failure = string.Empty;
        if (_run == null)
        {
            return true;
        }

        if (carried != 0 || !SameLoad(fuelItemName, carried, out failure))
        {
            if (failure.Length == 0)
            {
                failure = "The maintenance plan cannot finish while the Steward still carries material.";
            }

            Block(failure);
            return false;
        }

        if (_run.State.Phase != NpcPlanPhase.Reconciling
            && !TryBeginReconciliation(fuelItemName, carried, note, out failure))
        {
            return false;
        }

        NpcPlanState released = _run.State.WithHoldings(NoReservations, NoMaterial);
        if (!Write(_run.Record(released), "release the maintenance reservation", out failure))
        {
            return false;
        }

        NpcPlanPhase ending = _run.State.TargetsDone > 0
            ? NpcPlanPhase.Settled
            : NpcPlanPhase.Refunded;
        return RunWrite(run => run.Stop(ending, note), "finish the maintenance plan", out failure);
    }

    internal bool RefundEmpty(string note)
    {
        if (_run == null || !HasActivePlan)
        {
            return true;
        }

        if (_run.State.CarriedUnits != 0)
        {
            Block("The upkeep loop reported an empty pack while the durable maintenance plan still claims material.");
            return false;
        }

        NpcPlanSave released = _run.Record(_run.State.WithHoldings(NoReservations, NoMaterial));
        if (!released.IsSaved)
        {
            Block(SaveFailure("release an empty reservation", released));
            return false;
        }

        NpcPlanSave stopped = _run.Stop(NpcPlanPhase.Refunded, note);
        if (!stopped.IsSaved)
        {
            Block(SaveFailure("record the empty refund", stopped));
            return false;
        }

        return true;
    }

    internal void Suspend(string note)
    {
        if (_run != null && HasActivePlan)
        {
            // A failed note does not make the previous durable resume point
            // unsafe, and no world mutation rests on this write.
            _run.Suspend(note);
        }
    }

    internal void NeedsAttention(string reason) => StopForAttention(reason);

    /// <summary>The explicit human exit. An unreadable record is quarantined,
    /// never overwritten in place. An empty pack gets a fresh terminal
    /// tombstone; a measured non-empty pack gets a fresh return-only plan.
    /// Only after this returns true may UpkeepLoop clear its custody loss.</summary>
    internal bool Resolve(string fuelItemName, int carried, out string failure)
    {
        failure = string.Empty;
        if (!IsBlocked)
        {
            return true;
        }

        if (_journal == null || _world.IsUnknown)
        {
            failure = "The maintenance plan has no writable journal in a live world.";
            return false;
        }

        if (carried < 0 || (carried > 0 && string.IsNullOrEmpty(fuelItemName)))
        {
            failure = "The inspected pack did not provide a usable material count and name.";
            return false;
        }

        if (_unreadable)
        {
            string? quarantine = _journal.TryQuarantine();
            if (quarantine == null)
            {
                failure = "The unreadable maintenance plan could not be moved aside, so it was not overwritten.";
                return false;
            }

            _unreadable = false;
        }

        NpcPlanRun? resolved = NpcPlanRun.Begin(
            _journal,
            NpcPlanState.Opening(_identity, StewardRole.UpkeepJobId, _world, 1),
            out NpcPlanSave begun);
        if (resolved == null)
        {
            failure = SaveFailure("record the resolution", begun);
            return false;
        }

        _run = resolved;
        if (carried == 0)
        {
            NpcPlanSave ended = resolved.Stop(
                NpcPlanPhase.Refunded,
                "A person inspected the custody state and explicitly resolved it; nothing was recreated.");
            if (!ended.IsSaved)
            {
                failure = SaveFailure("finish the resolution", ended);
                return false;
            }

            _run = null;
        }
        else
        {
            _fuelItemName = fuelItemName;
            // The old blocked plan has now been replaced by a durable opening
            // record. Let only this fresh, measured return plan advance. Any
            // refused write below blocks it again before UpkeepLoop can move.
            _blockedReason = string.Empty;
            if (!Write(resolved.Advance(NpcPlanPhase.Planned, "a person inspected the carried load"),
                    "record the inspected load", out failure)
                || !Write(resolved.Advance(NpcPlanPhase.Manifested, "the inspected load was counted"),
                    "record the inspected manifest", out failure)
                || !Write(resolved.Advance(NpcPlanPhase.Reserved, "no new material was reserved"),
                    "record the empty resolution hold", out failure)
                || !TryIntend("about to adopt the person's measured pack", out failure)
                || !TryConclude(
                    true, fuelItemName, carried, false,
                    "a person established what remained in the pack; nothing was recreated", out failure)
                || !TryRouteToTarget(out failure)
                || !TryEnterExecuting(out failure)
                || !TryBeginReconciliation(
                    fuelItemName, carried, "the inspected load will only be returned", out failure))
            {
                return false;
            }
        }

        _blockedReason = string.Empty;
        return true;
    }

    private bool CanWrite(out string failure)
    {
        if (IsBlocked)
        {
            failure = _blockedReason;
            return false;
        }

        if (_journal == null)
        {
            failure = "No maintenance-plan journal is open for this world.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private bool RunWrite(
        Func<NpcPlanRun, NpcPlanSave> write, string action, out string failure)
    {
        if (!CanWrite(out failure) || _run == null)
        {
            if (failure.Length == 0)
            {
                failure = "There is no active maintenance plan to " + action + ".";
            }

            Block(failure);
            return false;
        }

        return Write(write(_run), action, out failure);
    }

    private bool Write(NpcPlanSave save, string action, out string failure)
    {
        if (save.IsSaved)
        {
            failure = string.Empty;
            return true;
        }

        failure = SaveFailure(action, save);
        Block(failure);
        return false;
    }

    private bool SameLoad(string fuelItemName, int carried, out string failure)
    {
        failure = string.Empty;
        if (_run == null)
        {
            return true;
        }

        IReadOnlyList<NpcMaterialStack> measured = Load(fuelItemName, carried);
        NpcPlanState comparison = _run.State.WithHoldings(_run.State.Reservations, measured);
        if (_run.State.CarriesTheSameLoadAs(comparison))
        {
            return true;
        }

        failure = "The Steward's measured pack no longer matches the durable maintenance plan; nothing was moved.";
        return false;
    }

    private IReadOnlyList<NpcMaterialStack> Load(string fuelItemName, int carried)
    {
        if (carried < 1)
        {
            return NoMaterial;
        }

        string named = string.IsNullOrEmpty(fuelItemName) ? _fuelItemName : fuelItemName;
        return new[] { new NpcMaterialStack(NpcMaterial.Of(named), carried) };
    }

    private void StopForAttention(string reason)
    {
        Block(reason);
        if (_run != null && _run.State.Phase != NpcPlanPhase.NeedsAttention)
        {
            NpcPlanSave stopped = _run.Stop(NpcPlanPhase.NeedsAttention, reason);
            if (!stopped.IsSaved)
            {
                _blockedReason = reason + " The stop also could not be saved (" + stopped.Failure + ").";
            }
        }
    }

    private void Block(string reason) =>
        _blockedReason = string.IsNullOrEmpty(reason)
            ? "The maintenance plan stopped without a recorded reason."
            : reason;

    private string DisplayFuel() =>
        string.IsNullOrEmpty(_fuelItemName) ? "material" : _fuelItemName;

    private static int Count(IItemStorePort? store, string item)
    {
        if (store == null || !store.IsAvailable || string.IsNullOrEmpty(item))
        {
            return string.IsNullOrEmpty(item) ? 0 : -1;
        }

        try
        {
            return store.Count(item);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static string SaveFailure(string action, NpcPlanSave save) =>
        "The Steward could not " + action + " (" + save.Failure
        + "), so no further material movement is allowed."
        + (save.CompleteCopyPath == null ? string.Empty : " A complete temporary copy was preserved.");
}
