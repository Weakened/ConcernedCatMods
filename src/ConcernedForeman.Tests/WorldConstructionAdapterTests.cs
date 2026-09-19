using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>#380 and #280 against the game surface: the cost really does come
/// out of <c>Piece.m_resources</c>, the ward really is
/// <c>PrivateArea.CheckAccess</c>, and every one of them refuses rather than
/// assuming when the world is not there.</summary>
public sealed class WorldConstructionAdapterTests : IDisposable
{
    private readonly List<string> _log = new List<string>();

    public WorldConstructionAdapterTests()
    {
        Reset();
    }

    public void Dispose() => Reset();

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

    private static GameObject PiecePrefab(string name, params (string Item, int Amount)[] costs)
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
        return host;
    }

    private WorldPieceCatalogue Catalogue() => new WorldPieceCatalogue(_log.Add);

    private static PiecePlacement Placement(
        string prefab = "wood_wall", float x = 0f, float y = 0f, float z = 0f) =>
        new PiecePlacement(
            new BlueprintPiece(3, prefab, BuildPhase.Walls, 0f, 0f, 0f, 0f),
            new SitePoint(x, y, z),
            0f);

    // ---- the real recipe -------------------------------------------------

    [Fact]
    public void The_cost_is_read_off_the_pieces_own_requirements()
    {
        PiecePrefab("wood_wall", ("Wood", 2));

        PieceRecipe recipe = Catalogue().Read("wood_wall");

        Assert.True(recipe.IsKnown);
        Assert.Equal(new PieceCost("Wood", 2), Assert.Single(recipe.Costs));
    }

    [Fact]
    public void The_item_is_named_the_way_custody_already_names_it()
    {
        // MaterialItem.PrefabName, and EngineInventoryPorts' m_dropPrefab.name
        // match. One vocabulary, so "the shelter needs Wood" and "the chest
        // holds Wood" are the same string.
        PiecePrefab("bed", ("Wood", 8), ("DeerHide", 4));

        PieceRecipe recipe = Catalogue().Read("bed");

        Assert.Equal(2, recipe.Costs.Count);
        Assert.Equal("Wood", recipe.Costs[0].Item);
        Assert.Equal("DeerHide", recipe.Costs[1].Item);
    }

    [Fact]
    public void A_piece_with_no_requirement_array_is_unreadable_and_not_free()
    {
        var host = new GameObject("wood_wall");
        host.Add(new Piece()).m_resources = null;
        ZNetScene.instance!.Prefabs["wood_wall"] = host;

        PieceRecipe recipe = Catalogue().Read("wood_wall");

        // Reading it as free is how a player's chest pays nothing for a cottage.
        Assert.False(recipe.IsKnown);
        Assert.Contains("could not be read", recipe.Refusal);
    }

    [Fact]
    public void A_requirement_naming_an_item_that_is_not_there_is_unreadable()
    {
        var host = new GameObject("wood_wall");
        host.Add(new Piece()).m_resources =
            new[] { new Piece.Requirement { m_resItem = null, m_amount = 2 } };
        ZNetScene.instance!.Prefabs["wood_wall"] = host;

        Assert.False(Catalogue().Read("wood_wall").IsKnown);
    }

    [Fact]
    public void A_requirement_of_none_is_not_a_line()
    {
        var host = new GameObject("wood_wall");
        host.Add(new Piece()).m_resources = new[]
        {
            new Piece.Requirement { m_resItem = Item("Wood").GetComponent<ItemDrop>(), m_amount = 2 },
            new Piece.Requirement { m_resItem = Item("Stone").GetComponent<ItemDrop>(), m_amount = 0 },
        };
        ZNetScene.instance!.Prefabs["wood_wall"] = host;

        PieceRecipe recipe = Catalogue().Read("wood_wall");

        Assert.True(recipe.IsKnown);
        Assert.Single(recipe.Costs);
    }

    [Fact]
    public void A_name_the_world_does_not_know_is_refused_and_never_substituted()
    {
        PieceRecipe recipe = Catalogue().Read("wood_door");

        Assert.False(recipe.IsKnown);
        Assert.Equal("wood_door", recipe.Prefab);
        Assert.Empty(recipe.Costs);
    }

    [Fact]
    public void A_prefab_that_is_not_a_buildable_piece_is_refused()
    {
        ZNetScene.instance!.Prefabs["Wood"] = new GameObject("Wood");

        Assert.False(Catalogue().Read("Wood").IsKnown);
        Assert.Contains("not a buildable piece", Catalogue().Read("Wood").Refusal);
    }

    [Fact]
    public void There_being_no_world_is_a_refusal_rather_than_a_free_piece()
    {
        ZNetScene.instance = null;

        PieceRecipe recipe = Catalogue().Read("wood_wall");

        Assert.False(recipe.IsKnown);
        Assert.Contains("no world", recipe.Refusal);
    }

    [Fact]
    public void A_cost_is_worked_out_once_per_piece_and_forgotten_on_request()
    {
        PiecePrefab("wood_wall", ("Wood", 2));
        WorldPieceCatalogue catalogue = Catalogue();

        catalogue.Read("wood_wall");
        catalogue.Read("wood_wall");
        Assert.Equal(1, catalogue.Priced);

        // A world load can replace the prefab table, and a cost remembered
        // across one is a number from a different game.
        catalogue.Forget();
        Assert.Equal(0, catalogue.Priced);
    }

    // ---- what is standing there -----------------------------------------

    [Fact]
    public void An_empty_spot_on_loaded_ground_is_a_piece_still_to_build()
    {
        Assert.Equal(PieceSighting.Missing, Catalogue().Look(Placement()));
    }

    [Fact]
    public void The_right_piece_at_the_right_spot_is_already_built()
    {
        Stand("wood_wall", 0f, 0f, 0f);

        Assert.Equal(PieceSighting.Standing, Catalogue().Look(Placement()));
    }

    [Fact]
    public void The_clone_suffix_the_engine_adds_is_not_part_of_the_name()
    {
        Stand("wood_wall(Clone)", 0f, 0f, 0f);

        Assert.Equal(PieceSighting.Standing, Catalogue().Look(Placement()));
    }

    [Fact]
    public void Somebody_elses_piece_on_the_spot_is_in_the_way_rather_than_built()
    {
        Stand("stone_wall", 0f, 0f, 0f);

        Assert.Equal(PieceSighting.Blocked, Catalogue().Look(Placement()));
    }

    [Fact]
    public void A_piece_further_off_than_the_tolerance_is_neither_ours_nor_in_the_way()
    {
        Stand("wood_wall", WorldPieceCatalogue.MatchMetres + 0.2f, 0f, 0f);

        Assert.Equal(PieceSighting.Missing, Catalogue().Look(Placement()));
    }

    [Fact]
    public void The_floor_above_is_not_the_floor_below()
    {
        // Two metres up is the roof deck. A vertical tolerance loose enough to
        // confuse them would report the roof finished the moment the floor was.
        Stand("wood_wall", 0f, 2f, 0f);

        Assert.Equal(PieceSighting.Missing, Catalogue().Look(Placement()));
    }

    [Fact]
    public void Ground_that_is_not_loaded_is_never_read_as_empty()
    {
        ZoneSystem.instance!.Unloaded.Add(ZoneSystem.Key(new Vector3(0f, 0f, 0f)));

        Assert.Equal(PieceSighting.Unknown, Catalogue().Look(Placement()));
    }

    [Fact]
    public void No_world_at_all_is_never_read_as_empty()
    {
        ZoneSystem.instance = null;

        Assert.Equal(PieceSighting.Unknown, Catalogue().Look(Placement()));
    }

    [Fact]
    public void A_world_that_throws_while_being_looked_at_defers_the_piece_and_says_so_once()
    {
        ZoneSystem.instance!.Throws = new InvalidOperationException("the zone went away");
        WorldPieceCatalogue catalogue = Catalogue();

        Assert.Equal(PieceSighting.Unknown, catalogue.Look(Placement()));
        Assert.Equal(PieceSighting.Unknown, catalogue.Look(Placement()));

        // This runs once per planned piece per round: an unreadable world would
        // otherwise write the same line into a log all evening.
        Assert.Single(_log);
        Assert.Contains("This is said once", _log[0]);
    }

    // ---- the gates -------------------------------------------------------

    [Fact]
    public void The_ward_gate_is_private_area_check_access()
    {
        PiecePrefab("wood_wall", ("Wood", 2));
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.Yes, probe.WardAllows(in placement));

        PrivateArea.Access = false;
        Assert.Equal(ProbeAnswer.No, probe.WardAllows(in placement));
    }

    [Fact]
    public void A_ward_on_unloaded_ground_is_invisible_rather_than_absent()
    {
        // CheckAccess reads a list that only holds LOADED wards, so answering
        // from it over unloaded ground is the silent skip the authority ADR
        // forbids.
        ZoneSystem.instance!.Unloaded.Add(ZoneSystem.Key(new Vector3(0f, 0f, 0f)));
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.CouldNotTell, probe.WardAllows(in placement));
    }

    [Fact]
    public void The_station_gate_is_have_build_station_in_range_and_a_piece_needing_none_passes()
    {
        GameObject prefab = PiecePrefab("wood_wall", ("Wood", 2));
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.Yes, probe.StationInRange(in placement));

        CraftingStation station = new GameObject("piece_workbench").Add(new CraftingStation());
        station.m_name = "$piece_workbench";
        prefab.GetComponent<Piece>()!.m_craftingStation = station;

        Assert.Equal(ProbeAnswer.No, probe.StationInRange(in placement));

        CraftingStation.InRange.Add("$piece_workbench");
        Assert.Equal(ProbeAnswer.Yes, probe.StationInRange(in placement));
    }

    [Fact]
    public void Ground_whose_height_cannot_be_measured_refuses()
    {
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.Yes, probe.GroundAllows(in placement));

        ZoneSystem.instance!.NoGround.Add(ZoneSystem.Key(new Vector3(0f, 0f, 0f)));
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.GroundAllows(in placement));
    }

    [Fact]
    public void Anything_already_on_the_spot_makes_it_not_clear()
    {
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.Yes, probe.SpaceIsClear(in placement));

        Stand("wood_wall", 0f, 0f, 0f);
        Assert.Equal(ProbeAnswer.No, probe.SpaceIsClear(in placement));
    }

    [Fact]
    public void A_piece_the_world_has_never_heard_of_does_not_exist()
    {
        var probe = new WorldPlacementProbe(() => true);

        Assert.Equal(ProbeAnswer.No, probe.PieceExists("wood_door"));

        PiecePrefab("wood_door", ("Wood", 10));
        Assert.Equal(ProbeAnswer.Yes, probe.PieceExists("wood_door"));

        ZNetScene.instance = null;
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.PieceExists("wood_door"));
    }

    [Fact]
    public void The_work_authority_answer_is_taken_from_the_product_and_not_recomputed()
    {
        Assert.Equal(ProbeAnswer.Yes, new WorldPlacementProbe(() => true).MayActAsHost());
        Assert.Equal(ProbeAnswer.No, new WorldPlacementProbe(() => false).MayActAsHost());
        Assert.Equal(
            ProbeAnswer.CouldNotTell,
            new WorldPlacementProbe(() => throw new InvalidOperationException()).MayActAsHost());
    }

    [Fact]
    public void Every_gate_answers_that_it_could_not_tell_when_there_is_no_world()
    {
        ZoneSystem.instance = null;
        ZNetScene.instance = null;
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.CouldNotTell, probe.IsLoaded(in placement));
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.WardAllows(in placement));
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.StationInRange(in placement));
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.GroundAllows(in placement));
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.SpaceIsClear(in placement));
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.PieceExists("wood_wall"));
    }

    // ---- the piece's own constraints -------------------------------------

    [Fact]
    public void A_plain_piece_that_declares_nothing_passes()
    {
        GameObject prefab = PiecePrefab("wood_wall", ("Wood", 2));
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.Yes, probe.ConstraintsAllow(in placement, out string named));
        Assert.Equal(string.Empty, named);
        Assert.NotNull(prefab);
    }

    [Theory]
    [InlineData("groundOnly", "directly on the ground")]
    [InlineData("cultivated", "cultivated ground")]
    [InlineData("water", "built on water")]
    [InlineData("noInWater", "may not be built in water")]
    [InlineData("tilting", "on a slope")]
    [InlineData("ceiling", "on a ceiling")]
    [InlineData("teleport", "teleport area")]
    [InlineData("deepSnow", "may not be built in deep snow")]
    [InlineData("space", "clear space around it")]
    [InlineData("connect", "must connect to something")]
    public void A_constraint_this_runtime_does_not_implement_refuses_and_says_which(
        string flag, string expected)
    {
        GameObject prefab = PiecePrefab("wood_wall", ("Wood", 2));
        Piece piece = prefab.GetComponent<Piece>()!;
        switch (flag)
        {
            case "groundOnly": piece.m_groundOnly = true; break;
            case "cultivated": piece.m_cultivatedGroundOnly = true; break;
            case "water": piece.m_waterPiece = true; break;
            case "noInWater": piece.m_noInWater = true; break;
            case "tilting": piece.m_notOnTiltingSurface = true; break;
            case "ceiling": piece.m_inCeilingOnly = true; break;
            case "teleport": piece.m_onlyInTeleportArea = true; break;
            case "deepSnow": piece.m_allowedInDeepSnow = false; break;
            case "space": piece.m_spaceRequirement = 2f; break;
            case "connect": piece.m_mustConnectTo = new GameObject("anchor").Add(new Piece()); break;
        }

        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        // CouldNotTell, not No: the piece may well be placeable there, and this
        // runtime has no way to find out. Either way it is not placed.
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.ConstraintsAllow(in placement, out string named));
        Assert.Contains(expected, named);
    }

    [Fact]
    public void A_permission_that_is_off_is_as_much_a_constraint_as_a_prohibition_that_is_on()
    {
        GameObject prefab = PiecePrefab("wood_wall", ("Wood", 2));
        prefab.GetComponent<Piece>()!.m_enabled = false;

        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.CouldNotTell, probe.ConstraintsAllow(in placement, out string named));
        Assert.Contains("not a piece the game currently offers", named);
    }

    [Fact]
    public void A_dungeon_is_judged_rather_than_refused()
    {
        GameObject prefab = PiecePrefab("wood_wall", ("Wood", 2));
        prefab.GetComponent<Piece>()!.m_allowedInDungeons = false;
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        // Out in the world it passes, because this runtime really can ask.
        Assert.Equal(ProbeAnswer.Yes, probe.ConstraintsAllow(in placement, out _));

        Character.Interiors.Add(ZoneSystem.Key(new Vector3(0f, 0f, 0f)));
        Assert.Equal(ProbeAnswer.No, probe.ConstraintsAllow(in placement, out string named));
        Assert.Contains("inside a dungeon", named);
    }

    [Fact]
    public void A_biome_restriction_is_judged_rather_than_refused()
    {
        GameObject prefab = PiecePrefab("wood_wall", ("Wood", 2));
        prefab.GetComponent<Piece>()!.m_onlyInBiome = Heightmap.Biome.Mountain | Heightmap.Biome.Plains;
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Heightmap.Here = Heightmap.Biome.Meadows;
        Assert.Equal(ProbeAnswer.No, probe.ConstraintsAllow(in placement, out string named));
        Assert.Contains("Meadows", named);

        // A flags mask, so one of the two is enough.
        Heightmap.Here = Heightmap.Biome.Plains;
        Assert.Equal(ProbeAnswer.Yes, probe.ConstraintsAllow(in placement, out _));
    }

    [Fact]
    public void Constraints_over_unloaded_ground_are_not_judged_at_all()
    {
        PiecePrefab("wood_wall", ("Wood", 2));
        ZoneSystem.instance!.Unloaded.Add(ZoneSystem.Key(new Vector3(0f, 0f, 0f)));

        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.CouldNotTell, probe.ConstraintsAllow(in placement, out string named));
        Assert.Contains("loaded world", named);
    }

    [Fact]
    public void A_no_build_location_is_its_own_gate()
    {
        var probe = new WorldPlacementProbe(() => true);
        PiecePlacement placement = Placement();

        Assert.Equal(ProbeAnswer.Yes, probe.OutsideNoBuildZone(in placement));

        Location.NoBuild.Add(ZoneSystem.Key(new Vector3(0f, 0f, 0f)));
        Assert.Equal(ProbeAnswer.No, probe.OutsideNoBuildZone(in placement));

        ZoneSystem.instance = null;
        Assert.Equal(ProbeAnswer.CouldNotTell, probe.OutsideNoBuildZone(in placement));
    }

    // ---- the placer ------------------------------------------------------

    [Fact]
    public void A_refusal_never_reaches_the_thing_that_changes_the_world()
    {
        var installer = new CountingInstaller();
        var placer = new WorldPiecePlacer(new FakeProbe { Ward = ProbeAnswer.No }, installer, _log.Add);

        PlacementReport report = placer.Place(Wall(), ConstructionRig.Tally(("Wood", 2)), true);

        Assert.Equal(PlacementResult.Refused, report.Result);
        Assert.Equal(PlacementRefusal.Ward, report.Refusal);
        Assert.Equal(0, installer.Calls);
        Assert.Equal(1, placer.Refused);
        Assert.Contains("Nothing was taken out of a container", _log[0]);
    }

    [Fact]
    public void A_placement_every_gate_allowed_reaches_it_exactly_once()
    {
        var installer = new CountingInstaller { Succeeds = true };
        var placer = new WorldPiecePlacer(new FakeProbe(), installer, _log.Add);

        PlacementReport report = placer.Place(Wall(), ConstructionRig.Tally(("Wood", 2)), true);

        Assert.True(report.Placed);
        Assert.Equal(1, installer.Calls);
        Assert.Equal(1, placer.Placed);
    }

    [Fact]
    public void A_placement_that_failed_is_not_the_same_as_one_that_was_refused()
    {
        var installer = new CountingInstaller { Succeeds = false, Failure = "the object was not created" };
        var placer = new WorldPiecePlacer(new FakeProbe(), installer, _log.Add);

        PlacementReport report = placer.Place(Wall(), ConstructionRig.Tally(("Wood", 2)), true);

        // A refusal is the system working and would be retried forever; this is
        // the system failing.
        Assert.Equal(PlacementResult.Failed, report.Result);
        Assert.Equal(PlacementRefusal.None, report.Refusal);
        Assert.Equal(0, placer.Placed);
    }

    [Fact]
    public void An_installer_that_throws_is_a_failure_rather_than_an_escape()
    {
        var placer = new WorldPiecePlacer(
            new FakeProbe(), new ThrowingInstaller(), _log.Add);

        PlacementReport report = placer.Place(Wall(), ConstructionRig.Tally(("Wood", 2)), true);

        Assert.Equal(PlacementResult.Failed, report.Result);
        Assert.Contains("the world went away", report.Reason);
    }

    [Fact]
    public void The_host_player_places_the_piece_where_the_order_says_and_never_as_cheated()
    {
        PiecePrefab("wood_wall", ("Wood", 2));
        var player = new Player();
        Player.m_localPlayer = player;

        PiecePlacement placement = Placement("wood_wall", 12f, 3f, -7f);
        Assert.True(new HostPlayerPieceInstaller().Install(in placement, out string failure));

        Assert.Equal(string.Empty, failure);
        string call = Assert.Single(player.Placed);
        Assert.Contains("wood_wall@12/3/-7", call);

        // cheated=False is the flag this whole product exists not to set, and
        // attack=False keeps the human build swing out of an NPC's work.
        Assert.Contains("attack=False", call);
        Assert.Contains("cheated=False", call);
    }

    [Fact]
    public void A_placement_the_game_refused_is_a_failure_and_not_a_silent_success()
    {
        PiecePrefab("wood_wall", ("Wood", 2));
        Player.m_localPlayer = new Player { PlaceSucceeds = false };

        PiecePlacement placement = Placement();
        Assert.False(new HostPlayerPieceInstaller().Install(in placement, out string failure));
        Assert.Contains("refused to create", failure);
    }

    [Fact]
    public void With_no_host_player_and_no_prefab_nothing_is_placed()
    {
        Player.m_localPlayer = null;
        PiecePlacement placement = Placement();
        Assert.False(new HostPlayerPieceInstaller().Install(in placement, out string noPlayer));
        Assert.Contains("no host player", noPlayer);

        Player.m_localPlayer = new Player();
        Assert.False(new HostPlayerPieceInstaller().Install(in placement, out string noPiece));
        Assert.Contains("no piece called wood_wall", noPiece);
    }

    private static CostedPiece Wall()
    {
        ShelterPlan plan = ConstructionRig.Plan();
        foreach (CostedPiece piece in plan.Pieces)
        {
            if (piece.Placement.Piece.Prefab == "wood_wall")
            {
                return piece;
            }
        }

        throw new Xunit.Sdk.XunitException("the blueprint has no wall");
    }

    private static void Stand(string name, float x, float y, float z)
    {
        var host = new GameObject(name);
        host.transform.position = new Vector3(x, y, z);
        Piece.s_allPieces.Add(host.Add(new Piece()));
    }

    private sealed class CountingInstaller : IPieceInstaller
    {
        internal int Calls { get; private set; }

        internal bool Succeeds { get; set; }

        internal string Failure { get; set; } = string.Empty;

        public bool Install(in PiecePlacement placement, out string failure)
        {
            Calls++;
            failure = Failure;
            return Succeeds;
        }
    }

    private sealed class ThrowingInstaller : IPieceInstaller
    {
        public bool Install(in PiecePlacement placement, out string failure) =>
            throw new InvalidOperationException("the world went away");
    }
}
