using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>One transfer as the ledger knows it: what was intended, where it
/// got to, how many units it actually moved, and what was seen.</summary>
internal sealed class NpcTransferRecord
{
    internal NpcTransferRecord(NpcTransferIntent intent)
    {
        Intent = intent;
        Status = NpcTransferStatus.Open;
        Evidence = string.Empty;
    }

    internal NpcTransferIntent Intent { get; }

    internal NpcTransferStatus Status { get; set; }

    /// <summary>Units the ledger actually moved for this transfer. Zero until a
    /// receipt the record believes arrives.</summary>
    internal int Applied { get; set; }

    internal string Evidence { get; set; }

    internal ReservationId Request => Intent.Request;

    internal string JobId => Intent.JobId;

    /// <summary>Waiting on a person: uncertain, ambiguous, or an intent whose
    /// result was never recorded and which is no longer in flight.</summary>
    internal bool AwaitsResolution =>
        Status == NpcTransferStatus.Uncertain || Status == NpcTransferStatus.Ambiguous;

    /// <summary>True once nothing more will happen to this transfer by itself.
    /// </summary>
    internal bool IsSettled =>
        Status == NpcTransferStatus.Completed
        || Status == NpcTransferStatus.Partial
        || Status == NpcTransferStatus.Refused
        || Status == NpcTransferStatus.Resolved
        || Status == NpcTransferStatus.Voided;

    public override string ToString() => Intent + " " + Status;
}
