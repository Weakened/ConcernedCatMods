using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>An anchor source a test writes: each of the three offers is set
/// independently, so exactly one thing can be wrong.</summary>
internal sealed class FakeAnchors : ICampAnchorSource
{
    internal FakeAnchors()
    {
        PlayerBed = CampAnchorOffer.Absent(CampAnchorKind.PlayerBed);
        Settlement = CampAnchorOffer.Absent(CampAnchorKind.Settlement);
        WorldSpawn = CampAnchorOffer.Absent(CampAnchorKind.WorldSpawn);
    }

    public CampAnchorOffer PlayerBed { get; set; }

    public CampAnchorOffer Settlement { get; set; }

    public CampAnchorOffer WorldSpawn { get; set; }
}

internal sealed class ThrowingAnchors : ICampAnchorSource
{
    public CampAnchorOffer PlayerBed => throw new InvalidOperationException("the profile could not be read");

    public CampAnchorOffer Settlement => CampAnchorOffer.At(CampAnchorKind.Settlement, new NpcPoint(50, 0, 50));

    public CampAnchorOffer WorldSpawn => CampAnchorOffer.At(CampAnchorKind.WorldSpawn, default);
}

/// <summary>A source that answers about the wrong kind: the mistake a role
/// makes once, by copying the bed property and forgetting the kind.</summary>
internal sealed class MislabelledAnchors : ICampAnchorSource
{
    public CampAnchorOffer PlayerBed => CampAnchorOffer.At(CampAnchorKind.Settlement, new NpcPoint(900, 0, 900));

    public CampAnchorOffer Settlement => CampAnchorOffer.Absent(CampAnchorKind.Settlement);

    public CampAnchorOffer WorldSpawn => CampAnchorOffer.At(CampAnchorKind.WorldSpawn, new NpcPoint(0, 0, 0));
}

public class CampAnchorTests
{
    private static readonly NpcPoint Bed = new NpcPoint(100, 30, 100);
    private static readonly NpcPoint Hall = new NpcPoint(50, 30, 50);
    private static readonly NpcPoint Spawn = new NpcPoint(0, 30, 0);

    [Fact]
    public void The_bed_outranks_the_settlement_and_the_settlement_outranks_the_world_start()
    {
        var anchors = new FakeAnchors
        {
            PlayerBed = CampAnchorOffer.At(CampAnchorKind.PlayerBed, Bed),
            Settlement = CampAnchorOffer.At(CampAnchorKind.Settlement, Hall),
            WorldSpawn = CampAnchorOffer.At(CampAnchorKind.WorldSpawn, Spawn),
        };

        Assert.Equal(CampAnchorKind.PlayerBed, CampAnchorResolver.Resolve(CampAnchor.None, anchors).Kind);

        anchors.PlayerBed = CampAnchorOffer.Absent(CampAnchorKind.PlayerBed);
        Assert.Equal(CampAnchorKind.Settlement, CampAnchorResolver.Resolve(CampAnchor.None, anchors).Kind);

        anchors.Settlement = CampAnchorOffer.Absent(CampAnchorKind.Settlement);
        CampAnchor last = CampAnchorResolver.Resolve(CampAnchor.None, anchors);
        Assert.Equal(CampAnchorKind.WorldSpawn, last.Kind);

        // And it says so: a camp on the world's start is temporary, and a role
        // that cannot tell would tell a player they have a home.
        Assert.True(last.IsProvisional);
    }

    [Fact]
    public void An_unreadable_bed_keeps_the_bed_rather_than_marching_him_to_the_world_start()
    {
        // The failure this prevents: a player sails away, the bed's zone
        // unloads, and every companion's camp collapses to the world's start
        // and re-expands on the way home - moving every boundary twice a
        // voyage.
        var anchors = new FakeAnchors
        {
            PlayerBed = CampAnchorOffer.At(CampAnchorKind.PlayerBed, Bed),
            WorldSpawn = CampAnchorOffer.At(CampAnchorKind.WorldSpawn, Spawn),
        };

        CampAnchor held = CampAnchorResolver.Resolve(CampAnchor.None, anchors);
        Assert.Equal(CampAnchorKind.PlayerBed, held.Kind);

        anchors.PlayerBed = CampAnchorOffer.Unreadable(CampAnchorKind.PlayerBed);
        CampAnchor kept = CampAnchorResolver.Resolve(held, anchors);

        Assert.Equal(CampAnchorKind.PlayerBed, kept.Kind);
        Assert.Equal(Bed, kept.Position);
    }

