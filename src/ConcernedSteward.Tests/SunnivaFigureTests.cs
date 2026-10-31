using System;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;
using TheConcernedCat.ConcernedSteward.Domain.Npc;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>#382: Sunniva at rest as a player-style figure, and the rule that
/// the figure and her worker body never coexist. The decision table is tested
/// on its own, the leases against the real Concerned NPC arbiter, and the
/// runtime's ordering (figure down before every worker claim and spawn) from
/// its source, because the runtime is not compiled here.</summary>
public sealed class SunnivaFigureTests
{
    private static readonly string DataRoot = Path.Combine(Path.GetTempPath(), "cs-steward-figure-tests");

    private static SunnivaBodyFacts AtHome(
        bool registered = true,
        bool worldLoaded = true,
        bool isHost = true,
        bool runtimeEnabled = true,
        bool showAtHome = true,
        bool settled = true,
        int savedWorkerBodies = 0,
        bool resting = true,
        bool homeKnown = true,
        bool playerNearHome = true) =>
        new SunnivaBodyFacts(
            registered, worldLoaded, isHost, runtimeEnabled, showAtHome, settled,
            savedWorkerBodies, resting, homeKnown, playerNearHome);

    private static StewardNpcAdoption Adopted(out NpcRoleRegistry registry)
    {
        registry = new NpcRoleRegistry();
        var adoption = new StewardNpcAdoption(registry, new StewardNpcRole(DataRoot));
        Assert.True(adoption.Register().IsRegistered);
        adoption.NoteWorldLoaded();
        return adoption;
    }

    // --- the decision ---------------------------------------------------------------------------------

    [Fact]
    public void Settled_resting_at_home_with_no_saved_body_is_the_figure()
    {
        Assert.True(SunnivaBodyPlan.Decide(AtHome()).ShowFigure);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_saved_worker_body_anywhere_in_the_world_outranks_everything(int bodies)
    {
        Assert.False(SunnivaBodyPlan.Decide(AtHome(savedWorkerBodies: bodies)).ShowFigure);
        Assert.False(SunnivaBodyPlan.Decide(AtHome(savedWorkerBodies: bodies, isHost: false)).ShowFigure);
    }

    [Fact]
    public void Every_unknown_shows_nothing_and_says_why()
    {
        foreach (SunnivaBodyFacts facts in new[]
                 {
                     AtHome(registered: false), AtHome(worldLoaded: false), AtHome(isHost: false),
                     AtHome(savedWorkerBodies: -1), AtHome(runtimeEnabled: false), AtHome(showAtHome: false),
                     AtHome(settled: false), AtHome(resting: false), AtHome(homeKnown: false),
                     AtHome(playerNearHome: false),
                 })
        {
            SunnivaBodyDecision decision = SunnivaBodyPlan.Decide(facts);
            Assert.False(decision.ShowFigure);
            Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        }
    }

    [Fact]
    public void A_client_never_shows_her_because_it_cannot_see_every_saved_body()
    {
        Assert.False(SunnivaBodyPlan.Decide(AtHome(isHost: false, savedWorkerBodies: 0)).ShowFigure);
    }

    // --- the contract ---------------------------------------------------------------------------------

    [Fact]
    public void Her_contract_admits_a_figure_with_the_shipped_prefab_and_keys_unchanged()
    {
        var role = new StewardNpcRole(DataRoot);

        Assert.Equal("CS_Steward", role.Body.PrefabName);
        Assert.Equal("tcc.steward.", role.Body.ZdoKeyPrefix);
        Assert.Equal(NpcBodyKind.Worker, role.Body.Kind);
        Assert.True(role.Body.Admits(NpcBodyKind.Worker));
        Assert.True(role.Body.Admits(NpcBodyKind.Presentation));
        Assert.Equal("steward/steward", role.Identity.Value);
    }

    // --- the leases, against the real arbiter --------------------------------------------------------

    [Fact]
    public void The_figure_is_granted_while_she_rests_with_no_body()
    {
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var figure = new SunnivaFigureLease(registry, adoption.Identity);

        Assert.True(figure.TryClaim(), figure.LastRefusal);
        Assert.True(figure.Holds);
        Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(adoption.Identity));
    }

