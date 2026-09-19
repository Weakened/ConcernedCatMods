using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

public class CustodyReconciliationTests
{
    private readonly NpcWorldEpoch _world = Identities.AWorld();
    private readonly NpcCustodyLedger _ledger = new NpcCustodyLedger();

    public CustodyReconciliationTests()
    {
        _ledger.OpenJob(Custody.Job);
        _ledger.Acquire(Custody.Step(0), Custody.Body(_world), Custody.Timber, 10);
    }

    [Fact]
    public void A_body_holding_what_the_record_says_matches()
    {
        var observer = new FakeObserver().Sees(Custody.Body(_world), Custody.Timber, 10);

        NpcReconciliationReport report = NpcCustodyReconciler.Reconcile(_ledger, observer, false);

        Assert.True(report.AllMatch);
        Assert.Null(report.Foremost(Custody.Job));
    }

    [Fact]
    public void Fewer_units_than_the_record_expects_is_reported_and_nothing_is_refunded()
    {
        var observer = new FakeObserver().Sees(Custody.Body(_world), Custody.Timber, 6);

        NpcReconciliationReport report = NpcCustodyReconciler.Reconcile(_ledger, observer, false);
        NpcReconciliationFinding? finding = report.Foremost(Custody.Job);

        Assert.NotNull(finding);
        Assert.Equal(NpcReconciliationFindingKind.BelowExpected, finding!.Kind);
        Assert.Equal(10, finding.Expected);
        Assert.Equal(6, finding.Actual);
        Assert.Equal(4, finding.Shortfall);

        // The record is untouched: a shortfall is something a person writes
        // off, never something reconciliation does.
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void More_units_than_the_record_expects_is_never_credited()
    {
        var observer = new FakeObserver().Sees(Custody.Body(_world), Custody.Timber, 14);

        NpcReconciliationFinding? finding =
            NpcCustodyReconciler.Reconcile(_ledger, observer, false).Foremost(Custody.Job);

        Assert.Equal(NpcReconciliationFindingKind.AboveExpected, finding!.Kind);
        Assert.Equal(0, finding.Shortfall);
        Assert.Equal(10, _ledger.TotalEverywhere(Custody.Job, Custody.Timber));
    }

    [Fact]
    public void A_place_that_cannot_be_checked_is_unclear_rather_than_empty()
    {
        // The difference that matters most: an unloaded zone reading as zero is
        // a player asked to write off material that is sitting safely in a
        // chest they cannot see.
        var observer = new FakeObserver().Sees(Custody.Body(_world), Custody.Timber, null);

        NpcReconciliationFinding? finding =
            NpcCustodyReconciler.Reconcile(_ledger, observer, false).Foremost(Custody.Job);

        Assert.Equal(NpcReconciliationFindingKind.Unclear, finding!.Kind);
        Assert.Null(finding.Actual);
        Assert.Equal(0, finding.Shortfall);
    }

    [Fact]
    public void An_observer_that_throws_is_unclear_and_never_a_shortfall()
    {
        NpcReconciliationFinding? finding =
            NpcCustodyReconciler.Reconcile(_ledger, new FakeObserver { Throws = true }, false)
                .Foremost(Custody.Job);

        Assert.Equal(NpcReconciliationFindingKind.Unclear, finding!.Kind);
    }

    [Fact]
    public void An_interrupted_transfer_the_counts_show_arrived_waits_for_a_person()
    {
        Interrupt(5);
        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 5)
            .Sees(Custody.Chest(_world), Custody.Timber, 5);

        NpcReconciliationFinding? finding =
            NpcCustodyReconciler.Reconcile(_ledger, observer, true).Foremost(Custody.Job);

        Assert.Equal(NpcReconciliationFindingKind.EffectVisible, finding!.Kind);
        Assert.Equal(Custody.Step(1), finding.Request);
        Assert.Equal(-5, finding.SourceDelta);
        Assert.Equal(5, finding.DestinationDelta);

        // Seen is not applied. The same counts are produced by the player
        // moving the material themselves.
        Assert.Equal(10, _ledger.HoldingAt(Custody.Job, Custody.Body(_world), Custody.Timber));
    }

    [Fact]
    public void An_interrupted_transfer_the_counts_show_did_not_happen_is_reported_as_such()
    {
        Interrupt(5);
        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 10)
            .Sees(Custody.Chest(_world), Custody.Timber, 0);

        NpcReconciliationFinding? finding =
            NpcCustodyReconciler.Reconcile(_ledger, observer, true).Foremost(Custody.Job);

