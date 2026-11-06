using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

public sealed class GunnarKeepsakeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ct-457-" + Guid.NewGuid().ToString("N"));
    private GunnarCompanionRecord Open() => GunnarCompanionRecord.Open(_root, 1, 2);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Flat : IPlacementProbe
    {
        public PlacementProbeSample Probe(WorldPoint point) => new(point, PlacementRejection.None, -1f, SeatAvailability.None);
    }
    [Fact] public void PickupIsDurableAndNeverRepeats()
    {
        var record = Open();
        Assert.Equal(GunnarPickupResult.Collected, GunnarKeepsake.Examine(record, true, false));
        Assert.False(Open().ShouldPresentMemento(true));
        Assert.Equal(GunnarPickupResult.AlreadyCollected, GunnarKeepsake.Examine(Open(), true, false));
        Assert.False(Open().IsRecruited);
    }
    [Fact] public void CollectedStorySurvivesSecondWriteFailureWithoutAnotherBuckle()
    {
        var record=Open(); Assert.Equal(GunnarPickupResult.Collected,GunnarKeepsake.Examine(record,true,false));
        string path=Directory.GetFiles(_root,"*.companions.tsv").Single(); Directory.CreateDirectory(path+".tmp");
        Assert.False(GunnarKeepsakeRecovery.ResumeCollected(record,true));
        Assert.False(record.ShouldPresentMemento(true)); Assert.False(Open().IsRecruited);
        Directory.Delete(path+".tmp");
        Assert.False(GunnarKeepsakeRecovery.ResumeCollected(record,false));
        Assert.True(GunnarKeepsakeRecovery.ResumeCollected(record,true));
        Assert.Equal(TheConcernedCat.Companions.Quest.QuestState.IntroPending,Open().State);
        Assert.False(Open().IsRecruited); Assert.False(Open().ShouldPresentMemento(true));
        Assert.False(GunnarKeepsakeRecovery.ResumeCollected(Open(),true));
    }
    [Fact] public void FailedWriteGrantsNothingAndCanRetry()
    {
        var record = Open(); File.WriteAllText(_root, "blocked");
        try
        {
            Assert.Equal(GunnarPickupResult.NotRecorded, GunnarKeepsake.Examine(record, true, false));
            Assert.True(record.ShouldPresentMemento(true)); Assert.False(record.IsRecruited);
        }
        finally { File.Delete(_root); }
        Assert.Equal(GunnarPickupResult.Collected, GunnarKeepsake.Examine(record, true, false));
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, true)] [InlineData(true, null)]
    public void DisabledOrWorkerOrUnknownMeansNothing(bool enabled, bool? worker)
    {
        Assert.False(GunnarKeepsake.Eligible(Open(), enabled, worker));
        Assert.Equal(GunnarPickupResult.Unavailable, GunnarKeepsake.Examine(Open(), enabled, worker));
        Assert.False(Directory.Exists(_root));
    }
    [Fact] public void SharedPlannerSeparatesCompassFlintAndBuckleAtOneAnchor()
    {
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(10, 3, 20));
        var compass = GunnarKeepsake.Plan(anchor, new Flat(), Array.Empty<WorldPoint>());
        var flint = GunnarKeepsake.Plan(anchor, new Flat(), new[] { compass.Position });
        var buckle = GunnarKeepsake.Plan(anchor, new Flat(), new[] { compass.Position, flint.Position });
        Assert.True(compass.Found); Assert.True(flint.Found); Assert.True(buckle.Found);
        Assert.True(buckle.Position.HorizontalDistanceTo(compass.Position) >= GunnarKeepsake.Clearance);
        Assert.True(buckle.Position.HorizontalDistanceTo(flint.Position) >= GunnarKeepsake.Clearance);
        Assert.True(buckle.CandidatesProbed <= PlacementPlanner.MaximumCandidates);
    }
    [Fact] public void MissingAnchorOrUnknownOccupancyNeverGuesses()
    {
        Assert.False(GunnarKeepsake.Plan(CompanionAnchor.None, new Flat(), Array.Empty<WorldPoint>()).Found);
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(1, 2, 3));
        Assert.False(GunnarKeepsake.Plan(anchor, new Flat(), null).Found);
        Assert.False(GunnarKeepsake.Plan(new CompanionAnchor(AnchorKind.ClaimedBed,
            new WorldPoint(float.NaN, 0, 0)), new Flat(), Array.Empty<WorldPoint>()).Found);
    }
    [Fact] public void EveryOccupiedCandidateDefersRatherThanOverlaps()
    {
        var anchor = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(0, 0, 0));
        var points = new List<WorldPoint>();
        for (int i = 0; i < PlacementPlanner.MaximumCandidates; i++)
        {
            var p = GunnarKeepsake.Plan(anchor, new Flat(), points);
            if (!p.Found) break; points.Add(p.Position);
        }
        Assert.False(GunnarKeepsake.Plan(anchor, new Flat(), points).Found);
    }
}
