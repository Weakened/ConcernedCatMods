using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;
using TheConcernedCat.Companions.Unlock;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>Regressions for defects an independent review found in the first
/// cut of the shared companion layer. Each test names the failure it prevents
/// rather than the method it calls.</summary>
public sealed class CompanionReviewRegressionTests : IDisposable
{
    private readonly string _root;
    private readonly CompanionSidecarStore _store;

    private static readonly QuestId FirstQuest = new("introduction");
    private static readonly QuestId SecondQuest = new("kitchen");

    public CompanionReviewRegressionTests()
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

    private static CompanionScope Scope(long world = 1, long character = 1)
    {
        return new CompanionScope(
            new ProductId("synthetic-alpha"), new WorldId(world), new CharacterId(character));
    }

    // ---------------------------------------------------------------- unlock

    [Fact]
    public void AddingASecondCompanionDoesNotWithdrawAccessEarnedByTheFirst()
    {
        // A product ships one companion; the player recruits them. A later
        // version adds a second companion with its own quest. Opening a session
        // for that untouched quest must not conclude the player is brand new.
        CompanionScope scope = Scope();
        CompanionProgress first = CompanionProgress.Open(_store, scope, FirstQuest);
        first.Advance(QuestTransition.Welcome);

        CompanionProgress second = CompanionProgress.Open(_store, scope, SecondQuest);

        Assert.True(second.IsUnlocked);
        Assert.Equal(QuestState.Unstarted, second.QuestState);
    }

    [Fact]
    public void ASecondCompanionIsStillIntroducedEvenThoughAccessIsAlreadyGranted()
    {
        // Access is per scope; presentation stays per quest.
        CompanionScope scope = Scope();
        CompanionProgress.Open(_store, scope, FirstQuest).Advance(QuestTransition.Welcome);

        CompanionProgress second = CompanionProgress.Open(_store, scope, SecondQuest);

        Assert.True(second.ShouldPresentCollectible);
        Assert.True(second.Decision.ShouldPresentIntroduction);
        Assert.True(second.Decision.ShouldOfferToolsOnly);
        Assert.False(second.HasCompanion);
    }

    [Fact]
    public void FinishingOneQuestDoesNotMarkAnotherQuestFinished()
    {
        CompanionScope scope = Scope();
        CompanionProgress.Open(_store, scope, FirstQuest).Advance(QuestTransition.Welcome);

        CompanionProgress second = CompanionProgress.Open(_store, scope, SecondQuest);
        Assert.False(second.CanReplayStory);
        Assert.Equal(
            QuestTransitionOutcome.Advanced, second.Advance(QuestTransition.Welcome));
    }

    // ------------------------------------------------------- tools-only flip

    [Fact]
    public void TryingToolsOnlyAndTurningItBackOffRestoresTheStory()
    {
        // Enabling the preference must not permanently finish the quest: the
        // same setting at the same final value has to produce the same result
        // whether it was flipped mid-session or set before the session opened.
        CompanionScope scope = Scope();
        CompanionProgress progress = CompanionProgress.Open(_store, scope, FirstQuest);

        progress.SetToolsOnlyPreference(true);
        Assert.True(progress.IsUnlocked);
        Assert.False(progress.ShouldPresentCollectible);

        progress.SetToolsOnlyPreference(false);

        Assert.True(progress.IsUnlocked);
        Assert.True(progress.ShouldPresentCollectible);
        Assert.NotEqual(QuestState.Skipped, progress.QuestState);
    }

    [Fact]
    public void FlippingThePreferenceMatchesHavingItSetAtOpenTime()
    {
        CompanionScope flipped = Scope(world: 1);
        CompanionScope atOpen = Scope(world: 2);

        CompanionProgress a = CompanionProgress.Open(_store, flipped, FirstQuest);
        a.SetToolsOnlyPreference(true);
        a.SetToolsOnlyPreference(false);

        CompanionProgress b = CompanionProgress.Open(
            _store, atOpen, FirstQuest, toolsOnlyPreference: true);
        b.SetToolsOnlyPreference(false);

        Assert.Equal(b.QuestState, a.QuestState);
        Assert.Equal(b.ShouldPresentCollectible, a.ShouldPresentCollectible);
        Assert.Equal(b.IsUnlocked, a.IsUnlocked);
    }

    [Fact]
    public void ExplicitlySkippingTheIntroductionStillFinishesIt()
    {
        // The preference no longer completes the quest, so the explicit skip
        // action from the story UI must still do it.
        CompanionProgress progress = CompanionProgress.Open(_store, Scope(), FirstQuest);

        Assert.Equal(QuestTransitionOutcome.Advanced, progress.Advance(QuestTransition.Skip));
        Assert.Equal(QuestState.Skipped, progress.QuestState);
        Assert.True(progress.IsUnlocked);
        Assert.False(progress.ShouldPresentCollectible);
    }

