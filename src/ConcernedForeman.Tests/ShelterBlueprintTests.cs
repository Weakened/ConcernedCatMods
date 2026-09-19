using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;

namespace ConcernedForeman.Tests;

/// <summary>#380: the shelter is the same seventeen pieces every time, it is
/// priced from the game's own recipes, and an order nobody confirmed authorises
/// nothing.</summary>
public sealed class ShelterBlueprintTests
{
    [Fact]
    public void The_shelter_is_a_floor_enclosing_walls_a_doorway_a_roof_and_one_bed()
    {
        Assert.Equal(4, ShelterBlueprint.Of(BuildPhase.Foundation).Count);
        Assert.Equal(8, ShelterBlueprint.Of(BuildPhase.Walls).Count);
        Assert.Equal(4, ShelterBlueprint.Of(BuildPhase.Roof).Count);
        Assert.Single(ShelterBlueprint.Of(BuildPhase.Interior));
        Assert.Equal(17, ShelterBlueprint.Pieces.Count);

        int doors = 0;
        foreach (BlueprintPiece piece in ShelterBlueprint.Of(BuildPhase.Walls))
        {
            if (piece.Prefab == "wood_door")
            {
                doors++;
            }
        }

        // Exactly one of the eight wall slots is the doorway. Two would be a
        // draughty cottage; none would be a sealed box with a bed in it.
        Assert.Equal(1, doors);
    }

    [Fact]
    public void Every_piece_names_a_phase_and_a_prefab_and_carries_its_own_index()
    {
        for (int index = 0; index < ShelterBlueprint.Pieces.Count; index++)
        {
            BlueprintPiece piece = ShelterBlueprint.Pieces[index];
            Assert.True(piece.IsValid, piece.ToString());
            Assert.Equal(index, piece.Index);
            Assert.NotEqual(BuildPhase.Unspecified, piece.Phase);
        }
    }

    [Fact]
    public void The_blueprint_is_written_in_build_order()
    {
        // A piece of a later phase never appears before a piece of an earlier
        // one. The round loop leans on this: the first unbuilt piece in the list
        // is the one that decides what phase the build is on.
        int furthest = 0;
        foreach (BlueprintPiece piece in ShelterBlueprint.Pieces)
        {
            int position = Array.IndexOf(BuildPhases.InOrder, piece.Phase);
            Assert.True(position >= furthest, piece + " comes after a later phase");
            furthest = position;
        }
    }

    [Fact]
    public void Every_phase_the_enum_names_is_in_the_build_order()
    {
        // A phase added to the enum and forgotten here would be built last and
        // silently, which for a roof means before its walls.
        foreach (object value in Enum.GetValues(typeof(BuildPhase)))
        {
            var phase = (BuildPhase)value;
            if (phase == BuildPhase.Unspecified)
            {
                continue;
            }

            Assert.Contains(phase, BuildPhases.InOrder);
            Assert.True(BuildPhases.PriorityOf(phase) > 0, phase + " has no priority");
        }
    }

    [Fact]
    public void Phase_priority_runs_down_so_the_shared_runtime_builds_in_order()
    {
        // Higher is serviced sooner, which is the runtime's convention. If these
        // ever ran the other way the cottage would be roofed first.
        int previous = int.MaxValue;
        foreach (BuildPhase phase in BuildPhases.InOrder)
        {
            int priority = BuildPhases.PriorityOf(phase);
            Assert.True(priority < previous, phase + " is not below the phase before it");
            previous = priority;
        }
    }

    [Fact]
    public void A_marker_nobody_confirmed_places_nothing()
    {
        BuildOrderMarker proposed =
            BuildOrderMarker.Proposed(BuildOrderKind.Shelter, new SitePoint(10f, 2f, -4f), 33f);

        Assert.False(proposed.IsConfirmed);
        Assert.Empty(ShelterBlueprint.PlaceAt(proposed));

        // And the default struct, which is the one a bug hands over.
        Assert.Empty(ShelterBlueprint.PlaceAt(default));
    }

