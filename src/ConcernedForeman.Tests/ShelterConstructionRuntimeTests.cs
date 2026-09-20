using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>The whole chain, driven end to end through the REAL composition: the
/// order desk, the real recipe reader, the real placement gate, the real
/// <see cref="WorldPiecePlacer"/>, the real <see cref="HostPlayerPieceInstaller"/>
/// calling the stubbed game's own <c>PlacePiece</c>, and the real material
/// adapter over the real inventory ports.
///
/// <b>Why this test and the reachability test are both needed.</b> This one
/// proves the chain WORKS; the reachability test proves it is CONNECTED to the
/// plugin. A behavioural test is itself a caller, so on its own it would have
/// passed just as happily at <c>49bb361</c> against a placer nothing shipped ever
/// reached.
///
/// <b>The one stand-in, and why it is honest.</b> The stubbed
/// <c>Player.PlacePiece</c> records the call and does not instantiate anything,
/// because instantiating a Unity prefab outside the player is not possible. So the
/// installer here delegates to the real one and then registers a piece in the
/// stub world, standing in for the object vanilla's own instantiate would have
/// created. Everything that decides is real; only the engine's act of creation is
/// modelled - and the real <c>PlacePiece</c> call is still made and still
/// asserted, booleans included.</summary>
public sealed class ShelterConstructionRuntimeTests : IDisposable
{
    private static readonly WorkerKey Thorstein = new WorkerKey("foreman", "thorstein");

    private readonly List<string> _log = new List<string>();
    private readonly FakeModes _modes = new FakeModes(Thorstein);
    private readonly FakeMotion _motion = new FakeMotion(Thorstein);
    private readonly FakePose _pose = new FakePose();
    private readonly WorkerBody _body;
    private readonly Container _chest;
    private readonly FakeCustody _custody;
    private readonly BuildOrderRuntime _orders;
    private readonly ShelterConstructionRuntime _runtime;
    private readonly StandingInstaller _installer;

    private float _now = 100f;
    private bool _mayWork = true;

    public ShelterConstructionRuntimeTests()
    {
        Reset();
        Game.instance ??= new Game();

        // The four vanilla pieces of the shelter, with real requirement arrays.
        PiecePrefab("wood_floor", ("Wood", 2));
        PiecePrefab("wood_wall", ("Wood", 2));
        PiecePrefab("wood_door", ("Wood", 4));
        PiecePrefab("bed", ("Wood", 8), ("DeerHide", 4));

        var host = new GameObject("Player");
        Player player = host.Add(new Player());
        player.transform.position = Vector3.zero;
        Player.m_localPlayer = player;

        _body = ForemanFixtures.Body();
        _chest = ForemanFixtures.Chest();
        _chest.transform.position = new Vector3(20f, 0f, 0f);
        Fill("Wood", 200);
        Fill("DeerHide", 20);

        _custody = new FakeCustody(
            new WorkerInventoryPort(_body, () => 1000f),
            new ContainerInventoryPort(
                _chest, ForemanFixtures.KeyOf(_chest), () => _motion.Position, 3f));

        _orders = new BuildOrderRuntime(() => _mayWork, () => "not the host", _log.Add);
        _installer = new StandingInstaller();
        _runtime = new ShelterConstructionRuntime(
            _orders,
            _modes,
            _custody,
            _motion,
            () => _mayWork,
            () => new SupplyChest(ForemanFixtures.KeyOf(_chest), new SitePoint(20f, 0f, 0f)),
            () => _now,
            _log.Add,
            _pose,
            _installer);
    }

    public void Dispose()
    {
        Reset();
        WorkerBody.Loaded = null;
        WorkerBody.Died = null;
        WorkerBody.ErrorLog = null;
    }

    private static void Reset()
    {
        Piece.s_allPieces.Clear();
        CraftingStation.InRange.Clear();
        ZoneSystem.instance = new ZoneSystem();
        ZNetScene.instance = new ZNetScene();
        PrivateArea.Access = true;
        Player.m_localPlayer = null;
        Character.Interiors.Clear();
        Location.NoBuild.Clear();
        Heightmap.Here = Heightmap.Biome.Meadows;
    }

    private static GameObject Item(string name)
    {
        var host = new GameObject(name);
        host.Add(new ItemDrop());
        return host;
    }

    private static void PiecePrefab(string name, params (string Item, int Amount)[] costs)
    {
        var host = new GameObject(name);
        Piece piece = host.Add(new Piece());
        var requirements = new List<Piece.Requirement>();
        foreach ((string item, int amount) in costs)
        {
            requirements.Add(new Piece.Requirement
            {
                m_resItem = Item(item).GetComponent<ItemDrop>(),
                m_amount = amount,
            });
        }

        piece.m_resources = requirements.ToArray();
        ZNetScene.instance!.Prefabs[name] = host;
    }

