using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>Issue #258 regressions: "survey never tries to pin dungeons"
/// (Cartographer 1.0.2 / Valheim 1.0.12, Burial Crypt, Troll Cave, Bear
/// Cave). Two independent defects had to hold for the report to be true,
/// and both are pinned down here:
///
/// 1. The scanner walked ONE loaded-world surface, the networked objects
///    in <c>ZNetScene.m_instances</c>. Valheim dungeon entrances are
///    LOCATIONS: the networked stand-in is a "LocationProxy(Clone)", and
///    <c>ZoneSystem.SpawnLocation</c> in client mode instantiates the real
///    location prefab with its ZNetView children deactivated, so nothing
///    ever called "Crypt3(Clone)" or "TrollCave02(Clone)" is registered.
/// 2. "BearCave" matched no rule at all, so even a correct scan could not
///    have offered it.
///
/// The sightings below use the exact prefab identities shipped in the
/// installed Valheim 1.0.12 location catalog
/// (Assets/world/Locations/BlackForest/{Crypt2,Crypt3,Crypt4,BearCave,
/// TrollCave02,HalfBurried_ForestCrypt,Hildir_crypt}.prefab,
/// Mountains/{MountainCave02,Hildir_cave}.prefab,
/// Swamp/SunkenCrypt4.prefab).</summary>
public class DungeonSurveyDiscoveryTests
{
    private const float ScanRadius = 40f;

    /// <summary>A snapshot source, exactly what the runtime adapters wrap,
    /// including the split read: the cheap placement first, the expensive
    /// name only for an entry the sweep accepted as in range.</summary>
    private sealed class FakeSource : ISurveySightingSource
    {
        private readonly List<SurveySighting?> _entries = new();

        public int Count => _entries.Count;

        /// <summary>How many times the sweep paid for a name read. The
        /// networked surface pays a native interop call and a fresh string
        /// per name, so this must stay proportional to in-range entries,
        /// not to snapshot size.</summary>
        public int NameReads { get; private set; }

        public FakeSource Add(string name, float x, float z, float y = 0f, float footprint = 0f)
        {
            _entries.Add(new SurveySighting(name, new RoadPoint(x, y, z), footprint));
            return this;
        }

        /// <summary>An entry the surface skips (destroyed object, character).</summary>
        public FakeSource AddSkipped()
        {
            _entries.Add(null);
            return this;
        }

        public bool TryReadPlacement(int index, out RoadPoint position, out float footprintRadiusMeters)
        {
            position = default;
            footprintRadiusMeters = 0f;
            SurveySighting? entry = _entries[index];
            if (!entry.HasValue)
            {
                return false;
            }

            position = entry.Value.Position;
            footprintRadiusMeters = entry.Value.FootprintRadiusMeters;
            return true;
        }

        public bool TryReadName(int index, out string name)
        {
            name = "";
            SurveySighting? entry = _entries[index];
            if (!entry.HasValue)
            {
                return false;
            }

            NameReads++;
            name = entry.Value.Name;
            return true;
        }
    }

    /// <summary>What a Valheim client actually has registered in
    /// ZNetScene near a Burial Chamber, a Troll Cave and a Bear Cave: the
    /// location proxy plus the location's own networked pieces. Not one
    /// of them carries the dungeon's name.</summary>
    private static FakeSource NetworkedObjectsNearDungeons() =>
        new FakeSource()
            .Add("LocationProxy(Clone)", 0f, 0f)
            .Add("LocationProxy(Clone)", 120f, 0f)
            .Add("LocationProxy(Clone)", 0f, 120f)
            .Add("dungeon_forestcrypt_door(Clone)", 3f, 1f)
            .Add("TreasureChest_forestcrypt(Clone)", 4f, 2f)
            .Add("TreasureChest_trollcave(Clone)", 121f, 2f)
            .AddSkipped();

