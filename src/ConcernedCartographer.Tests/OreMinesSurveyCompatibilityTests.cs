using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>Issue #385 (community report, kyoknightly): the mines added by
/// blacks7ar's OreMines are never surveyed or auto-pinned.
///
/// They are ordinary Valheim <c>Location</c> instances, so they already
/// ride the bounded loaded-location surface issue #258 added — nothing
/// about discovery was missing. They simply matched no survey rule, and a
/// rule that matches nothing is a silent no-op. So the whole fix is rule
/// identity plus the existing safe starter-file migration, and these tests
/// pin down both halves, including the half that can hurt somebody: a
/// player's hand-edited rules file must never be rewritten.
///
/// The identities below are the eight mine types OreMines 1.2.1 documents
/// for its UpgradeWorld commands, each placed as an "01" and an "02"
/// variant. They are NAMES ONLY: no assembly reference, no
/// BepInDependency, no foreign type is touched anywhere, so with OreMines
/// absent these rules are inert text (proved by
/// <see cref="WithoutOreMines_TheVanillaSurveyIsCompletelyUnchanged"/>).
///
/// What these tests do NOT prove: that a live OreMines install names its
/// spawned locations exactly this way. That is an in-game observation and
/// is still owed.</summary>
public class OreMinesSurveyCompatibilityTests
{
    private const float ScanRadius = 40f;

    /// <summary>Every documented OreMines 1.2.1 mine location, as Unity
    /// names an instantiated location prefab.</summary>
    public static TheoryData<string> DocumentedMineInstances() => new()
    {
        "BOM_FlintMine01(Clone)",
        "BOM_FlintMine02(Clone)",
        "BOM_CopperMine01(Clone)",
        "BOM_CopperMine02(Clone)",
        "BOM_TinMine01(Clone)",
        "BOM_TinMine02(Clone)",
        "BOM_CoalMine01(Clone)",
        "BOM_CoalMine02(Clone)",
        "BOM_IronMine01(Clone)",
        "BOM_IronMine02(Clone)",
        "BOM_SilverMine01(Clone)",
        "BOM_SilverMine02(Clone)",
        "BOM_BlackMetalMine01(Clone)",
        "BOM_BlackMetalMine02(Clone)",
        "BOM_FlametalMine01(Clone)",
        "BOM_FlametalMine02(Clone)",
    };

    private sealed class FakeSource : ISurveySightingSource
    {
        private readonly List<SurveySighting> _entries = new();

        public int Count => _entries.Count;

        public FakeSource Add(string name, float x, float z, float footprint = 0f)
        {
            _entries.Add(new SurveySighting(name, new RoadPoint(x, 0f, z), footprint));
            return this;
        }

        public bool TryReadPlacement(int index, out RoadPoint position, out float footprintRadiusMeters)
        {
            position = _entries[index].Position;
            footprintRadiusMeters = _entries[index].FootprintRadiusMeters;
            return true;
        }

        public bool TryReadName(int index, out string name)
        {
            name = _entries[index].Name;
            return true;
        }
    }

    private static SurveyEngine NewEngine(SurveyRuleSet rules) =>
        new() { Rules = rules, MaxObservations = 200, BaseExclusionRadiusMeters = 0f };

