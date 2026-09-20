using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>Build orders against the installed game: the marker takes the place
/// and the facing the player is standing in, the price comes from the world, and
/// a world going away takes the order with it.</summary>
public sealed class BuildOrderRuntimeTests : IDisposable
{
    private readonly List<string> _log = new List<string>();

    public BuildOrderRuntimeTests()
    {
        Reset();
    }

    public void Dispose() => Reset();

    private static void Reset()
    {
        Piece.s_allPieces.Clear();
        ZoneSystem.instance = new ZoneSystem();
        ZNetScene.instance = new ZNetScene();
        Player.m_localPlayer = null;
    }

    private static Player StandAt(float x, float y, float z, float facing)
    {
        var player = new Player();
        player.transform.position = new Vector3(x, y, z);
        player.transform.rotation = Quaternion.Euler(0f, facing, 0f);
        Player.m_localPlayer = player;
        return player;
    }

    private static void ShelterPieces()
    {
        foreach ((string name, int wood) in new[]
                 {
                     ("wood_floor", 4), ("wood_wall", 2), ("wood_door", 10), ("bed", 8),
                 })
        {
            var item = new GameObject("Wood");
            item.Add(new ItemDrop());
            var host = new GameObject(name);
            host.Add(new Piece()).m_resources = new[]
            {
                new Piece.Requirement { m_resItem = item.GetComponent<ItemDrop>(), m_amount = wood },
            };
            ZNetScene.instance!.Prefabs[name] = host;
        }
    }

    private BuildOrderRuntime Runtime(bool mayWork = true, string whyNot = "nobody is here to do it.") =>
        new BuildOrderRuntime(() => mayWork, () => whyNot, _log.Add);

    [Fact]
    public void Marking_takes_the_place_and_the_facing_the_player_is_standing_in()
    {
        ShelterPieces();
        StandAt(12f, 3f, -7f, 90f);
        BuildOrderRuntime runtime = Runtime();

        runtime.Execute(new[] { "here" });

        Assert.Equal(BuildOrderStatus.Proposed, runtime.Status);
        Assert.Equal(12f, runtime.Marker.At.X, 3);
        Assert.Equal(-7f, runtime.Marker.At.Z, 3);
        Assert.Equal(90f, runtime.Marker.Yaw, 3);
    }

    [Fact]
    public void The_price_comes_out_of_the_worlds_own_recipes()
    {
        ShelterPieces();
        StandAt(0f, 0f, 0f, 0f);
        BuildOrderRuntime runtime = Runtime();
        runtime.Execute(new[] { "here" });

        string said = runtime.Execute(new[] { "confirm" });

        Assert.True(runtime.IsAuthorised);
        Assert.Contains("64 Wood", said);
        Assert.Equal(64, runtime.Plan().Total.UnitsOf("Wood"));
    }

    [Fact]
    public void A_world_with_no_piece_by_that_name_authorises_nothing()
    {
        // Only three of the four. Nothing is substituted and nothing is priced.
        ShelterPieces();
        ZNetScene.instance!.Prefabs.Remove("wood_door");
        StandAt(0f, 0f, 0f, 0f);
        BuildOrderRuntime runtime = Runtime();
        runtime.Execute(new[] { "here" });

        string said = runtime.Execute(new[] { "confirm" });

        Assert.False(runtime.IsAuthorised);
        Assert.Contains("wood_door", said);
    }

    [Fact]
    public void With_nobody_in_the_world_nothing_can_be_marked()
    {
        BuildOrderRuntime runtime = Runtime();

        string said = runtime.Execute(new[] { "here" });

        Assert.Equal(BuildOrderStatus.None, runtime.Status);
        Assert.Contains("not a place a shelter can go", said);
    }

    [Fact]
    public void An_order_may_be_authorised_where_no_work_can_happen_and_says_why_not()
    {
        ShelterPieces();
        StandAt(0f, 0f, 0f, 0f);
        BuildOrderRuntime runtime = Runtime(mayWork: false, whyNot: "somebody else is connected.");
        runtime.Execute(new[] { "here" });

        string said = runtime.Execute(new[] { "confirm" });

        Assert.True(runtime.IsAuthorised);
        Assert.Contains("somebody else is connected.", said);
    }

    [Fact]
    public void An_authority_answer_that_throws_is_never_read_as_a_yes()
    {
        ShelterPieces();
        StandAt(0f, 0f, 0f, 0f);
        var runtime = new BuildOrderRuntime(
            () => throw new InvalidOperationException(), () => string.Empty, _log.Add);
        runtime.Execute(new[] { "here" });

        Assert.Contains("could not be established", runtime.Execute(new[] { "confirm" }));
    }

    [Fact]
    public void Progress_is_read_from_the_pieces_standing_at_the_site()
    {
        ShelterPieces();
        StandAt(0f, 0f, 0f, 0f);
        BuildOrderRuntime runtime = Runtime();
        runtime.Execute(new[] { "here" });
        runtime.Execute(new[] { "confirm" });

        Assert.Equal(17, runtime.Progress().Remaining.Count);

        // The player lays one floor panel by hand. Nothing was told; the next
        // look simply finds it.
        foreach (CostedPiece piece in runtime.Plan().Pieces)
        {
            if (piece.Phase == BuildPhase.Foundation)
            {
                var host = new GameObject("wood_floor");
                host.transform.position = new Vector3(
                    piece.Placement.At.X, piece.Placement.At.Y, piece.Placement.At.Z);
                Piece.s_allPieces.Add(host.Add(new Piece()));
                break;
            }
        }

        Assert.Equal(16, runtime.Progress().Remaining.Count);
    }

    [Fact]
    public void A_world_going_away_takes_the_order_with_it()
    {
        ShelterPieces();
        StandAt(0f, 0f, 0f, 0f);
        BuildOrderRuntime runtime = Runtime();
        runtime.Execute(new[] { "here" });
        runtime.Execute(new[] { "confirm" });

        runtime.Forget();

        // A marker names a place in a world that has gone. This is also the
        // known limitation on the owner go-around list: an order does not
        // survive a reload, and re-confirming it is the whole cost.
        Assert.Equal(BuildOrderStatus.None, runtime.Status);
        Assert.False(runtime.IsAuthorised);
    }

    [Fact]
    public void A_command_that_throws_is_answered_rather_than_escaping_into_the_console()
    {
        ShelterPieces();
        Player player = StandAt(0f, 0f, 0f, 0f);
        BuildOrderRuntime runtime = Runtime();
        runtime.Execute(new[] { "here" });

        ZoneSystem.instance!.Throws = new InvalidOperationException("the world went away");

        // The catalogue swallows a world that throws and reports Unknown, so the
        // command still answers. Nothing here may take the console out.
        Assert.NotNull(runtime.Execute(new[] { "status" }));
        Assert.NotNull(player);
    }
}
