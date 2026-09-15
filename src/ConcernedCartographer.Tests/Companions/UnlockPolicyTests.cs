using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Unlock;

namespace ConcernedCartographer.Tests.Companions;

public class UnlockPolicyTests
{
    private static UnlockDecision Decide(
        QuestState quest = QuestState.Unstarted,
        UnlockReason granted = UnlockReason.NotUnlocked,
        LegacyEvidence evidence = LegacyEvidence.None,
        bool unreadable = false,
        bool toolsOnly = false)
    {
        return UnlockPolicy.Decide(quest, granted, evidence, unreadable, toolsOnly);
    }

    [Fact]
    public void AFreshPlayerIsLockedAndOfferedBothTheStoryAndToolsOnly()
    {
        UnlockDecision decision = Decide();

        Assert.False(decision.IsUnlocked);
        Assert.Equal(UnlockReason.NotUnlocked, decision.Reason);
        Assert.True(decision.ShouldPresentIntroduction);
        Assert.True(decision.ShouldOfferToolsOnly);
    }

    [Fact]
    public void FinishingTheIntroductionUnlocks()
    {
        foreach (QuestState state in new[] { QuestState.Recruited, QuestState.Skipped })
        {
            UnlockDecision decision = Decide(quest: state);
            Assert.True(decision.IsUnlocked);
            Assert.Equal(UnlockReason.QuestCompleted, decision.Reason);
            Assert.False(decision.ShouldPresentIntroduction);
            Assert.False(decision.ShouldPersistGrant);
        }
    }

    [Fact]
    public void AnExistingUserKeepsTheirToolsOnUpgrade()
    {
        UnlockDecision decision = Decide(evidence: LegacyEvidence.Present);

        Assert.True(decision.IsUnlocked);
        Assert.Equal(UnlockReason.ExistingUserData, decision.Reason);
        Assert.True(decision.ShouldPersistGrant);
    }

    [Fact]
    public void AnExistingUserIsStillOfferedTheStory()
    {
        // Feature access and meeting the companion are separate. Grandfathering
        // someone in should not quietly cost them the introduction.
        UnlockDecision decision = Decide(evidence: LegacyEvidence.Present);

        Assert.True(decision.IsUnlocked);
        Assert.True(decision.ShouldPresentIntroduction);
        Assert.True(decision.ShouldOfferToolsOnly);
    }

    [Fact]
    public void AmbiguousEvidenceResolvesInThePlayersFavour()
    {
        UnlockDecision decision = Decide(evidence: LegacyEvidence.Ambiguous);

        Assert.True(decision.IsUnlocked);
        Assert.Equal(UnlockReason.AmbiguousLegacyEvidence, decision.Reason);
        Assert.True(decision.ShouldPersistGrant);
    }

    [Fact]
    public void UnreadableDataNeverLocksAnybodyOut()
    {
        UnlockDecision decision = Decide(unreadable: true);

        Assert.True(decision.IsUnlocked);
        Assert.Equal(UnlockReason.DataUnreadable, decision.Reason);

        // Persisting matters most here: a corrupt file gets quarantined, so the
        // next session finds nothing and would see a brand new player. Where the
        // file truly cannot be written, the store refuses the save instead.
        Assert.True(decision.ShouldPersistGrant);
    }

    [Fact]
    public void ToolsOnlyUnlocksWithoutTheStoryAndSuppressesTheCollectible()
    {
        UnlockDecision decision = Decide(toolsOnly: true);

        Assert.True(decision.IsUnlocked);
        Assert.Equal(UnlockReason.ToolsOnlyPreference, decision.Reason);
        Assert.False(decision.ShouldPresentIntroduction);
        Assert.True(decision.ShouldPersistGrant);
    }

    [Fact]
    public void TurningToolsOnlyBackOffDoesNotTakeTheToolsAway()
    {
        // The preference grant was written down the first time, so the second
        // session reads it back instead of re-deciding.
        UnlockDecision first = Decide(toolsOnly: true);
        Assert.True(first.ShouldPersistGrant);

        UnlockDecision second = Decide(
            granted: UnlockReason.ToolsOnlyPreference, toolsOnly: false);

        Assert.True(second.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, second.Reason);
    }

    [Fact]
    public void ARecordedGrantOutlivesTheEvidenceThatProducedIt()
    {
        // Somebody deletes their old data, or a probe stops finding it. Access
        // must not evaporate with it.
        UnlockDecision decision = Decide(
            granted: UnlockReason.ExistingUserData, evidence: LegacyEvidence.None);

        Assert.True(decision.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, decision.Reason);
        Assert.False(decision.ShouldPersistGrant);
    }

    [Fact]
    public void AGrantedPlayerWhoHasNotMetTheCompanionIsStillOfferedTheIntroduction()
    {
        UnlockDecision decision = Decide(granted: UnlockReason.ExistingUserData);

        Assert.True(decision.ShouldPresentIntroduction);
        Assert.True(decision.ShouldOfferToolsOnly);
    }

    [Fact]
    public void EveryInputCombinationExceptACompletelyFreshOneUnlocks()
    {
        // The policy is deliberately one-sided. This enumerates the whole input
        // space so a future edit cannot introduce a new way to lock somebody out
        // without failing here.
        var questStates = new[]
        {
            QuestState.Unstarted, QuestState.Discovered, QuestState.Collected,
            QuestState.IntroPending, QuestState.Recruited, QuestState.Skipped,
        };
        var grants = new[] { UnlockReason.NotUnlocked, UnlockReason.ExistingUserData };
        var evidences = new[] { LegacyEvidence.None, LegacyEvidence.Ambiguous, LegacyEvidence.Present };

        foreach (QuestState quest in questStates)
        {
            foreach (UnlockReason grant in grants)
            {
                foreach (LegacyEvidence evidence in evidences)
                {
                    foreach (bool unreadable in new[] { false, true })
                    {
                        foreach (bool toolsOnly in new[] { false, true })
                        {
                            UnlockDecision decision =
                                Decide(quest, grant, evidence, unreadable, toolsOnly);

                            bool expectedLocked =
                                grant == UnlockReason.NotUnlocked
                                && !QuestStateMachine.IsComplete(quest)
                                && evidence == LegacyEvidence.None
                                && !unreadable
                                && !toolsOnly;

                            Assert.Equal(!expectedLocked, decision.IsUnlocked);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ToolsOnlyIsOfferedWheneverTheIntroductionIsUnfinished()
    {
        // Nobody is ever obliged to finish a story to use the tools.
        foreach (QuestState state in new[]
                 {
                     QuestState.Unstarted, QuestState.Discovered,
                     QuestState.Collected, QuestState.IntroPending,
                 })
        {
            Assert.True(Decide(quest: state).ShouldOfferToolsOnly);
        }
    }
}
