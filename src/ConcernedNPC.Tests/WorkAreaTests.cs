using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Helpers every work-area test wants, so no test invents an id or a
/// shape twice.</summary>
internal static class Areas
{
    /// <summary>A provider id, chosen by the "role" that registers it - never by
    /// the package. Spelled here so the tests read the way a role's start-up
    /// would.</summary>
    internal const string CircleProvider = "a-role/circle";

    internal const string PolygonProvider = "a-map-mod/freehand";

    internal static NpcCircleWorkArea Circle(float x, float z, float radius, string describe = "the harvest area")
    {
        Assert.True(
            NpcCircleWorkArea.TryCreate(
                new NpcPoint(x, 0f, z), radius, describe, out NpcCircleWorkArea? area, out string reason),
            reason);
        return area!;
    }

    internal static NpcWorkAreaDescriptor CircleDescriptor(
        float x, float y, float z, float radius, string key = "harvest", string describe = "the harvest area") =>
        new NpcWorkAreaDescriptor(
            new NpcWorkAreaId(CircleProvider, key),
            NpcCircleAreaProvider.Shape(new NpcPoint(x, y, z), radius),
            describe);

    internal static NpcWorkAreaRegistry WithCircle()
    {
        var registry = new NpcWorkAreaRegistry();
        Assert.Equal(ProviderRegistration.Registered, registry.Register(new NpcCircleAreaProvider(CircleProvider)));
        return registry;
    }
}

public class CircleWorkAreaTests
{
    [Fact]
    public void AnAreaIsEstablishedFromCoordinatesAloneAndNeverFromWhereAnybodyIsStanding()
    {
        // The first of the two failures this leaf exists to prevent: making a
        // player walk somewhere to mark an area. Nothing in the construction
        // path reads a position from the world, so an area five kilometres away
        // is built exactly as easily as one underfoot - which is what lets a map
        // mod draw one on a map.
        NpcCircleWorkArea faraway = Areas.Circle(5000f, -5000f, 24f);

        Assert.True(faraway.Contains(new NpcPoint(5010f, 200f, -5000f)));
        Assert.False(faraway.Contains(new NpcPoint(0f, 0f, 0f)));
    }

    [Fact]
    public void ContainmentIsHorizontalAndInclusiveAtTheEdge()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 10f);

        Assert.True(area.Contains(new NpcPoint(0f, 400f, 0f)));
        Assert.True(area.Contains(new NpcPoint(10f, 0f, 0f)));
        Assert.False(area.Contains(new NpcPoint(10.01f, 0f, 0f)));
        Assert.False(area.Contains(new NpcPoint(float.NaN, 0f, 0f)));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ACircleRefusesARadiusThatIsNotABoundedNumber(float radius)
    {
        Assert.False(
            NpcCircleWorkArea.TryCreate(new NpcPoint(0f, 0f, 0f), radius, "x", out NpcCircleWorkArea? area, out string reason));
        Assert.Null(area);
        Assert.NotEqual(string.Empty, reason);
    }

    [Fact]
    public void ACircleRefusesACentreThatIsNotAPosition()
    {
        Assert.False(
            NpcCircleWorkArea.TryCreate(
                new NpcPoint(float.NaN, 0f, 0f), 10f, "x", out NpcCircleWorkArea? area, out _));
        Assert.Null(area);
    }

    [Fact]
    public void TheRevisionIsTheShippedFormulaAndMustNotDrift()
    {
        // These two numbers are the shipped ScopeRevision.ForArea over the same
        // inputs. They are pinned as literals rather than recomputed, because
        // the whole value of the formula is that it is the SAME formula: the
        // collection journal writes an accepted scope's revision into a named
        // field on a schema-3 row, and an order resumed after a reload compares
        // what it finds there against what the world computes now. A different
        // hash pauses every in-flight order on the first load after the upgrade.
        Assert.Equal(697519278, NpcAreaRevision.ForCircle(new NpcPoint(10f, 0f, -20f), 20f));
        Assert.Equal(183153680, NpcAreaRevision.ForCircle(new NpcPoint(0f, 0f, 0f), 4f));
    }

    [Fact]
    public void TheRevisionIsDerivedFromGeometrySoTheSameAreaAgreesAcrossSessions()
    {
        Assert.Equal(Areas.Circle(3f, 4f, 12f).Revision, Areas.Circle(3f, 4f, 12f).Revision);
        Assert.NotEqual(Areas.Circle(3f, 4f, 12f).Revision, Areas.Circle(3f, 4f, 12.5f).Revision);
        Assert.NotEqual(Areas.Circle(3f, 4f, 12f).Revision, Areas.Circle(3.01f, 4f, 12f).Revision);

        // The label is not geometry. Renaming an area must not pause a job.
        Assert.Equal(Areas.Circle(3f, 4f, 12f, "one").Revision, Areas.Circle(3f, 4f, 12f, "another").Revision);
    }

    [Fact]
    public void TheCircleDoesNotClampTheShippedFourToFortyEightMetreRange()
    {
        // Deliberate. The limit is a role's policy about how far a worker should
        // be sent, it is enforced where it ships, and a polygon has no radius to
        // clamp. A clamp here would be a rule only one shape can obey, applied
        // to every shape.
        Assert.Equal(1f, Areas.Circle(0f, 0f, 1f).BoundingRadiusMetres);
        Assert.Equal(400f, Areas.Circle(0f, 0f, 400f).BoundingRadiusMetres);
    }
}

