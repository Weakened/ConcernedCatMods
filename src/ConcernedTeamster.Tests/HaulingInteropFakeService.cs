using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>A stand-in for Gunnar's haul runtime (agent A) that follows the C1
/// phase table exactly: every move goes through <see cref="HaulPhases.CanTransition"/>
/// and bumps the revision, leases live in a real <see cref="CartLeaseBook"/>,
/// and the service applies whatever it is given once. The provider's own rules
/// (idempotence, staleness, legality) are therefore tested against a service
/// that would not enforce them for it.
///
/// Also compiled into the provider harness of <c>src/Interop.Tests</c>, which
/// is why it spells out every using.</summary>
internal sealed class HaulingInteropFakeService : IHaulService
{
    private CartLeaseBook _book;
    private bool? _cancelAfterUnloading;

    public HaulingInteropFakeService(Guid epoch)
    {
        _book = new CartLeaseBook(epoch);
        WorldLoadEpoch = epoch;
    }

    public WorkAuthorityVerdict Authority { get; set; } = WorkAuthorityVerdict.Granted;

    public bool WorkerAvailable { get; set; } = true;

    public Guid WorldLoadEpoch { get; private set; }

    public CartLease? ActiveLease =>
        _book.TryGetActiveForWorker(WorkerKey.Gunnar, out CartLease? lease) ? lease : null;

    public HaulSnapshot Snapshot => ThrowOnSnapshot != null
        ? throw ThrowOnSnapshot
        : new HaulSnapshot(
            HaulId, Phase, Revision, Attention, Attached, CartStill, CartUpright, Arrived, CartPosition, WorkerPosition);

    public string HaulId { get; private set; } = string.Empty;

    public HaulPhase Phase { get; private set; } = HaulPhase.Unassigned;

    public int Revision { get; private set; }

    public HaulAttentionReason Attention { get; private set; }

    public bool Attached { get; private set; }

    public bool CartStill { get; set; } = true;

    public bool CartUpright { get; set; } = true;

    public bool Arrived { get; private set; }

    public WorkPoint? CartPosition { get; set; } = new WorkPoint(10f, 30f, 10f);

    public WorkPoint? WorkerPosition { get; set; } = new WorkPoint(9f, 30f, 10f);

    public HaulLegRequest? LastLeg { get; private set; }

    public int LegCalls { get; private set; }

    public int AcknowledgeCalls { get; private set; }

    public int CancelCalls { get; private set; }

    /// <summary>Thrown by the next command, then cleared.</summary>
    public Exception? ThrowOnNextCommand { get; set; }

    /// <summary>Returned by every command instead of acting, while set.</summary>
    public HaulCommandResult? ForcedResult { get; set; }

    /// <summary>Thrown by the snapshot getter while set (a broken runtime).
    /// </summary>
    public Exception? ThrowOnSnapshot { get; set; }

    public LeaseOutcome AssignCart(string leaseId, string cartSessionId)
    {
        LeaseOutcome outcome = _book.Assign(leaseId, WorkerKey.Gunnar, new CartKey(cartSessionId, WorldLoadEpoch));
        if (outcome == LeaseOutcome.Assigned && Phase == HaulPhase.Unassigned)
        {
            Move(HaulPhase.Ready);
        }

        return outcome;
    }

    public void ReleaseLease()
    {
        CartLease? lease = ActiveLease;
        if (lease != null)
        {
            _book.Release(lease.LeaseId);
            HaulId = string.Empty;
            Attached = false;
            Arrived = false;
            ForceMove(HaulPhase.Unassigned);
        }
    }

    /// <summary>The provider's world reloads: every lease ends, nothing
    /// survives.</summary>
    public void ReloadWorld(Guid newEpoch)
    {
        _book.BeginWorldLoad(newEpoch);
        WorldLoadEpoch = newEpoch;
        HaulId = string.Empty;
        Attached = false;
        Arrived = false;
        Attention = HaulAttentionReason.Unspecified;
        ForceMove(HaulPhase.Unassigned);
    }

    /// <summary>The current leg reaches its final goal: through hitching and
    /// pulling as needed, to Waiting with the cart still.</summary>
    public void CompleteLeg(WorkPoint at)
    {
        if (Phase == HaulPhase.Approaching)
        {
            Move(HaulPhase.Hitching);
        }

        if (Phase == HaulPhase.Hitching)
        {
            Attached = true;
            Move(HaulPhase.Pulling);
        }

        if (Phase == HaulPhase.Pulling || Phase == HaulPhase.Recovering)
        {
            Move(HaulPhase.Stopping);
        }

        if (Phase == HaulPhase.Stopping)
        {
            Move(HaulPhase.Waiting);
        }

        if (Phase != HaulPhase.Waiting)
        {
            throw new InvalidOperationException("No leg to complete from " + Phase + ".");
        }

        CartPosition = at;
        WorkerPosition = new WorkPoint(at.X - 1.5f, at.Y, at.Z);
        CartStill = true;
        Arrived = true;
    }

