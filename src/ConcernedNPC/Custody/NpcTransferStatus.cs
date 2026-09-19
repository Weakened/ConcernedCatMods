namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Where one transfer's record stands.</summary>
internal enum NpcTransferStatus
{
    Unspecified = 0,

    /// <summary>The intent is recorded and no receipt is. In this session: in
    /// flight. After a reload: interrupted, and treated as uncertain, because
    /// nothing replayed from a record is still happening.</summary>
    Open = 1,

    Completed = 2,

    /// <summary>Some units arrived; the rest stayed at the source.</summary>
    Partial = 3,

    /// <summary>Nothing moved, verified from the counts.</summary>
    Refused = 4,

    /// <summary>A mutation may have happened and the counts could not prove
    /// which. Nothing is credited, replayed or compensated.</summary>
    Uncertain = 5,

    /// <summary>A person said which side is true.</summary>
    Resolved = 6,

    /// <summary>The world was loaded from a save made before this row, so the
    /// world itself rolled the change back. <b>Never credited, refunded or
    /// replayed</b> - there is nothing to undo, because the world already
    /// undid it.</summary>
    Voided = 7,

    /// <summary>The world-save marker rule could not place this row before or
    /// after the loaded save. Treated exactly like
    /// <see cref="Uncertain"/>.</summary>
    Ambiguous = 8,
}