public class WorkAreaRegistryTests
{
    [Fact]
    public void AnAreaWhoseProviderIsNotInstalledRefusesAndHandsBackNoArea()
    {
        // THE test for this leaf. The worse of the two named failures is
        // silently falling back from an explicitly assigned invalid area to some
        // unrelated radius, because it looks like it worked: an NPC standing in
        // a plausible circle, working ground nobody marked, until the player
        // notices their trees are gone.
        var registry = new NpcWorkAreaRegistry();

        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(
                new NpcWorkAreaId(Areas.PolygonProvider, "north-wood"),
                new[] { 0f, 0f, 10f, 0f, 10f, 10f },
                "the north wood"));

        Assert.Equal(WorkAreaResolution.ProviderMissing, result.Resolution);
        Assert.Null(result.Area);
        Assert.False(result.IsResolved);
        Assert.Contains(Areas.PolygonProvider, result.Reason);
    }

    [Fact]
    public void AMissingProviderIsStillMissingWhenAnotherOneIsRegistered()
    {
        // The nastiest version of the fallback: something IS registered, so a
        // registry that resolved "the only provider it has" would answer with a
        // shape the player never drew.
        NpcWorkAreaRegistry registry = Areas.WithCircle();

        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(
                new NpcWorkAreaId(Areas.PolygonProvider, "north-wood"), new[] { 0f, 0f, 10f, 0f, 10f, 10f }, "n"));

        Assert.Equal(WorkAreaResolution.ProviderMissing, result.Resolution);
        Assert.Null(result.Area);
    }

    [Fact]
    public void AShapeARegisteredProviderCannotReadRefusesRatherThanSubstitutingACircle()
    {
        NpcWorkAreaRegistry registry = Areas.WithCircle();

        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(new NpcWorkAreaId(Areas.CircleProvider, "harvest"), new[] { 1f, 2f }, "x"));

        Assert.Equal(WorkAreaResolution.ShapeUnreadable, result.Resolution);
        Assert.Null(result.Area);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-4f)]
    [InlineData(float.NaN)]
    public void ACircleWithNoUsableRadiusIsUnreadableRatherThanRepaired(float radius)
    {
        NpcWorkAreaRegistry registry = Areas.WithCircle();

        NpcWorkAreaResult result = registry.Resolve(Areas.CircleDescriptor(4f, 0f, 4f, radius));

        Assert.NotEqual(WorkAreaResolution.Resolved, result.Resolution);
        Assert.Null(result.Area);
    }

    [Fact]
    public void AProviderRegisteredFromOutsideThisPackageResolvesItsOwnShape()
    {
        // Registered from the test assembly, which is the point: the package
        // depends on no map product, and there is no code path here that knows
        // what a polygon is.
        var registry = new NpcWorkAreaRegistry();
        Assert.Equal(
            ProviderRegistration.Registered, registry.Register(new PolygonAreaProvider(Areas.PolygonProvider)));
        Assert.False(registry.Knows(Areas.CircleProvider));

        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(
                new NpcWorkAreaId(Areas.PolygonProvider, "north-wood"),
                new[] { 0f, 0f, 20f, 0f, 20f, 20f, 0f, 20f },
                "the north wood"));

        Assert.True(result.IsResolved);
        Assert.True(result.Area!.Contains(new NpcPoint(10f, 0f, 10f)));
        Assert.False(result.Area.Contains(new NpcPoint(30f, 0f, 10f)));
        Assert.Equal("the north wood", result.Area.Describe);
    }

    [Fact]
    public void TheBuiltInCircleHasNoPrivilegedPath()
    {
        // If the circle could resolve without being registered, that path would
        // be the fallback, whatever it was called.
        var registry = new NpcWorkAreaRegistry();
        Assert.Equal(0, registry.Count);

        Assert.Equal(WorkAreaResolution.ProviderMissing, registry.Resolve(Areas.CircleDescriptor(0f, 0f, 0f, 10f)).Resolution);
    }

    [Fact]
    public void OneIdIsOneProvider()
    {
        NpcWorkAreaRegistry registry = Areas.WithCircle();

        Assert.Equal(
            ProviderRegistration.DuplicateId, registry.Register(new PolygonAreaProvider(Areas.CircleProvider)));
        Assert.Equal(1, registry.Count);

        // And the first one still answers: a refused registration changes
        // nothing, so which shape a player's area comes back as never depends on
        // plugin load order.
        Assert.True(registry.Resolve(Areas.CircleDescriptor(0f, 0f, 0f, 10f)).IsResolved);
    }

    [Fact]
    public void AProviderThatCannotBeNamedOrCannotRunIsRefusedRatherThanCrashing()
    {
        var registry = new NpcWorkAreaRegistry();

        Assert.Equal(ProviderRegistration.NoProvider, registry.Register(null));
        Assert.Equal(ProviderRegistration.Unnamed, registry.Register(new PolygonAreaProvider(string.Empty)));
        Assert.Equal(ProviderRegistration.Unnamed, registry.Register(new BrokenAreaProvider("x", throwOnId: true)));

        Assert.Equal(ProviderRegistration.Registered, registry.Register(new BrokenAreaProvider("broken", false)));
        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(new NpcWorkAreaId("broken", "k"), new[] { 1f }, "x"));
        Assert.Equal(WorkAreaResolution.Refused, result.Resolution);
        Assert.Null(result.Area);
    }

    [Fact]
    public void AProviderThatClaimsSuccessWithNoAreaIsARefusal()
    {
        var registry = new NpcWorkAreaRegistry();
        registry.Register(new EmptySuccessProvider("hollow"));

        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(new NpcWorkAreaId("hollow", "k"), new[] { 1f }, "x"));

        Assert.Equal(WorkAreaResolution.Refused, result.Resolution);
        Assert.Null(result.Area);
    }

    [Fact]
    public void AnUnnamedOrUnreadableDescriptorNeverReachesAProvider()
    {
        NpcWorkAreaRegistry registry = Areas.WithCircle();

        Assert.Equal(
            WorkAreaResolution.ProviderMissing,
            registry.Resolve(new NpcWorkAreaDescriptor(new NpcWorkAreaId(string.Empty, "k"), new[] { 1f }, "x")).Resolution);
        Assert.Equal(
            WorkAreaResolution.ProviderMissing,
            registry.Resolve(new NpcWorkAreaDescriptor(new NpcWorkAreaId(Areas.CircleProvider, string.Empty), new[] { 1f }, "x")).Resolution);
        Assert.Equal(
            WorkAreaResolution.ShapeUnreadable,
            registry.Resolve(Areas.CircleDescriptor(float.NaN, 0f, 0f, 10f)).Resolution);
        Assert.Equal(
            WorkAreaResolution.ShapeUnreadable,
            registry.Resolve(
                new NpcWorkAreaDescriptor(new NpcWorkAreaId(Areas.CircleProvider, "k"), null, "x")).Resolution);
    }
}