    private void Fill(string item, int count)
    {
        ForemanFixtures.Prefab(item);
        _chest.GetInventory()!.AddItem(ForemanFixtures.Stack(new MaterialItem(item, 1, 0), count));
    }

    private int InChest(string item) =>
        EngineInventoryPort.CountIn(_chest.GetInventory()!, new MaterialItem(item, 1, 0));

    private int OnWorker(string item) =>
        EngineInventoryPort.CountIn(_body.Inventory!, new MaterialItem(item, 1, 0));

    private void Confirm()
    {
        _orders.Execute(new[] { "here" });
        string said = _orders.Execute(new[] { "confirm" });
        Assert.Contains("Authorised", said);
    }

    private int Said(string fragment)
    {
        int count = 0;
        foreach (string line in _log)
        {
            if (line.Contains(fragment, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private void Ticks(int count, float step = 2f)
    {
        for (int tick = 0; tick < count; tick++)
        {
            _now += step;
            _runtime.Tick();
        }
    }

    // ---- the whole thing -------------------------------------------------

    [Fact]
    public void A_confirmed_order_is_built_through_the_real_gate_and_the_real_placer()
    {
        Confirm();

        Ticks(80);

        Assert.False(_runtime.IsFaulted, string.Join(" | ", _log));
        Assert.Equal(BuildStep.Finished, _runtime.Loop.Step);
        Assert.Equal(17, _runtime.Placed);
        Assert.Equal(17, Player.m_localPlayer!.Placed.Count);
    }

    [Fact]
    public void Every_piece_is_placed_by_the_host_player_with_both_booleans_false()
    {
        Confirm();

        Ticks(80);

        Assert.NotEmpty(Player.m_localPlayer!.Placed);
        foreach (string placed in Player.m_localPlayer!.Placed)
        {
            // doAttack false, because an NPC putting a wall up must not make the
            // player swing; cheated false, because the material came out of a
            // player's chest through custody.
            Assert.Contains("attack=False", placed);
            Assert.Contains("cheated=False", placed);
        }
    }

    [Fact]
    public void The_chest_pays_for_exactly_the_manifest_and_he_ends_up_carrying_nothing()
    {
        Confirm();
        ShelterPlan plan = _orders.Plan();
        Assert.True(plan.IsPlanned);
        int wood = InChest("Wood");
        int hide = InChest("DeerHide");

        Ticks(80);

        Assert.Equal(wood - plan.Total.UnitsOf("Wood"), InChest("Wood"));
        Assert.Equal(hide - plan.Total.UnitsOf("DeerHide"), InChest("DeerHide"));
        Assert.Equal(0, OnWorker("Wood"));
        Assert.Equal(0, OnWorker("DeerHide"));
    }

    [Fact]
    public void He_walks_and_the_working_pose_runs_for_every_piece()
    {
        Confirm();

        Ticks(80);

        Assert.True(_motion.Walks >= 17, "he was sent to " + _motion.Walks + " places");
        Assert.Equal(17, _pose.TimesStarted);
        Assert.False(_pose.On);
    }

    // ---- authority and the mode hold -------------------------------------

    [Fact]
    public void Nothing_happens_at_all_without_a_confirmed_order()
    {
        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Null(_modes.JobId);
        Assert.Equal(0, _modes.Entries);
    }

    [Fact]
    public void Nothing_happens_when_this_is_not_a_world_the_runtime_may_work_in()
    {
        Confirm();
        _mayWork = false;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood"));
        Assert.Contains("only as the host", _runtime.Describe());
    }

    [Fact]
    public void A_mode_the_arbiter_will_not_grant_stops_everything_and_takes_nothing()
    {
        Confirm();
        _modes.Refuse = ActorModeOutcome.RefusedBusy;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood"));
        Assert.Null(_modes.JobId);
    }

    [Fact]
    public void An_unspecified_mode_answer_is_a_refusal_and_not_a_grant()
    {
        // The failure this seam exists to make impossible: a caller that only
        // looks for RefusedBusy drives a body it has no hold on.
        Confirm();
        _modes.Refuse = ActorModeOutcome.Unspecified;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Null(_modes.JobId);
    }

    [Fact]
    public void The_body_is_given_back_when_the_shelter_is_finished()
    {
        Confirm();

        Ticks(80);

        Assert.Equal(BuildStep.Finished, _runtime.Loop.Step);
        Assert.Null(_modes.JobId);

        // Exactly one, not "at least one". Review finding 4: `>= 1` was satisfied
        // by 153 Enter/Release pairs over 200 ticks - five a second, forever,
        // with MayRetireBody flickering false the whole time.
        Assert.Equal(1, _modes.Releases);
    }

    [Fact]
    public void A_finished_order_says_so_once_and_then_leaves_the_arbiter_alone()
    {
        Confirm();
        Ticks(80);
        Assert.Equal(BuildStep.Finished, _runtime.Loop.Step);
        Assert.Equal(1, Said("The shelter is finished"));
        int enters = _modes.Entries;

        // Four hundred more seconds of ticking, which is forty of the finished
        // recheck windows.
        Ticks(200);

        Assert.Equal(1, Said("The shelter is finished"));
        Assert.Equal(enters, _modes.Entries);
        Assert.Equal(1, _modes.Releases);
        Assert.Null(_modes.JobId);
        Assert.True(_modes.MayRetireBody, "a finished order must not keep his body busy");
        Assert.True(_modes.MayRelocateHome);
        Assert.Equal(17, Player.m_localPlayer!.Placed.Count);
    }

    [Fact]
    public void A_piece_knocked_down_after_completion_is_noticed_and_built_again()
    {
        // The reason the finished state is re-read at all rather than simply
        // stopped: the look is cheap and needs no body, so a player taking a wall
        // out still gets it back.
        Confirm();
        Ticks(80);
        Assert.Equal(17, Player.m_localPlayer!.Placed.Count);

        Piece bed = Piece.s_allPieces.Find(piece => piece.gameObject.name == "bed")!;
        Assert.NotNull(bed);
        Piece.s_allPieces.Remove(bed);

        Ticks(80);

        Assert.Equal(18, Player.m_localPlayer!.Placed.Count);
        Assert.Equal(BuildStep.Finished, _runtime.Loop.Step);
    }

    [Fact]
    public void Withdrawing_the_order_at_the_chest_puts_the_material_back_and_says_so()
    {
        Confirm();
        Ticks(6);
        int carried = OnWorker("Wood");
        Assert.True(carried > 0);
        Assert.NotNull(_modes.JobId);
        int inChest = InChest("Wood");

        // Standing at the chest, which is where a return can actually happen:
        // custody's own three-metre reach is not waived for a cancellation.
        _motion.Position = new Vector3(20f, 0f, 0f);
        _orders.Execute(new[] { "cancel" });
        Ticks(1);

        Assert.Null(_modes.JobId);
        Assert.Equal(0, OnWorker("Wood"));
        Assert.Equal(inChest + carried, InChest("Wood"));

        // Asserted on Describe(), not on Loop.Reason. Describe() IS
        // BuildOrderRuntime.WorkLine and WorkLine is what the panel and
        // cf_build status render; Loop.Reason is an internal property that
        // nothing shows a player in this state.
        Assert.Contains("went back where it came from", _runtime.Describe());
        Assert.Contains("went back where it came from", _orders.Execute(new[] { "status" }));
    }

    [Fact]
    public void Withdrawing_the_order_away_from_the_chest_says_he_is_still_carrying_it()
    {
        // The honest half of the same behaviour, and a real limitation rather
        // than a defect: the loop does not send him back to the chest to hand
        // material in, so a cancellation out of reach of it leaves the material
        // in his own persisted inventory - where nothing is lost - and the
        // sentence says exactly that.
        Confirm();
        Ticks(6);
        int carried = OnWorker("Wood");
        Assert.True(carried > 0);
        Assert.True(_motion.Position.x < 3f, "he should be at the site, not at the chest");

        _orders.Execute(new[] { "cancel" });
        Ticks(1);

        Assert.Null(_modes.JobId);
        Assert.Equal(carried, OnWorker("Wood"));

        // The case that matters most: the material is in his inventory and both
        // surfaces a player reads have to say so.
        Assert.Contains("still carrying", _runtime.Describe());
        Assert.Contains("nothing has been lost", _runtime.Describe());
        Assert.Contains("still carrying", _orders.Execute(new[] { "status" }));
    }

    [Fact]
    public void A_collection_order_still_on_the_record_holds_the_build_and_takes_nothing()
    {
        Confirm();
        _custody.Recovered = ForemanFixtures.Definition();
        _custody.RecoveredState = CollectionOrderState.Paused;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood"));
        Assert.Contains("collection order", _runtime.Describe());
    }

    [Fact]
    public void Material_the_record_says_he_holds_for_other_work_holds_the_build()
    {
        // Review finding 3. The refusal used to ask only whether a collection
        // order was NON-TERMINAL, and Cancelled is terminal - while its own
        // definition says the carried material stays exactly where it physically
        // is and keeps being recorded. So the ordinary outcome of a player
        // cancelling let a build order spend his gathered wood on a wall.
        Confirm();
        _custody.AtWorker["Wood"] = 12;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood"));
        Assert.Equal(0, OnWorker("Wood"));
        Assert.Contains("already holding 12 Wood for other work", _runtime.Describe());
        Assert.Contains("cf_settle reconcile", _runtime.Describe());
    }

    [Fact]
    public void The_refusal_reads_the_record_and_not_the_live_order_list()
    {
        // The proof that the fix is the record and not a second state check: no
        // collection order is recoverable at all here - `Recovered` is null, which
        // is what a CANCELLED order looks like through the recovery seam - and the
        // build still refuses.
        Confirm();
        Assert.Null(_custody.Recovered);
        _custody.AtWorker["DeerHide"] = 4;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Contains("already holding 4 DeerHide", _runtime.Describe());
    }

    [Fact]
    public void An_unwritable_record_takes_nothing_out_of_a_chest()
    {
        Confirm();
        _custody.IsWritable = false;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood"));
        Assert.Contains("record cannot be written", _runtime.Describe());
    }

    // ---- the real gates refuse -------------------------------------------

    [Fact]
    public void A_ward_over_the_site_refuses_every_piece_and_spends_nothing()
    {
        Confirm();
        PrivateArea.Access = false;

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood") + OnWorker("Wood"));
        Assert.Contains("ward", _runtime.Loop.Reason);
    }

    [Fact]
    public void Ground_that_is_not_loaded_is_left_alone_rather_than_built_on()
    {
        Confirm();
        foreach (CostedPiece piece in _orders.Plan().Pieces)
        {
            ZoneSystem.instance!.Unloaded.Add(ZoneSystem.Key(
                new Vector3(piece.Placement.At.X, piece.Placement.At.Y, piece.Placement.At.Z)));
        }

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Equal(200, InChest("Wood") + OnWorker("Wood"));
    }

    [Fact]
    public void A_no_build_location_over_the_site_is_a_refusal()
    {
        Confirm();
        foreach (CostedPiece piece in _orders.Plan().Pieces)
        {
            Location.NoBuild.Add(ZoneSystem.Key(
                new Vector3(piece.Placement.At.X, piece.Placement.At.Y, piece.Placement.At.Z)));
        }

        Ticks(20);

        Assert.Empty(Player.m_localPlayer!.Placed);
        Assert.Contains("forbids building", _runtime.Loop.Reason);
    }

    [Fact]
    public void Somebody_elses_wall_on_the_site_is_reported_and_never_taken_down()
    {
        Confirm();
        CostedPiece first = _orders.Plan().Pieces[0];
        var theirs = new GameObject("stone_wall");
        theirs.transform.position = new Vector3(first.Placement.At.X, first.Placement.At.Y, first.Placement.At.Z);
        Piece.s_allPieces.Add(theirs.Add(new Piece()));

        Ticks(30);

        Assert.Contains("Nothing will be cleared, levelled or taken down", _runtime.Loop.Reason);
        Assert.Contains(first.Placement.Piece.Prefab, _runtime.Loop.Reason);

        // Their wall is still standing - the placement-clearance policy is a
        // parked owner decision (DECISIONS.md D13) and this loop never resolves
        // an obstruction, it reports one.
        Assert.Contains(theirs.GetComponent<Piece>()!, Piece.s_allPieces);
        Assert.Equal(PieceSighting.Blocked, _runtime.Loop.Progress!.SightingOf(first.Key));
    }

    // ---- the world going away --------------------------------------------

    [Fact]
    public void A_world_that_goes_away_gives_the_body_back_and_forgets_the_load()
    {
        Confirm();
        Ticks(6);
        Assert.NotNull(_modes.JobId);

        _runtime.OnWorldUnloaded();

        Assert.Null(_modes.JobId);
        Assert.Equal(BuildStep.Idle, _runtime.Loop.Step);
    }

    /// <summary>The real installer, plus the object vanilla's own instantiate
    /// would have created.</summary>
    private sealed class StandingInstaller : IPieceInstaller
    {
        private readonly HostPlayerPieceInstaller _real = new HostPlayerPieceInstaller();

        public bool Install(in PiecePlacement placement, out string failure)
        {
            if (!_real.Install(in placement, out failure))
            {
                return false;
            }

            var standing = new GameObject(placement.Piece.Prefab);
            standing.transform.position =
                new Vector3(placement.At.X, placement.At.Y, placement.At.Z);
            Piece.s_allPieces.Add(standing.Add(new Piece()));
            return true;
        }
    }
}
