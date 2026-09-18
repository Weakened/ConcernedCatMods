using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-01: only the one cart the player explicitly selected and
/// confirmed is assigned; every refusal speaks for itself; an uncertain identity
/// is refused and never resolved by picking the nearest cart.</summary>
public class HaulingExecutionAssignmentTests
{
    private static readonly Guid Epoch = HaulingExecutionRig.Epoch;
    private static readonly CartKey Cart = new CartKey("-100:7", Epoch);

    private static AssignmentVerdict Evaluate(
        CartAssignmentFacts? facts = null,
        CartKey? key = null,
        CartLeaseBook? book = null,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool workerAvailable = true) =>
        CartAssignmentValidator.Evaluate(
            authority,
            workerAvailable,
            facts ?? HaulingExecutionRig.SelectableCart(),
            key ?? Cart,
            book ?? new CartLeaseBook(Epoch),
            WorkerKey.Gunnar,
            HaulLimits.Default,
            HaulExecutionLimits.Default);

    [Fact]
    public void AnExplicitlySelectedIdleOwnedUprightHandCartIsAssignable()
    {
        Assert.Equal(AssignmentOutcome.Assigned, Evaluate().Outcome);
    }

    public static IEnumerable<object[]> EachRefusal()
    {
        yield return new object[] { "authority", CartAssignmentRefusal.NoAuthority };
        yield return new object[] { "worker", CartAssignmentRefusal.WorkerUnavailable };
        yield return new object[] { "nothing selected", CartAssignmentRefusal.AmbiguousSelection };
        yield return new object[] { "several candidates", CartAssignmentRefusal.AmbiguousSelection };
        yield return new object[] { "no key", CartAssignmentRefusal.AmbiguousSelection };
        yield return new object[] { "old world load", CartAssignmentRefusal.StaleIdentity };
        yield return new object[] { "key from another epoch", CartAssignmentRefusal.StaleIdentity };
        yield return new object[] { "destroyed", CartAssignmentRefusal.Destroyed };
        yield return new object[] { "unloaded", CartAssignmentRefusal.NotLoaded };
        yield return new object[] { "siege engine", CartAssignmentRefusal.NotACart };
        yield return new object[] { "too far", CartAssignmentRefusal.TooFarToSelect };
        yield return new object[] { "remote owner", CartAssignmentRefusal.NotOwnedHere };
        yield return new object[] { "in use", CartAssignmentRefusal.InUse };
        yield return new object[] { "braked", CartAssignmentRefusal.Braked };
        yield return new object[] { "tipped", CartAssignmentRefusal.NotUpright };
        yield return new object[] { "worker holds another cart", CartAssignmentRefusal.WorkerBusy };
        yield return new object[] { "cart held by another worker", CartAssignmentRefusal.AlreadyLeased };
    }

