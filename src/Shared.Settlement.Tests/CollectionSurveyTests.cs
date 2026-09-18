using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>The bounded solo survey (GATHER-03): budgets per tick, honest
/// accounting of unloaded and truncated ground, and exhausted, inaccessible
/// and unknown kept apart.</summary>
public sealed class CollectionSurveyTests
{
    private static readonly Guid Epoch = new Guid("0f0e0d0c-0b0a-0908-0706-050403020100");
    private static readonly SitePoint Centre = new SitePoint(100f, 30f, -40f);

    private static WorkScope Scope(float radius = 30f) =>
        new WorkScope(WorkScopeSource.DefaultCampCircle, Centre, radius, "your bed", 7, Epoch);

    private static int _next;

    private static SurveyCandidate Candidate(
        NaturalSourceFacts facts, float dx, float dz, AreaAccess ward = AreaAccess.Granted, int yield = 1,
        bool owned = true, bool interior = false, bool location = false, Guid? epoch = null, string? session = null,
        LocationStanding? standing = null)
    {
        var key = new SourceKey(
            facts.NetworkPrefabName ?? "Unknown", session ?? ("0000000000000001:" + (++_next).ToString("x8")), epoch ?? Epoch,
            new SitePoint(Centre.X + dx, Centre.Y, Centre.Z + dz));
        return new SurveyCandidate(
            key, facts, owned, interior,
            standing ?? (location ? LocationStanding.Inside : LocationStanding.Outside), ward, yield);
    }

    private static SurveyScheduler Run(SurveyScheduler survey, float start = 0f, int maxTicks = 10000)
    {
        float now = start;
        for (int tick = 0; tick < maxTicks && !survey.IsComplete; tick++)
        {
            survey.Step(now);
            now += 0.05f;
        }

        Assert.True(survey.IsComplete);
        return survey;
    }