public class WorkAreaReconstructionTests
{
    [Fact]
    public void AnAreaComesBackFromItsNumbersAsTheSameArea()
    {
        // What a reload is, at this layer: the role persisted four numbers with
        // its own rows and its own tags, and hands them back. No codec here, no
        // file name here.
        NpcWorkAreaRegistry registry = Areas.WithCircle();
        NpcCircleWorkArea before = Areas.Circle(-36.8f, 23.9f, 20f, "the harvest area");

        IReadOnlyList<float> saved = NpcCircleAreaProvider.Shape(before.BoundingCentre, before.BoundingRadiusMetres);
        var reloaded = new List<float>(saved);

        NpcWorkAreaResult result = registry.Resolve(
            new NpcWorkAreaDescriptor(new NpcWorkAreaId(Areas.CircleProvider, "harvest"), reloaded, "the harvest area"));

        Assert.True(result.IsResolved);
        Assert.Equal(before.Revision, result.Area!.Revision);
        Assert.Equal(before.Describe, result.Area.Describe);
        foreach (NpcPoint probe in new[]
        {
            new NpcPoint(-36.8f, 0f, 23.9f), new NpcPoint(-20f, 0f, 23.9f), new NpcPoint(0f, 0f, 0f),
        })
        {
            Assert.Equal(before.Contains(probe), result.Area.Contains(probe));
        }
    }

