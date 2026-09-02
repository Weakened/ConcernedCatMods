using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class SurveyRuleSetTests
{
    [Fact]
    public void Parse_RulesBlacklistAndComments()
    {
        var lines = new[]
        {
            SurveyRuleSet.Header,
            "# a comment",
            "!piece_*",
            "rock4_copper*\tcc:resource\tResources\t40\t60",
            "crypt2\tcc:danger\tDanger\t80\t120",
            "badrow\tonly-two",
            "toolong\ticon\tcat\t40\t99999",
        };

        SurveyRuleSet set = SurveyRuleSet.Parse(lines, out int malformed);

        Assert.Equal(2, malformed);
        Assert.Equal(2, set.Rules.Count);
        Assert.Single(set.Blacklist);
    }

    [Fact]
    public void Matching_BlacklistWins_ExactBeatsPrefix_LongestPrefixWins()
    {
        var set = new SurveyRuleSet();
        set.AddRule(new SurveyRule("rock*", "vanilla:dot", "Explore", 10f, 10f));
        set.AddRule(new SurveyRule("rock4_copper*", "cc:resource", "Resources", 40f, 60f));
        set.AddRule(new SurveyRule("rock4_copper_frac", "cc:danger", "Danger", 5f, 5f));
        set.AddBlacklist("rock2*");

        Assert.True(set.TryMatch("rock4_copper(Clone)", out SurveyRule prefix));
        Assert.Equal("cc:resource", prefix.IconId);

        Assert.True(set.TryMatch("ROCK4_COPPER_FRAC(Clone)", out SurveyRule exact));
        Assert.Equal("cc:danger", exact.IconId);

        Assert.True(set.TryMatch("rock1_mountain", out SurveyRule broad));
        Assert.Equal("vanilla:dot", broad.IconId);

        Assert.False(set.TryMatch("rock2_heath", out _));
        Assert.False(set.TryMatch("boar", out _));
        Assert.False(set.TryMatch(null, out _));
    }

    [Fact]
    public void SerializeParse_Roundtrips_WithoutMachineState()
    {
        SurveyRuleSet original = SurveyRuleSet.Default();
        var lines = new List<string>(original.Serialize());

        foreach (string line in lines)
        {
            Assert.DoesNotContain(":\\", line);
            Assert.DoesNotContain("C:/", line);
        }

        SurveyRuleSet reloaded = SurveyRuleSet.Parse(lines, out int malformed);
        Assert.Equal(0, malformed);
        Assert.Equal(original.Rules.Count, reloaded.Rules.Count);
        Assert.Equal(original.Blacklist.Count, reloaded.Blacklist.Count);
    }
}