    // -------------------------------------------------------- forward stages

    [Fact]
    public void AnOlderBuildCannotOverwriteAQuestStageItDoesNotUnderstand()
    {
        // A newer build records a stage this one has never heard of. This build
        // must not write its own row for the same quest, because the newer
        // build would read that row first and adopt it, losing the progress.
        CompanionScope scope = Scope();
        var seeded = new CompanionSidecar(scope);
        seeded.Apply(FirstQuest, QuestTransition.Discover);
        _store.Save(seeded);

        string path = _store.ResolvePath(scope);
        File.AppendAllLines(path, new[] { "q\tintroduction\t99\t12\t1" });
        string before = File.ReadAllText(path);

        CompanionProgress progress = CompanionProgress.Open(_store, scope, FirstQuest);
        Assert.True(progress.IsUnlocked);

        progress.Advance(QuestTransition.Welcome);
        progress.TryRetirePresentation();

        Assert.Equal(before, File.ReadAllText(path));
        Assert.NotNull(progress.Notice);
    }

    [Fact]
    public void AForwardQuestStageMakesTheWholeFileReadOnly()
    {
        CompanionScope scope = Scope();
        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(
            new[]
            {
                "# ConcernedCatMods companions v1",
                "s\t1",
                "k\tsynthetic-alpha\t" + scope.World.ToStorageToken() + "\t" + scope.Character.ToStorageToken(),
                "q\tintroduction\t99\t3\t1",
            },
            scope);

        Assert.True(parsed.Sidecar.IsReadOnly);
        Assert.True(parsed.Sidecar.HasForwardData);
        Assert.False(_store.Save(parsed.Sidecar, force: true).Saved);
    }

    // ------------------------------------------------------- duplicate rows

    [Fact]
    public void ADuplicateQuestRowIsCarriedRatherThanDiscarded()
    {
        CompanionScope scope = Scope();
        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(
            new[]
            {
                "s\t1",
                "k\tsynthetic-alpha\t" + scope.World.ToStorageToken() + "\t" + scope.Character.ToStorageToken(),
                "q\tintroduction\t4\t2\t0",
                "q\tintroduction\t1\t1\t0",
            },
            scope);

        Assert.Equal(1, parsed.SkippedRows);
        Assert.Contains("q\tintroduction\t1\t1\t0", parsed.Sidecar.QuarantinedLines);
        Assert.True(parsed.Sidecar.TryGetQuest(FirstQuest, out CompanionQuestRecord record));
        Assert.Equal(QuestState.Recruited, record.State);
    }

    [Fact]
    public void ACarriedDuplicateIsNotRecountedAsFreshDamageOnEveryLoad()
    {
        // #292. The row survives a load, gets re-emitted on save, and is read
        // back next session. Counting it again there makes a permanent false
        // alarm out of the one notice that is supposed to be actionable.
        CompanionScope scope = Scope();
        string[] original =
        {
            "s\t1",
            "k\tsynthetic-alpha\t" + scope.World.ToStorageToken() + "\t" + scope.Character.ToStorageToken(),
            "q\tintroduction\t4\t2\t0",
            "q\tintroduction\t1\t1\t0",
        };

        CompanionSidecarCodec.ParseResult first = CompanionSidecarCodec.Parse(original, scope);
        Assert.Equal(1, first.SkippedRows);
        Assert.Equal(SidecarLoadOutcome.LoadedWithSkippedRows, first.Outcome);

        // A load that only carries rows advances nothing, so without an
        // explicit request for a rewrite the marker would never reach the file.
        Assert.True(first.Sidecar.IsDirty);

        var written = new List<string>(CompanionSidecarCodec.Serialize(first.Sidecar));
        CompanionSidecarCodec.ParseResult second = CompanionSidecarCodec.Parse(written, scope);

        Assert.Equal(0, second.SkippedRows);
        Assert.Equal(SidecarLoadOutcome.Loaded, second.Outcome);
        Assert.True(second.Sidecar.HasPreviouslyCarriedDamage);

        // Nothing was dropped: the loser is still there, byte for byte.
        Assert.Contains("q\tintroduction\t1\t1\t0", second.Sidecar.QuarantinedLines);

        // And the winner is still the winner.
        Assert.True(second.Sidecar.TryGetQuest(FirstQuest, out CompanionQuestRecord kept));
        Assert.Equal(QuestState.Recruited, kept.State);

        // Stable across further sessions: no growth, no re-marking, no notice.
        var again = new List<string>(CompanionSidecarCodec.Serialize(second.Sidecar));
        CompanionSidecarCodec.ParseResult third = CompanionSidecarCodec.Parse(again, scope);
        Assert.Equal(0, third.SkippedRows);
        Assert.Equal(written.Count, again.Count);
        Assert.Equal(written, again);
        Assert.Single(third.Sidecar.QuarantinedLines);
    }

