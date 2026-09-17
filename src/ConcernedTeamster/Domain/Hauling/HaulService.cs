using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling;

/// <summary>One leg Gunnar is asked to haul.</summary>
internal sealed class HaulLegRequest
{
    public HaulLegRequest(string haulId, string orderId, string leaseId, bool toDestination, WorkPoint target, float arrivalRadiusMetres)
    {
        if (string.IsNullOrEmpty(haulId) || string.IsNullOrEmpty(leaseId))
        {
            throw new ArgumentException("A leg needs a haul and a lease.");
        }

        if (!target.IsFinite || !(arrivalRadiusMetres > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(target), "A leg needs a finite target and a positive radius.");
        }

        HaulId = haulId;
        OrderId = orderId ?? string.Empty;
        LeaseId = leaseId;
        ToDestination = toDestination;
        Target = target;
        ArrivalRadiusMetres = arrivalRadiusMetres;
    }

    public string HaulId { get; }

    /// <summary>The Foreman order it serves; empty for a standalone haul.
    /// </summary>
    public string OrderId { get; }

    public string LeaseId { get; }

    /// <summary>False: to a rendezvous for loading. True: to the destination.
    /// </summary>
    public bool ToDestination { get; }

    public WorkPoint Target { get; }

    public float ArrivalRadiusMetres { get; }
}

/// <summary>What Gunnar and his cart are doing, read in one piece.</summary>
internal sealed class HaulSnapshot
{
    public HaulSnapshot(
        string haulId,
        HaulPhase phase,
        int revision,
        HaulAttentionReason attention,
        bool attached,
        bool cartStill,
        bool cartUpright,
        bool arrived,
        WorkPoint? cartPosition,
        WorkPoint? workerPosition)
    {
        HaulId = haulId ?? string.Empty;
        Phase = phase;
        Revision = revision;
        Attention = attention;
        Attached = attached;
        CartStill = cartStill;
        CartUpright = cartUpright;
        Arrived = arrived;
        CartPosition = cartPosition;
        WorkerPosition = workerPosition;
    }

    /// <summary>Empty when no haul is active.</summary>
    public string HaulId { get; }

    public HaulPhase Phase { get; }

    /// <summary>Increments on every phase, lease or reason change; never on
    /// position updates.</summary>
    public int Revision { get; }

    public HaulAttentionReason Attention { get; }

    public bool Attached { get; }

    public bool CartStill { get; }

    public bool CartUpright { get; }

    /// <summary>The current leg's final goal is reached and the cart is still
    /// (phase Waiting).</summary>
    public bool Arrived { get; }

    public WorkPoint? CartPosition { get; }

    public WorkPoint? WorkerPosition { get; }
}

internal enum HaulCommandOutcome
{
    Unspecified = 0,
    Accepted = 1,
    Rejected = 2,

    /// <summary>The expected revision is not current.</summary>
    Stale = 3,

    /// <summary>No authority, no worker or no world: nothing changed.</summary>
    Unavailable = 4,
}

internal readonly struct HaulCommandResult
{
    public HaulCommandResult(HaulCommandOutcome outcome, HaulAttentionReason reason, int revision)
        : this(outcome, reason, revision, string.Empty)
    {
    }

    /// <summary>C2: with a protocol-level detail the provider can pass on
    /// (for example <c>HaulBusy</c>), which has no attention reason.</summary>
    public HaulCommandResult(HaulCommandOutcome outcome, HaulAttentionReason reason, int revision, string detail)
    {
        Outcome = outcome;
        Reason = reason;
        Revision = revision;
        Detail = detail ?? string.Empty;
    }

    public HaulCommandOutcome Outcome { get; }

    /// <summary>Why a command was rejected, with protocol-level reasons such as
    /// a busy haul mapped by the provider.</summary>
    public HaulAttentionReason Reason { get; }

    public int Revision { get; }

    /// <summary>A wire reason name when <see cref="Reason"/> cannot say it;
    /// empty otherwise.</summary>
    public string Detail { get; }
}

/// <summary>The seam between Gunnar's runtime (agent A implements) and
/// everything that asks him to haul: the <c>concernedcat.haul/1</c> provider
/// (agent E) and Teamster's own UI. Main thread only. Request-id idempotence
/// lives in the provider, not here; this service applies each command it is
/// given once and reports the resulting revision.</summary>
internal interface IHaulService
{
    WorkAuthorityVerdict Authority { get; }

    /// <summary>Hauling is enabled, the seam is present and Gunnar's body can
    /// work now.</summary>
    bool WorkerAvailable { get; }

    Guid WorldLoadEpoch { get; }

    /// <summary>The active lease, or null.</summary>
    CartLease? ActiveLease { get; }

    HaulSnapshot Snapshot { get; }

    /// <summary>Starts a leg (§3.2 <c>requestHaul</c> semantics).</summary>
    HaulCommandResult RequestLeg(HaulLegRequest request, int expectedRevision);

    /// <summary>Waiting → Unloading when <paramref name="transferring"/>,
    /// Unloading → Waiting otherwise.</summary>
    HaulCommandResult AcknowledgeWait(string haulId, int expectedRevision, bool transferring);

    /// <summary>Stops safely; detaches and parks when asked.</summary>
    HaulCommandResult Cancel(string haulId, bool detachAndPark);
}
