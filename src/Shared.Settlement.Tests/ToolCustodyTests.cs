using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Tools;

namespace Shared.Settlement.Tests;

/// <summary>CF-NPC-001: the real-tool contract Thorstein cannot work without.
///
/// The owner's notes are unusually specific about what must not happen, and
/// every one of those is a test here rather than a sentence in a comment: a
/// handover moves the player's actual item and never clones it, an interrupted
/// transfer is never guessed either way, a completed introduction never relocks
/// because a hammer broke, and picking a branch off the ground does not wear an
/// axe nobody swung.
///
/// What these tests deliberately do <b>not</b> claim: that a real item moved
/// between two real inventories. That is the adapter's job and needs the game.
/// A dictionary changing state is not proof of a tool handover, and this file
/// is not offered as one.</summary>
public sealed class ToolCustodyTests
{
    private static readonly WorkerId Thorstein = new("thorstein");
    private static readonly OrderId Job = new("first-job");

    private static ToolSpecimen Axe(float durability = 100f, int tier = 2, int quality = 1) =>
        new(ToolKind.Axe, "AxeBronze", quality, durability, tier);

    private static ToolSpecimen Hammer(float durability = 100f) =>
        new(ToolKind.Hammer, "Hammer", 1, durability, 0);

    private static ToolHolding Handover(int step, ToolSpecimen tool) =>
        new(RequestId.For(Job, step), Thorstein, tool);

    /// <summary>Says yes to everything. Used where the test is about the ledger
    /// rather than about wear.</summary>
    private sealed class Serviceable : IToolCondition
    {
        public bool IsUsable(ToolHolding holding) => true;
    }

    /// <summary>Says no to one kind, yes to the rest — a broken axe with a sound
    /// hammer, which is the case a player actually hits.</summary>
    private sealed class BrokenKind : IToolCondition
    {
        private readonly ToolKind _broken;

        internal BrokenKind(ToolKind broken)
        {
            _broken = broken;
        }

        public bool IsUsable(ToolHolding holding) => holding.Tool.Kind != _broken;
    }