    /// <summary>The same three dungeons as the game's loaded Location
    /// instances — the surface the scanner was missing.</summary>
    private static FakeSource LoadedDungeonLocations() =>
        new FakeSource()
            .Add("Crypt3(Clone)", 0f, 0f, footprint: 24f)
            .Add("TrollCave02(Clone)", 120f, 0f, footprint: 20f)
            .Add("BearCave(Clone)", 0f, 120f, footprint: 20f);

    private static SurveyEngine NewEngine() =>
        new() { Rules = SurveyRuleSet.Default(), MaxObservations = 200, BaseExclusionRadiusMeters = 0f };

    /// <summary>Runs whole sweeps until they complete, from each position
    /// in turn — the player walking past each dungeon.</summary>
    private static void SweepFrom(
        SurveySweep sweep,
        IReadOnlyList<ISurveySightingSource> sources,
        SurveyEngine engine,
        PinStore pins,
        params RoadPoint[] positions)
    {
        foreach (RoadPoint position in positions)
        {
            sweep.Restart();
            int guard = 0;
            while (!sweep.Completed && guard++ < 1000)
            {
                sweep.Tick(sources, position, ScanRadius, engine, pins, DateTime.UtcNow);
            }
        }
    }

    private static readonly RoadPoint[] AtEachDungeon =
    {
        new(0f, 0f, 0f),
        new(120f, 0f, 0f),
        new(0f, 0f, 120f),
    };

    [Fact]
    public void NetworkedSurfaceAlone_OffersNothingForTheReportedDungeons()
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { NetworkedObjectsNearDungeons() };

        SweepFrom(new SurveySweep(48), sources, engine, pins, AtEachDungeon);

        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void LoadedLocationSurface_OffersBurialCryptTrollCaveAndBearCave()
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource>
        {
            NetworkedObjectsNearDungeons(),
            LoadedDungeonLocations(),
        };

        SweepFrom(new SurveySweep(48), sources, engine, pins, AtEachDungeon);

