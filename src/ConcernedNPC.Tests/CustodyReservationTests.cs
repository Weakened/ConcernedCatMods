using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A thing a book can hold, named within one world load.</summary>
internal readonly struct Subject : INpcEpochScoped, IEquatable<Subject>
{
    internal Subject(string key, NpcWorldEpoch epoch)
    {
        Key = key;
        Epoch = epoch;
    }

    internal string Key { get; }

    public NpcWorldEpoch Epoch { get; }

    public bool Equals(Subject other) =>
        string.Equals(Key, other.Key, StringComparison.Ordinal) && Epoch.Equals(other.Epoch);

    public override bool Equals(object? obj) => obj is Subject other && Equals(other);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(Key ?? string.Empty) * 397) ^ Epoch.GetHashCode());
}

public class SubjectReservationTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();

    [Fact]
    public void One_job_holds_a_subject_and_another_is_refused_rather_than_queued()
    {
        // Two NPCs walking to the same tree is a wasted trip; two NPCs
        // withdrawing the same forty nails is a player's chest emptied twice.
        var book = new NpcReservationBook<Subject>(_world);
        var chest = new Subject("chest-a", _world);

        Assert.Equal(ReservationOutcome.Reserved, book.Reserve(chest, Custody.Step(1)));
        Assert.Equal(ReservationOutcome.HeldByAnother, book.Reserve(chest, Custody.OtherStep(1)));

        Assert.True(book.TryGetHolder(chest, out ReservationId holder));
        Assert.Equal(Custody.Step(1), holder);
        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void The_same_job_asking_twice_is_satisfied_and_so_is_a_re_plan_that_renumbers_its_steps()
    {
        // Idempotence is not a convenience: it is what lets a job resumed after
        // an interruption re-establish its holds by walking its plan again. And
        // the conflict is decided on the job, so a re-plan that moves the same
        // chest from step 3 to step 2 is not refused by the job itself.
        var book = new NpcReservationBook<Subject>(_world);
        var chest = new Subject("chest-a", _world);

        Assert.Equal(ReservationOutcome.Reserved, book.Reserve(chest, Custody.Step(3)));
        Assert.Equal(ReservationOutcome.AlreadySatisfied, book.Reserve(chest, Custody.Step(3)));
        Assert.Equal(ReservationOutcome.AlreadySatisfied, book.Reserve(chest, Custody.Step(2)));
        Assert.True(book.IsHeldBy(chest, Custody.Step(9)));
        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void A_subject_named_in_another_world_load_is_refused_rather_than_matched()
    {
        // The saved name of yesterday's chest is, today, overwhelmingly likely
        // to name something else - a tree, a door, somebody's boat.
        var book = new NpcReservationBook<Subject>(_world);

        Assert.Equal(
            ReservationOutcome.StaleEpoch,
            book.Reserve(new Subject("chest-a", Identities.AWorld()), Custody.Step(1)));
        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void A_book_with_no_world_loaded_refuses_everything()
    {
        var book = new NpcReservationBook<Subject>(NpcWorldEpoch.Unknown);

        Assert.Equal(
            ReservationOutcome.StaleEpoch, book.Reserve(new Subject("chest-a", _world), Custody.Step(1)));
    }

    [Fact]
    public void Releasing_something_you_do_not_hold_is_an_answer_rather_than_a_failure()
    {
        // Cleanup after a reload, a death or a failure releases
        // unconditionally, so it has to be able to.
        var book = new NpcReservationBook<Subject>(_world);
        var chest = new Subject("chest-a", _world);

        Assert.Equal(ReservationOutcome.NotHeld, book.Release(chest, Custody.Step(1)));

        book.Reserve(chest, Custody.Step(1));
        Assert.Equal(ReservationOutcome.NotHeld, book.Release(chest, Custody.OtherStep(1)));
        Assert.Equal(ReservationOutcome.Released, book.Release(chest, Custody.Step(4)));
        Assert.Equal(ReservationOutcome.NotHeld, book.Release(chest, Custody.Step(1)));
    }

    [Fact]
    public void An_interruption_releases_everything_a_job_held_without_knowing_what_that_was()
    {
        var book = new NpcReservationBook<Subject>(_world);
        book.Reserve(new Subject("chest-a", _world), Custody.Step(1));
        book.Reserve(new Subject("chest-b", _world), Custody.Step(2));
        book.Reserve(new Subject("chest-c", _world), Custody.OtherStep(1));

        Assert.Equal(2, book.ReleaseAllFor(Custody.Job));
        Assert.Equal(0, book.ReleaseAllFor(Custody.Job));
        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void A_nameless_reservation_takes_nothing()
    {
        var book = new NpcReservationBook<Subject>(_world);

        Assert.Equal(
            ReservationOutcome.Unspecified, book.Reserve(new Subject("chest-a", _world), default));
        Assert.Equal(0, book.Count);
        Assert.False(book.IsHeldBy(new Subject("chest-a", _world), default));
    }

    [Fact]
    public void A_world_load_starts_a_new_book_and_the_job_re_establishes_its_holds_from_its_plan()
    {
        // Reconcile rather than replay, in its simplest form: the name is
        // derived, so re-deriving it is either already held by this job or not
        // held at all. There is no third case in which something is taken
        // twice.
        var before = new NpcReservationBook<Subject>(_world);
        before.Reserve(new Subject("chest-a", _world), Custody.Step(1));

        NpcWorldEpoch next = Identities.AWorld();
        var after = new NpcReservationBook<Subject>(next);

        Assert.Equal(0, after.Count);
        Assert.Equal(ReservationOutcome.Reserved, after.Reserve(new Subject("chest-a", next), Custody.Step(1)));
        Assert.Equal(
            ReservationOutcome.AlreadySatisfied, after.Reserve(new Subject("chest-a", next), Custody.Step(1)));
        Assert.Equal(1, after.Count);
    }
}

public class MaterialReservationTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();
    private readonly NpcMaterialReservationBook _book = new NpcMaterialReservationBook();

    [Fact]
    public void Two_jobs_never_plan_on_the_same_units()
    {
        // The failure this prevents: both plans look feasible, both start, and
        // the second discovers halfway through that its material is gone.
        NpcCustodyLocation chest = Custody.Chest(_world);
        Assert.Equal(NpcCustodyOutcome.Applied, _book.Reserve(Reservation(Custody.Step(1), chest, 40)));

        Assert.Equal(40, _book.ReservedIn(chest, Custody.Nails, Custody.OtherJob));
        Assert.Equal(10, _book.AvailableIn(chest, Custody.Nails, 50, Custody.OtherJob));

        // Its own job sees what it set aside as still its own to plan with.
        Assert.Equal(50, _book.AvailableIn(chest, Custody.Nails, 50, Custody.Job));

        // And a chest the player emptied is a reconciliation finding, never a
        // negative number handed to a planner.
        Assert.Equal(0, _book.AvailableIn(chest, Custody.Nails, 10, Custody.OtherJob));
    }

    [Fact]
    public void A_refund_happens_exactly_once()
    {
        _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world), 40));

        Assert.Equal(NpcCustodyOutcome.Applied, _book.Refund(Custody.Step(1)));
        Assert.Equal(NpcCustodyOutcome.AlreadySatisfied, _book.Refund(Custody.Step(1)));

        // And it cannot be turned into a commit afterwards, which would be the
        // double spend the whole type exists to prevent.
        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Commit(Custody.Step(1)));

        Assert.Equal(40, _book.Totals(NpcReservationState.Refunded)[Custody.Nails.ItemName]);
        Assert.Empty(_book.Totals(NpcReservationState.Held));
    }

    [Fact]
    public void A_release_of_everything_a_job_holds_pays_each_refund_once()
    {
        _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world), 40));
        _book.Reserve(Reservation(Custody.Step(2), Custody.Chest(_world, "chest-b"), 10));
        _book.Reserve(Reservation(Custody.OtherStep(1), Custody.Chest(_world), 5));

        Assert.Equal(2, _book.RefundAllFor(Custody.Job));
        Assert.Equal(0, _book.RefundAllFor(Custody.Job));
        Assert.Single(_book.HeldFor(Custody.OtherJob));
    }

    [Fact]
    public void The_same_reservation_restored_twice_sets_nothing_aside_twice()
    {
        NpcMaterialReservation first = Reservation(Custody.Step(1), Custody.Chest(_world), 40);

        Assert.Equal(NpcCustodyOutcome.Applied, _book.Reserve(first));
        Assert.Equal(
            NpcCustodyOutcome.AlreadySatisfied,
            _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world), 40)));

        Assert.Equal(1, _book.Count);
        Assert.Equal(40, _book.ReservedIn(Custody.Chest(_world), Custody.Nails, Custody.OtherJob));
    }

    [Fact]
    public void The_same_name_for_a_different_payload_is_refused_and_keeps_the_first()
    {
        _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world), 40));

        Assert.Equal(
            NpcCustodyOutcome.RejectedDifferentPayload,
            _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world), 41)));
        Assert.Equal(
            NpcCustodyOutcome.RejectedDifferentPayload,
            _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world, "chest-b"), 40)));

        Assert.True(_book.TryGet(Custody.Step(1), out NpcMaterialReservation kept));
        Assert.Equal(40, kept.CountOf(Custody.Nails));
        Assert.Equal("chest-a", kept.Container.Key);
    }

    [Fact]
    public void A_refund_belongs_to_the_container_it_came_from_in_the_world_it_came_from()
    {
        NpcMaterialReservation reservation = Reservation(Custody.Step(1), Custody.Chest(_world), 40);
        _book.Reserve(reservation);

        Assert.True(reservation.CameFrom(Custody.Chest(_world)));
        Assert.False(reservation.CameFrom(Custody.Chest(_world, "chest-b")));

        // A key alone cannot say which chest this was once a world has been
        // reloaded, so a refund across a load finds nothing rather than
        // somewhere plausible.
        Assert.False(reservation.CameFrom(Custody.Chest(Identities.AWorld())));
    }

    [Fact]
    public void A_commit_whose_outcome_is_unknown_is_neither_spent_nor_owed_back()
    {
        _book.Reserve(Reservation(Custody.Step(1), Custody.Chest(_world), 40));

        Assert.Equal(NpcCustodyOutcome.Applied, _book.MarkUncertain(Custody.Step(1)));
        Assert.True(_book.HasUncertain);

        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Refund(Custody.Step(1)));
        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Commit(Custody.Step(1)));
        Assert.Empty(_book.HeldFor(Custody.Job));
    }

    [Fact]
    public void A_reservation_with_nothing_in_it_or_nowhere_to_go_back_to_is_not_a_reservation()
    {
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _book.Reserve(new NpcMaterialReservation(Custody.Step(1), Custody.Chest(_world), null)));
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _book.Reserve(new NpcMaterialReservation(
                default, Custody.Chest(_world), new[] { new NpcMaterialStack(Custody.Nails, 1) })));
        Assert.Equal(
            NpcCustodyOutcome.Rejected,
            _book.Reserve(new NpcMaterialReservation(
                Custody.Step(1),
                new NpcCustodyLocation(NpcCustodyPlace.Unspecified, "nowhere", _world),
                new[] { new NpcMaterialStack(Custody.Nails, 1) })));

        Assert.Equal(0, _book.Count);
    }

    [Fact]
    public void Settling_something_that_was_never_reserved_is_refused()
    {
        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Refund(Custody.Step(7)));
        Assert.Equal(NpcCustodyOutcome.Rejected, _book.Commit(default));
    }

    [Fact]
    public void Empty_stacks_never_reach_the_book()
    {
        // An absent stack and a zero stack are the same thing, and having both
        // is how a total gets counted twice.
        var reservation = new NpcMaterialReservation(
            Custody.Step(1),
            Custody.Chest(_world),
            new[] { new NpcMaterialStack(Custody.Nails, 0), new NpcMaterialStack(Custody.Nails, 4) });

        Assert.Single(reservation.Stacks);
        Assert.Equal(4, reservation.CountOf(Custody.Nails));
    }

    private NpcMaterialReservation Reservation(ReservationId id, NpcCustodyLocation container, int count) =>
        new NpcMaterialReservation(id, container, new[] { new NpcMaterialStack(Custody.Nails, count) });
}
