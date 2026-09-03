using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC12 blockers 5/6: a pin the player just created must render
/// as itself. The clusterer's alwaysVisible exemption keeps it out of any
/// fold while its neighbors still cluster.</summary>
public class StickyVisibilityTests
{
    private static PinStore NewStoreWithCluster(out List<AtlasPin> pins)
    {
        var store = new PinStore();
        var list = new List<AtlasPin>();
        for (int index = 0; index < 3; index++)
        {
            int captured = index;
            list.Add(store.Create(pin =>
            {
                pin.Name = $"Neighbor {captured}";
                pin.IconId = "cc:resource";
                pin.Category = "Resources";
                pin.Position = new RoadPoint(10f + captured, 0f, 10f);
            }));
        }

        pins = list;
        return store;
    }

    [Fact]
    public void StickyPin_NeverFolds_WhileNeighborsStillCluster()
    {
        NewStoreWithCluster(out List<AtlasPin> pins);
        var sticky = new HashSet<Guid> { pins[0].Id.Value };

        PinClusterer.Result result = PinClusterer.Compute(pins, cellMeters: 96f, minClusterSize: 2, sticky);

        Assert.Contains(pins[0], result.Singles);
        Assert.Single(result.Clusters);
        Assert.Equal(2, result.Clusters[0].Members.Count);
        Assert.DoesNotContain(pins[0], result.Clusters[0].Members);
    }

    [Fact]
    public void WithoutSticky_TheSamePinFolds()
    {
        NewStoreWithCluster(out List<AtlasPin> pins);

        PinClusterer.Result result = PinClusterer.Compute(pins, cellMeters: 96f, minClusterSize: 2);

        Assert.Empty(result.Singles);
        Assert.Single(result.Clusters);
        Assert.Equal(3, result.Clusters[0].Members.Count);
    }
}

/// <summary>RC12 blocker 5: the pure rule that resolves a palette birth so
/// a confirmed name always yields exactly one managed marker.</summary>
public class PaletteBirthResolutionTests
{
    [Fact]
    public void RenderingSurvived_AdoptsInPlace()
    {
        Assert.Equal(
            PaletteBirthResolution.Action.AdoptBorn,
            PaletteBirthResolution.Decide(
                bornStillOnMap: true, bornAdoptable: true, replacementAtPositionExists: false, committedName: "Camp"));
    }

    [Fact]
    public void RenderingSurvivedButForeign_IsLeftAlone()
    {
        Assert.Equal(
            PaletteBirthResolution.Action.DropForeign,
            PaletteBirthResolution.Decide(true, false, false, "Camp"));
    }

    [Fact]
    public void RenderingReplaced_AdoptsTheReplacement()
    {
        Assert.Equal(
            PaletteBirthResolution.Action.AdoptReplacement,
            PaletteBirthResolution.Decide(false, false, true, "Camp"));
    }

    [Fact]
    public void RenderingVanishedWithACommittedName_RecreatesTheMarker()
    {
        Assert.Equal(
            PaletteBirthResolution.Action.RecreateManaged,
            PaletteBirthResolution.Decide(false, false, false, "Camp"));
    }

    [Fact]
    public void RenderingVanishedWithoutAName_HonorsTheCancel()
    {
        Assert.Equal(
            PaletteBirthResolution.Action.DropCancelled,
            PaletteBirthResolution.Decide(false, false, false, ""));
    }
}

/// <summary>RC12 blocker 6: accepting an observation surfaces the created
/// pin (so the runtime can guarantee its rendering), removes the pending
/// entry, and rejected rows resolve by stable identity.</summary>
public class SurveyAcceptRc12Tests
{
    private static readonly DateTime Now = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static (SurveyEngine Engine, PinStore Pins) Fixture()
    {
        var rules = new SurveyRuleSet();
        rules.AddRule(new SurveyRule("rock4_copper*", "cc:resource", "Resources", 40f, 60f));
        var engine = new SurveyEngine { Rules = rules };
        return (engine, new PinStore());
    }

    [Fact]
    public void Accept_SurfacesTheCreatedPin_AndRemovesThePendingEntry()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        Assert.Equal(SurveyEngine.OfferResult.Added,
            engine.Offer("rock4_copper(Clone)", new RoadPoint(100f, 5f, 200f), pins, Now));
        SurveyEngine.Observation observation = engine.Observations[0];

        Assert.True(engine.Accept(observation.Id, pins, out AtlasPin? created));

        Assert.NotNull(created);
        Assert.Equal(observation.SuggestedName, created!.Name);
        Assert.Equal("cc:resource", created.IconId);
        Assert.Equal("Resources", created.Category);
        Assert.Equal(100f, created.Position.X);
        Assert.Equal(200f, created.Position.Z);
        Assert.Contains("surveyed", created.Tags);
        Assert.Empty(engine.Observations);
        Assert.Single(pins.Living);
    }

    [Fact]
    public void Accept_UnknownId_ReportsStaleInsteadOfActing()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("rock4_copper(Clone)", new RoadPoint(100f, 5f, 200f), pins, Now);

        Assert.False(engine.Accept(Guid.NewGuid(), pins, out AtlasPin? created));
        Assert.Null(created);
        Assert.Single(engine.Observations);
        Assert.Empty(pins.Living);
    }

    [Fact]
    public void AcceptAll_ReportsEveryCreatedPin()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("rock4_copper_a", new RoadPoint(100f, 0f, 200f), pins, Now);
        engine.Offer("rock4_copper_b", new RoadPoint(500f, 0f, 600f), pins, Now);

        var created = new List<AtlasPin>();
        int accepted = engine.AcceptAll(pins, created.Add);

        Assert.Equal(2, accepted);
        Assert.Equal(2, created.Count);
    }

    [Fact]
    public void RejectedRows_ResolveByIdentity_EvenAfterTheListShifts()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("rock4_copper_a", new RoadPoint(100f, 0f, 200f), pins, Now);
        engine.Offer("rock4_copper_b", new RoadPoint(500f, 0f, 600f), pins, Now);
        engine.Reject(engine.Observations[0].Id, Now);
        engine.Reject(engine.Observations[0].Id, Now);

        string secondKey = SurveyEngine.IdentityKey(
            engine.Rejected[1].PrefabName, engine.Rejected[1].Position);

        // The list shifts under the panel: the first rejected entry leaves.
        Assert.True(engine.RestoreRejected(0, Now));

        int index = engine.FindRejectedIndex(secondKey);
        Assert.Equal(0, index);
        Assert.True(engine.AcceptRejected(index, pins, out AtlasPin? created));
        Assert.NotNull(created);
        Assert.Equal(500f, created!.Position.X);

        Assert.Equal(-1, engine.FindRejectedIndex(secondKey));
    }
}