    [Fact]
    public void While_her_worker_body_is_held_no_figure_may_be_claimed()
    {
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        Assert.True(adoption.ClaimBody().IsGranted);
        var figure = new SunnivaFigureLease(registry, adoption.Identity);

        Assert.False(figure.TryClaim());
        Assert.False(figure.Holds);
        Assert.NotEmpty(figure.LastRefusal);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(adoption.Identity));
    }

    [Fact]
    public void While_the_figure_stands_her_worker_lease_is_refused_until_the_figure_yields()
    {
        // The order the runtime keeps: figure down and its lease released, then
        // the worker claim. Asked the other way round the worker claim fails -
        // which is why YieldToWorker comes first.
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var figure = new SunnivaFigureLease(registry, adoption.Identity);
        Assert.True(figure.TryClaim());

        Assert.False(adoption.ClaimBody().IsGranted);

        figure.Release();
        Assert.True(adoption.ClaimBody().IsGranted);
        Assert.False(figure.Holds);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(adoption.Identity));
    }

    [Fact]
    public void After_her_worker_body_is_released_the_figure_may_return()
    {
        // S1's retire releases the worker hold; the figure then follows.
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        Assert.True(adoption.ClaimBody().IsGranted);
        adoption.ReleaseBody();
        var figure = new SunnivaFigureLease(registry, adoption.Identity);

        Assert.True(figure.TryClaim(), figure.LastRefusal);
    }

    [Fact]
    public void No_walk_through_the_transitions_ever_ends_holding_both()
    {
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var figure = new SunnivaFigureLease(registry, adoption.Identity);

        for (int round = 0; round < 3; round++)
        {
            figure.TryClaim();
            Assert.False(figure.Holds && adoption.HoldsBody);
            adoption.ClaimBody();
            Assert.False(figure.Holds && adoption.HoldsBody);
            figure.Release();
            adoption.ClaimBody();
            Assert.False(figure.Holds && adoption.HoldsBody);
            adoption.ReleaseBody();
            Assert.False(figure.Holds && adoption.HoldsBody);
        }
    }

    // --- her look ------------------------------------------------------------------------------------

    [Fact]
    public void Her_figure_is_female_with_Matronly_hair_no_beard_and_leather()
    {
        NpcLookSpec spec = SunnivaLookPlan.Spec(null, null, null);

        Assert.Equal(NpcBodyModel.Female, spec.Model);
        Assert.NotNull(spec.Hair);
        Assert.Equal("Matronly", spec.Hair!.DisplayName);
        Assert.Equal("$customization_hair12", spec.Hair.Token);
        Assert.Null(spec.Beard);
        Assert.Equal("ArmorLeatherChest", spec.Chest!.PrefabCandidates[0]);
        Assert.Equal("ArmorLeatherLegs", spec.Legs!.PrefabCandidates[0]);
    }

    [Fact]
    public void Her_figure_is_fair_and_light_blonde_lighter_than_Hulgi()
    {
        // Hulgi's reference is skin 0.50 and hair level 0.74 (Concerned
        // Cartographer); she is asked for fairer skin and blonder hair.
        Assert.True(SunnivaLookColour.SkinHue < 0.50f);
        Assert.True(SunnivaLookColour.HairLevel > 0.74f);
    }

    [Fact]
    public void The_players_overrides_pass_through()
    {
        NpcLookSpec spec = SunnivaLookPlan.Spec("Hair3", "ArmorRagsChest", "ArmorRagsLegs");

        Assert.Equal("Hair3", spec.HairOverride);
        Assert.Equal("ArmorRagsChest", spec.ChestOverride);
        Assert.Equal("ArmorRagsLegs", spec.LegsOverride);
        Assert.Null(spec.BeardOverride);
    }

    // --- the runtime's ordering, from its source -----------------------------------------------------

    private static string Runtime(string file) =>
        ProductSources.WithoutComments(File.ReadAllText(Path.Combine(ProductSources.Product, "Runtime", file)));

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " is gone");
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end + " is gone");
        return text.Substring(from, to - from);
    }

    [Fact]
    public void The_figure_yields_before_every_worker_claim()
    {
        string census = Between(Runtime("StewardRuntime.cs"), "private void RefreshCensus()", "private void BindBody(");
        int yields = census.IndexOf("_presentation.YieldToWorker();", StringComparison.Ordinal);
        int claims = census.IndexOf("_npc.ClaimBody();", StringComparison.Ordinal);

        Assert.True(yields >= 0 && claims > yields, "the worker lease is asked for before the figure has yielded");
    }

    [Fact]
    public void An_unidentified_or_uncounted_body_makes_the_count_unknown()
    {
        // A CS_Steward body whose identity stamp failed might be hers: the
        // figure's count is then unknown (-1), which the plan refuses.
        string inputs = Between(Runtime("StewardRuntime.cs"), "private SunnivaFigureInputs FigureInputs()", "bool settled");
        Assert.Contains("_census.Verdict == StewardCensusVerdict.Unknown || _census.Unidentified > 0", inputs);
        Assert.Contains("? -1", inputs);
    }

    [Fact]
    public void The_figure_yields_before_her_worker_body_is_spawned()
    {
        string ensure = Between(Runtime("StewardRuntime.cs"), "private string EnsureBody(", "internal string Status()");
        int yields = ensure.IndexOf("_presentation.YieldToWorker();", StringComparison.Ordinal);
        int spawns = ensure.IndexOf("StewardWorkerPrefab.Spawn(", StringComparison.Ordinal);

        Assert.True(yields >= 0 && spawns > yields, "her worker body can appear while the figure stands");
    }

    [Fact]
    public void The_figure_goes_before_the_world_does_and_while_the_runtime_is_off()
    {
        string runtime = Runtime("StewardRuntime.cs");
        string unload = Between(runtime, "internal void OnWorldUnloaded()", "_resin.Forget();");
        int figure = unload.IndexOf("_presentation.OnWorldUnloaded();", StringComparison.Ordinal);
        int world = unload.IndexOf("_npc.NoteWorldUnloaded();", StringComparison.Ordinal);
        Assert.True(figure >= 0 && world > figure, "the figure's lease could end with the world while it stands");

        string update = Between(runtime, "internal void Update()", "EnsureRestockProbed();");
        Assert.Contains("_presentation.YieldToWorker();", update);
    }

    [Fact]
    public void The_figure_is_built_only_with_its_lease_held_and_yields_down_before_its_lease()
    {
        string presentation = Runtime("SunnivaPresentation.cs");
        Assert.Contains("if (_figure == null && !_buildFailedThisWorld && _lease.Holds && player != null)", presentation);

        string yieldBody = Between(presentation, "internal void YieldToWorker()", "internal void OnWorldLoaded()");
        int down = yieldBody.IndexOf("TakeDown();", StringComparison.Ordinal);
        int release = yieldBody.IndexOf("_lease.Release();", StringComparison.Ordinal);
        Assert.True(down >= 0 && release > down, "the lease can be given up while the figure still stands");

        // Built from the game's character prefab, extracted dark by the library;
        // never a live Player stripped afterwards.
        Assert.Contains("NpcPresentationBody.TryExtract(", presentation);
        Assert.DoesNotContain("AddComponent<Player>", presentation);
    }

    [Fact]
    public void Her_figure_is_dressed_and_placed_only_by_the_library()
    {
        string presentation = Runtime("SunnivaPresentation.cs");
        Assert.Contains("new NpcFigureDresser(", presentation);
        Assert.Contains("_dresser.StandAt(", presentation);
        Assert.DoesNotContain(".position =", presentation);
        Assert.DoesNotContain(".rotation =", presentation);
    }

    [Fact]
    public void The_plugin_requires_the_Concerned_NPC_that_has_the_dresser()
    {
        string plugin = File.ReadAllText(Path.Combine(ProductSources.Product, "Plugin.cs"));
        Assert.Contains(
            "[BepInDependency(\"com.theconcernedcat.valheim.concernednpc\", \"0.5.0\")]", plugin);
        Assert.Contains("NpcCustomizationPalette.Poll(", plugin);
    }
}
