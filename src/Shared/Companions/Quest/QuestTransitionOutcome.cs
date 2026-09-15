namespace TheConcernedCat.Companions.Quest;

/// <summary>What a <see cref="QuestTransition"/> did.</summary>
internal enum QuestTransitionOutcome
{
    /// <summary>The state moved forward. This is the only outcome that may
    /// trigger a one-time side effect such as a memento entry or an unlock
    /// notice.</summary>
    Advanced = 0,

    /// <summary>The requested progress was already met or exceeded and the
    /// state is unchanged. Callers must treat this as success and must not
    /// re-grant anything: it is what makes a double interaction, a replayed
    /// journal, and a re-entered dialogue harmless.</summary>
    AlreadySatisfied = 1,

    /// <summary>The transition value is not one this build understands. The
    /// state is unchanged.</summary>
    Rejected = 2,
}
