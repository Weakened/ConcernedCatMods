using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;
using TheConcernedCat.Companions.Unlock;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>End-to-end behaviour over a real sidecar file: interrupted
/// introductions, relogging, upgrades, and two products sharing one config
/// directory without sharing anything else.</summary>
public sealed class CompanionLifecycleTests : IDisposable
{
    private readonly string _root;
    private readonly CompanionSidecarStore _store;

    private static readonly QuestId Introduction = new("introduction");

    public CompanionLifecycleTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "concerned-companions-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new CompanionSidecarStore(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // A locked temp file must not fail the suite.
        }
    }

    private CompanionScope Scope(
        string product = "synthetic-alpha", long world = 1, long character = 1)
    {
        return new CompanionScope(new ProductId(product), new WorldId(world), new CharacterId(character));
    }

    private CompanionProgress Open(
        CompanionScope scope,
        LegacyEvidence evidence = LegacyEvidence.None,
        bool toolsOnly = false)
    {
        return CompanionProgress.Open(_store, scope, Introduction, evidence, toolsOnly);
    }

    [Fact]
    public void AFreshPlayerSeesTheCollectibleAndIsNotUnlocked()
    {
        CompanionProgress progress = Open(Scope());

        Assert.False(progress.IsUnlocked);
        Assert.True(progress.ShouldPresentCollectible);
        Assert.True(progress.Decision.ShouldOfferToolsOnly);
        Assert.Equal(QuestState.Unstarted, progress.QuestState);
    }

    [Fact]
    public void ResettingAFinishedIntroductionOffersItAgainAndKeepsTheTools()
    {
        // The owner's test loop: finish the introduction, reset it, meet the
        // companion again. A completed quest's access is never written as a
        // grant of its own - it is read off the quest record - so a reset that
        // only dropped the record would lock the tools of any character with no
        // other evidence. Access is monotonic; a test tool is no exception.
        CompanionScope scope = Scope();
        CompanionProgress first = Open(scope);
        first.Advance(QuestTransition.Discover);
        first.Advance(QuestTransition.Collect);
        first.Advance(QuestTransition.BeginIntroduction);
        first.Advance(QuestTransition.Welcome);
        Assert.Equal(UnlockReason.QuestCompleted, first.Decision.Reason);

        Assert.True(first.ResetQuest());

        Assert.Equal(QuestState.Unstarted, first.QuestState);
        Assert.True(first.IsUnlocked);
        Assert.False(first.HasCompanion);
        Assert.True(first.ShouldPresentCollectible);
        Assert.False(first.HasUnsavedChanges);

        CompanionProgress relogged = Open(scope);
        Assert.Equal(QuestState.Unstarted, relogged.QuestState);
        Assert.True(relogged.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, relogged.Decision.Reason);
        Assert.True(relogged.ShouldPresentCollectible);

        // And the introduction can be walked through again from the start.
        Assert.Equal(QuestTransitionOutcome.Advanced, relogged.Advance(QuestTransition.Discover));
    }

    [Fact]
    public void TheCrewIsToldOnceWhenTheCompanionJoinsAndAgainAfterAReset()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);
        Assert.False(progress.ShouldAnnounceJoin);
        Assert.False(progress.AnnounceJoin());

        progress.Advance(QuestTransition.Collect);
        progress.Advance(QuestTransition.Welcome);
        Assert.True(progress.ShouldAnnounceJoin);

        Assert.True(progress.AnnounceJoin());
        Assert.False(progress.ShouldAnnounceJoin);
        Assert.False(progress.AnnounceJoin());

        // Remembered on disk, so a relog does not repeat it.
        CompanionProgress relogged = Open(scope);
        Assert.False(relogged.ShouldAnnounceJoin);

        // A reset drops the record, so meeting him again is news again.
        Assert.True(relogged.ResetQuest());
        relogged.Advance(QuestTransition.Collect);
        relogged.Advance(QuestTransition.Welcome);
        Assert.True(relogged.ShouldAnnounceJoin);
    }

    [Fact]
    public void ResettingAnUnstartedIntroductionChangesNothing()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);

        Assert.False(progress.ResetQuest());
        Assert.False(progress.IsUnlocked);
        Assert.False(File.Exists(_store.ResolvePath(scope)));
    }

    [Fact]
    public void AResetNeverWritesOverAFileThisBuildMayNotWrite()
    {
        // A read-only sidecar belongs to a newer build or could not be moved
        // aside. Resetting would mean rewriting it; refusing is the only
        // answer that cannot destroy somebody's data.
        CompanionScope scope = Scope();
        string path = _store.ResolvePath(scope);
        File.WriteAllText(path, "unreadable");
        for (int attempt = 0; attempt < 21; attempt++)
        {
            string taken = attempt == 0
                ? path + ".corrupt"
                : path + ".corrupt." + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(taken, "already here");
        }

        CompanionProgress progress = Open(scope);
        progress.Advance(QuestTransition.Welcome);

        Assert.False(progress.ResetQuest());
        Assert.True(progress.IsUnlocked);
        Assert.Equal("unreadable", File.ReadAllText(path).Trim());
    }

    [Fact]
    public void TheWholeIntroductionUnlocksAndSurvivesARelog()
    {
        CompanionScope scope = Scope();
        CompanionProgress first = Open(scope);

        first.Advance(QuestTransition.Discover);
        first.Advance(QuestTransition.Collect);
        first.Advance(QuestTransition.BeginIntroduction);
        first.Advance(QuestTransition.Welcome);

        Assert.True(first.IsUnlocked);
        Assert.True(first.HasCompanion);

        CompanionProgress relogged = Open(scope);
        Assert.True(relogged.IsUnlocked);
        Assert.True(relogged.HasCompanion);
        Assert.False(relogged.ShouldPresentCollectible);
        Assert.Equal(UnlockReason.QuestCompleted, relogged.Decision.Reason);
    }

    [Fact]
    public void AnInterruptedIntroductionResumesInsteadOfRestarting()
    {
        CompanionScope scope = Scope();
        CompanionProgress first = Open(scope);
        first.Advance(QuestTransition.Collect);
        first.Advance(QuestTransition.BeginIntroduction);

        // The player closes the dialogue and quits mid-story.
        CompanionProgress resumed = Open(scope);

        Assert.Equal(QuestState.IntroPending, resumed.QuestState);
        Assert.False(resumed.IsUnlocked);
        Assert.True(resumed.ShouldPresentCollectible);

        Assert.Equal(QuestTransitionOutcome.Advanced, resumed.Advance(QuestTransition.Welcome));
        Assert.True(resumed.IsUnlocked);
    }

    [Fact]
    public void RecruitmentIsOnDiskBeforeTheCollectibleIsRemoved()
    {
        // The rule that keeps a failed save from stranding somebody with no
        // collectible and no companion.
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);

        progress.Advance(QuestTransition.Collect);
        Assert.False(progress.TryRetirePresentation());

        progress.Advance(QuestTransition.Welcome);
        Assert.False(progress.HasUnsavedChanges);

        // Recruitment is already durable at this point.
        Assert.True(Open(scope).IsUnlocked);

        Assert.True(progress.TryRetirePresentation());
        Assert.True(Open(scope).PresentationRetired);
    }

    [Fact]
    public void RetiringThePresentationTwiceIsHarmless()
    {
        CompanionProgress progress = Open(Scope());
        progress.Advance(QuestTransition.Welcome);

        Assert.True(progress.TryRetirePresentation());
        Assert.False(progress.TryRetirePresentation());
    }

    [Fact]
    public void ACrashBetweenSavingAndRetiringSimplyRetiresNextSession()
    {
        CompanionScope scope = Scope();
        CompanionProgress interrupted = Open(scope);
        interrupted.Advance(QuestTransition.Welcome);
        // ... and the process dies before TryRetirePresentation runs.

        CompanionProgress next = Open(scope);
        Assert.True(next.IsUnlocked);
        Assert.False(next.PresentationRetired);
        Assert.True(next.TryRetirePresentation());
    }

    [Fact]
    public void RepeatedInteractionNeverUnlocksTwice()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);

        // Only the first welcome advances. Every repeat reports
        // AlreadySatisfied, which is what tells a caller not to hand out the
        // welcome notice - or anything else - a second time.
        Assert.Equal(QuestTransitionOutcome.Advanced, progress.Advance(QuestTransition.Welcome));
        for (int repeat = 0; repeat < 5; repeat++)
        {
            Assert.Equal(QuestTransitionOutcome.AlreadySatisfied, progress.Advance(QuestTransition.Welcome));
        }

        CompanionProgress reloaded = Open(scope);
        Assert.Equal(QuestState.Recruited, reloaded.QuestState);
        Assert.Single(_store.ListSidecarFiles());
    }

    [Fact]
    public void DuplicateDiscoveryDoesNotDisturbProgress()
    {
        CompanionProgress progress = Open(Scope());
        progress.Advance(QuestTransition.Collect);

        for (int repeat = 0; repeat < 5; repeat++)
        {
            Assert.Equal(QuestTransitionOutcome.AlreadySatisfied, progress.Advance(QuestTransition.Discover));
        }

        Assert.Equal(QuestState.Collected, progress.QuestState);
    }

    [Fact]
    public void AnExistingUserIsUnlockedOnUpgradeWithoutTouchingTheStory()
    {
        CompanionScope scope = Scope();
        CompanionProgress upgraded = Open(scope, evidence: LegacyEvidence.Present);

        Assert.True(upgraded.IsUnlocked);
        Assert.True(upgraded.ShouldPresentCollectible);
        Assert.Equal(QuestState.Unstarted, upgraded.QuestState);

        // The grant is on disk, so it survives the evidence disappearing.
        CompanionProgress later = Open(scope, evidence: LegacyEvidence.None);
        Assert.True(later.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, later.Decision.Reason);
    }

    [Fact]
    public void AmbiguousEvidenceStillKeepsTheToolsAvailable()
    {
        CompanionScope scope = Scope();
        Assert.True(Open(scope, evidence: LegacyEvidence.Ambiguous).IsUnlocked);
        Assert.True(Open(scope).IsUnlocked);
    }

    [Fact]
    public void ToolsOnlyUnlocksImmediatelyAndRemovesTheCollectible()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope, toolsOnly: true);

        Assert.True(progress.IsUnlocked);
        Assert.False(progress.ShouldPresentCollectible);

        // The preference alone unlocks and hides the collectible but leaves the
        // quest open, so turning it off restores the story. Finishing the
        // introduction is the separate, explicit skip below.
        Assert.Equal(QuestState.Unstarted, progress.QuestState);
        Assert.False(progress.HasCompanion);

        progress.Advance(QuestTransition.Skip);
        Assert.Equal(QuestState.Skipped, progress.QuestState);
        Assert.True(progress.CanReplayStory);
        Assert.False(progress.HasCompanion);
    }

    [Fact]
    public void ASkippedPlayerCanReplayTheStoryAndWelcomeTheCompanion()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);
        progress.Advance(QuestTransition.Skip);

        Assert.True(progress.CanReplayStory);
        Assert.Equal(QuestTransitionOutcome.Advanced, progress.Advance(QuestTransition.Welcome));
        Assert.True(progress.HasCompanion);
        Assert.True(Open(scope).HasCompanion);
    }

    [Fact]
    public void TurningToolsOnlyOffNeverRevokesAccess()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope, toolsOnly: true);
        progress.SetToolsOnlyPreference(false);

        Assert.True(progress.IsUnlocked);
        Assert.True(Open(scope).IsUnlocked);
    }

    [Fact]
    public void AnUnreadableSidecarKeepsAccessAndExplainsItself()
    {
        CompanionScope scope = Scope();
        File.WriteAllText(_store.ResolvePath(scope), "corrupted beyond recognition");

        CompanionProgress progress = Open(scope);

        Assert.True(progress.IsUnlocked);
        Assert.Equal(SidecarLoadOutcome.Corrupt, progress.LoadOutcome);
        Assert.NotNull(progress.Notice);
    }

    [Fact]
    public void AccessGrantedAfterCorruptionSurvivesIntoTheNextSession()
    {
        // The quarantined file is gone by the next login, so the session that
        // saw the corruption is the only one that can record what it knew.
        // Without that, the next session sees no file, decides this is a brand
        // new player, and takes the tools away.
        CompanionScope scope = Scope();
        File.WriteAllText(_store.ResolvePath(scope), "corrupted beyond recognition");

        Assert.True(Open(scope).IsUnlocked);

        CompanionProgress next = Open(scope);
        Assert.Equal(SidecarLoadOutcome.Loaded, next.LoadOutcome);
        Assert.True(next.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, next.Decision.Reason);
    }

    [Fact]
    public void DamagedRowsAlongsideGoodOnesStillCountAsPriorUse()
    {
        CompanionScope scope = Scope();
        CompanionProgress seeded = Open(scope);
        seeded.Advance(QuestTransition.Discover);

        string path = _store.ResolvePath(scope);
        File.AppendAllLines(path, new[] { "q\tbroken\tnope\tnope\tnope" });

        CompanionProgress damaged = Open(scope);

        Assert.Equal(SidecarLoadOutcome.LoadedWithSkippedRows, damaged.LoadOutcome);
        Assert.True(damaged.IsUnlocked);
        Assert.NotNull(damaged.Notice);
    }

    [Fact]
    public void ProgressIsIsolatedPerCharacterAndWorld()
    {
        CompanionProgress mine = Open(Scope(world: 1, character: 1));
        mine.Advance(QuestTransition.Welcome);

        Assert.False(Open(Scope(world: 2, character: 1)).IsUnlocked);
        Assert.False(Open(Scope(world: 1, character: 2)).IsUnlocked);
        Assert.True(Open(Scope(world: 1, character: 1)).IsUnlocked);
    }

    [Fact]
    public void FinishingOneProductsIntroductionNeverUnlocksAnother()
    {
        // The two synthetic products share a config directory, a world, a
        // character, and even their quest slug. Only the product identity
        // separates them, which is exactly the claim under test.
        CompanionScope alpha = Scope("synthetic-alpha", world: 7, character: 7);
        CompanionScope beta = Scope("synthetic-beta", world: 7, character: 7);

        CompanionProgress alphaProgress = Open(alpha);
        alphaProgress.Advance(QuestTransition.Welcome);

        CompanionProgress betaProgress = Open(beta);

        Assert.True(Open(alpha).IsUnlocked);
        Assert.False(betaProgress.IsUnlocked);
        Assert.Equal(QuestState.Unstarted, betaProgress.QuestState);
        Assert.True(betaProgress.ShouldPresentCollectible);
    }

    [Fact]
    public void EachProductGrandfathersItsOwnUsersIndependently()
    {
        CompanionScope alpha = Scope("synthetic-alpha", world: 8, character: 8);
        CompanionScope beta = Scope("synthetic-beta", world: 8, character: 8);

        Assert.True(Open(alpha, evidence: LegacyEvidence.Present).IsUnlocked);
        Assert.False(Open(beta, evidence: LegacyEvidence.None).IsUnlocked);
    }

    [Fact]
    public void TwoProductsWriteToSeparateFiles()
    {
        CompanionScope alpha = Scope("synthetic-alpha", world: 9, character: 9);
        CompanionScope beta = Scope("synthetic-beta", world: 9, character: 9);

        Open(alpha).Advance(QuestTransition.Welcome);
        Open(beta).Advance(QuestTransition.Skip);

        Assert.NotEqual(_store.ResolvePath(alpha), _store.ResolvePath(beta));
        Assert.Equal(2, _store.ListSidecarFiles().Count);
        Assert.True(Open(alpha).HasCompanion);
        Assert.False(Open(beta).HasCompanion);
    }

    [Fact]
    public void AccessIsNeverRevokedByAnythingThatHappensLater()
    {
        // Losing a bed, dying, hiding the companion, and toggling settings all
        // route through the same state. None of them touches the quest.
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);
        progress.Advance(QuestTransition.Welcome);
        progress.TryRetirePresentation();

        for (int session = 0; session < 5; session++)
        {
            CompanionProgress reopened = Open(scope);
            Assert.True(reopened.IsUnlocked);
            reopened.SetToolsOnlyPreference(session % 2 == 0);
            Assert.True(reopened.IsUnlocked);
        }

        Assert.True(Open(scope).IsUnlocked);
        Assert.True(Open(scope).HasCompanion);
    }

    [Fact]
    public void AReadOnlySidecarStillReportsAccessWithoutWriting()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = Open(scope);
        progress.Advance(QuestTransition.Welcome);

        // Simulate the file being replaced by a newer build's version.
        string path = _store.ResolvePath(scope);
        string[] lines = File.ReadAllLines(path);
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("s\t", StringComparison.Ordinal))
            {
                lines[index] = "s\t" + (CompanionSidecarCodec.SchemaVersion + 1);
            }
        }

        File.WriteAllLines(path, lines);
        string before = File.ReadAllText(path);

        CompanionProgress reopened = Open(scope);
        Assert.True(reopened.IsUnlocked);
        reopened.Advance(QuestTransition.Skip);

        Assert.Equal(before, File.ReadAllText(path));
        Assert.NotNull(reopened.Notice);
    }
}