    [Fact]
    public void AnAreasIdentityIsStableAndIsNotAWorldObjectsName()
    {
        var first = new NpcWorkAreaId(Areas.CircleProvider, "harvest");
        var same = new NpcWorkAreaId(Areas.CircleProvider, "harvest");
        var other = new NpcWorkAreaId(Areas.CircleProvider, "supply");

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.True(first.IsNamed);
        Assert.False(new NpcWorkAreaId(string.Empty, "harvest").IsNamed);
        Assert.False(new NpcWorkAreaId(Areas.CircleProvider, string.Empty).IsNamed);
    }
}

public class WorkAreaCheckpointTests
{
    private static NpcWorkAreaCommitment Commit(NpcWorldEpoch world, INpcWorkArea? area = null) =>
        new NpcWorkAreaCommitment(
            new NpcWorkAreaId(Areas.CircleProvider, "harvest"), area ?? Areas.Circle(0f, 0f, 20f), world);

    [Fact]
    public void AnUnchangedLoadedAreaInTheSameWorldIsTheOnlyWayToKeepWorking()
    {
        NpcWorldEpoch world = Identities.AWorld();
        NpcWorkAreaCommitment commitment = Commit(world);

        WorkAreaState state = NpcWorkAreaCheckpoint.Judge(
            commitment, new WorkAreaObservation(true, commitment.Revision, true, world));

        Assert.Equal(WorkAreaState.Valid, state);
        Assert.True(NpcWorkAreaCheckpoint.MayWork(state));
    }

    [Fact]
    public void AChangedAreaPausesAndAnUnloadedOneWaits()
    {
        NpcWorldEpoch world = Identities.AWorld();
        NpcWorkAreaCommitment commitment = Commit(world);

        Assert.Equal(
            WorkAreaState.Changed,
            NpcWorkAreaCheckpoint.Judge(commitment, new WorkAreaObservation(true, commitment.Revision + 1, true, world)));
        Assert.Equal(
            WorkAreaState.Unloaded,
            NpcWorkAreaCheckpoint.Judge(commitment, new WorkAreaObservation(true, commitment.Revision, false, world)));

        Assert.False(NpcWorkAreaCheckpoint.MayWork(WorkAreaState.Changed));
        Assert.False(NpcWorkAreaCheckpoint.MayWork(WorkAreaState.Unloaded));
        Assert.False(NpcWorkAreaCheckpoint.MayWork(WorkAreaState.Unspecified));
    }

    [Fact]
    public void AnAreaThatIsGoneOrFromAnotherWorldIsInvalidAndNeverWidens()
    {
        NpcWorldEpoch world = Identities.AWorld();
        NpcWorldEpoch otherWorld = Identities.AWorld();
        NpcWorkAreaCommitment commitment = Commit(world);

        Assert.Equal(
            WorkAreaState.Invalid,
            NpcWorkAreaCheckpoint.Judge(commitment, new WorkAreaObservation(false, commitment.Revision, true, world)));
        Assert.Equal(
            WorkAreaState.Invalid,
            NpcWorkAreaCheckpoint.Judge(commitment, new WorkAreaObservation(true, commitment.Revision, true, otherWorld)));
        Assert.Equal(
            WorkAreaState.Invalid,
            NpcWorkAreaCheckpoint.Judge(
                commitment, new WorkAreaObservation(true, commitment.Revision, true, NpcWorldEpoch.Unknown)));
        Assert.Equal(WorkAreaState.Invalid, NpcWorkAreaCheckpoint.Judge(null, new WorkAreaObservation(true, 0, true, world)));
    }