    [Fact]
    public void ASurveyClassifiesWhatItFindsAndCountsEachKindApart()
    {
        var probe = new FakeProbe();
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 1f));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), -5f, 3f));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaBranch(), 10f, -10f));
        probe.Candidates.Add(Candidate(
            SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, picked: true, canBePicked: false), 4f, 4f));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 2f, -2f, ward: AreaAccess.Denied));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 3f, -3f, location: true));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaBranch(), -3f, -3f, ward: AreaAccess.Unavailable));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaBranch(), -4f, -4f, owned: false));
        probe.Candidates.Add(Candidate(SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, tag: "spawned"), 5f, 5f));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 40f, 0f));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, revision: 3, SurveyProvenance.SoloForeman, startedAt: 0f));

        SurveyAccounting accounting = survey.Accounting;
        Assert.Equal(2, accounting.Available(CollectedResource.Stone));
        Assert.Equal(1, accounting.Available(CollectedResource.Wood));
        Assert.Equal(1, accounting.Exhausted(CollectedResource.Wood));
        Assert.Equal(2, accounting.Inaccessible(CollectedResource.Stone));
        Assert.Equal(2, accounting.Unknown(CollectedResource.Wood));
        Assert.Equal(1, accounting.RejectedNotNatural);
        Assert.Equal(1, accounting.OutsideScope);
        Assert.False(accounting.TruncatedByBudget);
        Assert.Equal(0, accounting.UnloadedCells);
        Assert.Equal(accounting.TotalCells, accounting.LoadedCells);

        SurveySnapshot snapshot = survey.Snapshot!;
        Assert.Equal(8, snapshot.Sources.Count);
        Assert.Equal(3, snapshot.Revision);
        Assert.Equal(SurveyProvenance.SoloForeman, snapshot.Provenance);
        Assert.False(snapshot.TruncatedByBudget);
        Assert.All(snapshot.Sources, source =>
        {
            Assert.Equal(3, source.SurveyRevision);
            Assert.Equal(SourceReachability.NotChecked, source.Reachability);
        });

        // Stable order, whatever order the scene handed them over in.
        for (int index = 1; index < snapshot.Sources.Count; index++)
        {
            Assert.True(string.CompareOrdinal(snapshot.Sources[index - 1].Key.SessionId, snapshot.Sources[index].Key.SessionId) < 0);
        }
    }

    [Fact]
    public void EveryTickStaysInsideItsBudget()
    {
        var parameters = new CollectionParameters(surveyCellsPerTick: 4, surveyEntriesPerTick: 3, surveyCandidatesPerTick: 2);
        var probe = new FakeProbe();
        for (int index = 0; index < 11; index++)
        {
            probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), index, 0f));
        }

        var survey = new SurveyScheduler(parameters, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f);
        int cells = survey.Accounting.TotalCells;
        Run(survey);

        Assert.True(probe.MaxReturnedPerCall <= 2);
        Assert.Equal(11, survey.Snapshot!.Sources.Count);
        int expectedTicks = (int)Math.Ceiling(cells / 4.0) + (int)Math.Ceiling(11 / 2.0);
        Assert.InRange(survey.TicksUsed, expectedTicks - 1, expectedTicks + 1);
    }

    [Fact]
    public void UnloadedGroundIsCountedAndNeverClaimedEmpty()
    {
        var probe = new FakeProbe { Loaded = point => point.X >= Centre.X };
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 10f, 0f));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.True(survey.Accounting.UnloadedCells > 0);
        Assert.True(survey.Accounting.LoadedCells > 0);
        Assert.Equal(survey.Accounting.TotalCells, survey.Accounting.UnloadedCells + survey.Accounting.LoadedCells);
        Assert.Equal(survey.Accounting.UnloadedCells, survey.Snapshot!.UnloadedCells);
        Assert.Single(survey.Snapshot.Sources);
    }

    [Fact]
    public void ACellStraddlingUnloadedGroundCountsAsUnloaded()
    {
        // Only one corner of the whole grid is unloaded: the cells touching it
        // are unloaded, not half-observed.
        var probe = new FakeProbe { Loaded = point => !(point.X < Centre.X - 20f && point.Z < Centre.Z - 20f) };

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.InRange(survey.Accounting.UnloadedCells, 1, survey.Accounting.TotalCells - 1);
    }

    [Fact]
    public void AScopeWithNothingLoadedIsNotDiscoveredAtAll()
    {
        var probe = new FakeProbe { Loaded = _ => false };
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 1f));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.Equal(0, probe.BeginCount);
        Assert.Empty(survey.Snapshot!.Sources);
        Assert.Equal(survey.Accounting.TotalCells, survey.Snapshot.UnloadedCells);
    }

    [Fact]
    public void TheSourceCapTruncatesInsteadOfClaimingEverythingWasSeen()
    {
        var parameters = new CollectionParameters(surveyMaxSources: 3);
        var probe = new FakeProbe();
        for (int index = 0; index < 5; index++)
        {
            probe.Candidates.Add(Candidate(SourceFacts.VanillaBranch(), index, index));
        }

        SurveyScheduler survey = Run(new SurveyScheduler(parameters, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.True(survey.Snapshot!.TruncatedByBudget);
        Assert.True(survey.Accounting.TruncatedByBudget);
        Assert.Equal(3, survey.Snapshot.Sources.Count);
    }

    [Fact]
    public void TheDeadlineTruncates()
    {
        var parameters = new CollectionParameters(surveyDeadlineSeconds: 1f, surveyCandidatesPerTick: 1);
        var probe = new FakeProbe();
        for (int index = 0; index < 50; index++)
        {
            probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), index % 20, 1f));
        }

        var survey = new SurveyScheduler(parameters, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f);
        survey.Step(0f);
        survey.Step(0.5f);
        Assert.False(survey.IsComplete);
        survey.Step(1f);

        Assert.True(survey.IsComplete);
        Assert.True(survey.Snapshot!.TruncatedByBudget);
        Assert.True(survey.Snapshot.Sources.Count < 50);
    }

    [Fact]
    public void AProbeThatBreaksItsBudgetIsNotTrusted()
    {
        var parameters = new CollectionParameters(surveyCandidatesPerTick: 2);
        var probe = new FakeProbe { OverDeliver = 3 };
        for (int index = 0; index < 6; index++)
        {
            probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), index, 0f));
        }

        SurveyScheduler survey = Run(new SurveyScheduler(parameters, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.True(survey.Snapshot!.TruncatedByBudget);
    }

    [Fact]
    public void KeysFromAnotherWorldLoadAndDuplicatesAreNotRecorded()
    {
        var probe = new FakeProbe();
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 0f, epoch: new Guid("99999999-0000-0000-0000-000000000001")));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 2f, 0f, session: "0000000000000001:0000beef"));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 2f, 0f, session: "0000000000000001:0000beef"));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.Single(survey.Snapshot!.Sources);
        Assert.Equal(1, survey.Accounting.RejectedNotNatural);
    }

    [Fact]
    public void AnAvailableSourceWhoseYieldCouldNotBeReadIsUnknown()
    {
        var probe = new FakeProbe();
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 0f, yield: 0));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.Equal(SourceAvailability.Unknown, Assert.Single(survey.Snapshot!.Sources).Availability);
        Assert.Equal(0, survey.Accounting.Available(CollectedResource.Stone));
    }

    [Theory]
    [InlineData(4f)]
    [InlineData(30f)]
    [InlineData(48f)]
    public void CellsCoverTheScopeDeterministicallyAndOnlyTheScope(float radius)
    {
        WorkScope scope = Scope(radius);
        List<SitePoint> first = SurveyScheduler.CellsFor(scope, 8f);
        List<SitePoint> second = SurveyScheduler.CellsFor(scope, 8f);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        float halfDiagonal = (float)Math.Sqrt(2) * 4f;
        Assert.All(first, cell => Assert.True(Centre.HorizontalDistanceTo(cell) <= radius + halfDiagonal));

        // Every point of the scope lies in some cell.
        for (float x = -radius; x <= radius; x += radius / 7f)
        {
            for (float z = -radius; z <= radius; z += radius / 7f)
            {
                var point = new SitePoint(Centre.X + x, Centre.Y, Centre.Z + z);
                if (!scope.Contains(point))
                {
                    continue;
                }

                Assert.Contains(first, cell => Math.Abs(cell.X - point.X) <= 4.001f && Math.Abs(cell.Z - point.Z) <= 4.001f);
            }
        }

        Assert.True(first.Count <= 144);
    }

    [Fact]
    public void ASurveyNeedsAProvenance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SurveyScheduler(CollectionParameters.Default, Scope(), new FakeProbe(), 1, SurveyProvenance.Unspecified, 0f));
    }

    [Fact]
    public void ASceneThatChangedUnderThePassIsTruncatedNotEmpty()
    {
        // A zone's objects are created over many frames, so a pass that walks a
        // copy of the scene taken at one moment has not seen everything there
        // is. R2 M2: "nothing found" must never stand for that.
        var probe = new FakeProbe { SceneMoved = true };
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 1f));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.True(survey.Snapshot!.TruncatedByBudget);
        Assert.True(survey.Accounting.TruncatedByBudget);

        // What it did see is still recorded.
        Assert.Single(survey.Snapshot.Sources);
    }

    [Fact]
    public void ASteadySceneIsNotTruncated()
    {
        var probe = new FakeProbe();
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 1f));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.False(survey.Snapshot!.TruncatedByBudget);
    }

    [Fact]
    public void ASurveyNeverAnswersThePickTimeQuestions()
    {
        // The site facts a survey builds carry no reach, no capacity and no
        // local player: it must not be able to admit a source on facts it never
        // established (R2 n3).
        var probe = new FakeProbe();
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 1f));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.Equal(SourceAvailability.Available, Assert.Single(survey.Snapshot!.Sources).Availability);
    }

    [Fact]
    public void ALocationTheWorldKnowsAboutButHasNotBuiltYetIsNotCollectable()
    {
        // R2 M3: while a location's objects are being created its Location
        // component does not exist, and "we could not tell" must refuse.
        var probe = new FakeProbe();
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 1f, 1f, standing: LocationStanding.Unknown));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 2f, 2f, standing: LocationStanding.Inside));
        probe.Candidates.Add(Candidate(SourceFacts.VanillaStone(), 3f, 3f, standing: LocationStanding.Unspecified));

        SurveyScheduler survey = Run(new SurveyScheduler(
            CollectionParameters.Default, Scope(), probe, 1, SurveyProvenance.SoloForeman, 0f));

        Assert.Equal(0, survey.Accounting.Available(CollectedResource.Stone));
        Assert.Equal(1, survey.Accounting.Inaccessible(CollectedResource.Stone));
        Assert.Equal(2, survey.Accounting.Unknown(CollectedResource.Stone));
    }
}
