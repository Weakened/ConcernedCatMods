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
}