public class SurveyEngineTests
{
    private static DateTime _now = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    private static (SurveyEngine Engine, PinStore Pins) Fixture(int maxObservations = 200)
    {
        var rules = new SurveyRuleSet();
        rules.AddRule(new SurveyRule("rock4_copper*", "cc:resource", "Resources", 40f, 60f));
        var engine = new SurveyEngine
        {
            Rules = rules,
            MaxObservations = maxObservations,
            BaseExclusionRadiusMeters = 30f,
        };
        return (engine, new PinStore(() => _now));
    }

    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    [Fact]
    public void Offer_MatchingPrefab_CreatesObservation_NotAPin()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();

        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("rock4_copper(Clone)", P(100f, 100f), pins, _now));
        Assert.Single(engine.Observations);
        Assert.Equal(0, pins.Count);
    }

    [Fact]
    public void DuplicateRadius_BlocksNearbyRepeats_AgainstPinsAndObservations()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("rock4_copper", P(0f, 0f), pins, _now);

        Assert.Equal(SurveyEngine.OfferResult.DuplicateObservation, engine.Offer("rock4_copper", P(10f, 0f), pins, _now));
        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("rock4_copper", P(100f, 0f), pins, _now));

        engine.Accept(engine.Observations[0].Id, pins);
        Assert.Equal(SurveyEngine.OfferResult.DuplicatePin, engine.Offer("rock4_copper", P(5f, 5f), pins, _now));
    }

    [Fact]
    public void BaseExclusion_RejectsObservationsNearBaseMarkers()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        pins.Create(pin =>
        {
            pin.Name = "Home";
            pin.Category = "Base";
            pin.Position = P(500f, 500f);
        });

        Assert.Equal(SurveyEngine.OfferResult.InsideBaseExclusion, engine.Offer("rock4_copper", P(510f, 500f), pins, _now));
        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("rock4_copper", P(600f, 500f), pins, _now));
    }

    [Fact]
    public void HardCap_BoundsObservationCount()
    {
        (SurveyEngine engine, PinStore pins) = Fixture(maxObservations: 3);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("rock4_copper", P(i * 100f, 0f), pins, _now));
        }

        Assert.Equal(SurveyEngine.OfferResult.CapReached, engine.Offer("rock4_copper", P(900f, 0f), pins, _now));
        Assert.Equal(3, engine.Observations.Count);
    }

    [Fact]
    public void Expiry_PrunesPredictably()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("rock4_copper", P(0f, 0f), pins, _now);
        engine.Offer("rock4_copper", P(100f, 0f), pins, _now.AddMinutes(30));

        Assert.Equal(0, engine.Prune(_now.AddMinutes(59)));
        Assert.Equal(1, engine.Prune(_now.AddMinutes(61)));
        Assert.Single(engine.Observations);
    }

    [Fact]
    public void AcceptAll_ConvertsEveryObservation_AndRejectAllClears()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("rock4_copper", P(0f, 0f), pins, _now);
        engine.Offer("rock4_copper", P(100f, 0f), pins, _now);

        Assert.Equal(2, engine.AcceptAll(pins));
        Assert.Empty(engine.Observations);
        Assert.Equal(2, pins.Count);
        foreach (AtlasPin pin in pins.Living)
        {
            Assert.Equal(AtlasPinSource.Generated, pin.Source);
            Assert.Contains("surveyed", pin.Tags);
            Assert.Equal("cc:resource", pin.IconId);
        }

        engine.Offer("rock4_copper", P(500f, 0f), pins, _now);
        Assert.Equal(1, engine.RejectAll());
        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void NoRule_MeansNoObservation()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        Assert.Equal(SurveyEngine.OfferResult.NoRule, engine.Offer("boar", P(0f, 0f), pins, _now));
        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void DefaultRules_MatchRealWorldPrefabNames_OutOfTheBox()
    {
        // RC8: the starter file must produce observations in ordinary play.
        // Representative live prefab names, as the scanner sees them
        // (instantiated clones, original casing).
        var engine = new SurveyEngine { Rules = SurveyRuleSet.Default() };
        var pins = new PinStore();
        float x = 0f;

        foreach ((string prefab, string icon, string category) in new[]
        {
            ("RaspberryBush(Clone)", "cc:resource", "Resources"),
            ("BlueberryBush(Clone)", "cc:resource", "Resources"),
            ("Pickable_Mushroom(Clone)", "cc:resource", "Resources"),
            ("Pickable_Thistle(Clone)", "cc:resource", "Resources"),
            ("rock4_copper(Clone)", "cc:mine", "Resources"),
            ("silvervein(Clone)", "cc:mine", "Resources"),
            ("mudpile2(Clone)", "cc:mine", "Resources"),
            ("Crypt2(Clone)", "cc:dungeon", "Dungeons"),
            ("SunkenCrypt4(Clone)", "cc:dungeon", "Dungeons"),
            ("TrollCave02(Clone)", "cc:dungeon", "Dungeons"),
            ("Vegvisir_Eikthyr(Clone)", "cc:objective", "Points of interest"),
            // RC10 feedback 10: broadened starter coverage.
            ("Pickable_Dandelion(Clone)", "cc:resource", "Resources"),
            ("Pickable_Flint(Clone)", "cc:resource", "Resources"),
            ("Pickable_SeedCarrot(Clone)", "cc:resource", "Resources"),
            ("GuckSack(Clone)", "cc:resource", "Resources"),
            ("Beehive(Clone)", "cc:resource", "Resources"),
            ("MountainCave02(Clone)", "cc:dungeon", "Dungeons"),
            ("RuneStone_Boars(Clone)", "cc:objective", "Points of interest"),
        })
        {
            x += 500f; // outside every duplicate radius
            SurveyEngine.OfferResult result = engine.Offer(prefab, P(x, 0f), pins, _now);
            Assert.Equal(SurveyEngine.OfferResult.Added, result);
            SurveyEngine.Observation added = engine.Observations[engine.Observations.Count - 1];
            Assert.Equal(icon, added.IconId);
            Assert.Equal(category, added.Category);
        }
    }

    [Fact]
    public void DefaultRules_BlacklistBuildPiecesAndEffects()
    {
        var engine = new SurveyEngine { Rules = SurveyRuleSet.Default() };
        var pins = new PinStore();

        Assert.Equal(SurveyEngine.OfferResult.NoRule, engine.Offer("piece_workbench(Clone)", P(0f, 0f), pins, _now));
        Assert.Equal(SurveyEngine.OfferResult.NoRule, engine.Offer("vfx_firework(Clone)", P(10f, 0f), pins, _now));
        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void DefaultRules_RoundTripThroughTheShareableFileFormat()
    {
        SurveyRuleSet defaults = SurveyRuleSet.Default();

        SurveyRuleSet reloaded = SurveyRuleSet.Parse(defaults.Serialize(), out int malformed);

        Assert.Equal(0, malformed);
        Assert.Equal(defaults.Rules.Count, reloaded.Rules.Count);
        Assert.Equal(defaults.Blacklist.Count, reloaded.Blacklist.Count);
        Assert.True(reloaded.TryMatch("raspberrybush", out SurveyRule match));
        Assert.Equal("cc:resource", match.IconId);
    }

    [Fact]
    public void LegacyStarterSet_DiffersFromTheNewDefaults()
    {
        // The in-place upgrade recognizes an untouched pre-RC8 starter file
        // by exact content; the two sets must therefore serialize
        // differently, and both must parse cleanly.
        var legacyLines = new List<string>(SurveyRuleSet.LegacyStarterSet().Serialize());
        var defaultLines = new List<string>(SurveyRuleSet.Default().Serialize());

        Assert.NotEqual(legacyLines, defaultLines);
        SurveyRuleSet.Parse(legacyLines, out int malformedLegacy);
        SurveyRuleSet.Parse(defaultLines, out int malformedDefault);
        Assert.Equal(0, malformedLegacy);
        Assert.Equal(0, malformedDefault);
    }

    [Fact]
    public void Rc8StarterSet_DiffersFromTheBroadenedDefaults()
    {
        // The in-place upgrade recognizes an untouched RC8/RC9 starter file
        // by exact content; the sets must serialize differently and both
        // must parse cleanly (RC10 feedback 10).
        var rc8Lines = new List<string>(SurveyRuleSet.Rc8StarterSet().Serialize());
        var defaultLines = new List<string>(SurveyRuleSet.Default().Serialize());

        Assert.NotEqual(rc8Lines, defaultLines);
        SurveyRuleSet.Parse(rc8Lines, out int malformedRc8);
        Assert.Equal(0, malformedRc8);
    }
}
