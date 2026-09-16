using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Companions;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>CC-NPC-003: the product half of the companion wiring — Concerned
/// Cartographer's own registration, its legacy-evidence rule, the feature
/// gate in front of its added affordances, the story reader, and the
/// collectible's proximity behaviour.
///
/// The shared layer's own tests already prove the quest machine and the
/// sidecar. What these prove is the thing a player actually experiences:
/// that the tools appear when they should, that they never disappear for
/// somebody who had them, and that no amount of repeated input produces two
/// introductions.</summary>
public sealed class CartographerCompanionTests : IDisposable
{
    private readonly string _root;
    private readonly CompanionSidecarStore _store;

    public CartographerCompanionTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-companion-tests", Guid.NewGuid().ToString("N"));
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
            // A locked temp file must never fail the suite.
        }
    }

    private static CompanionScope Scope(long world = 7001, long character = 4242)
    {
        return new CompanionScope(
            CartographerCompanions.Product, new WorldId(world), new CharacterId(character));
    }

    private CompanionProgress Open(
        LegacyEvidence evidence = LegacyEvidence.None,
        bool toolsOnly = false,
        long world = 7001,
        long character = 4242)
    {
        return CompanionProgress.Open(
            _store, Scope(world, character), CartographerCompanions.BrokenCompass, evidence, toolsOnly);
    }

    // ------------------------------------------------------------------
    // Product registration
    // ------------------------------------------------------------------

    [Fact]
    public void Product_RegistersHulgiAndTheBrokenCompass()
    {
        var registry = new TheConcernedCat.Companions.Definitions.CompanionRegistry();

        TheConcernedCat.Companions.Definitions.RegistrationOutcome outcome =
            registry.Register(CartographerCompanions.Instance);

        Assert.Equal(
            TheConcernedCat.Companions.Definitions.RegistrationOutcome.Registered, outcome);
        Assert.Equal("concerned-cartographer", CartographerCompanions.Product.Value);
        Assert.Equal("hulgi", CartographerCompanions.Hulgi.Value);
        Assert.Equal("broken-compass", CartographerCompanions.BrokenCompass.Value);
        Assert.Equal(
            CartographerCompanions.BrokenCompass,
            CartographerCompanions.HulgiDefinition.IntroductionQuest);
    }

    [Fact]
    public void Product_RegisteringTwiceIsNotAnError()
    {
        var registry = new TheConcernedCat.Companions.Definitions.CompanionRegistry();
        registry.Register(CartographerCompanions.Instance);

        Assert.NotEqual(
            TheConcernedCat.Companions.Definitions.RegistrationOutcome.Registered,
            registry.Register(CartographerCompanions.Instance));
    }

    // ------------------------------------------------------------------
    // Legacy evidence — the upgrade path
    // ------------------------------------------------------------------

    [Fact]
    public void Evidence_WorldDataInThisWorldIsPresent()
    {
        Assert.Equal(
            LegacyEvidence.Present,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: true, anyWorldHasData: true, profileWideDataExists: false,
                configuredBeforeThisRelease: false, probeFailed: false)));
    }

    [Fact]
    public void Evidence_DataInAnotherWorldStillCountsAsAnExistingPlayer()
    {
        // The upgrade case that would otherwise bite: a long-time player
        // starting a brand new world must not be treated as a new player.
        Assert.Equal(
            LegacyEvidence.Present,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: true, profileWideDataExists: false,
                configuredBeforeThisRelease: false, probeFailed: false)));
    }

    [Fact]
    public void Evidence_ProfileLevelDataCounts()
    {
        Assert.Equal(
            LegacyEvidence.Present,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: false, profileWideDataExists: true,
                configuredBeforeThisRelease: false, probeFailed: false)));
    }

    [Fact]
    public void Evidence_AFailedProbeGrantsRatherThanLocks()
    {
        Assert.Equal(LegacyEvidence.Ambiguous, LegacyEvidenceRule.Evaluate(LegacyEvidenceFacts.Failed));
    }

    [Fact]
    public void Evidence_AConfigFileAloneIsAmbiguousNotAbsent()
    {
        Assert.Equal(
            LegacyEvidence.Ambiguous,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: false, profileWideDataExists: false,
                configuredBeforeThisRelease: true, probeFailed: false)));
    }

    [Fact]
    public void Evidence_OnlyAGenuinelyCleanInstallReportsNone()
    {
        Assert.Equal(LegacyEvidence.None, LegacyEvidenceRule.Evaluate(LegacyEvidenceFacts.None));
    }

    [Fact]
    public void Evidence_OurOwnFirstRunFilesAreNotEvidenceOfAnything()
    {
        // Observed in game: a brand new character on a brand new world in a
        // brand new profile was granted access as an EXISTING user, because
        // the plugin writes its starter survey rules during startup and the
        // probe then read that file back as a deliberate past action. The gate
        // #264 asks for could never engage for anybody.
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("survey-rules.tsv"));
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("author-id.txt"));
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("cartographer-strings-template.tsv"));
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("onboarding-shown.txt"));
        Assert.True(CartographerFirstRunFiles.IsSelfWritten(
            "concerned-cartographer.ffffffffef0f98ae.ffffffffbfeddb85.companions.tsv"));

        // Exactly the directory listing this build produced on its first run.
        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(new[]
        {
            "author-id.txt",
            "cartographer-strings-template.tsv",
            "onboarding-shown.txt",
            "survey-rules.tsv",
            "concerned-cartographer.ffffffffef0f98ae.ffffffffbfeddb85.companions.tsv",
        }));
    }

    [Fact]
    public void Evidence_AFileAPlayerHadToCreateStillCounts()
    {
        // The translator's override has a different name from the template
        // this build writes, and somebody has to make it.
        Assert.False(CartographerFirstRunFiles.IsSelfWritten("cartographer-strings.tsv"));
        Assert.False(CartographerFirstRunFiles.IsSelfWritten("views.tsv"));
        Assert.False(CartographerFirstRunFiles.IsSelfWritten("468215918.roads.tsv"));

        Assert.False(CartographerFirstRunFiles.IsOnlySelfWritten(new[]
        {
            "author-id.txt",
            "survey-rules.tsv",
            "468215918.roads.tsv",
        }));
    }

    [Fact]
    public void Evidence_AnEmptyDirectoryIsNotAHistoryButAnUnreadableOneIsNotProofEither()
    {
        // Nothing there means nothing there.
        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(new string[0]));

        // A listing we could not take, or a name we could not read, is not
        // evidence that a player is new. Every unknown in this rule resolves
        // towards granting: telling a new player the story a few minutes late
        // costs nothing, and telling a returning one they are new takes their
        // toolbar away.
        Assert.False(CartographerFirstRunFiles.IsOnlySelfWritten(null));
        Assert.False(CartographerFirstRunFiles.IsSelfWritten(null));
        Assert.False(CartographerFirstRunFiles.IsSelfWritten(""));
    }

    // ------------------------------------------------------------------
    // The gate
    // ------------------------------------------------------------------

    [Fact]
    public void Gate_EveryUnresolvedStateIsOpen()
    {
        foreach (CompanionFeatureGate gate in new[]
        {
            CompanionFeatureGate.Unresolved,
            CompanionFeatureGate.CompanionsDisabled,
            CompanionFeatureGate.NoScope,
        })
        {
            Assert.True(gate.IsUnlocked);
            foreach (CartographerFeature feature in Enum.GetValues(typeof(CartographerFeature)))
            {
                Assert.True(gate.Allows(feature));
            }
        }
    }

    [Fact]
    public void Gate_LockedStateLocksEveryAddedAffordance()
    {
        CompanionFeatureGate gate = CompanionFeatureGate.FromUnlock(false);

        Assert.False(gate.IsUnlocked);
        foreach (CartographerFeature feature in Enum.GetValues(typeof(CartographerFeature)))
        {
            Assert.False(gate.Allows(feature));
        }

        Assert.Equal(CompanionFeatureGate.GateReason.IntroductionPending, gate.Reason);
    }

    [Fact]
    public void Gate_AnUnrecognisedFeatureIsAllowedRatherThanRefused()
    {
        // A caller bug must never present as a player losing a feature.
        Assert.True(CompanionFeatureGate.FromUnlock(false).Allows((CartographerFeature)9999));
    }

    // ------------------------------------------------------------------
    // Fresh player, legacy player, tools-only
    // ------------------------------------------------------------------

    [Fact]
    public void FreshPlayer_IsLockedAndIsOfferedBothWaysOut()
    {
        CompanionProgress progress = Open();

        Assert.False(progress.IsUnlocked);
        Assert.True(progress.ShouldPresentCollectible);
        Assert.True(progress.Decision.ShouldOfferToolsOnly);
        Assert.False(CompanionFeatureGate.FromUnlock(progress.IsUnlocked).Allows(CartographerFeature.Atlas));
    }

    [Fact]
    public void LegacyPlayer_IsUnlockedImmediatelyAndStillMeetsHulgi()
    {
        CompanionProgress progress = Open(LegacyEvidence.Present);

        Assert.True(progress.IsUnlocked);
        Assert.Equal(UnlockReason.ExistingUserData, progress.Decision.Reason);

        // Unlocked is not the same as "story skipped": the introduction is
        // still offered, it just is not standing between them and their map.
        Assert.True(progress.ShouldPresentCollectible);
        Assert.True(CompanionFeatureGate.FromUnlock(progress.IsUnlocked).Allows(CartographerFeature.Routes));
    }

    [Fact]
    public void LegacyGrant_SurvivesTheEvidenceDisappearing()
    {
        Open(LegacyEvidence.Present);

        // Next session the probe finds nothing at all — the player deleted
        // their road data, or moved a folder. Their tools must not go with it.
        CompanionProgress later = Open(LegacyEvidence.None);

        Assert.True(later.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, later.Decision.Reason);
    }

    [Fact]
    public void ToolsOnly_UnlocksWithoutEverMeetingHulgi()
    {
        CompanionProgress progress = Open(LegacyEvidence.None, toolsOnly: true);

        Assert.True(progress.IsUnlocked);
        Assert.False(progress.ShouldPresentCollectible);
        Assert.Equal(QuestState.Unstarted, progress.QuestState);
    }

    [Fact]
    public void ToolsOnly_TurnedOffAgainRestoresTheStoryButNotTheLock()
    {
        CompanionProgress progress = Open();
        progress.SetToolsOnlyPreference(true);
        Assert.True(progress.IsUnlocked);

        progress.SetToolsOnlyPreference(false);

        Assert.True(progress.IsUnlocked);
        Assert.True(progress.ShouldPresentCollectible);
    }

    // ------------------------------------------------------------------
    // The introduction itself
    // ------------------------------------------------------------------

    [Fact]
    public void Examining_RecordsEveryStageItPassedThrough()
    {
        CompanionProgress progress = Open();

        foreach (QuestTransition transition in IntroductionSequence.OnExamine)
        {
            progress.Advance(transition);
        }

        Assert.Equal(QuestState.IntroPending, progress.QuestState);
        Assert.False(progress.HasUnsavedChanges);
        Assert.False(progress.IsUnlocked);
    }

    [Fact]
    public void ExaminingTwice_ChangesNothingTheSecondTime()
    {
        CompanionProgress progress = Open();
        ApplyAll(progress, IntroductionSequence.OnExamine);
        QuestState after = progress.QuestState;

        ApplyAll(progress, IntroductionSequence.OnExamine);

        Assert.Equal(after, progress.QuestState);
        Assert.False(progress.IsUnlocked);
    }

    [Fact]
    public void Welcoming_UnlocksSavesAndOnlyThenRetiresTheCollectible()
    {
        CompanionProgress progress = Open();
        ApplyAll(progress, IntroductionSequence.OnExamine);

        ApplyAll(progress, IntroductionSequence.OnWelcome);

        Assert.True(progress.IsUnlocked);
        Assert.True(progress.HasCompanion);
        Assert.False(progress.HasUnsavedChanges);
        Assert.False(progress.ShouldPresentCollectible);

        Assert.True(progress.TryRetirePresentation());
        Assert.True(progress.PresentationRetired);

        // Retiring is a one-time event, so a second call is a no-op rather
        // than a second removal.
        Assert.False(progress.TryRetirePresentation());
    }

    [Fact]
    public void WelcomingTwice_ProducesOneRecruitment()
    {
        CompanionProgress progress = Open();
        ApplyAll(progress, IntroductionSequence.OnExamine);

        ApplyAll(progress, IntroductionSequence.OnWelcome);
        ApplyAll(progress, IntroductionSequence.OnWelcome);

        Assert.Equal(QuestState.Recruited, progress.QuestState);
    }

    [Fact]
    public void SkippingFromTheStory_CompletesWithoutRecruiting()
    {
        CompanionProgress progress = Open();
        ApplyAll(progress, IntroductionSequence.OnExamine);

        ApplyAll(progress, IntroductionSequence.OnSkip);

        Assert.True(progress.IsUnlocked);
        Assert.False(progress.HasCompanion);
        Assert.True(progress.CanReplayStory);
    }

    [Fact]
    public void InterruptedIntroduction_ResumesAfterAReload()
    {
        CompanionProgress first = Open();
        ApplyAll(first, IntroductionSequence.OnExamine);

        // The player closed the panel and quit. Nothing was decided.
        CompanionProgress second = Open();

        Assert.Equal(QuestState.IntroPending, second.QuestState);
        Assert.False(second.IsUnlocked);
        Assert.True(second.ShouldPresentCollectible);

        ApplyAll(second, IntroductionSequence.OnWelcome);
        Assert.True(second.IsUnlocked);
    }

    [Fact]
    public void Recruitment_SurvivesARelog()
    {
        CompanionProgress first = Open();
        ApplyAll(first, IntroductionSequence.OnExamine);
        ApplyAll(first, IntroductionSequence.OnWelcome);
        first.TryRetirePresentation();

        CompanionProgress second = Open();

        Assert.True(second.IsUnlocked);
        Assert.True(second.HasCompanion);
        Assert.True(second.PresentationRetired);
        Assert.False(second.ShouldPresentCollectible);
    }

    [Fact]
    public void ADifferentCharacterInTheSameWorld_StartsItsOwnIntroduction()
    {
        CompanionProgress first = Open();
        ApplyAll(first, IntroductionSequence.OnExamine);
        ApplyAll(first, IntroductionSequence.OnWelcome);

        CompanionProgress other = Open(character: 9999);

        Assert.Equal(QuestState.Unstarted, other.QuestState);
        Assert.False(other.IsUnlocked);
    }

    [Fact]
    public void CorruptCompanionData_GrantsAccessAndExplainsItself()
    {
        CompanionScope scope = Scope();
        string path = _store.ResolvePath(scope);
        File.WriteAllText(path, "this is not a companion sidecar\nnor is this\n");

        CompanionProgress progress = Open();

        Assert.True(progress.IsUnlocked);
        Assert.NotNull(progress.Notice);
        Assert.NotEqual(SidecarLoadOutcome.Loaded, progress.LoadOutcome);

        // And the grant is durable: the quarantined file is gone next session,
        // so the reason for granting has to already be written down.
        CompanionProgress later = Open();
        Assert.True(later.IsUnlocked);
        Assert.Equal(UnlockReason.PreviouslyGranted, later.Decision.Reason);
    }

    // ------------------------------------------------------------------
    // The story reader
    // ------------------------------------------------------------------

    [Fact]
    public void Story_HasFourPagesAndEveryPageNamesItsKeys()
    {
        Assert.Equal(4, CompanionStory.PageCount);
        foreach (StoryPage page in CompanionStory.Pages)
        {
            Assert.False(string.IsNullOrWhiteSpace(page.SpeakerKey));
            Assert.False(string.IsNullOrWhiteSpace(page.BodyKey));
        }
    }

    [Fact]
    public void Reader_ClampsAtBothEndsUnderRepeatedInput()
    {
        var reader = new StoryReader();

        for (int i = 0; i < 50; i++)
        {
            reader.Back();
        }

        Assert.Equal(0, reader.PageIndex);
        Assert.True(reader.IsFirstPage);

        for (int i = 0; i < 50; i++)
        {
            reader.Next();
        }

        Assert.Equal(CompanionStory.PageCount - 1, reader.PageIndex);
        Assert.True(reader.IsLastPage);
    }

    [Fact]
    public void Reader_NextReportsWhetherItActuallyTurnedAPage()
    {
        var reader = new StoryReader(2);

        Assert.True(reader.Next());
        Assert.False(reader.Next());
    }

    [Fact]
    public void Reader_ResumeIsClampedBothWays()
    {
        var reader = new StoryReader();

        reader.Resume(-5);
        Assert.Equal(0, reader.PageIndex);

        reader.Resume(999);
        Assert.Equal(CompanionStory.PageCount - 1, reader.PageIndex);
    }

    [Fact]
    public void Reader_PageNumberIsOneBasedForDisplay()
    {
        var reader = new StoryReader();
        Assert.Equal(1, reader.PageNumber);
        reader.Next();
        Assert.Equal(2, reader.PageNumber);
    }

    // ------------------------------------------------------------------
    // Proximity
    // ------------------------------------------------------------------

    [Fact]
    public void Proximity_ReportsNoticedThenInReachAsThePlayerApproaches()
    {
        var proximity = new CompassProximity();

        Assert.Equal(CompassSignal.Idle, proximity.Update(40f));
        Assert.Equal(CompassSignal.Noticed, proximity.Update(10f));
        Assert.Equal(CompassSignal.InReach, proximity.Update(3f));
    }

    [Fact]
    public void Proximity_DoesNotStrobeAtTheBoundary()
    {
        var proximity = new CompassProximity(reachRadius: 4f, hysteresis: 1f);

        Assert.Equal(CompassSignal.InReach, proximity.Update(3.9f));

        // Drifting just past the entry radius keeps the prompt: leaving needs
        // the wider exit radius.
        Assert.Equal(CompassSignal.InReach, proximity.Update(4.5f));
        Assert.NotEqual(CompassSignal.InReach, proximity.Update(6f));
    }

    [Fact]
    public void Proximity_AnUnmeasurableDistanceIsOutOfRangeNotZero()
    {
        var proximity = new CompassProximity();

        Assert.Equal(CompassSignal.Idle, proximity.Update(-1f));
        Assert.Equal(CompassSignal.Idle, proximity.Update(float.NaN));
    }

    [Fact]
    public void Proximity_ResetForgetsThePreviousPlacement()
    {
        var proximity = new CompassProximity();
        proximity.Update(2f);
        Assert.True(proximity.HasNoticed);

        proximity.Reset();

        Assert.False(proximity.HasNoticed);
    }

    private static void ApplyAll(CompanionProgress progress, QuestTransition[] transitions)
    {
        foreach (QuestTransition transition in transitions)
        {
            progress.Advance(transition);
        }
    }
}
