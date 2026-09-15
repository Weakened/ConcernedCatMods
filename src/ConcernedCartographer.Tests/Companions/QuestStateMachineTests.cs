using TheConcernedCat.Companions.Quest;

namespace ConcernedCartographer.Tests.Companions;

public class QuestStateMachineTests
{
    private static readonly QuestState[] AllStates =
    {
        QuestState.Unstarted,
        QuestState.Discovered,
        QuestState.Collected,
        QuestState.IntroPending,
        QuestState.Recruited,
        QuestState.Skipped,
    };

    private static readonly QuestTransition[] AllTransitions =
    {
        QuestTransition.Discover,
        QuestTransition.Collect,
        QuestTransition.BeginIntroduction,
        QuestTransition.Welcome,
        QuestTransition.Skip,
    };

    [Fact]
    public void HappyPathWalksDiscoverToRecruited()
    {
        QuestState state = QuestState.Unstarted;

        Assert.Equal(QuestTransitionOutcome.Advanced, QuestStateMachine.TryApply(state, QuestTransition.Discover, out state));
        Assert.Equal(QuestState.Discovered, state);

        Assert.Equal(QuestTransitionOutcome.Advanced, QuestStateMachine.TryApply(state, QuestTransition.Collect, out state));
        Assert.Equal(QuestState.Collected, state);

        Assert.Equal(QuestTransitionOutcome.Advanced, QuestStateMachine.TryApply(state, QuestTransition.BeginIntroduction, out state));
        Assert.Equal(QuestState.IntroPending, state);

        Assert.Equal(QuestTransitionOutcome.Advanced, QuestStateMachine.TryApply(state, QuestTransition.Welcome, out state));
        Assert.Equal(QuestState.Recruited, state);
        Assert.True(QuestStateMachine.IsComplete(state));
        Assert.True(QuestStateMachine.HasCompanion(state));
    }

    [Fact]
    public void ProgressNeverDecreasesForAnyStateAndTransition()
    {
        // The property the whole design leans on: no input sequence can take
        // access away from someone who already earned it.
        foreach (QuestState state in AllStates)
        {
            foreach (QuestTransition transition in AllTransitions)
            {
                QuestStateMachine.TryApply(state, transition, out QuestState next);
                Assert.True(
                    QuestStateMachine.Rank(next) >= QuestStateMachine.Rank(state),
                    $"{state} + {transition} regressed to {next}");
            }
        }
    }

    [Fact]
    public void CompletionIsNeverLost()
    {
        foreach (QuestState state in AllStates)
        {
            if (!QuestStateMachine.IsComplete(state))
            {
                continue;
            }

            foreach (QuestTransition transition in AllTransitions)
            {
                QuestStateMachine.TryApply(state, transition, out QuestState next);
                Assert.True(QuestStateMachine.IsComplete(next), $"{state} + {transition} un-completed the quest");
            }
        }
    }

    [Fact]
    public void RepeatingATransitionIsAlwaysIdempotent()
    {
        foreach (QuestState state in AllStates)
        {
            foreach (QuestTransition transition in AllTransitions)
            {
                QuestStateMachine.TryApply(state, transition, out QuestState first);
                QuestTransitionOutcome second = QuestStateMachine.TryApply(first, transition, out QuestState settled);

                Assert.Equal(QuestTransitionOutcome.AlreadySatisfied, second);
                Assert.Equal(first, settled);
            }
        }
    }

    [Fact]
    public void DoubleInteractionDoesNotAdvanceTwice()
    {
        QuestStateMachine.TryApply(QuestState.Discovered, QuestTransition.Collect, out QuestState once);
        QuestTransitionOutcome outcome = QuestStateMachine.TryApply(once, QuestTransition.Collect, out QuestState twice);

        Assert.Equal(QuestState.Collected, once);
        Assert.Equal(QuestState.Collected, twice);
        Assert.Equal(QuestTransitionOutcome.AlreadySatisfied, outcome);
    }

