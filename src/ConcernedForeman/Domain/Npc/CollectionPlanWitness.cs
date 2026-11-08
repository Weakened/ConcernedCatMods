using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>The collection loop's witness (#379), over Thorstein's shared plan
/// for the <see cref="ThorsteinWorkPlan.CollectJobId"/> job. The loop decides
/// when; the plan decides what the record says; this only measures what he
/// carries and supplies the world's evidence, both through delegates so the
/// game side stays in the runtime and this stays testable.</summary>
internal sealed class CollectionPlanWitness : ICollectionWorkWitness
{
    private readonly ThorsteinWorkPlan _plan;
    private readonly Func<NpcPlanEvidence> _evidence;
    private readonly Func<IReadOnlyList<KeyValuePair<string, int>>?> _measure;

    /// <param name="measure">What he carries of the order's materials, by item
    /// name; null when it cannot be read.</param>
    internal CollectionPlanWitness(
        ThorsteinWorkPlan plan, Func<NpcPlanEvidence> evidence, Func<IReadOnlyList<KeyValuePair<string, int>>?> measure)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (!string.Equals(plan.JobId, ThorsteinWorkPlan.CollectJobId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The collection witness keeps the collection plan.", nameof(plan));
        }

        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _measure = measure ?? throw new ArgumentNullException(nameof(measure));
    }

    internal ThorsteinWorkPlan Plan => _plan;

    public bool Start(CollectionOrderDefinition order, out CollectionAttentionReason reason, out string why)
    {
        string source = order.Scope.Source + ":" + order.Scope.AnchorDescription;
        string destination = order.Delivery.Kind == DeliveryKind.Container ? order.Delivery.ContainerKey : "hold for the player";
        if (_plan.Start(_evidence(), source, destination, _measure(), out why))
        {
            reason = CollectionAttentionReason.Unspecified;
            return true;
        }

        reason = ReasonOf(_plan);
        return false;
    }

    public bool Intend(string note, out CollectionAttentionReason reason, out string why)
    {
        if (_plan.Intend(note, out why))
        {
            reason = CollectionAttentionReason.Unspecified;
            return true;
        }

        reason = ReasonOf(_plan);
        return false;
    }

    public bool Conclude(bool established, string note, out string why) =>
        _plan.Conclude(established, _measure(), note, out why);

    public void Suspend(string note) => _plan.Suspend(note);

    public void Ended(bool finished, string note) => _plan.Ended(finished, _measure(), note);

    /// <summary>A plan stopped for a person is a movement nobody can vouch for;
    /// anything else that refuses is a record that cannot be written.</summary>
    private static CollectionAttentionReason ReasonOf(ThorsteinWorkPlan plan) =>
        plan.Standing == ThorsteinPlanStanding.NeedsAttention
            ? CollectionAttentionReason.TransferUncertain
            : CollectionAttentionReason.JournalReadOnly;
}