        Assert.Equal(3, engine.Observations.Count);
        Assert.All(engine.Observations, observation =>
        {
            Assert.Equal("Dungeons", observation.Category);
            Assert.Equal("cc:dungeon", observation.IconId);
        });
        Assert.Contains(engine.Observations, o => o.PrefabName == "crypt3");
        Assert.Contains(engine.Observations, o => o.PrefabName == "trollcave02");
        Assert.Contains(engine.Observations, o => o.PrefabName == "bearcave");
    }

    [Theory]
    // The identities the reporter named, plus every other Valheim 1.0.12
    // dungeon-entrance location, as the game names the spawned instance.
    [InlineData("Crypt2(Clone)")]
    [InlineData("Crypt3(Clone)")]
    [InlineData("Crypt4(Clone)")]
    [InlineData("TrollCave02(Clone)")]
    [InlineData("BearCave(Clone)")]
    [InlineData("SunkenCrypt4(Clone)")]
    [InlineData("MountainCave02(Clone)")]
    [InlineData("HalfBurried_ForestCrypt(Clone)")]
    [InlineData("Hildir_crypt(Clone)")]
    [InlineData("Hildir_cave(Clone)")]
    public void DefaultRules_MatchEveryInstalledDungeonLocationIdentity(string instanceName)
    {
        Assert.True(SurveyRuleSet.Default().TryMatch(instanceName, out SurveyRule rule));
        Assert.Equal("Dungeons", rule.Category);
        Assert.Equal("cc:dungeon", rule.IconId);
    }

    [Theory]
    // Bear Cave was the identity no shipped rule could ever have matched.
    [InlineData("BearCave(Clone)")]
    [InlineData("HalfBurried_ForestCrypt(Clone)")]
    [InlineData("Hildir_crypt(Clone)")]
    [InlineData("Hildir_cave(Clone)")]
    public void ShippedV1Rules_CouldNotMatchTheseIdentities(string instanceName)
    {
        Assert.False(SurveyRuleSet.V1StarterSet().TryMatch(instanceName, out _));
    }

    [Theory]
    // Ordinary Valheim locations must stay silent: no rule, no observation.
    [InlineData("LocationProxy(Clone)")]
    [InlineData("WoodHouse5(Clone)")]
    [InlineData("Ruin1(Clone)")]
    [InlineData("StoneTowerRuins03(Clone)")]
    [InlineData("Greydwarf_camp1(Clone)")]
    [InlineData("SwampHut3(Clone)")]
    [InlineData("TarPit1(Clone)")]
    public void NonMatchingLocations_AreNeverOffered(string instanceName)
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource>
        {
            new FakeSource().Add(instanceName, 0f, 0f, footprint: 24f),
        };

        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void RepeatedSweeps_DoNotDuplicateTheSameDungeon()
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { LoadedDungeonLocations() };
        var sweep = new SurveySweep(48);

        for (int pass = 0; pass < 5; pass++)
        {
            SweepFrom(sweep, sources, engine, pins, AtEachDungeon);
        }

        Assert.Equal(3, engine.Observations.Count);
    }

    [Fact]
    public void BothSurfacesSeeingTheSameSite_YieldsOneObservation()
    {
        // A runestone is both a networked prefab and its own location;
        // the rule's duplicate radius must collapse the pair.
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource>
        {
            new FakeSource().Add("RuneStone_BlackForest(Clone)", 2f, 1f),
            new FakeSource().Add("Runestone_BlackForest(Clone)", 0f, 0f, footprint: 12f),
        };

        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Single(engine.Observations);
    }

    [Fact]
    public void AcceptingADungeonObservation_CreatesOneVisibleManagedPin()
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { LoadedDungeonLocations() };

        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 0f, 0f));
        SurveyEngine.Observation observation = Assert.Single(engine.Observations);
        Assert.True(engine.Accept(observation.Id, pins, out AtlasPin? created));

        Assert.NotNull(created);
        AtlasPin pin = Assert.Single(pins.Living);
        Assert.Equal(created!.Id, pin.Id);
        Assert.Equal(AtlasPinSource.Generated, pin.Source);
        Assert.Contains("surveyed", pin.Tags);
        Assert.Equal("cc:dungeon", pin.IconId);
        Assert.Equal("Dungeons", pin.Category);
        Assert.Equal(0f, pin.Position.X);
        Assert.Equal(0f, pin.Position.Z);

        // An accepted site never returns to the pending list.
        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 0f, 0f));
        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void RejectedDungeon_StaysSuppressedOnLaterSweeps()
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { LoadedDungeonLocations() };

        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 0f, 0f));
        SurveyEngine.Observation observation = Assert.Single(engine.Observations);
        Assert.True(engine.Reject(observation.Id, DateTime.UtcNow));

        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Empty(engine.Observations);
        Assert.Single(engine.Rejected);
    }

    [Fact]
    public void WorldSwitch_ResetsPendingButKeepsTheSiteDiscoverableAgain()
    {
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { LoadedDungeonLocations() };

        SweepFrom(new SurveySweep(48), sources, engine, pins, AtEachDungeon);
        Assert.Equal(3, engine.Observations.Count);

        engine.ResetSession();
        engine.LoadRejected(Array.Empty<SurveyEngine.RejectedObservation>());
        Assert.Empty(engine.Observations);

        SweepFrom(new SurveySweep(48), sources, engine, pins, AtEachDungeon);
        Assert.Equal(3, engine.Observations.Count);
    }

    [Fact]
    public void DungeonInteriorFiveThousandMetresUp_IsNotNearby()
    {
        // Valheim parks dungeon interiors 5000 m above the entrance; the
        // sweep's 3D range check must not treat that as "at the entrance".
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { LoadedDungeonLocations() };

        SweepFrom(new SurveySweep(48), sources, engine, pins, new RoadPoint(0f, 5000f, 0f));

        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void FootprintExtendsRangeButOnlyUpToTheClamp()
    {
        SurveyEngine inRange = NewEngine();
        var inRangePins = new PinStore();
        SweepFrom(
            new SurveySweep(48),
            new List<ISurveySightingSource> { new FakeSource().Add("Crypt3(Clone)", 0f, 0f, footprint: 24f) },
            inRange, inRangePins, new RoadPoint(0f, 0f, ScanRadius + 20f));
        Assert.Single(inRange.Observations);

        SurveyEngine clamped = NewEngine();
        var clampedPins = new PinStore();
        SweepFrom(
            new SurveySweep(48),
            new List<ISurveySightingSource> { new FakeSource().Add("Crypt3(Clone)", 0f, 0f, footprint: 100000f) },
            clamped, clampedPins,
            new RoadPoint(0f, 0f, ScanRadius + SurveySweep.MaxFootprintBonusMeters + 1f));
        Assert.Empty(clamped.Observations);
    }

    [Fact]
    public void NameIsReadOnlyForEntriesTheSweepAcceptedAsInRange()
    {
        // The networked surface pays a native interop call and a fresh
        // string per name, so the range test must run first. Before #258
        // the inline loop did exactly this; the extracted sweep must too.
        var far = new FakeSource();
        for (int index = 0; index < 200; index++)
        {
            far.Add("RaspberryBush(Clone)", 10000f + index, 0f);
        }

        far.Add("Crypt3(Clone)", 0f, 0f);

        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        SweepFrom(
            new SurveySweep(48, 1),
            new List<ISurveySightingSource> { far },
            engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Single(engine.Observations);
        Assert.Equal(1, far.NameReads);
    }

    [Fact]
    public void AFullPendingListCannotStarveTheLocationSurface()
    {
        // The engine cap ends a sweep. With the networked surface walked
        // first, a pending list full of berry bushes would mean a dungeon
        // under the player's feet is never reached — the very symptom of
        // #258. The scanner therefore walks locations first.
        var networked = new FakeSource();
        for (int index = 0; index < 50; index++)
        {
            networked.Add($"RaspberryBush{index}(Clone)", index * 0.5f, 0f);
        }

        var locations = new FakeSource().Add("Crypt3(Clone)", 1f, 1f, footprint: 24f);

        var engine = new SurveyEngine
        {
            Rules = SurveyRuleSet.Default(),
            MaxObservations = 3,
            BaseExclusionRadiusMeters = 0f,
        };
        var pins = new PinStore();

        // Locations first, exactly as SurveyScanner orders its sources.
        SweepFrom(
            new SurveySweep(48, 2),
            new List<ISurveySightingSource> { locations, networked },
            engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Contains(engine.Observations, o => o.PrefabName == "crypt3");
    }

    [Fact]
    public void PerSourceExaminedCountsAddUpInsteadOfDoubleCounting()
    {
        var networked = new FakeSource();
        for (int index = 0; index < 20; index++)
        {
            networked.Add("Rock_4(Clone)", index, 0f);
        }

        var locations = LoadedDungeonLocations();
        var sources = new List<ISurveySightingSource> { locations, networked };
        var sweep = new SurveySweep(48, 2);
        sweep.Restart();
        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        int guard = 0;
        while (!sweep.Completed && guard++ < 100)
        {
            sweep.Tick(sources, new RoadPoint(0f, 0f, 0f), ScanRadius, engine, pins, DateTime.UtcNow);
        }

        Assert.Equal(3, sweep.ExaminedFrom(0));
        Assert.Equal(20, sweep.ExaminedFrom(1));
        Assert.Equal(sweep.Examined, sweep.ExaminedFrom(0) + sweep.ExaminedFrom(1));
    }

    [Fact]
    public void SweepBudget_IsSharedAcrossSurfacesAndCoversThemAll()
    {
        var networked = new FakeSource();
        for (int index = 0; index < 100; index++)
        {
            networked.Add("Rock_4(Clone)", index, 0f);
        }

        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sources = new List<ISurveySightingSource> { networked, LoadedDungeonLocations() };
        var sweep = new SurveySweep(48);
        sweep.Restart();

        int ticks = 0;
        while (!sweep.Completed && ticks < 100)
        {
            int examinedBefore = sweep.Examined;
            sweep.Tick(sources, new RoadPoint(0f, 0f, 0f), ScanRadius, engine, pins, DateTime.UtcNow);
            Assert.True(sweep.Examined - examinedBefore <= 48);
            ticks++;
        }

        Assert.True(sweep.Completed);
        // 100 networked + 3 locations, all visited exactly once.
        Assert.Equal(103, sweep.Examined);
        Assert.True(ticks >= 3, "a 48-entry budget cannot cover 103 entries in fewer than three ticks");
        Assert.Equal(1, sweep.Added);
    }

    [Fact]
    public void ObservationCapEndsTheSweepInsteadOfSpinning()
    {
        var locations = new FakeSource();
        for (int index = 0; index < 20; index++)
        {
            locations.Add($"Crypt{index}(Clone)", index * 2f, 0f);
        }

        var engine = new SurveyEngine
        {
            Rules = SurveyRuleSet.Default(),
            MaxObservations = 3,
            BaseExclusionRadiusMeters = 0f,
        };
        var pins = new PinStore();
        var sweep = new SurveySweep(48);
        sweep.Restart();
        sweep.Tick(
            new List<ISurveySightingSource> { locations },
            new RoadPoint(0f, 0f, 0f), ScanRadius, engine, pins, DateTime.UtcNow);

        Assert.True(sweep.Completed);
        Assert.Equal(3, engine.Observations.Count);
    }

    [Fact]
    public void SkippedEntriesStillCountAgainstTheBudget()
    {
        var source = new FakeSource();
        for (int index = 0; index < 10; index++)
        {
            source.AddSkipped();
        }

        SurveyEngine engine = NewEngine();
        var pins = new PinStore();
        var sweep = new SurveySweep(4);
        sweep.Restart();
        sweep.Tick(
            new List<ISurveySightingSource> { source },
            new RoadPoint(0f, 0f, 0f), ScanRadius, engine, pins, DateTime.UtcNow);

        Assert.Equal(4, sweep.Examined);
        Assert.False(sweep.Completed);
    }

    /// <summary>The EXACT survey-rules.tsv that Cartographer 1.0.x and the
    /// pre-fix 1.1.0 wrote. This is a golden constant on purpose: the
    /// in-place upgrade only fires on a byte-identical match, so if
    /// V1StarterSet ever drifts from what shipped, every existing player
    /// silently keeps the broken rules and #258 re-opens for them. A
    /// self-comparison would not catch that; this does.</summary>
    private static readonly string[] ShippedV1RuleFile =
    {
        "# ConcernedCartographer survey rules v1",
        "# pattern<TAB>icon<TAB>category<TAB>duplicate-radius-m<TAB>expiry-minutes ('pattern*' = prefix, '!pattern' = never pin; an optional 6th field 'off' disables a rule)",
        "!piece_*",
        "!vfx_*",
        "!sfx_*",
        "!fx_*",
        "raspberrybush*\tcc:resource\tResources\t30\t120",
        "blueberrybush*\tcc:resource\tResources\t30\t120",
        "cloudberrybush*\tcc:resource\tResources\t30\t120",
        "pickable_mushroom*\tcc:resource\tResources\t30\t120",
        "pickable_thistle*\tcc:resource\tResources\t30\t120",
        "pickable_dandelion*\tcc:resource\tResources\t30\t120",
        "pickable_flint*\tcc:resource\tResources\t30\t120",
        "pickable_seedcarrot*\tcc:resource\tResources\t40\t120",
        "pickable_seedturnip*\tcc:resource\tResources\t40\t120",
        "pickable_seedonion*\tcc:resource\tResources\t40\t120",
        "gucksack*\tcc:resource\tResources\t40\t120",
        "beehive*\tcc:resource\tResources\t60\t240",
        "rock4_copper*\tcc:mine\tResources\t40\t240",
        "minerock_tin*\tcc:mine\tResources\t30\t240",
        "silvervein*\tcc:mine\tResources\t40\t240",
        "minerock_obsidian*\tcc:mine\tResources\t40\t240",
        "mudpile*\tcc:mine\tResources\t40\t240",
        "crypt*\tcc:dungeon\tDungeons\t80\t480",
        "sunkencrypt*\tcc:dungeon\tDungeons\t80\t480",
        "trollcave*\tcc:dungeon\tDungeons\t80\t480",
        "mountaincave*\tcc:dungeon\tDungeons\t80\t480",
        "runestone*\tcc:objective\tPoints of interest\t80\t480",
        "vegvisir*\tcc:objective\tPoints of interest\t80\t480",
    };

    [Fact]
    public void V1StarterSet_StillMatchesTheFileThatActuallyShipped()
    {
        Assert.Equal(ShippedV1RuleFile, SurveyRuleSet.V1StarterSet().Serialize().ToArray());
    }

    [Fact]
    public void UntouchedV1StarterFile_UpgradesToTheCorrectedDungeonRules()
    {
        // The in-place upgrade path the persistence layer uses: parse the
        // shipped file, confirm it normalizes back to itself (so the
        // equality check in SurveyRulePersistence fires), and confirm the
        // current Default is genuinely different where it matters.
        SurveyRuleSet reparsed = SurveyRuleSet.Parse(ShippedV1RuleFile, out int malformed);

        Assert.Equal(0, malformed);
        Assert.Equal(ShippedV1RuleFile, reparsed.Serialize().ToArray());
        Assert.False(reparsed.TryMatch("BearCave(Clone)", out _));
        Assert.True(SurveyRuleSet.Default().TryMatch("BearCave(Clone)", out _));
    }

    [Fact]
    public void EditedRuleFile_IsNotConfusedWithAStarterFile()
    {
        SurveyRuleSet edited = SurveyRuleSet.V1StarterSet();
        edited.AddRule(new SurveyRule("mypattern*", "cc:resource", "Resources", 10f, 60f));

        string[] editedRows = edited.Serialize().ToArray();

        Assert.NotEqual(SurveyRuleSet.V1StarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.Rc8StarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.LegacyStarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.Default().Serialize().ToArray(), editedRows);
    }

    [Fact]
    public void DisabledDungeonRule_StopsOfferingThatDungeon()
    {
        SurveyRuleSet rules = SurveyRuleSet.Default();
        for (int index = 0; index < rules.Rules.Count; index++)
        {
            if (rules.Rules[index].Pattern == "bearcave*")
            {
                rules.SetRuleEnabled(index, enabled: false);
            }
        }

        var engine = new SurveyEngine { Rules = rules, MaxObservations = 200, BaseExclusionRadiusMeters = 0f };
        var pins = new PinStore();

        SweepFrom(
            new SurveySweep(48),
            new List<ISurveySightingSource> { LoadedDungeonLocations() },
            engine, pins, AtEachDungeon);

        Assert.Equal(2, engine.Observations.Count);
        Assert.DoesNotContain(engine.Observations, o => o.PrefabName == "bearcave");
    }

    [Fact]
    public void BaseExclusionStillSuppressesADungeonInsideTheHomestead()
    {
        SurveyEngine engine = NewEngine();
        engine.BaseExclusionRadiusMeters = 30f;
        var pins = new PinStore();
        pins.Create(pin =>
        {
            pin.Name = "Home";
            pin.Category = "Base";
            pin.Position = new RoadPoint(5f, 0f, 5f);
        });

        SweepFrom(
            new SurveySweep(48),
            new List<ISurveySightingSource> { LoadedDungeonLocations() },
            engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Empty(engine.Observations);
    }
}
