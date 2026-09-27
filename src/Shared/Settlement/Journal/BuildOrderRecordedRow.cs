using System;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Orders;

namespace TheConcernedCat.Settlement.Journal;

/// <summary>The journal owns identity and transitions; the consuming product
/// owns the versioned, immutable blueprint payload. No engine types or second
/// persistence file. An older reader refuses the unknown row and stays read-only.</summary>
internal sealed class BuildOrderRecordedRow : CustodyRow
{
    public BuildOrderRecordedRow(OrderId order, WorkerId worker, OrderTransition transition, string payload)
    {
        if (order.IsEmpty || worker.IsEmpty || string.IsNullOrEmpty(payload))
            throw new ArgumentException("A build record requires its order, worker and approved payload.");
        if (transition != OrderTransition.Approve && transition != OrderTransition.Cancel)
            throw new ArgumentException("A build record records approval or withdrawal, not remembered progress.");
        Order = order;
        Worker = worker;
        Transition = transition;
        Payload = payload;
    }

    public override JournalEntryKind Kind => JournalEntryKind.BuildOrderRecorded;
    public override OrderId Order { get; }
    public WorkerId Worker { get; }
    public OrderTransition Transition { get; }
    public string Payload { get; }
}
