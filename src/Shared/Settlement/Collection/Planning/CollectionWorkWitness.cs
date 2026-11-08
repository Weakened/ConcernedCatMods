namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>The shared interruption layer's write-ahead witness around the
/// collection loop (#379), as a game-free port. Foreman implements it over
/// ConcernedNPC's <c>NpcPlanRun</c>, which this tree cannot name.
///
/// <b>Ordering, which is the whole contract.</b> <see cref="Intend"/> is asked
/// before the loop's first settlement-journal row for a movement of material (a
/// pick, a felling hit, a deposit), and <see cref="Conclude"/> after its last.
/// The settlement journal's own rows, their order and their voiding rule are
/// untouched; it stays the only custody truth (D8). A false from
/// <see cref="Intend"/> means the movement does not happen. A movement whose
/// conclusion was never written is found on the next load as an intent with no
/// outcome, and is never replayed.</summary>
internal interface ICollectionWorkWitness
{
    /// <summary>Asked before an order starts or resumes. False refuses, with the
    /// order's attention reason and a sentence: a stopped plan needs a person,
    /// a plan read back after a reload is revalidated here first.</summary>
    bool Start(CollectionOrderDefinition order, out CollectionAttentionReason reason, out string why);

    /// <summary>Written before the world is touched. False: do not touch it.
    /// </summary>
    bool Intend(string note, out CollectionAttentionReason reason, out string why);

    /// <summary>Written after, with whether the outcome is established from
    /// evidence. False: the plan has stopped for a person.</summary>
    bool Conclude(bool established, string note, out string why);

    /// <summary>The order stopped without ending (paused, needs attention):
    /// the plan waits as it stands.</summary>
    void Suspend(string note);

    /// <summary>The order ended. What he still carries keeps the plan open for
    /// the next order; empty-handed, it closes.</summary>
    void Ended(bool finished, string note);
}