    [Fact]
    public void DuplicateDiscoveryIsHarmless()
    {
        QuestStateMachine.TryApply(QuestState.Unstarted, QuestTransition.Discover, out QuestState first);
        Assert.Equal(
            QuestTransitionOutcome.AlreadySatisfied,
            QuestStateMachine.TryApply(first, QuestTransition.Discover, out QuestState second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void ExaminingOnTheFirstFrameSkipsStraightToCollected()
    {
        // A player can interact the moment the collectible appears, before a
        // separate discovery step has been recorded.
        Assert.Equal(
            QuestTransitionOutcome.Advanced,
            QuestStateMachine.TryApply(QuestState.Unstarted, QuestTransition.Collect, out QuestState state));
        Assert.Equal(QuestState.Collected, state);
    }

    [Fact]
    public void ClosingTheDialogueDefersRatherThanCompleting()
    {
        // Closing is the absence of a transition: the stage stays IntroPending
        // so the next interaction resumes instead of restarting.
        QuestState state = QuestState.IntroPending;
        Assert.False(QuestStateMachine.IsComplete(state));
        Assert.True(QuestStateMachine.ShouldPresentCollectible(state));

        Assert.Equal(
            QuestTransitionOutcome.AlreadySatisfied,
            QuestStateMachine.TryApply(state, QuestTransition.BeginIntroduction, out QuestState resumed));
        Assert.Equal(QuestState.IntroPending, resumed);
    }

    [Fact]
    public void SkippingCompletesWithoutACompanion()
    {
        Assert.Equal(
            QuestTransitionOutcome.Advanced,
            QuestStateMachine.TryApply(QuestState.Collected, QuestTransition.Skip, out QuestState state));

        Assert.Equal(QuestState.Skipped, state);
        Assert.True(QuestStateMachine.IsComplete(state));
        Assert.False(QuestStateMachine.HasCompanion(state));
        Assert.False(QuestStateMachine.ShouldPresentCollectible(state));
    }

    [Fact]
    public void ASkippedPlayerCanStillWelcomeTheCompanionLater()
    {
        Assert.Equal(
            QuestTransitionOutcome.Advanced,
            QuestStateMachine.TryApply(QuestState.Skipped, QuestTransition.Welcome, out QuestState state));

        Assert.Equal(QuestState.Recruited, state);
        Assert.True(QuestStateMachine.HasCompanion(state));
    }

    [Fact]
    public void SkippingAfterRecruitingDoesNotSendTheCompanionAway()
    {
        QuestTransitionOutcome outcome =
            QuestStateMachine.TryApply(QuestState.Recruited, QuestTransition.Skip, out QuestState state);

        Assert.Equal(QuestTransitionOutcome.AlreadySatisfied, outcome);
        Assert.Equal(QuestState.Recruited, state);
        Assert.True(QuestStateMachine.HasCompanion(state));
    }

    [Fact]
    public void AnUnknownTransitionValueIsRejectedWithoutChangingState()
    {
        QuestTransitionOutcome outcome =
            QuestStateMachine.TryApply(QuestState.Collected, (QuestTransition)999, out QuestState state);

        Assert.Equal(QuestTransitionOutcome.Rejected, outcome);
        Assert.Equal(QuestState.Collected, state);
    }

    [Fact]
    public void ReplayIsOfferedOnlyAfterCompletionAndNeverMovesTheState()
    {
        Assert.False(QuestStateMachine.CanReplayStory(QuestState.IntroPending));
        Assert.True(QuestStateMachine.CanReplayStory(QuestState.Recruited));
        Assert.True(QuestStateMachine.CanReplayStory(QuestState.Skipped));
    }

    [Fact]
    public void CollectibleIsPresentedUntilTheIntroductionIsFinished()
    {
        Assert.True(QuestStateMachine.ShouldPresentCollectible(QuestState.Unstarted));
        Assert.True(QuestStateMachine.ShouldPresentCollectible(QuestState.Discovered));
        Assert.True(QuestStateMachine.ShouldPresentCollectible(QuestState.Collected));
        Assert.True(QuestStateMachine.ShouldPresentCollectible(QuestState.IntroPending));
        Assert.False(QuestStateMachine.ShouldPresentCollectible(QuestState.Recruited));
        Assert.False(QuestStateMachine.ShouldPresentCollectible(QuestState.Skipped));
    }

    [Fact]
    public void UnknownPersistedStatesAreRecognisedAsUnknown()
    {
        foreach (QuestState state in AllStates)
        {
            Assert.True(QuestStateMachine.IsKnown(state));
        }

        Assert.False(QuestStateMachine.IsKnown((QuestState)42));
    }
}