    [Fact]
    public void An_order_with_no_kind_or_no_place_cannot_be_confirmed()
    {
        Assert.False(BuildOrderMarker.Proposed(BuildOrderKind.None, new SitePoint(1f, 1f, 1f), 0f)
            .Confirm().IsConfirmed);
        Assert.False(BuildOrderMarker
            .Proposed(BuildOrderKind.Shelter, new SitePoint(float.NaN, 0f, 0f), 0f)
            .Confirm().IsConfirmed);
    }

    [Fact]
    public void Cancelling_an_order_takes_the_authority_away_and_keeps_the_place()
    {
        BuildOrderMarker confirmed = ConstructionRig.Confirmed(5f, 1f, 5f, 90f);
        BuildOrderMarker withdrawn = confirmed.Withdraw();

        Assert.False(withdrawn.IsConfirmed);
        Assert.Equal(confirmed.At, withdrawn.At);
        Assert.Equal(confirmed.Yaw, withdrawn.Yaw);
        Assert.Empty(ShelterBlueprint.PlaceAt(withdrawn));
    }

    [Fact]
    public void Turning_the_marker_turns_the_whole_cottage_and_keeps_its_shape()
    {
        IReadOnlyList<PiecePlacement> north = ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed());
        IReadOnlyList<PiecePlacement> turned =
            ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed(yaw: 90f));

        Assert.Equal(north.Count, turned.Count);
        for (int index = 0; index < north.Count; index++)
        {
            // Every piece keeps its distance from the marker, and its facing
            // turns with it. That is the whole of "the same shape, wherever it
            // is put, however it is pointed".
            var origin = new SitePoint(0f, 0f, 0f);
            Assert.Equal(
                north[index].At.HorizontalDistanceTo(origin),
                turned[index].At.HorizontalDistanceTo(origin),
                3);
            Assert.Equal(north[index].At.Y, turned[index].At.Y, 3);
            Assert.Equal(
                ShelterBlueprint.Wrap(north[index].Yaw + 90f), turned[index].Yaw, 3);
        }
    }

    [Fact]
    public void Moving_the_marker_moves_every_piece_by_the_same_amount()
    {
        IReadOnlyList<PiecePlacement> here = ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed());
        IReadOnlyList<PiecePlacement> there =
            ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed(100f, 7f, -40f));

        for (int index = 0; index < here.Count; index++)
        {
            Assert.Equal(here[index].At.X + 100f, there[index].At.X, 3);
            Assert.Equal(here[index].At.Y + 7f, there[index].At.Y, 3);
            Assert.Equal(here[index].At.Z - 40f, there[index].At.Z, 3);
        }
    }

    [Fact]
    public void The_roof_sits_on_top_of_the_walls_and_the_walls_stand_on_the_floor()
    {
        IReadOnlyList<PiecePlacement> placed = ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed());
        foreach (PiecePlacement placement in placed)
        {
            switch (placement.Piece.Phase)
            {
                case BuildPhase.Foundation:
                case BuildPhase.Interior:
                    Assert.Equal(0f, placement.At.Y, 3);
                    break;
                case BuildPhase.Walls:
                    Assert.Equal(ShelterBlueprint.PanelMetres / 2f, placement.At.Y, 3);
                    break;
                case BuildPhase.Roof:
                    Assert.Equal(ShelterBlueprint.PanelMetres, placement.At.Y, 3);
                    break;
            }
        }
    }

    [Fact]
    public void Two_pieces_never_want_the_same_place()
    {
        // Seventeen placements at seventeen places. Two pieces on one spot is a
        // build that refuses itself on the second one, forever.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PiecePlacement placement in ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed()))
        {
            Assert.True(seen.Add(placement.At.ToString()), "two pieces want " + placement.At);
        }

        Assert.Equal(17, seen.Count);
    }

    [Fact]
    public void Every_placement_is_named_by_its_blueprint_index_and_not_by_where_it_is()
    {
        // A key made of coordinates turns a piece that had to move into a second
        // piece, and the reservation over the first is then never released.
        IReadOnlyList<PiecePlacement> here = ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed());
        IReadOnlyList<PiecePlacement> there =
            ShelterBlueprint.PlaceAt(ConstructionRig.Confirmed(60f, 0f, 60f, 45f));

        for (int index = 0; index < here.Count; index++)
        {
            Assert.Equal(here[index].Key, there[index].Key);
        }
    }
}