    private static ToolLedger Equipped()
    {
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe()));
        ledger.Issue(Handover(1, Hammer()));
        return ledger;
    }

    // ------------------------------------------------------------------
    // A handover moves one instance, and can be repeated safely
    // ------------------------------------------------------------------

    [Fact]
    public void AHandoverRecordsTheActualToolThePlayerGave()
    {
        var ledger = new ToolLedger();
        ToolSpecimen given = Axe(durability: 63.5f, tier: 2, quality: 3);

        Assert.Equal(ToolOutcome.Applied, ledger.Issue(Handover(0, given)));

        Assert.True(ledger.TryGetHeld(Thorstein, ToolKind.Axe, out ToolHolding held));
        Assert.Equal("AxeBronze", held.Tool.ItemKey);
        Assert.Equal(3, held.Tool.Quality);
        Assert.Equal(2, held.Tool.ToolTier);
        Assert.Equal(63.5f, held.Tool.DurabilityAtIssue);
    }

    [Fact]
    public void RepeatingAHandoverDoesNotProduceASecondTool()
    {
        // The adapter may not know whether its previous attempt reached disk --
        // a crash between taking the item and recording it is the whole reason
        // the transaction id exists. Retrying must repeat the REQUEST, not the
        // effect.
        var ledger = new ToolLedger();

        Assert.Equal(ToolOutcome.Applied, ledger.Issue(Handover(0, Axe())));
        Assert.Equal(ToolOutcome.AlreadySatisfied, ledger.Issue(Handover(0, Axe())));

        Assert.Single(ledger.HeldBy(Thorstein));
        Assert.Equal(1, ledger.CountIn(ToolHoldingState.Held));
    }

    [Fact]
    public void ReusingATransactionIdForADifferentToolIsRefusedNotWavedThrough()
    {
        // "Already satisfied" told a caller that the thing it asked for had
        // happened. If it asked for something ELSE under a used id, that answer
        // is a lie in the most dangerous direction -- the caller believes a
        // second tool was recorded and moves on.
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe()));

        Assert.Equal(ToolOutcome.Rejected, ledger.Issue(Handover(0, Hammer())));
        Assert.Equal(
            ToolOutcome.Rejected,
            ledger.Issue(new ToolHolding(RequestId.For(Job, 0), new WorkerId("somebody-else"), Axe())));

        Assert.Single(ledger.Holdings);
        Assert.Equal(ToolKind.Axe, ledger.HeldBy(Thorstein)[0].Tool.Kind);
    }

    [Fact]
    public void AnInterruptedHandoverCanActuallyBeResolved()
    {
        // The previous version had no way out of Uncertain at all, while the
        // player-facing text said "resolve it". Both answers a person can give
        // are available, and only a person can give them.
        var toWorker = new ToolLedger();
        toWorker.Issue(Handover(0, Axe()));
        toWorker.MarkUncertain(RequestId.For(Job, 0));

        Assert.Equal(ToolOutcome.Applied, toWorker.Resolve(RequestId.For(Job, 0), workerHasIt: true));
        Assert.Equal(1, toWorker.CountIn(ToolHoldingState.Held));
        Assert.False(toWorker.HasUncertainHandover(Thorstein));

        var toPlayer = new ToolLedger();
        toPlayer.Issue(Handover(0, Axe()));
        toPlayer.MarkUncertain(RequestId.For(Job, 0));

        Assert.Equal(ToolOutcome.Applied, toPlayer.Resolve(RequestId.For(Job, 0), workerHasIt: false));
        Assert.Equal(1, toPlayer.CountIn(ToolHoldingState.Returned));

        // And resolving is only ever available FROM Uncertain.
        Assert.Equal(ToolOutcome.Rejected, toPlayer.Resolve(RequestId.For(Job, 0), true));
    }

    [Fact]
    public void OneWorkersInterruptedHandoverDoesNotStopEverybodyElse()
    {
        // It used to. HasUncertainHandover asked the question of the whole
        // ledger, so a single interrupted transfer refused every worker every
        // job -- including bare-handed gathering that touches no tool.
        var ledger = new ToolLedger();
        var other = new WorkerId("other-hand");
        ledger.Issue(Handover(0, Axe()));
        ledger.MarkUncertain(RequestId.For(Job, 0));

        Assert.True(ledger.HasUncertainHandover(Thorstein));
        Assert.False(ledger.HasUncertainHandover(other));
        Assert.True(ledger.HasAnyUncertainHandover);

        Assert.True(WorkerReadiness.Assess(
            other, true, ledger, WorkerReadiness.ForGathering, new Serviceable()).IsReady);
    }

    [Fact]
    public void ACallerThatDoesNotSayWhatTheJobNeedsIsRefused()
    {
        // The one fail-OPEN path: a null requirement list fell back to "needs
        // nothing" and answered Ready.
        ToolLedger ledger = Equipped();

        ReadinessVerdict verdict = WorkerReadiness.Assess(
            Thorstein, true, ledger, null!, new Serviceable());

        Assert.False(verdict.IsReady);
        Assert.Equal(ReadinessRefusal.ToolMissing, verdict.Refusal);
        Assert.Equal(ToolKind.None, verdict.Tool);
        Assert.DoesNotContain("hammer", verdict.Describe());
    }

    [Fact]
    public void IssuingIsGatedClosedWhileAHandoverCannotSurviveAReload()
    {
        // Not a preference and not a setting. The ledger does not persist, so
        // taking somebody's axe would lose the record of it on the next reload.
        Assert.True(ToolLedger.RequiresPersistence);
    }

    [Fact]
    public void AToolIsGivenBackExactlyOnce()
    {
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe()));
        RequestId transaction = RequestId.For(Job, 0);

        Assert.Equal(ToolOutcome.Applied, ledger.Return(transaction));
        Assert.Equal(ToolOutcome.AlreadySatisfied, ledger.Return(transaction));

        Assert.Empty(ledger.HeldBy(Thorstein));
        Assert.Equal(1, ledger.CountIn(ToolHoldingState.Returned));
    }

    [Fact]
    public void AReturnedToolIsNeverQuietlyTakenBack()
    {
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe()));
        RequestId transaction = RequestId.For(Job, 0);
        ledger.Return(transaction);

        // Settling a settled holding a DIFFERENT way is the move that turns one
        // axe into two.
        Assert.Equal(ToolOutcome.Rejected, ledger.MarkUncertain(transaction));
        Assert.Equal(1, ledger.CountIn(ToolHoldingState.Returned));
        Assert.Equal(0, ledger.CountIn(ToolHoldingState.Held));
    }

    [Fact]
    public void AnUnknownTransactionIsRefusedRatherThanInvented()
    {
        var ledger = new ToolLedger();

        Assert.Equal(ToolOutcome.Rejected, ledger.Return(RequestId.For(Job, 7)));
        Assert.Equal(ToolOutcome.Rejected, ledger.MarkUncertain(default));
        Assert.Empty(ledger.Holdings);
    }

    [Fact]
    public void NoToolIsEverConjuredOrLostAcrossASequenceOfFailures()
    {
        // The conservation statement for tools, and it is a count rather than a
        // sum because a tool is not fungible: two axes are two items, not "200
        // durability".
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe()));
        ledger.Issue(Handover(1, Hammer()));

        // Counting every state would be a tautology -- the three states
        // partition a set nothing is ever removed from, so the total is
        // identically the number of holdings whatever TrySettle does. An
        // independent review caught exactly that shape in this project before.
        // So the assertions below name WHICH state each tool is in, which is
        // the thing a wrong settle would actually change.
        Assert.Equal(2, ledger.Holdings.Count);
        Assert.Equal(2, ledger.CountIn(ToolHoldingState.Held));

        ledger.Return(RequestId.For(Job, 0));
        ledger.Return(RequestId.For(Job, 0));
        ledger.MarkUncertain(RequestId.For(Job, 1));
        ledger.Issue(Handover(0, Axe()));
        ledger.Return(RequestId.For(Job, 1));

        Assert.Equal(2, ledger.Holdings.Count);
        Assert.Equal(0, ledger.CountIn(ToolHoldingState.Held));
        Assert.Equal(1, ledger.CountIn(ToolHoldingState.Returned));
        Assert.Equal(1, ledger.CountIn(ToolHoldingState.Uncertain));

        // The uncertain one specifically was NOT quietly returned by that last
        // call, which is the settle a broken implementation would have made.
        Assert.True(ledger.TryGet(RequestId.For(Job, 1), out ToolHolding stillUnknown));
        Assert.Equal(ToolHoldingState.Uncertain, stillUnknown.State);
    }

    // ------------------------------------------------------------------
    // An interrupted handover is never guessed
    // ------------------------------------------------------------------

    [Fact]
    public void AnInterruptedHandoverStopsEverythingUntilAPersonResolvesIt()
    {
        ToolLedger ledger = Equipped();
        ledger.MarkUncertain(RequestId.For(Job, 0));

        Assert.True(ledger.HasUncertainHandover(Thorstein));

        ReadinessVerdict verdict = WorkerReadiness.Assess(
            Thorstein, isRecruited: true, ledger, WorkerReadiness.ForGathering, new Serviceable());

        // Even bare-handed gathering stops. Until somebody says where the axe
        // went, acting at all risks compounding it.
        Assert.False(verdict.IsReady);
        Assert.Equal(ReadinessRefusal.HandoverUncertain, verdict.Refusal);
        Assert.Contains("will not guess", verdict.Describe());
    }

    // ------------------------------------------------------------------
    // Readiness is not recruitment
    // ------------------------------------------------------------------

    [Fact]
    public void LosingAToolDoesNotUnhireAnybody()
    {
        // The owner's rule, stated as a property: "a completed introduction
        // never relocks after equipment loss; readiness is a separate state."
        ToolLedger ledger = Equipped();

        ReadinessVerdict broken = WorkerReadiness.Assess(
            Thorstein, isRecruited: true, ledger, WorkerReadiness.ForFelling,
            new BrokenKind(ToolKind.Axe));

        Assert.False(broken.IsReady);
        Assert.Equal(ReadinessRefusal.ToolUnusable, broken.Refusal);
        Assert.Equal(ToolKind.Axe, broken.Tool);

        // Still recruited: the refusal is about the axe, never about employment.
        Assert.NotEqual(ReadinessRefusal.NotRecruited, broken.Refusal);

        ReadinessVerdict repaired = WorkerReadiness.Assess(
            Thorstein, isRecruited: true, ledger, WorkerReadiness.ForFelling, new Serviceable());

        Assert.True(repaired.IsReady);
    }

    [Fact]
    public void WithNobodyRecruitedNoToolMakesAnybodyReady()
    {
        ToolLedger ledger = Equipped();

        ReadinessVerdict verdict = WorkerReadiness.Assess(
            Thorstein, isRecruited: false, ledger, WorkerReadiness.ForGathering, new Serviceable());

        Assert.False(verdict.IsReady);
        Assert.Equal(ReadinessRefusal.NotRecruited, verdict.Refusal);
    }

    // ------------------------------------------------------------------
    // Each job asks for what it actually needs
    // ------------------------------------------------------------------

    [Fact]
    public void PickingUpALooseBranchNeedsNoToolsAtAll()
    {
        // "Hammer wear only for actions that actually use it; branch pickup must
        // not wear an unused axe." The first half of that is enforced here by
        // not requiring a tool the job does not use -- a worker sent to collect
        // fallen wood is not blocked for want of a hammer.
        var empty = new ToolLedger();

        ReadinessVerdict verdict = WorkerReadiness.Assess(
            Thorstein, isRecruited: true, empty, WorkerReadiness.ForGathering, new Serviceable());

        Assert.True(verdict.IsReady);
        Assert.Empty(WorkerReadiness.ForGathering);
    }

    [Fact]
    public void FellingNeedsAnAxeAndNotAHammer()
    {
        var axeOnly = new ToolLedger();
        axeOnly.Issue(Handover(0, Axe()));

        Assert.True(WorkerReadiness.Assess(
            Thorstein, true, axeOnly, WorkerReadiness.ForFelling, new Serviceable()).IsReady);

        ReadinessVerdict building = WorkerReadiness.Assess(
            Thorstein, true, axeOnly, WorkerReadiness.ForBuilding, new Serviceable());

        Assert.False(building.IsReady);
        Assert.Equal(ReadinessRefusal.ToolMissing, building.Refusal);
        Assert.Equal(ToolKind.Hammer, building.Tool);
        Assert.Contains("no hammer", building.Describe());
    }

    [Fact]
    public void BuildingNeedsBothAndSaysWhichIsMissing()
    {
        var none = new ToolLedger();

        ReadinessVerdict verdict = WorkerReadiness.Assess(
            Thorstein, true, none, WorkerReadiness.ForBuilding, new Serviceable());

        Assert.Equal(ReadinessRefusal.ToolMissing, verdict.Refusal);
        Assert.Equal(ToolKind.Axe, verdict.Tool);
        Assert.Contains("no axe", verdict.Describe());
    }

    // ------------------------------------------------------------------
    // Wear is a live question, and an unanswerable one refuses
    // ------------------------------------------------------------------

    [Fact]
    public void UsabilityIsAskedOfTheToolAndNotOfTheRecord()
    {
        // The specimen says 100 durability, because that is what it had when it
        // was handed over. It has been swung since. Answering from the snapshot
        // is precisely the zero-wear infinite tool the owner notes rule out.
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe(durability: 100f)));

        ReadinessVerdict verdict = WorkerReadiness.Assess(
            Thorstein, true, ledger, WorkerReadiness.ForFelling, new BrokenKind(ToolKind.Axe));

        Assert.False(verdict.IsReady);
        Assert.Equal(100f, ledger.HeldBy(Thorstein)[0].Tool.DurabilityAtIssue);
    }

    [Fact]
    public void AnAdapterThatCannotAnswerRefuses()
    {
        ToolLedger ledger = Equipped();

        Assert.False(WorkerReadiness.Assess(
            Thorstein, true, ledger, WorkerReadiness.ForFelling, null!).IsReady);
        Assert.False(WorkerReadiness.Assess(
            Thorstein, true, ledger, WorkerReadiness.ForFelling,
            UnknownToolCondition.Instance).IsReady);
    }

    [Fact]
    public void ARefusalWithNoReasonSaysSoRatherThanInventingOne()
    {
        Assert.Contains(
            "bug",
            ReadinessVerdict.Refused(ReadinessRefusal.Unspecified).Describe());
    }

    // ------------------------------------------------------------------
    // A specimen cannot be used to invent a tool
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AToolWithAnImpossibleQualityIsRefused(int quality)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ToolSpecimen(ToolKind.Axe, "AxeBronze", quality, 100f, 2));
    }

    [Fact]
    public void AToolWithNoIdentityOrNoKindIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ToolSpecimen(ToolKind.Axe, "", 1, 100f, 2));
        Assert.Throws<ArgumentException>(() => new ToolSpecimen(ToolKind.None, "AxeBronze", 1, 100f, 2));
    }

    [Fact]
    public void AToolWithUnusableDurabilityNumbersIsRefused()
    {
        // NaN compares false against itself, so a specimen carrying one could
        // never be recognised as the tool that was handed over.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ToolSpecimen(ToolKind.Axe, "AxeBronze", 1, float.NaN, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ToolSpecimen(ToolKind.Axe, "AxeBronze", 1, float.PositiveInfinity, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ToolSpecimen(ToolKind.Axe, "AxeBronze", 1, -1f, 2));
    }

    [Fact]
    public void AHandoverWithNoTransactionOrNoWorkerIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ToolHolding(default, Thorstein, Axe()));
        Assert.Throws<ArgumentException>(() => new ToolHolding(RequestId.For(Job, 0), default, Axe()));
        Assert.Throws<ArgumentException>(
            () => new ToolHolding(RequestId.For(Job, 0), Thorstein, default));
    }

    [Fact]
    public void WhichAxeAWorkerSwingsDoesNotDependOnIterationOrder()
    {
        var ledger = new ToolLedger();
        ledger.Issue(Handover(0, Axe(durability: 10f)));
        ledger.Issue(Handover(1, Axe(durability: 90f)));

        Assert.True(ledger.TryGetHeld(Thorstein, ToolKind.Axe, out ToolHolding first));
        Assert.Equal(10f, first.Tool.DurabilityAtIssue);

        for (int repeat = 0; repeat < 5; repeat++)
        {
            ledger.TryGetHeld(Thorstein, ToolKind.Axe, out ToolHolding again);
            Assert.Equal(first.Transaction.Value, again.Transaction.Value);
        }
    }

    [Fact]
    public void AnotherWorkersToolIsNotThisWorkersTool()
    {
        var ledger = new ToolLedger();
        ledger.Issue(new ToolHolding(RequestId.For(Job, 0), new WorkerId("somebody-else"), Axe()));

        Assert.False(ledger.TryGetHeld(Thorstein, ToolKind.Axe, out _));
        Assert.Empty(ledger.HeldBy(Thorstein));
    }
}