        Assert.Equal(NpcReconciliationFindingKind.NoEffect, finding!.Kind);
        Assert.Equal(0, finding.SourceDelta);
        Assert.Equal(0, finding.DestinationDelta);
    }

    [Fact]
    public void Counts_that_fit_neither_outcome_are_unclear()
    {
        Interrupt(5);
        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 8)
            .Sees(Custody.Chest(_world), Custody.Timber, 5);

        NpcReconciliationFinding? finding =
            NpcCustodyReconciler.Reconcile(_ledger, observer, true).Foremost(Custody.Job);

        Assert.Equal(NpcReconciliationFindingKind.Unclear, finding!.Kind);
        Assert.Equal(-2, finding.SourceDelta);
        Assert.Equal(5, finding.DestinationDelta);
    }

    [Fact]
    public void Two_uncertain_changes_over_one_place_make_the_counts_mean_nothing()
    {
        // Either transfer's units could account for the difference, and
        // attributing it to one of them at random is worse than saying so.
        Interrupt(5);
        Interrupt(3, step: 2);

        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 5)
            .Sees(Custody.Chest(_world), Custody.Timber, 5);

        NpcReconciliationReport report = NpcCustodyReconciler.Reconcile(_ledger, observer, true);

        Assert.Equal(2, report.Findings.Count);
        Assert.All(report.Findings, finding =>
            Assert.Equal(NpcReconciliationFindingKind.Unclear, finding.Kind));
    }

    [Fact]
    public void An_uncertain_transfer_outranks_a_count_mismatch_about_the_same_material()
    {
        // The mismatch may well BE that transfer, and asking a player about the
        // same material twice under two descriptions is how a report stops
        // being believed.
        Interrupt(5);
        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 5)
            .Sees(Custody.Chest(_world), Custody.Timber, 5);

        NpcReconciliationReport report = NpcCustodyReconciler.Reconcile(_ledger, observer, true);

        Assert.Single(report.Findings);
        Assert.False(report.Foremost(Custody.Job)!.Request.IsEmpty);
    }

    [Fact]
    public void Delivered_material_is_never_checked_against_the_chest_it_went_into()
    {
        // A delivery container also holds the player's own things, and later
        // changes to it are theirs. Checking it would report the player's own
        // withdrawals as the NPC's shortfalls.
        _ledger.CountRecord();
        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Delivered(_world), Custody.Timber, 10,
            _ledger.Revision);
        _ledger.Begin(intent);
        _ledger.CountRecord();
        _ledger.Finish(new NpcTransferReceipt(
            intent.Request, NpcTransferOutcome.Completed, 10, intent.From, "delivered"));

        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 0)
            .Sees(Custody.Delivered(_world), Custody.Timber, 0);

        NpcReconciliationReport report = NpcCustodyReconciler.Reconcile(_ledger, observer, false);

        Assert.True(report.AllMatch);
        Assert.DoesNotContain(
            report.Findings, finding => finding.Location.Place == NpcCustodyPlace.Delivered);
    }

    [Fact]
    public void A_place_the_record_has_emptied_is_still_checked()
    {
        // Where material nobody accounted for would otherwise go unseen,
        // because nothing would ever look there.
        _ledger.CountRecord();
        var intent = new NpcTransferIntent(
            Custody.Step(1), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, 10,
            _ledger.Revision);
        _ledger.Begin(intent);
        _ledger.CountRecord();
        _ledger.Finish(new NpcTransferReceipt(
            intent.Request, NpcTransferOutcome.Completed, 10, intent.From, "moved"));

        var observer = new FakeObserver()
            .Sees(Custody.Body(_world), Custody.Timber, 3)
            .Sees(Custody.Chest(_world), Custody.Timber, 10);

        NpcReconciliationReport report = NpcCustodyReconciler.Reconcile(_ledger, observer, false);

        Assert.Contains(
            report.Findings,
            finding => finding.Location.Equals(Custody.Body(_world))
                && finding.Kind == NpcReconciliationFindingKind.AboveExpected);
    }

    [Fact]
    public void A_reconciler_with_no_ledger_or_no_observer_refuses()
    {
        Assert.Throws<ArgumentNullException>(
            () => NpcCustodyReconciler.Reconcile(null!, new FakeObserver(), false));
        Assert.Throws<ArgumentNullException>(
            () => NpcCustodyReconciler.Reconcile(_ledger, null!, false));
    }

    /// <summary>Starts a transfer and ends the session before its result is
    /// recorded - an interruption, as the next load sees it.</summary>
    private void Interrupt(int count, int step = 1)
    {
        _ledger.CountRecord();
        var intent = new NpcTransferIntent(
            Custody.Step(step), Custody.Body(_world), Custody.Chest(_world), Custody.Timber, count,
            _ledger.Revision);
        _ledger.Begin(intent);
        _ledger.CloseOpenIntents();
    }
}
