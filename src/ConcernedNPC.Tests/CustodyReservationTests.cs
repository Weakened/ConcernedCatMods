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

/// <summary>What a role actually reserves: a wrapper it re-reads every tick,
/// with no equality of its own. The hazardous subject, written down so the
/// hazard has a test.</summary>
internal sealed class Wrapper : INpcEpochScoped
{
    internal Wrapper(string key, NpcWorldEpoch epoch)
    {
        Key = key;
        Epoch = epoch;
    }

    internal string Key { get; }

    public NpcWorldEpoch Epoch { get; }
}

/// <summary>An interface subject, which is what a role will really name -
/// and which can promise nothing about equality.</summary>
internal interface IHaveAKey : INpcEpochScoped
{
    string Key { get; }
}

/// <summary>A reference-type subject that does carry value equality, so the
/// refusal is not a blanket one.</summary>
internal sealed class KeyedWrapper : INpcEpochScoped
{
    internal KeyedWrapper(string key, NpcWorldEpoch epoch)
    {
        Key = key;
        Epoch = epoch;
    }

    internal string Key { get; }

    public NpcWorldEpoch Epoch { get; }

    public override bool Equals(object? obj) =>
        obj is KeyedWrapper other
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Epoch.Equals(other.Epoch);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
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
    public void A_book_that_would_compare_subjects_by_reference_refuses_to_be_built()
    {
        // The trap this closes is not "the book holds nothing". A role that
        // re-reads its container wrapper each tick - which the container seam's
        // own doc requires - hands a fresh object every tick, so job B misses
        // in the dictionary and is GRANTED a chest job A already holds. Two
        // NPCs withdraw from one chest, both believing they are the only
        // holder, and releasing still clears both so nothing looks wrong.
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new NpcReservationBook<Wrapper>(_world));

        Assert.Contains("by reference", refused.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(NpcSubjectComparer.ByKey), refused.Message, StringComparison.Ordinal);

