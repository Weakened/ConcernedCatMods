using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

public class CampBoundaryTests
{
    [Fact]
    public void A_square_camp_has_a_perimeter_outside_its_outermost_pieces()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(10, 0);
        camp.Build(10, 10);
        camp.Build(0, 10);

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(4, surveyed.Perimeter.Count);
        Assert.True(surveyed.Contains(new NpcPoint(5, 0, 5)));

        // Standing against the outside of the wall is still inside: that is
        // what the margin is for.
        Assert.True(surveyed.Contains(new NpcPoint(-2, 0, 5)));
        Assert.False(surveyed.Contains(new NpcPoint(-20, 0, 5)));
    }

    [Fact]
    public void A_camp_of_one_piece_still_has_a_perimeter()
    {
        // Day one of every save. A caller with a special case for "too few
        // pieces to have a shape" is a caller that will get it wrong.
        var camp = new CampFixture();
        camp.Build(0, 0);

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(1, surveyed.Members);
        Assert.True(surveyed.Perimeter.Count >= 3);
        Assert.True(surveyed.Contains(new NpcPoint(1, 0, 1)));
        Assert.False(surveyed.Contains(new NpcPoint(40, 0, 40)));
    }

    [Fact]
    public void A_camp_in_a_straight_line_has_a_perimeter_too()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(6, 0);
        camp.Build(12, 0);

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(3, surveyed.Members);
        Assert.True(surveyed.Perimeter.Count >= 3);
        Assert.True(surveyed.Contains(new NpcPoint(6, 0, 0)));
    }

    [Fact]
    public void The_centre_is_the_mean_of_the_pieces_and_not_the_anchor()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(10, 0);
        camp.Build(10, 10);
        camp.Build(0, 10);

        CampSnapshot surveyed = camp.Survey();

        Assert.Equal(5f, surveyed.Centre.X, 3);
        Assert.Equal(5f, surveyed.Centre.Z, 3);
    }

    [Fact]
    public void A_point_outside_camp_is_answered_with_one_just_inside_it()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(20, 0);
        camp.Build(20, 20);
        camp.Build(0, 20);

        CampSnapshot surveyed = camp.Survey();

        Assert.True(surveyed.TryNearestInteriorPoint(new NpcPoint(60, 0, 10), out NpcPoint interior));
        Assert.True(surveyed.Contains(interior));

        // Nearest, not "the middle": it comes back on the side he approached
        // from, or an NPC sent there walks through camp to reach its centre.
        Assert.True(interior.X > surveyed.Centre.X);
    }

    [Fact]
    public void A_point_already_inside_camp_is_left_alone()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(20, 0);
        camp.Build(20, 20);
        camp.Build(0, 20);

        CampSnapshot surveyed = camp.Survey();
        var standing = new NpcPoint(9, 0, 11);

        Assert.True(surveyed.TryNearestInteriorPoint(standing, out NpcPoint interior));
        Assert.Equal(standing, interior);
    }

    [Fact]
    public void The_same_camp_always_gives_the_same_answer()
    {
        // Determinism is not tidiness here. An NPC that gets two different
        // "nearest interior points" for one standpoint paces between them.
        var first = new CampFixture();
        var second = new CampFixture();
        float[] xs = { 0, 12, 12, 0, 6 };
        float[] zs = { 0, 0, 12, 12, 6 };

        for (int index = 0; index < xs.Length; index++)
        {
            first.Build(xs[index], zs[index]);
        }

        for (int index = xs.Length - 1; index >= 0; index--)
        {
            second.Build(xs[index], zs[index]);
        }

        CampSnapshot a = first.Survey();
        CampSnapshot b = second.Survey();

        Assert.Equal(a.Members, b.Members);
        Assert.Equal(a.ExtentMetres, b.ExtentMetres, 4);
        Assert.Equal(a.Perimeter.Count, b.Perimeter.Count);

        Assert.True(a.TryNearestInteriorPoint(new NpcPoint(-30, 0, 6), out NpcPoint fromA));
        Assert.True(b.TryNearestInteriorPoint(new NpcPoint(-30, 0, 6), out NpcPoint fromB));
        Assert.Equal(fromA, fromB);
    }

    [Fact]
    public void A_bigger_margin_makes_a_bigger_camp()
    {
        var tight = new CampFixture(new CampSurveyOptions(24f, 8f, 0f, 2048));
        var loose = new CampFixture(new CampSurveyOptions(24f, 8f, 10f, 2048));
        foreach (CampFixture camp in new[] { tight, loose })
        {
            camp.Build(0, 0);
            camp.Build(10, 0);
            camp.Build(10, 10);
            camp.Build(0, 10);
        }

        Assert.False(tight.Survey().Contains(new NpcPoint(-6, 0, 5)));
        Assert.True(loose.Survey().Contains(new NpcPoint(-6, 0, 5)));
    }

    [Fact]
    public void A_position_nobody_could_compute_is_never_inside_camp()
    {
        var camp = new CampFixture();
        camp.Build(0, 0);
        camp.Build(10, 0);
        camp.Build(10, 10);

        CampSnapshot surveyed = camp.Survey();

        Assert.False(surveyed.Contains(new NpcPoint(float.NaN, 0, 0)));
        Assert.False(surveyed.Contains(new NpcPoint(0, 0, float.PositiveInfinity)));

        // But it is still answerable: an NPC whose own position could not be
        // read is sent to the middle rather than refused.
        Assert.True(surveyed.TryNearestInteriorPoint(new NpcPoint(float.NaN, 0, 0), out NpcPoint interior));
        Assert.Equal(surveyed.Centre, interior);
    }

    [Fact]
    public void Options_out_of_range_are_clamped_rather_than_thrown_on()
    {
        // These arrive from a player's configuration file, where a zero is a
        // typo and a crash on world load is not a proportionate answer.
        var silly = new CampSurveyOptions(-5f, 0f, float.NaN, -1);

        Assert.True(silly.SeedRadiusMetres > 0f);
        Assert.True(silly.LinkDistanceMetres > 0f);
        Assert.True(silly.PerimeterMarginMetres >= 0f);
        Assert.True(silly.MaxMembers >= 16);
    }
}
