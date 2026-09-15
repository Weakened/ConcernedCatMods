namespace TheConcernedCat.Companions.Quest;

/// <summary>The single authority on companion quest progress.
///
/// Three properties are load bearing and every caller depends on them:
///
/// <list type="bullet">
/// <item>Total. Any state plus any transition yields an answer, so no sequence
/// of interactions - however interleaved with reloads, crashes, or duplicate
/// input - can reach an undefined state.</item>
/// <item>Monotonic. Progress never decreases. A finished introduction cannot be
/// un-finished, which is why losing a bed, dying, hiding the companion, or
/// replaying the story can never revoke access.</item>
/// <item>Idempotent. Re-applying a transition reports
/// <see cref="QuestTransitionOutcome.AlreadySatisfied"/> and changes nothing,
/// so one-time side effects stay one-time.</item>
/// </list></summary>
internal static class QuestStateMachine
{
    /// <summary>True once the introduction is finished, by welcome or by an
    /// explicit tools-only choice. This, not the presence of the companion, is
    /// what the unlock policy consults.</summary>
    public static bool IsComplete(QuestState state)
    {
        return state == QuestState.Recruited || state == QuestState.Skipped;
    }

    /// <summary>True when the companion has actually been welcomed. Governs
    /// presentation only.</summary>
    public static bool HasCompanion(QuestState state)
    {
        return state == QuestState.Recruited;
    }

    /// <summary>The story may be replayed once it has been finished either
    /// way. Replaying is a presentation action and never moves the state.</summary>
    public static bool CanReplayStory(QuestState state)
    {
        return IsComplete(state);
    }

    /// <summary>The collectible should only be presented while the
    /// introduction is unfinished.</summary>
    public static bool ShouldPresentCollectible(QuestState state)
    {
        return !IsComplete(state);
    }

    /// <summary>Applies <paramref name="transition"/> to
    /// <paramref name="current"/>, reporting what happened and writing the
    /// resulting state to <paramref name="next"/>.</summary>
    public static QuestTransitionOutcome TryApply(
        QuestState current, QuestTransition transition, out QuestState next)
    {
        next = current;

        switch (transition)
        {
            case QuestTransition.Discover:
                return Promote(current, QuestState.Discovered, ref next);

            case QuestTransition.Collect:
                return Promote(current, QuestState.Collected, ref next);

            case QuestTransition.BeginIntroduction:
                return Promote(current, QuestState.IntroPending, ref next);

            case QuestTransition.Welcome:
                // Welcoming stays available after a tools-only choice: a player
                // who skipped may replay the story and let the companion join.
                // That adds presence and never removes access.
                if (current == QuestState.Recruited)
                {
                    return QuestTransitionOutcome.AlreadySatisfied;
                }

                next = QuestState.Recruited;
                return QuestTransitionOutcome.Advanced;

            case QuestTransition.Skip:
                // A skip requested after the companion was welcomed is a
                // redundant completion request, not a request to send them
                // away: hiding the companion is a separate visibility setting.
                if (IsComplete(current))
                {
                    return QuestTransitionOutcome.AlreadySatisfied;
                }

                next = QuestState.Skipped;
                return QuestTransitionOutcome.Advanced;

            default:
                return QuestTransitionOutcome.Rejected;
        }
    }

    /// <summary>Advances to <paramref name="target"/> unless the quest already
    /// reached at least that far. Intermediate stages may be skipped forward -
    /// a player who examines the collectible on the frame it appears lands on
    /// <see cref="QuestState.Collected"/> directly - but the rank never
    /// decreases.</summary>
    private static QuestTransitionOutcome Promote(
        QuestState current, QuestState target, ref QuestState next)
    {
        if (IsComplete(current) || Rank(current) >= Rank(target))
        {
            return QuestTransitionOutcome.AlreadySatisfied;
        }

        next = target;
        return QuestTransitionOutcome.Advanced;
    }

    /// <summary>Progress ordering. Both completions rank above every
    /// in-progress stage, so nothing can walk back out of them.</summary>
    public static int Rank(QuestState state)
    {
        switch (state)
        {
            case QuestState.Unstarted: return 0;
            case QuestState.Discovered: return 1;
            case QuestState.Collected: return 2;
            case QuestState.IntroPending: return 3;
            case QuestState.Recruited: return 4;
            case QuestState.Skipped: return 4;
            default: return 0;
        }
    }

    /// <summary>True when <paramref name="state"/> is a value this build
    /// defines. A sidecar written by a newer build can carry a stage this one
    /// has never heard of; the codec uses this to fall back deliberately
    /// instead of trusting an out-of-range cast.</summary>
    public static bool IsKnown(QuestState state)
    {
        switch (state)
        {
            case QuestState.Unstarted:
            case QuestState.Discovered:
            case QuestState.Collected:
            case QuestState.IntroPending:
            case QuestState.Recruited:
            case QuestState.Skipped:
                return true;
            default:
                return false;
        }
    }
}
