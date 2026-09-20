namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What a worker body should do with the inventory record it read from
/// its own network object. Zero is the unspecified value, and it is the one that
/// does <b>not</b> let the body work.</summary>
internal enum WorkerRecordLoad
{
    /// <summary>Nobody decided. The body stays inert rather than working with an
    /// inventory nobody established.</summary>
    Unspecified = 0,

    /// <summary>There is nothing stored. Start with an empty inventory and carry
    /// on — this is what every body saved before the field existed looks like.
    /// </summary>
    LoadEmpty = 1,

    /// <summary>There is a stored package. Load it before anything may be
    /// written back.</summary>
    LoadStored = 2,
}

/// <summary>Whether a worker body's record can be believed about what the body
/// holds - which is the same question as whether the body may be handed anything
/// more to hold. Zero is the unspecified value and it refuses, so a state nobody
/// decided cannot be picked into.
///
/// <b>What this does and does not save.</b> It stops the loss <i>growing</i>: a
/// body whose last change did not reach its network object is not handed more,
/// so a second, third and fourth unit are not piled onto a record that is not
/// keeping up. It does <b>not</b> recover the units in the change that failed -
/// those are in the live inventory and not in the stored package, and the next
/// load rebuilds the live one from the stored one. So a failed write still loses
/// what that one change added. That is a narrower claim than "nothing but death
/// loses material", and it is the true one.
///
/// <b>Nothing latches.</b> This is a decision over the current state and holds
/// no memory: the same body answers <see cref="Trusted"/> again the moment a
/// change does persist, and a body re-created by a zone load starts from a
/// record that has not failed. What a caller must not do is remember a refusal,
/// which is why this is a function of three booleans and not a
/// flag.</summary>
internal enum WorkerRecordTrust
{
    /// <summary>Nobody decided, and that refuses.</summary>
    Unspecified = 0,

    /// <summary>The record cannot be believed: no record, not loaded, or a change
    /// that did not reach the object. Nothing may be added and no count may be
    /// reported as fact.</summary>
    Refuses = 1,

    /// <summary>The record is the account of what the body holds, and the body
    /// may be handed more.</summary>
    Trusted = 2,
}

/// <summary>Reading a worker body's stored inventory (#381), as the part of it
/// that can be decided without the game.
///
/// <b>Why the body has to store its inventory at all.</b> The game never saves a
/// non-player character's inventory: it is a plain field with no save and no
/// load, rebuilt every time the body is created. So a stone Gunnar
/// picked up was destroyed by a zone unload, a relog or a world reload while the
/// body itself came back — silently, with no refusal, no record and no drop.
/// That is the same loss <c>WorkerRetirement</c> refuses, reached by a route no
/// player has to opt into. Concerned Foreman's worker had to solve this first and
/// did (<c>SETTLEMENT_AUTHORITY.md</c> §5a); this is the same solution, in the
/// same format, deliberately.
///
/// <b>Zero migration is the load rule, and it is why this is a decision rather
/// than a null check.</b> A Gunnar body saved before the field existed has no
/// stored package. That must be an <b>empty inventory</b>, not a refusal and not
/// a fault: refusing would strand a body that is perfectly fine, and faulting
/// would make an old save look broken. <see cref="Decide"/> never answers with
/// anything but a load, which is the property <c>WorkerInventoryRecordTests</c>
/// pins.
///
/// <b>What is deliberately not decided here.</b> Whether the load <i>succeeded</i>
/// - that is vanilla's own deserialization, and a body that could not read what
/// it carries has to go inert rather than save an empty inventory over a carried
/// one. Only the adapter can see that, and it is where the "no save before a
/// successful load" rule lives.</summary>
internal static class WorkerInventoryRecord
{
    /// <summary>What a read of the stored package means.</summary>
    /// <param name="storedPackage">The bytes found under
    /// <c>tcc.worker.inventory</c>, or null when the field is absent.</param>
    public static WorkerRecordLoad Decide(byte[]? storedPackage) =>
        storedPackage == null || storedPackage.Length == 0
            ? WorkerRecordLoad.LoadEmpty
            : WorkerRecordLoad.LoadStored;

    /// <summary>The next revision to write. Starts at one for a body that has
    /// never written, so "revision 0" always means "has never saved" and a
    /// player reading the record can tell those apart.</summary>
    public static int Next(int revision) => revision < 0 ? 1 : revision + 1;

    /// <summary>Whether a body's record can be believed about what it holds right
    /// now, which is the same question as whether it may be handed anything
    /// more.</summary>
    /// <param name="hasRecord">Whether the body has a record component at all. A
    /// body without one is not empty, it is unknown: the live inventory the game
    /// rebuilt is empty whatever the body was carrying.</param>
    /// <param name="isLoaded">Whether the record read its stored inventory. Not
    /// loaded means writing would save an empty inventory over a carried
    /// one.</param>
    /// <param name="lastChangePersisted">Whether the most recent change reached
    /// the network object. False means the live inventory and the stored one
    /// disagree and nothing here can say which the next load will see.</param>
    public static WorkerRecordTrust Trust(bool hasRecord, bool isLoaded, bool lastChangePersisted) =>
        hasRecord && isLoaded && lastChangePersisted
            ? WorkerRecordTrust.Trusted
            : WorkerRecordTrust.Refuses;
}