    [Theory]
    [MemberData(nameof(EachRefusal))]
    public void EachRefusalIsItsOwn(string broken, object expectedRefusal)
    {
        var expected = (CartAssignmentRefusal)expectedRefusal;
        CartAssignmentFacts facts = HaulingExecutionRig.SelectableCart();
        CartKey? key = Cart;
        var book = new CartLeaseBook(Epoch);
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted;
        bool worker = true;
        switch (broken)
        {
            case "authority": authority = WorkAuthorityVerdict.NotHost; break;
            case "worker": worker = false; break;
            case "nothing selected": facts.Selection = CartSelectionState.NothingSelected; break;
            case "several candidates": facts.Selection = CartSelectionState.SeveralCandidates; break;
            case "no key": key = null; break;
            case "old world load": facts.SelectedInThisWorldLoad = false; break;
            case "key from another epoch": key = new CartKey("-100:7", HaulingExecutionRig.NextEpoch); break;
            case "destroyed": facts.Resolved = false; facts.RecordExists = false; break;
            case "unloaded": facts.Resolved = false; facts.RecordExists = true; break;
            case "siege engine": facts.IsHandCart = false; break;
            case "too far": facts.PlayerDistanceMetres = 8.5f; break;
            case "remote owner": facts.IsOwner = false; break;
            case "in use": facts.InUse = true; break;
            case "braked": facts.Braked = true; break;
            case "tipped": facts.UpDot = 0.2f; break;
            case "worker holds another cart":
                Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-0", WorkerKey.Gunnar, new CartKey("-100:8", Epoch)));
                break;
            case "cart held by another worker":
                Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-0", new WorkerKey("teamster", "someone-else"), Cart));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(broken));
        }

        AssignmentVerdict verdict = CartAssignmentValidator.Evaluate(
            authority, worker, facts, key, book, WorkerKey.Gunnar, HaulLimits.Default, HaulExecutionLimits.Default);

        Assert.Equal(AssignmentOutcome.Refused, verdict.Outcome);
        Assert.Equal(expected, verdict.Refusal);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Detail));
    }

    [Fact]
    public void AssigningGunnarsOwnCartAgainIsIdempotentEvenWhileHitched()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        int revision = rig.Executor.Revision;

        CartAssignmentFacts inUse = HaulingExecutionRig.SelectableCart();
        inUse.InUse = true;
        AssignmentVerdict again = rig.Executor.AssignCart("lease-2", rig.Cart, inUse);

        Assert.Equal(AssignmentOutcome.AlreadyAssigned, again.Outcome);
        Assert.Equal(revision, rig.Executor.Revision);
        Assert.Equal("lease-1", rig.Executor.ActiveLease!.LeaseId);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AssignmentMovesUnassignedToReadyAndCountsAsARevision()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        int before = rig.Executor.Revision;

        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);

        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.True(rig.Executor.Revision > before);
        Assert.Equal(rig.Cart, rig.Executor.ActiveLease!.Cart);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARefusedAssignmentChangesNothing()
    {
        var rig = new HaulingExecutionRig();
        rig.Authority.Verdict = WorkAuthorityVerdict.RuntimeDisabled;
        int before = rig.Executor.Revision;

        AssignmentVerdict verdict = rig.Assign();

        Assert.Equal(CartAssignmentRefusal.NoAuthority, verdict.Refusal);
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Equal(before, rig.Executor.Revision);
        Assert.Null(rig.Executor.ActiveLease);
    }

    [Fact]
    public void AnAbsentBodyMakesGunnarUnavailableForAssignment()
    {
        var rig = new HaulingExecutionRig();
        rig.Body.Facts = rig.Body.Facts.With(f => f.Present = false);
        Assert.Equal(CartAssignmentRefusal.WorkerUnavailable, rig.Assign().Refusal);

        var seamless = new HaulingExecutionRig();
        seamless.Seam.IsAvailable = false;
        Assert.Equal(CartAssignmentRefusal.WorkerUnavailable, seamless.Assign().Refusal);
    }

    [Fact]
    public void TheLeaseBookRefusalsMapToAssignmentRefusals()
    {
        Assert.Equal(AssignmentOutcome.Assigned, CartAssignmentValidator.FromLeaseOutcome(LeaseOutcome.Assigned).Outcome);
        Assert.Equal(AssignmentOutcome.AlreadyAssigned, CartAssignmentValidator.FromLeaseOutcome(LeaseOutcome.AlreadySatisfied).Outcome);
        Assert.Equal(CartAssignmentRefusal.WorkerBusy, CartAssignmentValidator.FromLeaseOutcome(LeaseOutcome.RefusedWorkerBusy).Refusal);
        Assert.Equal(CartAssignmentRefusal.AlreadyLeased, CartAssignmentValidator.FromLeaseOutcome(LeaseOutcome.RefusedCartLeased).Refusal);
        Assert.Equal(CartAssignmentRefusal.AlreadyLeased, CartAssignmentValidator.FromLeaseOutcome(LeaseOutcome.RejectedDifferentPayload).Refusal);
        Assert.Equal(CartAssignmentRefusal.StaleIdentity, CartAssignmentValidator.FromLeaseOutcome(LeaseOutcome.RefusedStaleEpoch).Refusal);
    }

    [Fact]
    public void ASelectionMustBeConfirmedInItsWorldLoadAndInTime()
    {
        var limits = HaulExecutionLimits.Default;
        var selections = new CartSelectionBook();

        Assert.False(selections.TryConfirm(Epoch, 10f, limits, out _, out CartAssignmentRefusal nothing));
        Assert.Equal(CartAssignmentRefusal.AmbiguousSelection, nothing);

        selections.Propose(Cart, "Cart", 10f);
        Assert.True(selections.TryConfirm(Epoch, 10f + limits.SelectionConfirmSeconds, limits, out PendingCartSelection confirmed, out _));
        Assert.Equal(Cart, confirmed.Cart);

        // Consumed: a second confirm does not reuse it.
        Assert.False(selections.TryConfirm(Epoch, 11f, limits, out _, out CartAssignmentRefusal consumed));
        Assert.Equal(CartAssignmentRefusal.AmbiguousSelection, consumed);

        selections.Propose(Cart, "Cart", 10f);
        Assert.False(selections.TryConfirm(Epoch, 10.1f + limits.SelectionConfirmSeconds, limits, out _, out CartAssignmentRefusal expired));
        Assert.Equal(CartAssignmentRefusal.AmbiguousSelection, expired);

        selections.Propose(Cart, "Cart", 10f);
        Assert.False(selections.TryConfirm(HaulingExecutionRig.NextEpoch, 11f, limits, out _, out CartAssignmentRefusal stale));
        Assert.Equal(CartAssignmentRefusal.StaleIdentity, stale);
    }

    [Fact]
    public void EveryAssignmentRefusalHasItsOwnPlayerSentence()
    {
        var sentences = new HashSet<string>();
        foreach (CartAssignmentRefusal refusal in Enum.GetValues<CartAssignmentRefusal>())
        {
            string sentence = HaulRefusalSentences.Describe(refusal);
            if (refusal == CartAssignmentRefusal.Unspecified)
            {
                Assert.Equal(HaulRefusalSentences.BugSentence, sentence);
                continue;
            }

            Assert.NotEqual(HaulRefusalSentences.BugSentence, sentence);
            Assert.True(sentences.Add(sentence), refusal + " shares a sentence");
        }
    }
}