    [Fact]
    public void CarriedDamageIsNeverMistakenForANewerBuildsData()
    {
        // A quarantined row must not become evidence. Garbage that unlocked
        // features would be a worse bug than the one #292 describes.
        CompanionScope scope = Scope();
        CompanionSidecarCodec.ParseResult first = CompanionSidecarCodec.Parse(
            new[]
            {
                "s\t1",
                "k\tsynthetic-alpha\t" + scope.World.ToStorageToken() + "\t" + scope.Character.ToStorageToken(),
                "q\tbroken\tnot-a-state\tnope\t?",
            },
            scope);

        var written = new List<string>(CompanionSidecarCodec.Serialize(first.Sidecar));
        CompanionSidecarCodec.ParseResult second = CompanionSidecarCodec.Parse(written, scope);

        Assert.False(second.Sidecar.HasForwardData);
        Assert.Equal(UnlockReason.NotUnlocked, second.Sidecar.GrantedReason);
        Assert.Equal(0, second.SkippedRows);
        Assert.Contains("q\tbroken\tnot-a-state\tnope\t?", second.Sidecar.QuarantinedLines);
    }

    [Fact]
    public void RealDamageFoundAfterACarriedRowIsStillReported()
    {
        // The fix must silence only the rows this build already carried. A
        // file that acquires NEW damage later has to say so.
        CompanionScope scope = Scope();
        CompanionSidecarCodec.ParseResult first = CompanionSidecarCodec.Parse(
            new[]
            {
                "s\t1",
                "k\tsynthetic-alpha\t" + scope.World.ToStorageToken() + "\t" + scope.Character.ToStorageToken(),
                "q\tintroduction\t4\t2\t0",
                "q\tintroduction\t1\t1\t0",
            },
            scope);

        var written = new List<string>(CompanionSidecarCodec.Serialize(first.Sidecar));
        written.Add("q\tsecond\tmangled\tby\tsomething");

        CompanionSidecarCodec.ParseResult second = CompanionSidecarCodec.Parse(written, scope);

        Assert.Equal(1, second.SkippedRows);
        Assert.Equal(SidecarLoadOutcome.LoadedWithSkippedRows, second.Outcome);
        Assert.Equal(2, second.Sidecar.QuarantinedLines.Count);
    }

    [Fact]
    public void AReadOnlyFileIsNotRewrittenJustToTidyCarriedDamage()
    {
        // Not overwriting a newer build's file outranks our own bookkeeping,
        // even at the cost of the notice recurring there.
        CompanionScope scope = Scope();
        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(
            new[]
            {
                "s\t1",
                "k\tsynthetic-alpha\t" + scope.World.ToStorageToken() + "\t" + scope.Character.ToStorageToken(),
                "q\tintroduction\t99\t3\t0",
                "q\tbroken\tnot-a-state\tnope\t?",
            },
            scope);

        Assert.True(parsed.Sidecar.IsReadOnly);
        Assert.False(parsed.Sidecar.IsDirty);
        Assert.False(_store.Save(parsed.Sidecar, force: true).Saved);
    }

    [Fact]
    public void AppendingAMatchingScopeRowCannotLaunderAnotherCharactersFile()
    {
        // Every scope row has to match, not just the last one seen.
        CompanionScope mine = Scope(world: 1, character: 1);
        CompanionScope theirs = Scope(world: 1, character: 2);

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(
            new[]
            {
                "s\t1",
                "k\tsynthetic-alpha\t" + theirs.World.ToStorageToken() + "\t" + theirs.Character.ToStorageToken(),
                "q\tintroduction\t4\t2\t0",
                "k\tsynthetic-alpha\t" + mine.World.ToStorageToken() + "\t" + mine.Character.ToStorageToken(),
            },
            mine);

        Assert.Equal(SidecarLoadOutcome.ScopeMismatch, parsed.Outcome);
    }

    // ------------------------------------------------------------ quarantine

