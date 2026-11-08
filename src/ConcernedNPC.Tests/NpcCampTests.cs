using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The public camp facade (#461, #295 S5): what a role outside this
/// assembly can do with camp, through plain values only.</summary>
public class NpcCampTests
{
    private static readonly NpcPoint Bed = new NpcPoint(100, 30, 100);

    private static NpcWorldEpoch Epoch()
    {
        var registry = new NpcRoleRegistry();
        return registry.BeginWorldLoad(out _);
    }

    private static NpcCamp Camp(NpcWorldEpoch epoch)
    {
        var camp = new NpcCamp();
        camp.BeginWorld(epoch);
        return camp;
    }

    [Fact]
    public void A_player_built_piece_near_home_makes_camp_a_place()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);

        Assert.True(camp.NotePiece("wall", new NpcPoint(103, 30, 100), true, epoch));
        NpcCampView view = camp.Survey(NpcCampHome.PlayerBed, Bed);

        Assert.True(view.IsEstablished);
        Assert.False(view.IsProvisional);
        Assert.Equal(NpcCampHome.PlayerBed, view.Home);
        Assert.Equal(1, view.Members);
        Assert.True(view.Contains(new NpcPoint(102, 30, 100)));
        Assert.False(view.Contains(new NpcPoint(400, 30, 400)));
        Assert.True(view.Epoch.Matches(epoch));
    }

    [Fact]
    public void Terrain_and_unclassified_pieces_are_refused_and_a_reclassified_one_is_let_go_of()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);

        Assert.False(camp.NotePiece("path", new NpcPoint(101, 30, 100), false, epoch));
        Assert.Equal(0, camp.Known);

        Assert.True(camp.NotePiece("wall", new NpcPoint(101, 30, 100), true, epoch));
        Assert.False(camp.NotePiece("wall", new NpcPoint(101, 30, 100), false, epoch));
        Assert.Equal(0, camp.Known);
    }

    [Fact]
    public void A_piece_from_another_world_load_or_with_no_name_is_refused()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);

        Assert.False(camp.NotePiece("old", new NpcPoint(101, 30, 100), true, Epoch()));
        Assert.False(camp.NotePiece("", new NpcPoint(101, 30, 100), true, epoch));
        Assert.False(camp.NotePiece("nan", new NpcPoint(float.NaN, 30, 100), true, epoch));
        Assert.False(new NpcCamp().NotePiece("before-any-world", Bed, true, epoch));
        Assert.Equal(0, camp.Known);
    }

    [Fact]
    public void An_unchanged_camp_hands_back_the_same_view_and_a_change_a_new_revision()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);
        camp.NotePiece("wall", new NpcPoint(103, 30, 100), true, epoch);

        NpcCampView first = camp.Survey(NpcCampHome.PlayerBed, Bed);
        Assert.Same(first, camp.Survey(NpcCampHome.PlayerBed, Bed));

        // A bed read with a hair of float noise is the same home.
        Assert.Same(first, camp.Survey(NpcCampHome.PlayerBed, new NpcPoint(100.2f, 30, 100.1f)));

        camp.NotePiece("bench", new NpcPoint(98, 30, 101), true, epoch);
        NpcCampView second = camp.Survey(NpcCampHome.PlayerBed, Bed);
        Assert.NotSame(first, second);
        Assert.True(second.Revision > first.Revision);
        Assert.Equal(2, second.Members);

        // The old view never changes under its holder.
        Assert.Equal(1, first.Members);
    }

    [Fact]
    public void A_moved_bed_or_a_forgotten_piece_resurveys()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);
        camp.NotePiece("wall", new NpcPoint(103, 30, 100), true, epoch);
        NpcCampView atBed = camp.Survey(NpcCampHome.PlayerBed, Bed);

        NpcCampView moved = camp.Survey(NpcCampHome.PlayerBed, new NpcPoint(300, 30, 300));
        Assert.True(moved.Revision > atBed.Revision);
        Assert.False(moved.IsEstablished);

        NpcCampView back = camp.Survey(NpcCampHome.PlayerBed, Bed);
        Assert.True(back.IsEstablished);
        Assert.True(camp.ForgetPiece("wall"));
        Assert.False(camp.ForgetPiece("wall"));
        Assert.False(camp.Survey(NpcCampHome.PlayerBed, Bed).IsEstablished);
    }

    [Fact]
    public void An_unknown_home_is_no_camp_and_contains_nothing()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);
        camp.NotePiece("wall", new NpcPoint(103, 30, 100), true, epoch);

        NpcCampView none = camp.Survey(NpcCampHome.Unknown, Bed);
        Assert.False(none.IsEstablished);
        Assert.Equal(NpcCampHome.Unknown, none.Home);
        Assert.False(none.Contains(Bed));
        Assert.False(none.TryNearestInteriorPoint(Bed, out _));

        NpcCampView nan = camp.Survey(NpcCampHome.PlayerBed, new NpcPoint(float.NaN, 0, 0));
        Assert.False(nan.IsEstablished);
        Assert.Equal(NpcCampHome.Unknown, nan.Home);
    }

    [Fact]
    public void The_world_start_is_provisional_and_a_new_world_forgets_everything()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);
        camp.NotePiece("fire", new NpcPoint(2, 30, 0), true, epoch);

        NpcCampView start = camp.Survey(NpcCampHome.WorldSpawn, new NpcPoint(0, 30, 0));
        Assert.True(start.IsProvisional);
        Assert.Equal(NpcCampHome.WorldSpawn, start.Home);
        Assert.Equal(0, camp.BeginWorld(epoch));

        NpcWorldEpoch next = Epoch();
        Assert.Equal(1, camp.BeginWorld(next));
        Assert.Equal(0, camp.Known);
        NpcCampView after = camp.Survey(NpcCampHome.WorldSpawn, new NpcPoint(0, 30, 0));
        Assert.NotSame(start, after);
        Assert.True(after.Epoch.Matches(next));
        Assert.False(after.Epoch.Matches(epoch));
        Assert.False(after.IsEstablished);
    }

    [Fact]
    public void The_nearest_interior_point_comes_from_the_library_rule()
    {
        NpcWorldEpoch epoch = Epoch();
        NpcCamp camp = Camp(epoch);
        camp.NotePiece("a", new NpcPoint(95, 30, 95), true, epoch);
        camp.NotePiece("b", new NpcPoint(105, 30, 95), true, epoch);
        camp.NotePiece("c", new NpcPoint(100, 30, 106), true, epoch);
        NpcCampView view = camp.Survey(NpcCampHome.PlayerBed, Bed);

        Assert.True(view.TryNearestInteriorPoint(new NpcPoint(160, 30, 100), out NpcPoint inside));
        Assert.True(view.Contains(inside));
        Assert.True(view.ExtentMetres > 0f);
    }
}