    [Fact]
    public void ExistenceOutranksLoadingSoAnAreaThatIsGoneIsNeverMerelyUnloaded()
    {
        NpcWorldEpoch world = Identities.AWorld();
        NpcWorkAreaCommitment commitment = Commit(world);

        Assert.Equal(
            WorkAreaState.Invalid,
            NpcWorkAreaCheckpoint.Judge(commitment, new WorkAreaObservation(false, commitment.Revision, false, world)));
    }

    [Fact]
    public void ACommitmentFreezesTheRevisionItStartedWith()
    {
        NpcWorldEpoch world = Identities.AWorld();
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcWorkAreaCommitment commitment = Commit(world, area);

        Assert.Equal(area.Revision, commitment.Revision);
        Assert.Equal(area.Revision, Areas.Circle(0f, 0f, 20f).Revision);
        Assert.NotEqual(commitment.Revision, Areas.Circle(0f, 0f, 30f).Revision);
    }

    [Fact]
    public void ACommitmentRefusesToBeMadeWithoutAnAreaANameOrAWorld()
    {
        NpcWorldEpoch world = Identities.AWorld();

        Assert.Throws<System.ArgumentNullException>(
            () => new NpcWorkAreaCommitment(new NpcWorkAreaId(Areas.CircleProvider, "h"), null!, world));
        Assert.Throws<System.ArgumentException>(
            () => new NpcWorkAreaCommitment(new NpcWorkAreaId(string.Empty, "h"), Areas.Circle(0f, 0f, 10f), world));
        Assert.Throws<System.ArgumentException>(
            () => new NpcWorkAreaCommitment(
                new NpcWorkAreaId(Areas.CircleProvider, "h"), Areas.Circle(0f, 0f, 10f), NpcWorldEpoch.Unknown));
    }

    [Fact]
    public void AnAreaThatCannotAnswerContainsNothing()
    {
        var commitment = new NpcWorkAreaCommitment(
            new NpcWorkAreaId(Areas.CircleProvider, "h"), new ThrowingWorkArea(), Identities.AWorld());

        Assert.False(commitment.Contains(new NpcPoint(0f, 0f, 0f)));
    }
}

public class AreaSweepTests
{
    private static NpcAreaSweep Sweep(
        INpcWorkArea area, INpcAreaProbe probe, INpcTargetFilter? filter = null) =>
        new NpcAreaSweep(area, probe, filter, rings: 3, spokes: 4);

    [Fact]
    public void ASweepYieldsStandableTargetsInsideTheArea()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweepPass pass = Sweep(area, RecordingProbe.Standable()).Next(100);

