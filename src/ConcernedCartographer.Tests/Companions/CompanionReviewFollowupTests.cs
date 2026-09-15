using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>Regressions for the independent review of the merged foundation
/// and of CC-NPC-003. Each test is named for the finding it pins, so a
/// successor can trace a behaviour back to the argument for it.</summary>
public sealed class CompanionReviewFollowupTests : IDisposable
{
    private readonly string _root;
    private readonly CompanionSidecarStore _store;

    private static readonly QuestId Introduction = new("introduction");

    public CompanionReviewFollowupTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-companion-followup", Guid.NewGuid().ToString("N"));
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

    private static CompanionScope Scope(string product, long world, long character)
    {
        return new CompanionScope(
            new ProductId(product), new WorldId(world), new CharacterId(character));
    }

    private void Seed(CompanionScope scope)
    {
        var sidecar = new CompanionSidecar(scope);
        sidecar.Apply(Introduction, QuestTransition.Discover);
        Assert.True(_store.Save(sidecar).Saved);
    }

    // ------------------------------------------------------------------
    // F1 — a shared root must not let one product inherit another's history
    // ------------------------------------------------------------------

    [Fact]
    public void F1_HasSidecarFor_IgnoresOtherProductsWorldsAndCharacters()
    {
        CompanionScope mine = Scope("alpha", world: 1, character: 1);

        Seed(Scope("beta", world: 1, character: 1));    // another product
        Seed(Scope("alpha", world: 2, character: 1));   // another world
        Seed(Scope("alpha", world: 1, character: 2));   // another character

        // Three files exist. None of them is this character's.
        Assert.Equal(3, _store.ListSidecarFiles().Count);
        Assert.False(_store.HasSidecarFor(mine));

        Seed(mine);
        Assert.True(_store.HasSidecarFor(mine));
    }

    [Fact]
    public void F1_ProductScopedListing_ExcludesOtherProducts()
    {
        Seed(Scope("alpha", world: 1, character: 1));
        Seed(Scope("alpha", world: 2, character: 1));
        Seed(Scope("beta", world: 1, character: 1));

        Assert.Equal(2, _store.ListSidecarFiles(new ProductId("alpha")).Count);
        Assert.Single(_store.ListSidecarFiles(new ProductId("beta")));
        Assert.Empty(_store.ListSidecarFiles(default));
    }

    [Fact]
    public void F1_AProductWhoseSlugPrefixesAnotherIsNotConfusedForIt()
    {
        // "alpha" must not swallow "alpha-two": the separator has to be part
        // of the comparison, not an afterthought.
        Seed(Scope("alpha", world: 1, character: 1));
        Seed(Scope("alpha-two", world: 1, character: 1));

        Assert.Single(_store.ListSidecarFiles(new ProductId("alpha")));
        Assert.Single(_store.ListSidecarFiles(new ProductId("alpha-two")));
    }

    // ------------------------------------------------------------------
    // F8 — a save failure that a later save disproves must stop being shown
    // ------------------------------------------------------------------

    [Fact]
    public void F8_ASuccessfulSaveClearsAStaleSaveFailureNotice()
    {
        string blockedRoot = Path.Combine(_root, "blocked");

        // A FILE where the store wants a directory: every write fails, and
        // nothing about it is specific to one operating system's permissions.
        File.WriteAllText(blockedRoot, "not a directory");

        var store = new CompanionSidecarStore(blockedRoot);
        CompanionProgress progress = CompanionProgress.Open(
            store, Scope("alpha", 1, 1), Introduction);

        progress.Advance(QuestTransition.Discover);
        Assert.True(progress.HasUnsavedChanges);
        Assert.NotNull(progress.Notice);

        // Clear the obstruction; the next transition writes for real.
        File.Delete(blockedRoot);
        progress.Advance(QuestTransition.Collect);

        Assert.False(progress.HasUnsavedChanges);
        Assert.Null(progress.Notice);
    }

    [Fact]
    public void F8_ALoadNoticeSurvivesASuccessfulSave()
    {
        // A load notice describes the file the player still has. Writing
        // successfully afterwards does not make it untrue, so it must stay.
        CompanionScope scope = Scope("alpha", 1, 1);
        File.WriteAllText(_store.ResolvePath(scope), "garbage\nmore garbage\n");

        CompanionProgress progress = CompanionProgress.Open(_store, scope, Introduction);
        string? loadNotice = progress.Notice;
        Assert.NotNull(loadNotice);

        progress.Advance(QuestTransition.Discover);

        Assert.False(progress.HasUnsavedChanges);
        Assert.Equal(loadNotice, progress.Notice);
    }

    // ------------------------------------------------------------------
    // F7 — "Advanced" means in memory, not on disk
    // ------------------------------------------------------------------

    [Fact]
    public void F7_AdvanceReportsAdvancedEvenWhenTheWriteFailed_SoCallersCheckHasUnsavedChanges()
    {
        string blockedRoot = Path.Combine(_root, "blocked-advance");
        File.WriteAllText(blockedRoot, "not a directory");

        var store = new CompanionSidecarStore(blockedRoot);
        CompanionProgress progress = CompanionProgress.Open(
            store, Scope("alpha", 1, 1), Introduction);

        Assert.Equal(
            QuestTransitionOutcome.Advanced, progress.Advance(QuestTransition.Welcome));

        // The outcome says "advanced" and the disk says nothing of the sort.
        // HasUnsavedChanges is the signal a caller must consult before firing a
        // one-time side effect...
        Assert.True(progress.HasUnsavedChanges);

        // ...and the layer's own one-time side effect refuses on exactly that.
        Assert.False(progress.TryRetirePresentation());
    }

    // ------------------------------------------------------------------
    // F4 — FireComfortRadius must actually change the outcome
    // ------------------------------------------------------------------

    [Fact]
    public void F4_WideningTheComfortRadiusChangesTheChosenPose()
    {
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(0f, 30f, 0f));

        // A probe that reports warmth only inside the radius it was given —
        // the shape the vanilla API forces, and the one the adapter now uses.
        var cold = new RadiusAwareProbe(warmthRadius: 1f);
        var warm = new RadiusAwareProbe(warmthRadius: 20f);

        PlacementResult coldResult =
            new PlacementPlanner(new PlacementRules(fireComfortRadius: 1f)).Plan(anchor, cold);
        PlacementResult warmResult =
            new PlacementPlanner(new PlacementRules(fireComfortRadius: 20f)).Plan(anchor, warm);

        Assert.True(coldResult.Found);
        Assert.True(warmResult.Found);
        Assert.Equal(CompanionPose.SitOnGround, coldResult.Pose);
        Assert.Equal(CompanionPose.SitByFire, warmResult.Pose);
    }

    /// <summary>Stands in for the vanilla warmth call: a fire sits 8 m from the
    /// anchor, and the probe only reports it when asked with a radius that
    /// reaches it.</summary>
    private sealed class RadiusAwareProbe : IPlacementProbe
    {
        private static readonly WorldPoint Fire = new WorldPoint(8f, 30f, 0f);

        private readonly float _warmthRadius;

        public RadiusAwareProbe(float warmthRadius)
        {
            _warmthRadius = warmthRadius;
        }

        public PlacementProbeSample Probe(WorldPoint position)
        {
            bool warm = position.HorizontalDistanceTo(Fire) <= _warmthRadius;
            return new PlacementProbeSample(
                position, PlacementRejection.None, warm ? 0f : -1f, SeatAvailability.None);
        }
    }
}