    [Fact]
    public void A_bed_that_is_positively_gone_is_given_up_and_an_unreadable_one_is_not()
    {
        // The whole reason validity is three-way, in one test: the same
        // situation, two answers, two outcomes.
        var anchors = new FakeAnchors { WorldSpawn = CampAnchorOffer.At(CampAnchorKind.WorldSpawn, Spawn) };
        var held = new CampAnchor(CampAnchorKind.PlayerBed, Bed);

        anchors.PlayerBed = CampAnchorOffer.Unreadable(CampAnchorKind.PlayerBed);
        Assert.Equal(CampAnchorKind.PlayerBed, CampAnchorResolver.Resolve(held, anchors).Kind);

        anchors.PlayerBed = CampAnchorOffer.Absent(CampAnchorKind.PlayerBed);
        Assert.Equal(CampAnchorKind.WorldSpawn, CampAnchorResolver.Resolve(held, anchors).Kind);
    }

    [Fact]
    public void An_unreadable_offer_of_another_kind_never_stands_in_for_the_one_held()
    {
        // Keeping the previous anchor is only correct when the unreadable offer
        // is the SAME kind. An unreadable settlement must not preserve a bed
        // that is gone.
        var anchors = new FakeAnchors
        {
            PlayerBed = CampAnchorOffer.Absent(CampAnchorKind.PlayerBed),
            Settlement = CampAnchorOffer.Unreadable(CampAnchorKind.Settlement),
            WorldSpawn = CampAnchorOffer.At(CampAnchorKind.WorldSpawn, Spawn),
        };

        CampAnchor resolved = CampAnchorResolver.Resolve(new CampAnchor(CampAnchorKind.PlayerBed, Bed), anchors);

        Assert.Equal(CampAnchorKind.WorldSpawn, resolved.Kind);
    }

    [Fact]
    public void Nothing_offered_is_no_anchor_and_never_the_origin()
    {
        CampAnchor resolved = CampAnchorResolver.Resolve(CampAnchor.None, new FakeAnchors());

        Assert.Equal(CampAnchorKind.None, resolved.Kind);
        Assert.False(resolved.HasAnchor);
    }

    [Fact]
    public void A_source_that_throws_is_unreadable_rather_than_absent()
    {
        // A role's reader touches the game. A game read that fails is exactly
        // the case the third validity value is for, and a resolver that let the
        // exception out would take the whole tick with it.
        CampAnchor held = new CampAnchor(CampAnchorKind.PlayerBed, Bed);

        CampAnchor kept = CampAnchorResolver.Resolve(held, new ThrowingAnchors());

        Assert.Equal(CampAnchorKind.PlayerBed, kept.Kind);
        Assert.Equal(Bed, kept.Position);
    }

    [Fact]
    public void An_offer_labelled_with_the_wrong_kind_is_not_believed()
    {
        CampAnchor resolved = CampAnchorResolver.Resolve(CampAnchor.None, new MislabelledAnchors());

        // Not the settlement point smuggled in as a bed 900 metres away.
        Assert.Equal(CampAnchorKind.WorldSpawn, resolved.Kind);
    }

    [Fact]
    public void An_offer_with_a_position_nobody_could_compute_is_not_an_anchor()
    {
        var anchors = new FakeAnchors
        {
            PlayerBed = CampAnchorOffer.At(CampAnchorKind.PlayerBed, new NpcPoint(float.NaN, 0, 0)),
            WorldSpawn = CampAnchorOffer.At(CampAnchorKind.WorldSpawn, Spawn),
        };

        Assert.Equal(CampAnchorKind.WorldSpawn, CampAnchorResolver.Resolve(CampAnchor.None, anchors).Kind);
    }

    [Fact]
    public void There_is_no_offer_a_role_can_make_that_adopts_a_distant_structure()
    {
        // The promise is structural rather than a matter of care: the resolver
        // takes an anchor source and nothing else. It cannot see a piece, a
        // cluster or a building, so there is no path by which one becomes home.
        Type[] parameters = typeof(CampAnchorResolver)
            .GetMethod(
                nameof(CampAnchorResolver.Resolve),
                System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public)!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal(new[] { typeof(CampAnchor), typeof(ICampAnchorSource) }, parameters);
    }

    [Fact]
    public void A_resolver_with_no_source_refuses_rather_than_inventing_one()
    {
        Assert.Throws<ArgumentNullException>(() => CampAnchorResolver.Resolve(CampAnchor.None, null!));
    }

    [Fact]
    public void An_anchor_that_drifts_by_float_noise_is_the_same_place()
    {
        var here = new CampAnchor(CampAnchorKind.PlayerBed, Bed);
        var nudged = new CampAnchor(CampAnchorKind.PlayerBed, new NpcPoint(100.2f, 30.1f, 100.3f));
        var moved = new CampAnchor(CampAnchorKind.PlayerBed, new NpcPoint(140, 30, 100));

        Assert.True(here.SamePlaceAs(nudged));
        Assert.False(here.SamePlaceAs(moved));

        // Different kinds at the same point are different anchors: one is
        // temporary and the other is home.
        Assert.False(here.SamePlaceAs(new CampAnchor(CampAnchorKind.Settlement, Bed)));
    }
}