    /// <summary>Provider-ended control (§3.3): player takeover, a destroyed or
    /// unloaded cart, lost authority.</summary>
    public void EndControl(HaulAttentionReason reason)
    {
        Attention = reason;
        Attached = false;
        Arrived = false;
        if (Phase == HaulPhase.Unloading)
        {
            Move(HaulPhase.NeedsAttention);
            return;
        }

        if (HaulPhases.CanTransition(Phase, HaulPhase.NeedsAttention))
        {
            Move(HaulPhase.NeedsAttention);
        }
        else
        {
            Revision++;
        }
    }

    public HaulCommandResult RequestLeg(HaulLegRequest request, int expectedRevision)
    {
        LegCalls++;
        if (TryInterrupt(out HaulCommandResult interrupted))
        {
            return interrupted;
        }

        if (!WorkerAvailable)
        {
            return Result(HaulCommandOutcome.Unavailable, HaulAttentionReason.Unspecified);
        }

        if (expectedRevision != Revision)
        {
            return Result(HaulCommandOutcome.Stale, HaulAttentionReason.Unspecified);
        }

        HaulId = request.HaulId;
        LastLeg = request;
        Arrived = false;
        if (Phase == HaulPhase.Ready)
        {
            Move(HaulPhase.Approaching);
        }
        else if (Phase == HaulPhase.Waiting)
        {
            CartStill = false;
            Move(HaulPhase.Pulling);
        }
        else
        {
            return Result(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified);
        }

        return Result(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified);
    }

    public HaulCommandResult AcknowledgeWait(string haulId, int expectedRevision, bool transferring)
    {
        AcknowledgeCalls++;
        if (TryInterrupt(out HaulCommandResult interrupted))
        {
            return interrupted;
        }

        if (expectedRevision != Revision)
        {
            return Result(HaulCommandOutcome.Stale, HaulAttentionReason.Unspecified);
        }

        if (transferring)
        {
            if (Phase != HaulPhase.Waiting)
            {
                return Result(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified);
            }

            Move(HaulPhase.Unloading);
        }
        else
        {
            if (Phase != HaulPhase.Unloading)
            {
                return Result(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified);
            }

            Move(HaulPhase.Waiting);
            if (_cancelAfterUnloading.HasValue)
            {
                bool detach = _cancelAfterUnloading.Value;
                _cancelAfterUnloading = null;
                ApplyCancel(detach);
            }
        }

        return Result(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified);
    }

    public HaulCommandResult Cancel(string haulId, bool detachAndPark)
    {
        CancelCalls++;
        if (TryInterrupt(out HaulCommandResult interrupted))
        {
            return interrupted;
        }

        if (Phase == HaulPhase.Unloading)
        {
            // Never refused while unloading; completes after the consumer's Done.
            _cancelAfterUnloading = detachAndPark;
            return Result(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified);
        }

        ApplyCancel(detachAndPark);
        return Result(HaulCommandOutcome.Accepted, HaulAttentionReason.Unspecified);
    }

    private void ApplyCancel(bool detachAndPark)
    {
        Arrived = false;
        if (Phase == HaulPhase.Approaching)
        {
            HaulId = string.Empty;
            Move(HaulPhase.Ready);
            return;
        }

        if (Phase == HaulPhase.Pulling || Phase == HaulPhase.Recovering)
        {
            Move(HaulPhase.Stopping);
        }

        if (Phase == HaulPhase.Stopping)
        {
            CartStill = true;
            Move(HaulPhase.Waiting);
        }

        if (!detachAndPark)
        {
            return;
        }

        if (HaulPhases.CanTransition(Phase, HaulPhase.Detaching))
        {
            Move(HaulPhase.Detaching);
            Attached = false;
            HaulId = string.Empty;
            Move(HaulPhase.Ready);
        }
    }

    private bool TryInterrupt(out HaulCommandResult result)
    {
        if (ThrowOnNextCommand != null)
        {
            Exception exception = ThrowOnNextCommand;
            ThrowOnNextCommand = null;
            throw exception;
        }

        if (ForcedResult.HasValue)
        {
            result = ForcedResult.Value;
            return true;
        }

        result = default;
        return false;
    }

    private void Move(HaulPhase to)
    {
        if (!HaulPhases.CanTransition(Phase, to))
        {
            throw new InvalidOperationException("Illegal haul transition " + Phase + " -> " + to + ".");
        }

        Phase = to;
        Revision++;
    }

    private void ForceMove(HaulPhase to)
    {
        Phase = to;
        Revision++;
    }

    private HaulCommandResult Result(HaulCommandOutcome outcome, HaulAttentionReason reason) =>
        new HaulCommandResult(outcome, reason, Revision);
}
