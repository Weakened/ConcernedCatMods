using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>What actually happened, recorded after the engine mutations.
///
/// <b>The only number that moves anything is <see cref="Accepted"/>, and it is
/// measured.</b> Not the intended count, not what the engine's add returned -
/// the difference between two counts taken either side of the mutation. Every
/// way material has gone missing in this repository's history began with
/// crediting an intention.</summary>
internal sealed class NpcTransferReceipt
{
    internal NpcTransferReceipt(
        ReservationId request,
        NpcTransferOutcome outcome,
        int accepted,
        NpcCustodyLocation remainderAt,
        string? evidence)
    {
        Request = request;
        Outcome = outcome;
        Accepted = accepted < 0 ? 0 : accepted;
        RemainderAt = remainderAt;
        Evidence = evidence ?? string.Empty;
    }

    internal ReservationId Request { get; }

    internal NpcTransferOutcome Outcome { get; }

    /// <summary>Units the two inventories agree moved.</summary>
    internal int Accepted { get; }

    /// <summary>Where units that did not arrive are - normally the source.
    /// </summary>
    internal NpcCustodyLocation RemainderAt { get; }

    /// <summary>What was observed, in enough detail for a person to decide:
    /// both counts before, both counts after, what was asked for, what the
    /// engine claimed, and any fault. Written into the record, not only a log,
    /// because the next session's reconciliation is the reader.</summary>
    internal string Evidence { get; }

    public override string ToString() => Outcome + " " + Accepted + " (" + Request + ")";
}