        // An interface subject cannot promise value equality either, so it is
        // refused for the same reason - and that is the case a role will
        // actually write.
        Assert.Throws<ArgumentException>(() => new NpcReservationBook<IHaveAKey>(_world));
    }

    [Fact]
    public void A_subject_with_value_equality_of_its_own_needs_no_comparer()
    {
        // The refusal must not be a blanket one, or every struct subject pays
        // for the reference types' problem.
        var book = new NpcReservationBook<Subject>(_world);
        Assert.Equal(
            ReservationOutcome.Reserved, book.Reserve(new Subject("chest-a", _world), Custody.Step(1)));

        // A class that overrides Equals is fine too.
        var overriding = new NpcReservationBook<KeyedWrapper>(_world);
        Assert.Equal(
            ReservationOutcome.Reserved,
            overriding.Reserve(new KeyedWrapper("chest-a", _world), Custody.Step(1)));
    }

    [Fact]
    public void Two_wrappers_for_one_chest_are_one_subject_when_the_book_is_told_how_to_look()
    {
        var book = new NpcReservationBook<Wrapper>(
            _world, NpcSubjectComparer.ByKey<Wrapper>(wrapper => wrapper.Key));

        Assert.Equal(
            ReservationOutcome.Reserved, book.Reserve(new Wrapper("chest-a", _world), Custody.Step(1)));

        // A different object, the same chest, a different job: refused, which
        // is the whole promise.
        Assert.Equal(
            ReservationOutcome.HeldByAnother,
            book.Reserve(new Wrapper("chest-a", _world), Custody.OtherStep(1)));

        // And the same job asking again through yet another wrapper is
        // satisfied rather than granted twice.
        Assert.Equal(
            ReservationOutcome.AlreadySatisfied,
            book.Reserve(new Wrapper("chest-a", _world), Custody.Step(2)));
        Assert.Equal(1, book.Count);

        // A genuinely different chest is still its own subject.
        Assert.Equal(
            ReservationOutcome.Reserved, book.Reserve(new Wrapper("chest-b", _world), Custody.OtherStep(1)));
    }

    [Fact]
    public void A_subject_that_cannot_say_what_it_is_matches_nothing_including_another_that_cannot()
    {
        // Falling back to a shared blank would make every broken wrapper the
        // same chest, which is the same failure arrived at from the other side.
        var book = new NpcReservationBook<Wrapper>(
            _world, NpcSubjectComparer.ByKey<Wrapper>(wrapper => wrapper.Key));

        Assert.Equal(
            ReservationOutcome.Reserved, book.Reserve(new Wrapper(string.Empty, _world), Custody.Step(1)));
        Assert.Equal(
            ReservationOutcome.Reserved,
            book.Reserve(new Wrapper(string.Empty, _world), Custody.OtherStep(1)));
        Assert.Equal(2, book.Count);

        // And a wrapper whose key getter throws is not a match for anything
        // either, rather than taking the whole tick with it.
        var throwing = new NpcReservationBook<Wrapper>(
            _world, NpcSubjectComparer.ByKey<Wrapper>(wrapper => throw new InvalidOperationException("gone")));
        Assert.Equal(
            ReservationOutcome.Reserved, throwing.Reserve(new Wrapper("chest-a", _world), Custody.Step(1)));
        Assert.Equal(
            ReservationOutcome.Reserved,
            throwing.Reserve(new Wrapper("chest-a", _world), Custody.OtherStep(1)));
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

        Assert.Equal(40, _book.ReservedIn(chest, Custody.Nails, default));
        Assert.Equal(10, _book.AvailableIn(chest, Custody.Nails, 50, default));

        // And a chest the player emptied is a reconciliation finding, never a
        // negative number handed to a planner.
        Assert.Equal(0, _book.AvailableIn(chest, Custody.Nails, 10, default));
    }

    [Fact]
    public void Two_steps_of_one_job_never_plan_on_the_same_units_either()
    {
        // The same failure, moved from two jobs to two steps of one - and the
        // one the book used to have, because the exclusion was keyed on the job
        // rather than on the claim. Step 1 sets aside all forty nails; step 2
        // then asks how many are free and, being told forty, reserves them
        // again under a name the payload check has no reason to object to.
        NpcCustodyLocation chest = Custody.Chest(_world);
        Assert.Equal(NpcCustodyOutcome.Applied, _book.Reserve(Reservation(Custody.Step(1), chest, 40)));

        Assert.Equal(0, _book.AvailableIn(chest, Custody.Nails, 40, Custody.Step(2)));
        Assert.Equal(40, _book.ReservedIn(chest, Custody.Nails, Custody.Step(2)));

        // Eighty nails out of a chest holding forty is what this stops. The
        // book cannot refuse the reservation itself - it is not told what the
        // chest holds - so the arithmetic a planner asks has to be right.
        Assert.Equal(
            40, _book.Totals(NpcReservationState.Held)[Custody.Nails.ItemName]);
    }

    [Fact]
    public void A_step_re_planning_its_own_claim_can_ask_what_it_would_have_without_it()
    {
        // The reason the exclusion exists at all, preserved: a claim asking
        // about itself must not be blocked by itself.
        NpcCustodyLocation chest = Custody.Chest(_world);
        _book.Reserve(Reservation(Custody.Step(1), chest, 40));

        Assert.Equal(40, _book.AvailableIn(chest, Custody.Nails, 40, Custody.Step(1)));
        Assert.Equal(0, _book.AvailableIn(chest, Custody.Nails, 40, Custody.Step(2)));
    }

    [Fact]
    public void A_chest_that_could_not_be_read_is_not_an_empty_chest()
    {
        // Null in, null out - the same rule the observer seam already keeps. A
        // planner told "zero" sends the player looking for material sitting in
        // an unloaded zone; a planner told "unknown" waits.
        NpcCustodyLocation chest = Custody.Chest(_world);
        _book.Reserve(Reservation(Custody.Step(1), chest, 40));

        Assert.Null(_book.AvailableIn(chest, Custody.Nails, null, default));
        Assert.Equal(0, _book.AvailableIn(chest, Custody.Nails, 0, default));
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
        Assert.Equal(40, _book.ReservedIn(Custody.Chest(_world), Custody.Nails, default));
    }

    [Fact]
    public void A_spent_name_cannot_be_reserved_again()
    {
        // The same defect the ledger's acquisition half had, in the place
        // nobody looked for it. Comparing the payload and ignoring the state
        // made a refunded claim indistinguishable from a live one, so a job
        // re-stating a name it had already given back was told "already
        // satisfied" - a success - and held nothing at all.
        NpcCustodyLocation chest = Custody.Chest(_world);
        _book.Reserve(Reservation(Custody.Step(1), chest, 40));
        Assert.Equal(NpcCustodyOutcome.Applied, _book.Refund(Custody.Step(1)));

        Assert.Equal(NpcCustodyOutcome.Stale, _book.Reserve(Reservation(Custody.Step(1), chest, 40)));
        Assert.Equal(0, _book.ReservedIn(chest, Custody.Nails, default));
        Assert.Empty(_book.HeldFor(Custody.Job));

        // Committed and uncertain are spent too: a name is spent once,
        // whichever way it went.
        _book.Reserve(Reservation(Custody.Step(2), chest, 10));
        _book.Commit(Custody.Step(2));
        Assert.Equal(NpcCustodyOutcome.Stale, _book.Reserve(Reservation(Custody.Step(2), chest, 10)));

        _book.Reserve(Reservation(Custody.Step(3), chest, 5));
        _book.MarkUncertain(Custody.Step(3));
        Assert.Equal(NpcCustodyOutcome.Stale, _book.Reserve(Reservation(Custody.Step(3), chest, 5)));

        // And a different payload under a spent name is still the louder
        // complaint, because it is a different defect.
        Assert.Equal(
            NpcCustodyOutcome.RejectedDifferentPayload,
            _book.Reserve(Reservation(Custody.Step(1), chest, 41)));
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
