using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>Why an order was not accepted (GATHER-01, CONTRACTS.md §4). Zero is
/// "not refused". Every value has a sentence in
/// <see cref="CollectionIntake.Describe"/>.</summary>
internal enum CollectionIntakeRefusal
{
    /// <summary>Not refused.</summary>
    Unspecified = 0,

    NoQuotas = 1,
    DuplicateResource = 2,
    DeliveryMissing = 3,
    ParticipationMissing = 4,
    NoAuthority = 5,
    OtherPeersConnected = 6,

    /// <summary>No custody runtime is present in this build.</summary>
    CustodyUnavailable = 7,

    JournalReadOnly = 8,
    AnotherOrderActive = 9,

    /// <summary>A different job holds Thorstein's actor mode.</summary>
    WorkerBusy = 10,

    /// <summary>His body is not here, not owned here, or faulted.</summary>
    WorkerAbsent = 11,

    WorkerNotRecruited = 12,
    ToolMissing = 13,
    ToolBroken = 14,
    ToolHandoverUncertain = 15,
    ScopeInvalid = 16,
    ScopeUnloaded = 17,

    /// <summary>No work area is assigned and the default circle was not shown
    /// to the player first (GATHER-02).</summary>
    PreviewRequired = 18,

    /// <summary>The world's drop scaling could not be read.</summary>
    YieldUnknown = 19,

    /// <summary>The world's resource rate gives more than one unit per pick,
    /// and the quota is not a multiple of it: it could only be met by
    /// overcollecting.</summary>
    QuotaNotMultipleOfYield = 20,

    DestinationUnavailable = 21,
    DestinationAccessDenied = 22,
    DestinationStale = 23,

    /// <summary>Hold-for-player mode: everything requested must fit on his back
    /// at once.</summary>
    HoldExceedsCarry = 24,

    HaulerUnavailable = 25,

    /// <summary>The acceptance could not be journaled, so no work starts.
    /// </summary>
    NotRecorded = 26,

    /// <summary>The order names a different worker than this loop drives.
    /// </summary>
    WrongWorker = 27,

    /// <summary>Nothing could establish whose identity this is, so nothing could
    /// take hold of him.
    ///
    /// <b>Distinct from <see cref="WorkerBusy"/> on purpose, and the distinction
    /// is the whole reason this member exists.</b> Busy means another job holds
    /// him and the fix is to wait or end that job. This means the shared NPC
    /// runtime never accepted him at startup, nothing is busy, and waiting will
    /// never help. Reporting it as busy sends a player looking for a job that
    /// does not exist.</summary>
    WorkerIdentityUnknown = 28,
}

/// <summary>Everything acceptance needs to know, gathered at the moment of
/// asking.</summary>
internal sealed class CollectionIntakeFacts
{
    public CollectionIntakeFacts(
        WorkAuthorityVerdict authority,
        bool custodyAvailable,
        bool journalWritable,
        bool anotherOrderActive,
        bool workerBusy,
        bool workerPresent,
        ReadinessVerdict readiness,
        ScopeCheck scope,
        bool defaultCirclePreviewed,
        IReadOnlyDictionary<CollectedResource, int>? yieldPerPick,
        bool destinationResolved,
        CollectionAttentionReason destinationRefusal,
        float workerCarryWeight,
        Func<CollectedResource, float>? unitWeight,
        bool haulerAvailable)
    {
        Authority = authority;
        CustodyAvailable = custodyAvailable;
        JournalWritable = journalWritable;
        AnotherOrderActive = anotherOrderActive;
        WorkerBusy = workerBusy;
        WorkerPresent = workerPresent;
        Readiness = readiness;
        Scope = scope;
        DefaultCirclePreviewed = defaultCirclePreviewed;
        YieldPerPick = yieldPerPick ?? new Dictionary<CollectedResource, int>();
        DestinationResolved = destinationResolved;
        DestinationRefusal = destinationRefusal;
        WorkerCarryWeight = workerCarryWeight;
        UnitWeight = unitWeight ?? (_ => 0f);
        HaulerAvailable = haulerAvailable;
    }

    public WorkAuthorityVerdict Authority { get; }

    public bool CustodyAvailable { get; }

    public bool JournalWritable { get; }

    public bool AnotherOrderActive { get; }

    public bool WorkerBusy { get; }

    public bool WorkerPresent { get; }

    public ReadinessVerdict Readiness { get; }

    public ScopeCheck Scope { get; }

    public bool DefaultCirclePreviewed { get; }

    public IReadOnlyDictionary<CollectedResource, int> YieldPerPick { get; }

    public bool DestinationResolved { get; }

    public CollectionAttentionReason DestinationRefusal { get; }

    public float WorkerCarryWeight { get; }

    public Func<CollectedResource, float> UnitWeight { get; }

    public bool HaulerAvailable { get; }
}