    [Fact]
    public void AnUnreadableFileThatCannotBeMovedAsideIsNotOverwrittenLater()
    {
        // The notice promises the old file was kept. Leaving the sidecar
        // writable would let the very next save destroy it.
        CompanionScope scope = Scope();
        string path = _store.ResolvePath(scope);
        File.WriteAllText(path, "unreadable");

        // Occupy every quarantine name the store will try.
        for (int attempt = 0; attempt < 21; attempt++)
        {
            string taken = attempt == 0
                ? path + ".corrupt"
                : path + ".corrupt." + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(taken, "already here");
        }

        CompanionSidecarStore.LoadReport report = _store.Load(scope);

        Assert.Equal(SidecarLoadOutcome.Corrupt, report.Outcome);
        Assert.Null(report.QuarantinePath);
        Assert.True(report.Sidecar.IsReadOnly);
        Assert.NotNull(report.Notice);

        CompanionProgress progress = CompanionProgress.Open(_store, scope, FirstQuest);
        Assert.True(progress.IsUnlocked);
        progress.Advance(QuestTransition.Welcome);

        Assert.Equal("unreadable", File.ReadAllText(path).Trim());
    }

    // --------------------------------------------------------- retire result

    [Fact]
    public void RetiringThePresentationReportsFalseWhenNothingReachedDisk()
    {
        CompanionScope scope = Scope();
        CompanionProgress progress = CompanionProgress.Open(_store, scope, FirstQuest);
        progress.Advance(QuestTransition.Welcome);

        // Make the sidecar unwritable the way a newer-schema file does.
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

        CompanionProgress reopened = CompanionProgress.Open(_store, scope, FirstQuest);
        Assert.True(reopened.IsUnlocked);
        Assert.False(reopened.TryRetirePresentation());
    }

    // ------------------------------------------------------------- placement

    [Fact]
    public void APartiallyStreamedWorldDefersInsteadOfPlacingFromWhatLoaded()
    {
        // Otherwise the chosen spot depends on how far streaming got, and the
        // companion moves once the rest of the world arrives.
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(0f, 30f, 0f));
        var probe = new PartialProbe();

        PlacementResult result = new PlacementPlanner().Plan(anchor, probe);

        Assert.False(result.Found);
        Assert.True(result.BlockedBy.HasFlag(PlacementRejection.NotLoaded));
    }

    [Fact]
    public void AFullyLoadedWorldStillPlaces()
    {
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(0f, 30f, 0f));
        var probe = new AllClearProbe();

        Assert.True(new PlacementPlanner().Plan(anchor, probe).Found);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1234.5f, -987.25f)]
    [InlineData(9500.125f, -9500.125f)]
    public void TheSweepDoesNotRejectItsOwnCandidatesAtFarWorldCoordinates(float x, float z)
    {
        // Ring candidates sit exactly on the band edges, and recovering the
        // distance in float at world coordinates in the thousands loses enough
        // precision to push them a hair outside. Every candidate must survive.
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(x, 30f, z));
        var probe = new AllClearProbe();

        PlacementResult result = new PlacementPlanner().Plan(anchor, probe);

        Assert.True(result.Found);
        Assert.False(result.BlockedBy.HasFlag(PlacementRejection.OutOfRange));
    }

    [Fact]
    public void ACollapsedBandDoesNotRejectItsOwnCandidates()
    {
        var rules = new PlacementRules(minimumRadius: 7f, maximumRadius: 7f);
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(4000f, 30f, 4000f));

        PlacementResult result = new PlacementPlanner(rules).Plan(anchor, new AllClearProbe());

        Assert.True(result.Found);
        Assert.False(result.BlockedBy.HasFlag(PlacementRejection.OutOfRange));
    }

    [Theory]
    [InlineData(float.NaN, 10f)]
    [InlineData(3f, float.NaN)]
    [InlineData(3f, float.PositiveInfinity)]
    public void NonFiniteBoundsAreRejectedRatherThanDisablingTheBandSilently(
        float minimum, float maximum)
    {
        // Every comparison is false for NaN, so a NaN that survived validation
        // would make the planner's own range checks pass for everything.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlacementRules(minimum, maximum));
    }

    [Fact]
    public void NegativeSecondaryBoundsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlacementRules(fireComfortRadius: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlacementRules(maximumHeightDelta: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlacementRules(anchorMoveTolerance: -1f));
    }

    private sealed class AllClearProbe : IPlacementProbe
    {
        public PlacementProbeSample Probe(WorldPoint position)
        {
            return new PlacementProbeSample(
                position, PlacementRejection.None, -1f, SeatAvailability.None);
        }
    }

    /// <summary>Most of the sweep is still streaming; a few spots near the
    /// player are already usable.</summary>
    private sealed class PartialProbe : IPlacementProbe
    {
        private int _calls;

        public PlacementProbeSample Probe(WorldPoint position)
        {
            _calls++;
            return _calls <= 3
                ? new PlacementProbeSample(position, PlacementRejection.None, -1f, SeatAvailability.None)
                : PlacementProbeSample.NotLoaded(position);
        }
    }
}
