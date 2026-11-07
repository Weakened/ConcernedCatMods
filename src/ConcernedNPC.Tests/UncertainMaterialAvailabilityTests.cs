using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

public sealed class UncertainMaterialAvailabilityTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();
    private readonly NpcMaterialReservationBook _book = new();

    private NpcMaterialReservation Claim() => new(Custody.Step(1), Custody.Chest(_world),
        new[] { new NpcMaterialStack(Custody.Nails, 40) });

    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(40, false)]
    [InlineData(100, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(0, true)]
    [InlineData(40, true)]
    [InlineData(100, true)]
    public void Unknown_commit_does_not_turn_its_material_into_free_stock(int observed, bool excludeOwn)
    {
        Assert.Equal(NpcCustodyOutcome.Applied, _book.Reserve(Claim()));
        Assert.Equal(NpcCustodyOutcome.Applied, _book.MarkUncertain(Custody.Step(1)));
        ReservationId except = excludeOwn ? Custody.Step(1) : default;
        for (int retry = 0; retry < 3; retry++)
            Assert.Null(_book.AvailableIn(Custody.Chest(_world), Custody.Nails, observed, except));
        Assert.True(_book.HasUncertain);
        Assert.Single(_book.Reservations);
        Assert.Equal(NpcReservationState.Uncertain, _book.Reservations[0].State);
        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Refund(Custody.Step(1)));
        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Commit(Custody.Step(1)));
        Assert.Equal(0, _book.RefundAllFor(Custody.Job));
        Assert.Null(_book.AvailableIn(Custody.Chest(_world), Custody.Nails, observed, default));
    }

    [Fact]
    public void Restating_an_uncertain_name_cannot_make_its_stock_known_again()
    {
        _book.Reserve(Claim());
        _book.MarkUncertain(Custody.Step(1));
        Assert.Equal(NpcCustodyOutcome.Stale, _book.Reserve(Claim()));
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, _book.MarkUncertain(Custody.Step(1)));
        Assert.Null(_book.AvailableIn(Custody.Chest(_world), Custody.Nails, 40, Custody.Step(2)));
        Assert.Single(_book.Reservations);
    }

    [Fact]
    public void Uncertainty_is_scoped_to_exact_container_epoch_and_material_identity()
    {
        _book.Reserve(Claim());
        _book.MarkUncertain(Custody.Step(1));
        var otherChest = new NpcCustodyLocation(NpcCustodyPlace.Stored, "other", _world);
        Assert.Equal(100, _book.AvailableIn(otherChest, Custody.Nails, 100, default));
        Assert.Equal(100, _book.AvailableIn(Custody.Chest(Identities.AWorld()), Custody.Nails, 100, default));
        Assert.Equal(100, _book.AvailableIn(Custody.Chest(_world), Custody.Timber, 100, default));
        Assert.Equal(100, _book.AvailableIn(Custody.Chest(_world), new NpcMaterial(Custody.Nails.ItemName, 2, 0), 100, default));
        Assert.Equal(100, _book.AvailableIn(Custody.Chest(_world), new NpcMaterial(Custody.Nails.ItemName, 1, 1), 100, default));
        Assert.Null(_book.AvailableIn(Custody.Chest(_world), Custody.Nails, null, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Known_settlement_releases_availability_while_an_ordinary_hold_still_subtracts(bool refund)
    {
        _book.Reserve(Claim());
        Assert.Equal(60, _book.AvailableIn(Custody.Chest(_world), Custody.Nails, 100, default));
        Assert.Equal(100, _book.AvailableIn(Custody.Chest(_world), Custody.Nails, 100, Custody.Step(1)));
        Assert.Equal(NpcCustodyOutcome.Applied, refund ? _book.Refund(Custody.Step(1)) : _book.Commit(Custody.Step(1)));
        Assert.Equal(100, _book.AvailableIn(Custody.Chest(_world), Custody.Nails, 100, default));
    }
}