/// <summary>The acceptance rule of an order (GATHER-01, D12): every check that
/// needs no world is here, over facts the loop gathers, so the whole refusal
/// table is tested game-free. Order of the checks is the order a player would
/// fix things in: the order itself, who may act, who is free, whether he is
/// equipped, where he works, what he collects, where it goes.</summary>
internal static class CollectionIntake
{
    public static CollectionIntakeRefusal Check(CollectionOrderDefinition order, CollectionIntakeFacts facts)
    {
        if (order == null || facts == null)
        {
            throw new ArgumentNullException(order == null ? nameof(order) : nameof(facts));
        }

        switch (order.CheckShape())
        {
            case CollectionOrderRefusal.Unspecified:
                break;
            case CollectionOrderRefusal.NoQuotas:
                return CollectionIntakeRefusal.NoQuotas;
            case CollectionOrderRefusal.DuplicateResource:
                return CollectionIntakeRefusal.DuplicateResource;
            case CollectionOrderRefusal.DeliveryMissing:
                return CollectionIntakeRefusal.DeliveryMissing;
            default:
                return CollectionIntakeRefusal.ParticipationMissing;
        }

        switch (facts.Authority)
        {
            case WorkAuthorityVerdict.Granted:
                break;
            case WorkAuthorityVerdict.OtherPeersConnected:
                return CollectionIntakeRefusal.OtherPeersConnected;
            default:
                return CollectionIntakeRefusal.NoAuthority;
        }

        if (!facts.CustodyAvailable)
        {
            return CollectionIntakeRefusal.CustodyUnavailable;
        }

        if (!facts.JournalWritable)
        {
            return CollectionIntakeRefusal.JournalReadOnly;
        }

        if (facts.AnotherOrderActive)
        {
            return CollectionIntakeRefusal.AnotherOrderActive;
        }

        if (facts.WorkerBusy)
        {
            return CollectionIntakeRefusal.WorkerBusy;
        }

        if (!facts.WorkerPresent)
        {
            return CollectionIntakeRefusal.WorkerAbsent;
        }

        CollectionIntakeRefusal readiness = FromReadiness(facts.Readiness);
        if (readiness != CollectionIntakeRefusal.Unspecified)
        {
            return readiness;
        }

        switch (facts.Scope)
        {
            case ScopeCheck.Valid:
                break;
            case ScopeCheck.Unloaded:
                return CollectionIntakeRefusal.ScopeUnloaded;
            default:
                return CollectionIntakeRefusal.ScopeInvalid;
        }

        if (order.Scope.Source == WorkScopeSource.DefaultCampCircle && !facts.DefaultCirclePreviewed)
        {
            return CollectionIntakeRefusal.PreviewRequired;
        }

        foreach (ResourceQuota quota in order.Quotas)
        {
            if (!facts.YieldPerPick.TryGetValue(quota.Resource, out int yield) || yield < 1)
            {
                return CollectionIntakeRefusal.YieldUnknown;
            }

            if (quota.Requested % yield != 0)
            {
                return CollectionIntakeRefusal.QuotaNotMultipleOfYield;
            }
        }

        if (order.Delivery.Kind == DeliveryKind.Container)
        {
            if (!facts.DestinationResolved)
            {
                switch (facts.DestinationRefusal)
                {
                    case CollectionAttentionReason.DestinationStale:
                        return CollectionIntakeRefusal.DestinationStale;
                    case CollectionAttentionReason.DestinationAccessDenied:
                        return CollectionIntakeRefusal.DestinationAccessDenied;
                    default:
                        return CollectionIntakeRefusal.DestinationUnavailable;
                }
            }
        }
        else if (CarryPlanner.QuotaWeight(order.Quotas, facts.UnitWeight) > facts.WorkerCarryWeight)
        {
            return CollectionIntakeRefusal.HoldExceedsCarry;
        }

        if (order.Participation == ParticipationMode.WithHauler && !facts.HaulerAvailable)
        {
            return CollectionIntakeRefusal.HaulerUnavailable;
        }

        return CollectionIntakeRefusal.Unspecified;
    }

    public static CollectionIntakeRefusal FromReadiness(ReadinessVerdict verdict)
    {
        if (verdict.IsReady)
        {
            return CollectionIntakeRefusal.Unspecified;
        }

        switch (verdict.Refusal)
        {
            case ReadinessRefusal.NotRecruited:
                return CollectionIntakeRefusal.WorkerNotRecruited;
            case ReadinessRefusal.ToolUnusable:
                return CollectionIntakeRefusal.ToolBroken;
            case ReadinessRefusal.HandoverUncertain:
                return CollectionIntakeRefusal.ToolHandoverUncertain;
            default:
                // ToolMissing, and the unrecorded refusal: not ready is not ready.
                return CollectionIntakeRefusal.ToolMissing;
        }
    }

