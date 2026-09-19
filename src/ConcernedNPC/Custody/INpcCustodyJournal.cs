namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Where the role writes custody down, and the only thing this library
/// knows about that writing: whether it can happen, and whether it did.
///
/// <b>What this seam deliberately does not have: a format.</b> No row tag, no
/// field name, no schema number, no path. Those are durable facts belonging to
/// a role's own file - one of them is a settlement journal at schema 3 with
/// named fields inside fixed kinds - and a library that named any of them would
/// have to change one to change anything, which is a cost paid by everyone who
/// has ever played. The role receives an intent and a receipt as objects and
/// writes whatever its own format says, unchanged.
///
/// <b>The ordering this seam exists to enforce.</b> Intent before any engine
/// mutation; receipt after. Both return a plain boolean, and a false is an
/// answer rather than an exception: an intent that could not be written means
/// nothing moves, and a receipt that could not be written means the transfer is
/// uncertain. Both of those are correct behaviour, and both are unreachable if
/// a failure to write throws past the executor.</summary>
internal interface INpcCustodyJournal
{
    /// <summary>Whether new custody rows can be written at all right now - the
    /// file is open, the role's own gate is satisfied, nothing is owed. Read
    /// before a transfer starts, so that "the record cannot be written" refuses
    /// the transfer instead of stranding it half done.</summary>
    bool IsWritable { get; }

    /// <summary>Writes what is about to happen, and returns whether it reached
    /// the role's record. <b>Called before any engine mutation.</b></summary>
    bool TryRecordIntent(NpcTransferIntent intent);

    /// <summary>Writes what did happen. A false here is why
    /// <see cref="NpcTransferOutcome.Uncertain"/> exists: the units may have
    /// moved and the record does not say so, which only the next session's
    /// reconciliation can settle.</summary>
    bool TryRecordReceipt(NpcTransferIntent intent, NpcTransferReceipt receipt);
}
