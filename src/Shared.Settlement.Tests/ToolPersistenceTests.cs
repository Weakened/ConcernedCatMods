using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;

namespace Shared.Settlement.Tests;

/// <summary>CF-NPC-002 (#299): a tool handover that survives a reload.
///
/// The contract in <see cref="ToolCustodyTests"/> was idempotent within one
/// session and claimed to be idempotent across a crash. It was not — there was
/// no persistence at all, and an independent review found the claim before a
/// player found the missing axe. These tests are the difference.
///
/// The rule being pinned is the material journal's, deliberately: record the
/// intention, do the thing, record the outcome, and let a replay that finds a
/// start with no finish report that it does not know. Guessing either way hands
/// somebody a tool they may still be holding, or loses the record of one they
/// are not.</summary>
public sealed class ToolPersistenceTests : IDisposable
{
    private readonly string _root;
    private readonly JournalStore _store;

    private static readonly SettlementScope Scope =
        new(worldId: 1234, settlement: new SettlementId("tool-camp"));

    private static readonly WorkerId Thorstein = new("thorstein");
    private static readonly OrderId Job = new("handover");

    private static readonly ToolSpecimen BronzeAxe =
        new(ToolKind.Axe, "$item_axe_bronze", 2, 63.5f, 2);

    public ToolPersistenceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-tool-persistence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new JournalStore(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // A locked temp file must never fail the suite.
        }
    }

    private static RequestId Transaction(int step = 0) => RequestId.For(Job, step);

    private SettlementJournal Handed(bool finish = true, int step = 0)
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction(step),
            worker: Thorstein, tool: BronzeAxe);

        if (finish)
        {
            journal.Append(
                JournalEntryKind.ToolHandoverFinished, default, Transaction(step),
                worker: Thorstein, tool: BronzeAxe);
        }

        return journal;
    }

    private SettlementJournal Reload(SettlementJournal journal)
    {
        Assert.True(_store.Save(journal).Saved);
        return _store.Load(Scope).Journal;
    }

    // ------------------------------------------------------------------
    // The thing that was missing
    // ------------------------------------------------------------------

    [Fact]
    public void AHandoverSurvivesASaveAndReload()
    {
        SettlementJournal reloaded = Reload(Handed());

        ToolLedger tools = reloaded.Replay().Tools;

        Assert.True(tools.TryGetHeld(Thorstein, ToolKind.Axe, out ToolHolding held));
        Assert.Equal("$item_axe_bronze", held.Tool.ItemKey);
        Assert.Equal(2, held.Tool.Quality);
        Assert.Equal(2, held.Tool.ToolTier);
        Assert.Equal(63.5f, held.Tool.DurabilityAtIssue);
        Assert.Equal(ToolHoldingState.Held, held.State);
    }

    [Fact]
    public void AfterAReloadARetriedHandoverIsRecognisedAsTheSameOne()
    {
        // The exact sentence the old comment claimed and the code could not
        // deliver: before persistence this returned Applied, silently recording
        // a second handover of an axe that had only ever moved once.
        SettlementJournal reloaded = Reload(Handed());
        ToolLedger tools = reloaded.Replay().Tools;

        Assert.Equal(
            ToolOutcome.AlreadySatisfied,
            tools.Issue(new ToolHolding(Transaction(), Thorstein, BronzeAxe)));

        Assert.Single(tools.Holdings);
    }

    [Fact]
    public void AToolCanStillBeGivenBackAfterAReload()
    {
        // Before persistence nothing knew the axe had ever been taken, so a
        // return could never be offered at all.
        SettlementJournal reloaded = Reload(Handed());

        reloaded.Append(
            JournalEntryKind.ToolReturned, default, Transaction(),
            worker: Thorstein, tool: BronzeAxe);

        ToolLedger tools = Reload(reloaded).Replay().Tools;

        Assert.Empty(tools.HeldBy(Thorstein));
        Assert.Equal(1, tools.CountIn(ToolHoldingState.Returned));
    }

    [Fact]
    public void AReturnIsStillOnlyEverAppliedOnce()
    {
        SettlementJournal journal = Handed();
        journal.Append(
            JournalEntryKind.ToolReturned, default, Transaction(),
            worker: Thorstein, tool: BronzeAxe);
        journal.Append(
            JournalEntryKind.ToolReturned, default, Transaction(),
            worker: Thorstein, tool: BronzeAxe);

        ToolLedger tools = Reload(journal).Replay().Tools;

        Assert.Single(tools.Holdings);
        Assert.Equal(1, tools.CountIn(ToolHoldingState.Returned));
    }

    // ------------------------------------------------------------------
    // An interrupted handover is never guessed
    // ------------------------------------------------------------------

    [Fact]
    public void AHandoverInterruptedMidTransferReplaysAsUnknownAndSaysSo()
    {
        // Killed between taking the item and recording that it arrived. Both
        // available assumptions are wrong in one direction each: one hands the
        // worker a tool the player may still hold, the other forgets one they
        // do not.
        SettlementJournal reloaded = Reload(Handed(finish: false));

        ReplayResult replayed = reloaded.Replay();

        Assert.True(replayed.NeedsRepair);
        Assert.True(replayed.Tools.HasUncertainHandover(Thorstein));
        Assert.Equal(1, replayed.Tools.CountIn(ToolHoldingState.Uncertain));

        string repair = Assert.Single(replayed.Repairs);
        Assert.Contains("thorstein", repair);
        Assert.Contains("not recorded", repair);
        Assert.Contains(Transaction().Value, repair);
    }

    [Fact]
    public void AnInterruptedHandoverIsStillResolvableAfterTheReload()
    {
        SettlementJournal reloaded = Reload(Handed(finish: false));
        ToolLedger tools = reloaded.Replay().Tools;

        Assert.Equal(ToolOutcome.Applied, tools.Resolve(Transaction(), workerHasIt: true));
        Assert.True(tools.TryGetHeld(Thorstein, ToolKind.Axe, out _));
    }

    [Fact]
    public void AnInterruptedHandoverDoesNotStopAnUnrelatedWorker()
    {
        SettlementJournal reloaded = Reload(Handed(finish: false));
        ToolLedger tools = reloaded.Replay().Tools;

        Assert.True(tools.HasUncertainHandover(Thorstein));
        Assert.False(tools.HasUncertainHandover(new WorkerId("somebody-else")));
    }

    // ------------------------------------------------------------------
    // The record itself
    // ------------------------------------------------------------------

    [Fact]
    public void AToolEntryBelongsToAWorkerAndRefusesToPretendOtherwise()
    {
        // A handover is keyed by a worker, not an order. An entry carrying a
        // synthetic order id so it could reuse the material shape would read
        // wrongly forever after.
        Assert.Throws<ArgumentException>(() => new SettlementJournal(Scope).Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction(), tool: BronzeAxe));

        Assert.Throws<ArgumentException>(() => new SettlementJournal(Scope).Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction(), worker: Thorstein));

        // And a material entry still needs its order.
        Assert.Throws<ArgumentException>(() => new SettlementJournal(Scope).Append(
            JournalEntryKind.Reserved, default, Transaction()));
    }

    [Fact]
    public void ADamagedToolRowIsTreatedAsDamageRatherThanRepaired()
    {
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v2",
            "v\t2\t" + Scope.ToStorageKey(),
            // Quality zero: the specimen type forbids it, and guessing which
            // column was wrong would be inventing data.
            "t\t0\t6\thandover-0\tthorstein\t1\t$item_axe_bronze\t0\t50\t2",
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.Empty(report.Journal.Replay().Tools.Holdings);
    }

    [Fact]
    public void ANonToolKindOnAToolRowIsRefused()
    {
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v2",
            "v\t2\t" + Scope.ToStorageKey(),
            "t\t0\t1\thandover-0\tthorstein\t1\t$item_axe_bronze\t1\t50\t2",
        });

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, _store.Load(Scope).Outcome);
    }

    [Fact]
    public void AFileWeWriteCarriesTheVersionAnOlderBuildWillRefuse()
    {
        // The previous version of this test wrote a v3 header and checked that
        // THIS build refused it -- the opposite direction from its own name, and
        // it would have passed with every line of tool code deleted.
        //
        // The direction that matters is that a file containing handovers
        // announces a version an older build rejects outright, because such a
        // build cannot account for a tool somebody handed over and half-reading
        // would let it rewrite the file with the handovers deleted.
        Assert.Equal(2, JournalStore.SchemaVersion);

        Assert.True(_store.Save(Handed()).Saved);
        string[] written = File.ReadAllLines(_store.ResolvePath(Scope));

        Assert.Contains(written, line => line.StartsWith("v\t2\t", StringComparison.Ordinal));
        Assert.Contains(written, line => line.StartsWith("t\t", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileFromANewerBuildIsRefusedWholeRatherThanHalfRead()
    {
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v3",
            "v\t3\t" + Scope.ToStorageKey(),
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.UnsupportedSchema, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.False(_store.Save(report.Journal).Saved);
    }

    [Fact]
    public void AToolKindOnAnEntryRowIsDamageRatherThanACrash()
    {
        // Enum.IsDefined accepted a tool kind on an 'e' row, and the
        // JournalEntry constructor that then threw sits outside the try -- so
        // one corrupted tag byte threw straight out of Load and every command
        // for that world failed forever, with no read-only mode and no notice.
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v2",
            "v\t2\t" + Scope.ToStorageKey(),
            "e\t0\t6\tcottage-1\thandover-0\t0\t",
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.Empty(report.Journal.Entries);
    }

    [Fact]
    public void AToolRowWithNoRequestIsDamageRatherThanSilentlyIgnored()
    {
        File.WriteAllLines(_store.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v2",
            "v\t2\t" + Scope.ToStorageKey(),
            "t\t0\t6\t\tthorstein\t1\t$item_axe_bronze\t1\t50\t2",
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.True(report.ReadOnly);
    }

    [Fact]
    public void ASecondStartUnderTheSameIdDoesNotReplaceTheFirst()
    {
        var journal = new SettlementJournal(Scope);
        var other = new ToolSpecimen(ToolKind.Hammer, "$item_hammer", 1, 10f, 0);
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction(),
            worker: Thorstein, tool: BronzeAxe);
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction(),
            worker: Thorstein, tool: other);

        ToolLedger tools = Reload(journal).Replay().Tools;

        // Last-write-wins would have quietly swapped the axe for a hammer,
        // bypassing the mismatch rejection ToolLedger.Issue makes a point of.
        Assert.True(tools.TryGet(Transaction(), out ToolHolding held));
        Assert.Equal(ToolKind.Axe, held.Tool.Kind);
    }

    [Fact]
    public void AToolEntryRefusesAnOrderRatherThanSwallowingIt()
    {
        Assert.Throws<ArgumentException>(() => new SettlementJournal(Scope).Append(
            JournalEntryKind.ToolHandoverStarted, new OrderId("cottage-1"), Transaction(),
            worker: Thorstein, tool: BronzeAxe));
    }

    [Fact]
    public void AResolutionSurvivesTheNextReload()
    {
        // It did not. Resolve changed an in-memory ledger that Replay rebuilds
        // from scratch, so the answer was discarded and the holding went back to
        // unknown -- while the documentation called it "resolvable".
        SettlementJournal journal = Handed(finish: false);
        Assert.True(_store.Save(journal).Saved);

        journal.Append(
            JournalEntryKind.ToolResolvedToWorker, default, Transaction(),
            worker: Thorstein, tool: BronzeAxe);
        Assert.True(_store.Save(journal).Saved);

        ReplayResult replayed = _store.Load(Scope).Journal.Replay();

        Assert.False(replayed.Tools.HasUncertainHandover(Thorstein));
        Assert.True(replayed.Tools.TryGetHeld(Thorstein, ToolKind.Axe, out _));
        Assert.Empty(replayed.Repairs);
    }

    [Fact]
    public void AResolutionTheOtherWayAlsoSurvives()
    {
        SettlementJournal journal = Handed(finish: false);
        journal.Append(
            JournalEntryKind.ToolResolvedToPlayer, default, Transaction(),
            worker: Thorstein, tool: BronzeAxe);

        ReplayResult replayed = Reload(journal).Replay();

        Assert.False(replayed.Tools.HasUncertainHandover(Thorstein));
        Assert.Equal(1, replayed.Tools.CountIn(ToolHoldingState.Returned));
        Assert.Empty(replayed.Repairs);
    }

    [Fact]
    public void AFileWithNoToolRowsStillReadsExactlyAsBefore()
    {
        // The material half is untouched: a journal written before tools existed
        // loads and replays the same way.
        var journal = new SettlementJournal(Scope);
        journal.Append(
            JournalEntryKind.OrderTransition, new OrderId("cottage-1"),
            transition: TheConcernedCat.Settlement.Orders.OrderTransition.Approve);

        SettlementJournal reloaded = Reload(journal);
        ReplayResult replayed = reloaded.Replay();

        Assert.Single(reloaded.Entries);
        Assert.Empty(replayed.Tools.Holdings);
        Assert.False(replayed.NeedsRepair);
    }

    [Fact]
    public void AToolKeyWithTabsSurvivesTheRoundTrip()
    {
        var journal = new SettlementJournal(Scope);
        var odd = new ToolSpecimen(ToolKind.Hammer, "ham\tmer%09two", 1, 12.25f, 0);
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, Transaction(), worker: Thorstein, tool: odd);
        journal.Append(
            JournalEntryKind.ToolHandoverFinished, default, Transaction(), worker: Thorstein, tool: odd);

        ToolLedger tools = Reload(journal).Replay().Tools;

        Assert.True(tools.TryGetHeld(Thorstein, ToolKind.Hammer, out ToolHolding held));
        Assert.Equal("ham\tmer%09two", held.Tool.ItemKey);
        Assert.Equal(12.25f, held.Tool.DurabilityAtIssue);
    }

    [Fact]
    public void TwoWorkersToolsDoNotMixAcrossAReload()
    {
        var journal = new SettlementJournal(Scope);
        var other = new WorkerId("second-hand");
        var hammer = new ToolSpecimen(ToolKind.Hammer, "$item_hammer", 1, 100f, 0);

        journal.Append(JournalEntryKind.ToolHandoverStarted, default, Transaction(0), worker: Thorstein, tool: BronzeAxe);
        journal.Append(JournalEntryKind.ToolHandoverFinished, default, Transaction(0), worker: Thorstein, tool: BronzeAxe);
        journal.Append(JournalEntryKind.ToolHandoverStarted, default, Transaction(1), worker: other, tool: hammer);
        journal.Append(JournalEntryKind.ToolHandoverFinished, default, Transaction(1), worker: other, tool: hammer);

        ToolLedger tools = Reload(journal).Replay().Tools;

        Assert.Single(tools.HeldBy(Thorstein));
        Assert.Single(tools.HeldBy(other));
        Assert.False(tools.TryGetHeld(Thorstein, ToolKind.Hammer, out _));
        Assert.False(tools.TryGetHeld(other, ToolKind.Axe, out _));
    }
}
