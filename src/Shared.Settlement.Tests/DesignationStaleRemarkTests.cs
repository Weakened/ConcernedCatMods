using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>#294: re-marking a supply chest that belongs to a previous run of
/// the world used to overwrite its row without the undesignation cascade, so a
/// reservation held against the old key was orphaned — never refunded, its order
/// never cancelled, and unreachable by any later cascade. These pin the fix:
/// the replacement runs the cascade first, matches reservations by key AND
/// epoch, tells the player what happened, and conserves the material.</summary>
public sealed class DesignationStaleRemarkTests
{
    private static readonly SettlementScope Scope = new(worldId: 294, settlement: new SettlementId("stale-camp"));
    private static readonly OrderId Cottage = new("cottage-1");
    private static readonly SitePoint Origin = new(0f, 10f, 0f);

    private const string FirstRun = "run-a";
    private const string SecondRun = "run-b";

    private sealed class Site : IDesignationSite
    {
        public AreaAccess Answer { get; set; } = AreaAccess.Granted;

        public AreaAccess CheckAccess(SitePoint centre, float radius) => Answer;
    }

    private static SettlementRegister Settlement(Site site, string run, string chest = "chest-a")
    {
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch(run);
        register.Designate(DesignationRequest.Area(DesignationKind.SettlementArea, Origin, 24f), site, true);
        register.Designate(DesignationRequest.Container(new SitePoint(3f, 10f, 3f), chest), site, true);
        return register;
    }

    private static SettlementJournal ReservedFrom(string chest, string? epoch, int wood = 20)
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Approve);
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Reserve);
        journal.Append(
            JournalEntryKind.Reserved, Cottage, RequestId.For(Cottage, 0),
            container: chest, stacks: new[] { new MaterialStack("Wood", wood) }, containerEpoch: epoch);
        return journal;
    }

    [Fact]
    public void ReMarkingAStaleChestReturnsWhatWasHeldAgainstItAndCancelsItsOrder()
    {
        var site = new Site();
        SettlementRegister register = Settlement(site, FirstRun);
        SettlementJournal journal = ReservedFrom("chest-a", FirstRun);

        // Reload: the key names nothing now.
        register.UseIdentityEpoch(SecondRun);
        Assert.True(register.HasStaleSupplyIdentity);

        DesignationResult result = register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-a-reloaded"), site, true, journal,
            out UndesignationPlan? replaced);

        Assert.Equal(DesignationOutcome.Designated, result.Outcome);
        Assert.NotNull(replaced);
        Assert.Single(replaced!.OrdersToCancel);
        Assert.Equal(20, replaced.Totals()["Wood"]);
        Assert.Contains("returns 20 Wood", replaced.Describe());

        ReplayResult after = journal.Replay();
        Assert.Equal(OrderState.Cancelled, after.StateOf(Cottage));
        Assert.Equal(20, after.Ledger.Totals(ReservationState.Refunded)["Wood"]);
        Assert.Empty(after.Ledger.Totals(ReservationState.Held));
        Assert.True(register.IsSupplyContainer("chest-a-reloaded"));
        Assert.False(register.HasStaleSupplyIdentity);

        // Conservation: held + refunded + committed is what was reserved.
        int total = after.Ledger.Totals(ReservationState.Refunded).Values.Sum()
            + after.Ledger.Totals(ReservationState.Held).Values.Sum()
            + after.Ledger.Totals(ReservationState.Committed).Values.Sum();
        Assert.Equal(20, total);
    }

    [Fact]
    public void ARefundKeepsTheContainersEpochInTheRecord()
    {
        var site = new Site();
        SettlementRegister register = Settlement(site, FirstRun);
        SettlementJournal journal = ReservedFrom("chest-a", FirstRun);
        register.UseIdentityEpoch(SecondRun);

        register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-b"), site, true, journal, out _);

        JournalEntry refund = journal.Entries.Single(e => e.Kind == JournalEntryKind.Refunded);
        Assert.Equal("chest-a", refund.Container);
        Assert.Equal(FirstRun, refund.ContainerEpoch);
    }

    [Fact]
    public void AReservationFromAnotherRunUnderTheSameKeyIsNotTheClearedChests()
    {
        // Keys are renumbered on every load: "chest-a" in run A and "chest-a"
        // in run B are likely different chests. Clearing the run-B chest must
        // not cancel an order that drew from the run-A one.
        var site = new Site();
        SettlementRegister register = Settlement(site, SecondRun);
        SettlementJournal journal = ReservedFrom("chest-a", FirstRun);

        UndesignationPlan plan = register.PlanUndesignation(DesignationKind.SupplyContainer, journal.Replay(), true);

        Assert.Empty(plan.OrdersToCancel);
        Assert.Empty(plan.ToRefund);
    }

    [Fact]
    public void AReservationWrittenBeforeEpochsStillMatchesByKey()
    {
        var site = new Site();
        SettlementRegister register = Settlement(site, FirstRun);
        SettlementJournal journal = ReservedFrom("chest-a", epoch: null);

        UndesignationPlan plan = register.PlanUndesignation(DesignationKind.SupplyContainer, journal.Replay(), true);

        Assert.Single(plan.OrdersToCancel);
    }

    [Fact]
    public void AReplacementThatWouldBeRefusedRunsNoCascade()
    {
        var site = new Site();
        SettlementRegister register = Settlement(site, FirstRun);
        SettlementJournal journal = ReservedFrom("chest-a", FirstRun);
        register.UseIdentityEpoch(SecondRun);
        int rows = journal.Entries.Count;

        // Outside the settlement: refused before anything is returned or
        // cancelled, and the stale row is still there to be seen.
        site.Answer = AreaAccess.Granted;
        DesignationResult outside = register.Designate(
            DesignationRequest.Container(new SitePoint(300f, 10f, 300f), "chest-far"), site, true, journal,
            out UndesignationPlan? replaced);
        Assert.Equal(DesignationRefusal.ContainerOutsideSettlement, outside.Refusal);
        Assert.Null(replaced);

        site.Answer = AreaAccess.Denied;
        DesignationResult warded = register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-warded"), site, true, journal, out replaced);
        Assert.Equal(DesignationRefusal.WardDenied, warded.Refusal);
        Assert.Null(replaced);

        Assert.Equal(rows, journal.Entries.Count);
        Assert.True(register.HasStaleSupplyIdentity);
        Assert.Equal(OrderState.Reserved, journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void WithoutTheRecordAStaleChestIsNeverOverwritten()
    {
        var site = new Site();
        SettlementRegister register = Settlement(site, FirstRun);
        register.UseIdentityEpoch(SecondRun);

        DesignationResult result = register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-new"), site, true);

        Assert.Equal(DesignationRefusal.StaleContainerNeedsTheRecord, result.Refusal);
        Assert.Contains("cf_settle supply", result.Describe());
        Assert.True(register.HasStaleSupplyIdentity);
        Assert.False(register.IsSupplyContainer("chest-new"));
    }

    [Fact]
    public void AReadOnlyRecordRefusesTheReplacementAndChangesNothing()
    {
        var site = new Site();
        SettlementRegister register = Settlement(site, FirstRun);
        SettlementJournal journal = ReservedFrom("chest-a", FirstRun);
        journal.MarkReadOnly();
        register.UseIdentityEpoch(SecondRun);

        DesignationResult result = register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-new"), site, true, journal, out _);

        Assert.Equal(DesignationRefusal.RecordReadOnly, result.Refusal);
        Assert.True(register.HasStaleSupplyIdentity);
    }
}