        Assert.Equal(AreaScanOutcome.Found, pass.Report.Outcome);
        Assert.NotEmpty(pass.Targets);
        Assert.All(pass.Targets, point => Assert.True(area.Contains(point)));
    }

    [Fact]
    public void TheSameAreaProposesTheSameCandidatesInTheSameOrderEverySession()
    {
        NpcCircleWorkArea area = Areas.Circle(7f, -3f, 20f);
        var first = RecordingProbe.Answering(AreaSampleVerdict.Rejected);
        var second = RecordingProbe.Answering(AreaSampleVerdict.Rejected);

        Sweep(area, first).Next(100);
        Sweep(area, second).Next(100);

        Assert.Equal(first.Asked, second.Asked);
        Assert.Equal(area.BoundingCentre, first.Asked[0]);
    }

    [Fact]
    public void EverythingLookedAtAndNothingFoundIsTheOnlyConclusiveEmpty()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweep sweep = Sweep(area, RecordingProbe.Answering(AreaSampleVerdict.Rejected));

        NpcAreaSweepPass pass = sweep.Next(100);

        Assert.True(sweep.IsFinished);
        Assert.Equal(AreaScanOutcome.Empty, pass.Report.Outcome);
        Assert.True(pass.Report.IsConclusive);
        Assert.True(pass.Report.CountsAgree);
        Assert.Empty(pass.Targets);
    }

    [Fact]
    public void GroundThatIsNotLoadedIsNeverAnEmptyArea()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweepPass pass = Sweep(area, RecordingProbe.Answering(AreaSampleVerdict.NotLoaded)).Next(100);

        Assert.Equal(AreaScanOutcome.NotLoaded, pass.Report.Outcome);
        Assert.False(pass.Report.IsConclusive);
        Assert.True(pass.Report.NotLoaded > 0);
    }

    [Fact]
    public void AProbeThatCouldNotTellCountsAsGroundNobodyLookedAt()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweepPass pass = Sweep(area, RecordingProbe.Answering(AreaSampleVerdict.Unreadable)).Next(100);

        Assert.Equal(AreaScanOutcome.NotLoaded, pass.Report.Outcome);
        Assert.False(pass.Report.IsConclusive);
    }

    [Fact]
    public void AProbeThatThrowsIsUnknownGroundRatherThanAFault()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        var angry = new RecordingProbe(point => throw new System.InvalidOperationException("no"));

        NpcAreaSweepPass pass = Sweep(area, angry).Next(100);

        Assert.Equal(AreaScanOutcome.NotLoaded, pass.Report.Outcome);
        Assert.False(pass.Report.IsConclusive);
    }

    [Fact]
    public void EverythingUsedUpIsExhaustedRatherThanEmptyBecauseItTellsThePlayerToWait()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweepPass pass = Sweep(
            area, RecordingProbe.Standable(), FixedTargetFilter.Always(NpcTargetVerdict.Exhausted)).Next(100);

        Assert.Equal(AreaScanOutcome.Exhausted, pass.Report.Outcome);
        Assert.True(pass.Report.Exhausted > 0);
        Assert.True(pass.Report.IsConclusive);
    }

    [Fact]
    public void AFilterThatCannotAnswerKeepsTheJobOpen()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweepPass pass = Sweep(
            area, RecordingProbe.Standable(), FixedTargetFilter.Always(NpcTargetVerdict.Unknown)).Next(100);

        Assert.Equal(AreaScanOutcome.NotLoaded, pass.Report.Outcome);
        Assert.False(pass.Report.IsConclusive);
    }

    [Fact]
    public void ASpentBudgetIsIncompleteAndTheSweepResumesWhereItStopped()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        var probe = RecordingProbe.Answering(AreaSampleVerdict.Rejected);
        NpcAreaSweep sweep = Sweep(area, probe);

        NpcAreaSweepPass first = sweep.Next(3);
        Assert.Equal(AreaScanOutcome.Incomplete, first.Report.Outcome);
        Assert.True(first.Report.TruncatedByBudget);
        Assert.False(first.Report.IsConclusive);
        Assert.Equal(3, probe.Asked.Count);

        NpcAreaSweepPass second = sweep.Next(100);
        Assert.True(sweep.IsFinished);
        Assert.Equal(AreaScanOutcome.Empty, second.Report.Outcome);
        Assert.True(second.Report.IsConclusive);

        // Cumulative, so a finished sweep's conclusion rests on everything it
        // looked at rather than on the last pass alone.
        Assert.Equal(sweep.CandidateCount, second.Report.Examined);
        Assert.Equal(probe.Asked.Count, sweep.CandidateCount);
    }

    [Fact]
    public void ARestartedSweepForgetsWhatItCounted()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        NpcAreaSweep sweep = Sweep(area, RecordingProbe.Answering(AreaSampleVerdict.Rejected));
        sweep.Next(100);
        sweep.Restart();

        NpcAreaSweepPass pass = sweep.Next(1);

        Assert.Equal(1, pass.Report.Examined);
        Assert.False(sweep.IsFinished);
    }

    [Fact]
    public void GroundThatSlidOutsideTheAreaIsRefusedRatherThanWalkedTo()
    {
        // The probe answers with where the ground actually is, which is rarely
        // the point asked about. The bound is allowed to be generous; the area
        // is not, so the answer is offered to Contains before anything acts on
        // it.
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);
        var displacing = new RecordingProbe(
            point => new AreaSample(AreaSampleVerdict.Standable, new NpcPoint(500f, 0f, 500f), AreaRejection.None));

        NpcAreaSweepPass pass = Sweep(area, displacing).Next(100);

        Assert.Equal(AreaScanOutcome.Empty, pass.Report.Outcome);
        Assert.Empty(pass.Targets);
        Assert.True(pass.Report.Rejected > 0);
    }

    [Fact]
    public void CandidatesOutsideTheAreaAreCountedRatherThanSilentlySkipped()
    {
        // A generous bound over a narrow shape should be visible, not merely
        // slow. The polygon here covers a corner of its own bounding circle.
        var polygon = new PolygonWorkArea(
            new[] { 0f, 0f, 6f, 0f, 6f, 6f, 0f, 6f }, new NpcPoint(3f, 0f, 3f), 40f, "a corner", 7);
        var probe = RecordingProbe.Standable();

        NpcAreaSweepPass pass = new NpcAreaSweep(polygon, probe, null, rings: 4, spokes: 8).Next(200);

        Assert.True(pass.Report.Rejected > 0);
        Assert.True(pass.Report.Examined > probe.Asked.Count);
    }

    [Fact]
    public void AnAreaThatCannotBeReadIsInvalidAndNothingIsScanned()
    {
        Assert.Equal(
            AreaScanOutcome.AreaInvalid, new NpcAreaSweep(null, RecordingProbe.Standable()).Next(10).Report.Outcome);
        Assert.Equal(AreaScanOutcome.AreaInvalid, new NpcAreaSweep(Areas.Circle(0f, 0f, 10f), null).Next(10).Report.Outcome);

        NpcAreaSweepPass pass = Sweep(new ThrowingWorkArea(), RecordingProbe.Standable()).Next(10);
        Assert.Equal(AreaScanOutcome.AreaInvalid, pass.Report.Outcome);
        Assert.False(pass.Report.IsConclusive);
        Assert.Empty(pass.Targets);
    }

    [Fact]
    public void TheLoadCheckPointsAreTheShippedNineForACircle()
    {
        // Centre plus eight points on a ring at 70 % of the radius: the exact
        // set the shipped scope check uses, so a role adopting this asks the
        // same question it asks today.
        NpcCircleWorkArea area = Areas.Circle(5f, -5f, 20f);

        IReadOnlyList<NpcPoint> points = NpcAreaSweep.LoadCheckPoints(area, 9);

        Assert.Equal(9, points.Count);
        Assert.Equal(area.BoundingCentre, points[0]);
        Assert.All(points, point => Assert.True(area.Contains(point)));
        Assert.Contains(points.Skip(1), point => System.Math.Abs(point.HorizontalDistanceTo(area.BoundingCentre) - 14f) < 0.01f);
    }

    [Fact]
    public void LoadCheckPointsNeverProposeGroundOutsideTheArea()
    {
        // For a shape whose bounding centre is outside it, the shipped nine
        // would have reported "none of this is loaded" for an area that is
        // perfectly workable. Every point offered here is inside.
        var slab = new PolygonWorkArea(
            new[] { 20f, -45f, 45f, -45f, 45f, 45f, 20f, 45f }, new NpcPoint(0f, 0f, 0f), 70f, "a slab", 11);

        IReadOnlyList<NpcPoint> points = NpcAreaSweep.LoadCheckPoints(slab, 5);

        Assert.NotEmpty(points);
        Assert.All(points, point => Assert.True(slab.Contains(point)));
        Assert.DoesNotContain(points, point => point.Equals(slab.BoundingCentre));
    }

    [Fact]
    public void AnAreaThatCannotBeReadOffersNoLoadCheckPoints()
    {
        Assert.Empty(NpcAreaSweep.LoadCheckPoints(null, 9));
        Assert.Empty(NpcAreaSweep.LoadCheckPoints(Areas.Circle(0f, 0f, 10f), 0));
        Assert.Empty(NpcAreaSweep.LoadCheckPoints(new ThrowingWorkArea(), 9));
    }
}