    private static void SweepFrom(
        IReadOnlyList<ISurveySightingSource> sources,
        SurveyEngine engine,
        PinStore pins,
        params RoadPoint[] positions)
    {
        var sweep = new SurveySweep(48);
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

    [Theory]
    [MemberData(nameof(DocumentedMineInstances))]
    public void DefaultRules_MatchEveryDocumentedOreMinesIdentity(string instanceName)
    {
        Assert.True(SurveyRuleSet.Default().TryMatch(instanceName, out SurveyRule rule));
        Assert.Equal("cc:mine", rule.IconId);
        Assert.Equal("Resources", rule.Category);
    }

    [Theory]
    [MemberData(nameof(DocumentedMineInstances))]
    public void ShippedRules_CouldNotHaveMatchedAnyOreMinesIdentity(string instanceName)
    {
        // Why the reporter saw nothing: every starter set shipped so far
        // is silent on these names, so no amount of walking past a mine
        // could have produced an observation.
        Assert.False(SurveyRuleSet.V103StarterSet().TryMatch(instanceName, out _));
        Assert.False(SurveyRuleSet.V1StarterSet().TryMatch(instanceName, out _));
        Assert.False(SurveyRuleSet.Rc8StarterSet().TryMatch(instanceName, out _));
        Assert.False(SurveyRuleSet.LegacyStarterSet().TryMatch(instanceName, out _));
    }

    [Fact]
    public void OreMines_AreOfferedThroughTheSameLoadedLocationSweep()
    {
        // The three mine types the reporter said he had to pin by hand,
        // fed through the surface Cartographer already walks: a loaded
        // Location snapshot, the shared per-tick budget, the same engine.
        var locations = new FakeSource()
            .Add("BOM_CopperMine01(Clone)", 0f, 0f, footprint: 16f)
            .Add("BOM_TinMine02(Clone)", 200f, 0f, footprint: 16f)
            .Add("BOM_SilverMine01(Clone)", 0f, 200f, footprint: 16f);

        SurveyEngine engine = NewEngine(SurveyRuleSet.Default());
        var pins = new PinStore();

        SweepFrom(
            new List<ISurveySightingSource> { locations },
            engine, pins,
            new RoadPoint(0f, 0f, 0f), new RoadPoint(200f, 0f, 0f), new RoadPoint(0f, 0f, 200f));

        Assert.Equal(3, engine.Observations.Count);
        Assert.All(engine.Observations, observation => Assert.Equal("cc:mine", observation.IconId));
        Assert.Contains(engine.Observations, o => o.PrefabName == "bom_coppermine01");
        Assert.Contains(engine.Observations, o => o.PrefabName == "bom_tinmine02");
        Assert.Contains(engine.Observations, o => o.PrefabName == "bom_silvermine01");
    }

    [Fact]
    public void OreMines_KeepTheExistingRangeAndDuplicateBounds()
    {
        // Nothing about the budgets moves for issue #385: a mine outside
        // the configured scan radius is not discovered (no world-database
        // lookup, no fog reveal), and the same mine seen twice inside its
        // 80 m duplicate radius is offered once.
        var locations = new FakeSource()
            .Add("BOM_CopperMine01(Clone)", 0f, 0f, footprint: 16f)
            .Add("BOM_CopperMine01(Clone)", 30f, 0f, footprint: 16f)
            .Add("BOM_IronMine01(Clone)", 4000f, 4000f, footprint: 16f);

        SurveyEngine engine = NewEngine(SurveyRuleSet.Default());
        var pins = new PinStore();

        SweepFrom(new List<ISurveySightingSource> { locations }, engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Single(engine.Observations);
        Assert.Equal("bom_coppermine01", engine.Observations[0].PrefabName);
        Assert.DoesNotContain(engine.Observations, o => o.PrefabName.Contains("iron"));
    }

    [Theory]
    // The rules are per mine type on purpose. A blanket "bom_*" would pin
    // every prop, piece and creature the mod ships, and the mod author's
    // prefix is shared across his other mods.
    [InlineData("BOM_Chest(Clone)")]
    [InlineData("BOM_MineCart(Clone)")]
    [InlineData("BOM_Mine(Clone)")]
    [InlineData("BOM_FlintPile(Clone)")]
    [InlineData("MineEntrance(Clone)")]
    [InlineData("CopperMine(Clone)")]
    public void NamesOutsideTheDocumentedMineList_AreNeverOffered(string instanceName)
    {
        SurveyEngine engine = NewEngine(SurveyRuleSet.Default());
        var pins = new PinStore();

        SweepFrom(
            new List<ISurveySightingSource> { new FakeSource().Add(instanceName, 0f, 0f, footprint: 16f) },
            engine, pins, new RoadPoint(0f, 0f, 0f));

        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void WithoutOreMines_TheVanillaSurveyIsCompletelyUnchanged()
    {
        // A player who does not run OreMines must get exactly the survey
        // he got before: same objects, same icons, same categories, same
        // order. The new rules must not outrank a vanilla rule.
        var vanillaWorld = new FakeSource()
            .Add("Crypt3(Clone)", 0f, 0f, footprint: 24f)
            .Add("BearCave(Clone)", 10f, 0f, footprint: 20f)
            .Add("rock4_copper(Clone)", 12f, 4f)
            .Add("MineRock_Tin(Clone)", 14f, 6f)
            .Add("silvervein(Clone)", 16f, 8f)
            .Add("Pickable_Flint(Clone)", 18f, 2f)
            .Add("RaspberryBush(Clone)", 20f, 2f)
            .Add("Vegvisir(Clone)", 22f, 2f);

        var before = NewEngine(SurveyRuleSet.V103StarterSet());
        var after = NewEngine(SurveyRuleSet.Default());
        SweepFrom(new List<ISurveySightingSource> { vanillaWorld }, before, new PinStore(), new RoadPoint(0f, 0f, 0f));
        SweepFrom(new List<ISurveySightingSource> { vanillaWorld }, after, new PinStore(), new RoadPoint(0f, 0f, 0f));

        Assert.Equal(
            before.Observations.Select(o => $"{o.PrefabName}|{o.IconId}|{o.Category}").ToArray(),
            after.Observations.Select(o => $"{o.PrefabName}|{o.IconId}|{o.Category}").ToArray());
        Assert.NotEmpty(after.Observations);
    }

    /// <summary>The EXACT survey-rules.tsv that Cartographer wrote from
    /// v1.0.3 (the issue #258 dungeon fix) through v1.2.2. A golden
    /// constant on purpose, exactly as the #258 fix did before: the
    /// in-place upgrade only fires on a byte-identical match, so if
    /// <see cref="SurveyRuleSet.V103StarterSet"/> ever drifts from what
    /// shipped, every current player silently keeps a rules file with no
    /// OreMines identities in it and #385 stays open for them. A
    /// self-comparison would not catch that; this does.</summary>
    private static readonly string[] ShippedV103RuleFile =
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
        "halfburried_forestcrypt*\tcc:dungeon\tDungeons\t80\t480",
        "hildir_crypt*\tcc:dungeon\tDungeons\t80\t480",
        "sunkencrypt*\tcc:dungeon\tDungeons\t80\t480",
        "trollcave*\tcc:dungeon\tDungeons\t80\t480",
        "bearcave*\tcc:dungeon\tDungeons\t80\t480",
        "mountaincave*\tcc:dungeon\tDungeons\t80\t480",
        "hildir_cave*\tcc:dungeon\tDungeons\t80\t480",
        "runestone*\tcc:objective\tPoints of interest\t80\t480",
        "vegvisir*\tcc:objective\tPoints of interest\t80\t480",
    };

    [Fact]
    public void V103StarterSet_StillMatchesTheFileThatActuallyShipped()
    {
        Assert.Equal(ShippedV103RuleFile, SurveyRuleSet.V103StarterSet().Serialize().ToArray());
    }

    [Fact]
    public void UntouchedCurrentStarterFile_IsUpgradedAndGainsTheMineIdentities()
    {
        SurveyRuleSet onDisk = SurveyRuleSet.Parse(ShippedV103RuleFile, out int malformed);

        Assert.Equal(0, malformed);
        Assert.Equal(ShippedV103RuleFile, onDisk.Serialize().ToArray());
        Assert.False(onDisk.TryMatch("BOM_CopperMine01(Clone)", out _));
        Assert.True(SurveyStarterUpgrade.ShouldUpgrade(ShippedV103RuleFile));
        Assert.True(SurveyRuleSet.Default().TryMatch("BOM_CopperMine01(Clone)", out _));
    }

    [Fact]
    public void EveryEarlierUntouchedStarterFile_StillUpgrades()
    {
        Assert.True(SurveyStarterUpgrade.ShouldUpgrade(SurveyRuleSet.LegacyStarterSet().Serialize()));
        Assert.True(SurveyStarterUpgrade.ShouldUpgrade(SurveyRuleSet.Rc8StarterSet().Serialize()));
        Assert.True(SurveyStarterUpgrade.ShouldUpgrade(SurveyRuleSet.V1StarterSet().Serialize()));
    }

    [Fact]
    public void AFileThatIsAlreadyCurrent_IsNotRewritten()
    {
        Assert.False(SurveyStarterUpgrade.ShouldUpgrade(SurveyRuleSet.Default().Serialize()));
    }

    public static TheoryData<string, string[]> EditedRuleFiles()
    {
        var data = new TheoryData<string, string[]>();

        // One rule added at the end — the "I added my own pattern" case,
        // and literally what the reporter did as his workaround.
        SurveyRuleSet added = SurveyRuleSet.V103StarterSet();
        added.AddRule(new SurveyRule("bom_coppermine*", "cc:mine", "Resources", 40f, 240f));
        data.Add("added a rule", added.Serialize().ToArray());

        // One rule removed.
        SurveyRuleSet removed = SurveyRuleSet.V103StarterSet();
        removed.RemoveRuleAt(0);
        data.Add("removed a rule", removed.Serialize().ToArray());

        // One rule switched off from the Survey panel.
        SurveyRuleSet disabled = SurveyRuleSet.V103StarterSet();
        disabled.SetRuleEnabled(0, enabled: false);
        data.Add("disabled a rule", disabled.Serialize().ToArray());

        // One bound retuned.
        var retuned = new List<string>(ShippedV103RuleFile);
        retuned[retuned.IndexOf("beehive*\tcc:resource\tResources\t60\t240")] =
            "beehive*\tcc:resource\tResources\t120\t240";
        data.Add("retuned a bound", retuned.ToArray());

        // One blacklist row added.
        var blacklisted = new List<string>(ShippedV103RuleFile);
        blacklisted.Insert(2, "!beehive*");
        data.Add("added a blacklist row", blacklisted.ToArray());

        // A comment of his own at the top of the file.
        var commented = new List<string>(ShippedV103RuleFile);
        commented.Insert(1, "# my rules, do not touch");
        data.Add("added a comment", commented.ToArray());

        // An edited file that is also several releases old: still his.
        SurveyRuleSet oldAndEdited = SurveyRuleSet.V1StarterSet();
        oldAndEdited.AddRule(new SurveyRule("mypattern*", "cc:resource", "Resources", 10f, 60f));
        data.Add("edited an older starter file", oldAndEdited.Serialize().ToArray());

        return data;
    }

    [Theory]
    [MemberData(nameof(EditedRuleFiles))]
    public void AnEditedRuleFile_IsNeverUpgraded(string edit, string[] fileLines)
    {
        // The sharp constraint. survey-rules.tsv is the player's document
        // and the shareable import/export format; a shipped rule addition
        // must never cost him his edits, however small the edit was.
        Assert.False(SurveyStarterUpgrade.ShouldUpgrade(fileLines), edit);
    }

    [Fact]
    public void AnEditedFile_IsNotConfusedWithAnyStarterSnapshot()
    {
        SurveyRuleSet edited = SurveyRuleSet.V103StarterSet();
        edited.AddRule(new SurveyRule("mypattern*", "cc:resource", "Resources", 10f, 60f));
        string[] editedRows = edited.Serialize().ToArray();

        Assert.NotEqual(SurveyRuleSet.V103StarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.V1StarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.Rc8StarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.LegacyStarterSet().Serialize().ToArray(), editedRows);
        Assert.NotEqual(SurveyRuleSet.Default().Serialize().ToArray(), editedRows);
    }

    [Fact]
    public void TheNewStarterSet_DiffersFromTheOneItReplaces()
    {
        // The upgrade recognizes files by exact content, so the two sets
        // must serialize differently and both must parse cleanly.
        string[] current = SurveyRuleSet.V103StarterSet().Serialize().ToArray();
        string[] updated = SurveyRuleSet.Default().Serialize().ToArray();

        Assert.NotEqual(current, updated);
        SurveyRuleSet.Parse(current, out int malformedCurrent);
        SurveyRuleSet.Parse(updated, out int malformedUpdated);
        Assert.Equal(0, malformedCurrent);
        Assert.Equal(0, malformedUpdated);
    }
}