    /// <summary>The attention reason a readiness refusal pauses an accepted
    /// order with. Readiness pauses; it never relocks recruitment.</summary>
    public static CollectionAttentionReason AttentionFor(ReadinessVerdict verdict)
    {
        switch (FromReadiness(verdict))
        {
            case CollectionIntakeRefusal.Unspecified:
                return CollectionAttentionReason.Unspecified;
            case CollectionIntakeRefusal.WorkerNotRecruited:
                return CollectionAttentionReason.WorkerNotRecruited;
            case CollectionIntakeRefusal.ToolBroken:
                return CollectionAttentionReason.ToolBroken;
            case CollectionIntakeRefusal.ToolHandoverUncertain:
                return CollectionAttentionReason.ToolHandoverUncertain;
            default:
                return CollectionAttentionReason.ToolMissing;
        }
    }

    public static string Describe(CollectionIntakeRefusal refusal)
    {
        switch (refusal)
        {
            case CollectionIntakeRefusal.Unspecified:
                return "Accepted.";
            case CollectionIntakeRefusal.NoQuotas:
                return "Ask for at least one Stone or Wood.";
            case CollectionIntakeRefusal.DuplicateResource:
                return "Each resource can be asked for once per order.";
            case CollectionIntakeRefusal.DeliveryMissing:
                return "Choose a chest to deliver to, or ask him to hold the materials for you.";
            case CollectionIntakeRefusal.ParticipationMissing:
                return "The order does not say whether he works alone or with Gunnar; that is a bug.";
            case CollectionIntakeRefusal.NoAuthority:
                return "Workers may not act here: turn the settlement runtime on and play as the host.";
            case CollectionIntakeRefusal.OtherPeersConnected:
                return "Workers only take orders in single player or while nobody else is connected, for now.";
            case CollectionIntakeRefusal.CustodyUnavailable:
                return "This build has no custody record to keep materials honest with, so he takes no orders.";
            case CollectionIntakeRefusal.JournalReadOnly:
                return "The settlement record cannot be written, so no order can start. Check the log.";
            case CollectionIntakeRefusal.AnotherOrderActive:
                return "Thorstein already has a collection order. Finish or cancel it first.";
            case CollectionIntakeRefusal.WorkerBusy:
                return "Thorstein is busy with another job.";
            case CollectionIntakeRefusal.WorkerIdentityUnknown:
                return "Thorstein's identity was not accepted by the shared Concerned NPC runtime when the game " +
                    "started, so nothing can take hold of him and he will take no order this session. Nothing is " +
                    "busy and waiting will not help: check the startup log for the line that says why, and check " +
                    "that Concerned NPC is installed and enabled.";
            case CollectionIntakeRefusal.WorkerAbsent:
                return "Thorstein is not here to take the order.";
            case CollectionIntakeRefusal.WorkerNotRecruited:
                return "Thorstein is not employed here yet.";
            case CollectionIntakeRefusal.ToolMissing:
                return "Thorstein starts only with his own axe and hammer. Give him the missing tool first.";
            case CollectionIntakeRefusal.ToolBroken:
                return "One of Thorstein's tools is broken. Repair or replace it first.";
            case CollectionIntakeRefusal.ToolHandoverUncertain:
                return "A tool handover is unresolved. Resolve it first.";
            case CollectionIntakeRefusal.ScopeInvalid:
                return "The work area could not be established. Mark a harvest area, or claim a bed, and try again.";
            case CollectionIntakeRefusal.ScopeUnloaded:
                return "The work area is not loaded. Go there first.";
            case CollectionIntakeRefusal.PreviewRequired:
                return "No harvest area is marked. Look at the default work area first (preview), then give the order.";
            case CollectionIntakeRefusal.YieldUnknown:
                return "This world's drop amounts could not be read, so he cannot plan honestly.";
            case CollectionIntakeRefusal.QuotaNotMultipleOfYield:
                return "This world gives several units per pick. Ask for a multiple of that, so he never collects " +
                    "more than you asked.";
            case CollectionIntakeRefusal.DestinationUnavailable:
                return "He cannot use that chest right now.";
            case CollectionIntakeRefusal.DestinationAccessDenied:
                return "He is not allowed to use that chest.";
            case CollectionIntakeRefusal.DestinationStale:
                return "That chest was chosen before the world was reloaded. Look at it again.";
            case CollectionIntakeRefusal.HoldExceedsCarry:
                return "In hold-for-you mode everything must fit on his back at once. Ask for less, or choose a chest.";
            case CollectionIntakeRefusal.HaulerUnavailable:
                return "Gunnar or his cart is not available.";
            case CollectionIntakeRefusal.NotRecorded:
                return "The order could not be written to the settlement record, so it did not start.";
            case CollectionIntakeRefusal.WrongWorker:
                return "That order is for someone else.";
            default:
                return "Refused for a reason this build does not know; that is a bug.";
        }
    }
}
