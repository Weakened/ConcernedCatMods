using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>#300: the tool-custody gaps #299's final review left, pinned
/// against the real handover procedure, a real journal file and the fault
/// harness's world.
///
/// <list type="number">
/// <item>An interrupted <b>return</b> is in the record, survives a reload, and
/// is settled by the same <c>cf_settle resolve</c> answer as a give.</item>
/// <item>A result that could not be written is reported, and the holding is
/// unsettled rather than silently unrecorded.</item>
/// <item>Which tool a worker reaches for follows the record's order, not a
/// dictionary's.</item>
/// <item>A repair is raised from a holding's own state, once per unsettled
/// handover and never for a settled one.</item>
/// <item>A row the handover writes itself says so, and is never read as a
/// person's answer.</item>
/// </list>
///
/// Across all of them: whatever the procedure answers, the ledger it was
/// handed says what a replay of the record says.</summary>
public sealed class ToolReturnTests : IDisposable
{
    private static readonly ToolSpecimen Axe = new(ToolKind.Axe, "$item_axe_bronze", 1, 100f, 2);
    private static readonly RequestId Give1 = new("give-1");

    private readonly string _root;

    public ToolReturnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-tool-return", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    // ------------------------------------------------------------------
    // The world
    // ------------------------------------------------------------------

    private sealed class Scene
    {
        public Scene(FakeWorld world, FaultyDisk disk, CustodyProcess process, FakeToolInventory player, FakeToolInventory worker, object axe)
        {
            World = world;
            Disk = disk;
            Process = process;
            PlayerTools = player;
            WorkerTools = worker;
            Item = axe;
        }

        public FakeWorld World { get; }

        public FaultyDisk Disk { get; }

        public CustodyProcess Process { get; private set; }

        public SettlementJournal Journal => Process.Journal;

        public FakeToolInventory PlayerTools { get; }

        public FakeToolInventory WorkerTools { get; }

        public object Item { get; }

        public JournalStamp Stamp => new(World.Clock, Process.Core.Load.LoadEpoch);

        public bool Save() => Disk.Persist(Process.Journal);

        public void Restart() => Process = Process.Restart();

        public HandoverOutcome Give(ToolLedger ledger, out string message, Func<bool>? save = null) =>
            ToolHandoverProcedure.Give(
                PlayerTools, WorkerTools, Item, Axe, Worker, Give1, ledger, Journal, save ?? Save, Stamp, out message);

        public HandoverOutcome Return(ToolLedger ledger, out string message, Func<bool>? save = null) =>
            ToolHandoverProcedure.Return(
                WorkerTools, PlayerTools, Item, Give1, ledger, Journal, save ?? Save, Stamp, out message);
    }

    /// <summary>The player holds the axe, and the world has been saved.</summary>
    private Scene Start()
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(Path.Combine(_root, Guid.NewGuid().ToString("N"))));
        FakeToolInventory player = world.Tools("player");
        FakeToolInventory worker = world.Tools("worker");
        object axe = new();
        player.Put(axe);
        player.Snapshot();

        CustodyProcess process = CustodyProcess.Start(world, disk);
        process.SaveWorld();
        return new Scene(world, disk, process, player, worker, axe);
    }

    /// <summary>The axe handed over, and the world saved with him holding it:
    /// where every return starts.</summary>
    private Scene HandedOver()
    {
        Scene scene = Start();
        Assert.Equal(HandoverOutcome.Given, scene.Give(scene.Journal.Replay().Tools, out _));
        scene.Process.SaveWorld();
        return scene;
    }

    /// <summary>A save that fails without writing on its <paramref name="call"/>-th
    /// use and works otherwise — for failing a result row after its intention
    /// reached disk.</summary>
    private static Func<bool> FailingOn(Scene scene, int call)
    {
        int calls = 0;
        return () => ++calls != call && scene.Save();
    }

    private static ToolHoldingState? StateIn(ToolLedger ledger, RequestId transaction) =>
        ledger.TryGet(transaction, out ToolHolding holding) ? holding.State : null;

    private static void AssertLedgerIsTheRecord(ToolLedger ledger, SettlementJournal journal)
    {
        Assert.Equal(StateIn(journal.Replay().Tools, Give1), StateIn(ledger, Give1));
    }

    // ------------------------------------------------------------------
    // 1. An interrupted return is recoverable after a reload
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInterruptedReturnIsStillAQuestionAfterAReloadAndTheSameAnswerSettlesIt(bool workerHasIt)
    {
        Scene scene = HandedOver();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        // The axe reaches the player but does not leave him: referenced twice.
        scene.WorkerTools.RefuseRemove = true;
        Assert.Equal(HandoverOutcome.Uncertain, scene.Return(ledger, out string message));
        Assert.Contains("cf_settle resolve give-1 mine|his", message);
        AssertLedgerIsTheRecord(ledger, scene.Journal);

        // Saved like that; then the session ends.
        scene.Process.SaveWorld();
        scene.Restart();

        // Before #300 a return wrote nothing until it had finished, so this
        // replayed as plainly Held and the answer below was refused as "not
        // waiting on an answer".
        ReplayResult replay = scene.Journal.Replay();
        Assert.True(replay.Tools.HasUncertainHandover(Worker));
        string repair = Assert.Single(replay.Repairs, line => line.Contains("request \"give-1\""));
        Assert.Contains("was being given back", repair);
        Assert.Contains("cf_settle resolve give-1 mine|his", repair);

        Assert.True(
            ToolResolution.TryRecord(Give1, workerHasIt, replay.Tools, scene.Journal, scene.Save, out string answer),
            answer);

        // A person's answer is a record row, not a world effect: it survives
        // the next load whether or not the world was saved again.
        scene.Restart();
        ReplayResult settled = scene.Journal.Replay();
        Assert.False(settled.Tools.HasUncertainHandover(Worker));
        Assert.Equal(workerHasIt ? ToolHoldingState.Held : ToolHoldingState.Returned, StateIn(settled.Tools, Give1));
        Assert.DoesNotContain(settled.Repairs, line => line.Contains("request \"give-1\""));
    }

    public static IEnumerable<object[]> ReturnKillPoints => new[]
    {
        new object[] { "persist.before.ToolReturned" },
        new object[] { "persist.after.ToolReturned" },
        new object[] { "player.tool.add.before" },
        new object[] { "player.tool.add.after" },
        new object[] { "worker.tool.remove.before" },
        new object[] { "worker.tool.remove.after" },
        new object[] { "persist.before.ToolReturned#2" },
        new object[] { "persist.after.ToolReturned#2" },
    };

    [Theory]
    [MemberData(nameof(ReturnKillPoints))]
    public void AKilledReturnRollsBackWithTheAxeInExactlyOnePlace(string killAt)
    {
        Scene scene = HandedOver();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        scene.World.KillAt = killAt;
        Assert.ThrowsAny<Exception>(() =>
        {
            scene.Return(ledger, out _);
            scene.World.Guard();
        });

        // Killed where the name says: a return's intention and its result are
        // both ToolReturned rows, so "#2" must really be the second.
        string[] point = killAt.Split('#');
        Assert.Equal(
            point.Length == 2 ? int.Parse(point[1], System.Globalization.CultureInfo.InvariantCulture) : 1,
            scene.World.Steps.Count(step => step == point[0]));

        scene.Restart();
        ToolLedger tools = scene.Journal.Replay().Tools;

        // The world is its last save -- he has it -- and so is the record: no
        // second axe, no lost one, and no question nobody can answer.
        Assert.Single(scene.WorkerTools.Items);
        Assert.Empty(scene.PlayerTools.Items);
        Assert.Equal(ToolHoldingState.Held, StateIn(tools, Give1));
        Assert.False(tools.HasUncertainHandover(Worker));
    }

    [Fact]
    public void AnAttemptTheWorldNeverSavedLeavesNothingToAnswerAfterAReload()
    {
        // What the player is told about a failed write is true in the session.
        // It is not promised past a reload that rolls the attempt back.
        Scene scene = Start();
        scene.WorkerTools.RefuseAdd = true;
        Assert.Equal(HandoverOutcome.Refused, scene.Give(scene.Journal.Replay().Tools, out _, FailingOn(scene, call: 2)));

        scene.Restart();

        ReplayResult replay = scene.Journal.Replay();
        Assert.False(replay.Tools.TryGet(Give1, out _));
        Assert.DoesNotContain(replay.Repairs, line => line.Contains("request \"give-1\""));
        Assert.Single(scene.PlayerTools.Items);
        Assert.Empty(scene.WorkerTools.Items);
    }

    // ------------------------------------------------------------------
    // 2. A result that could not be written is reported, and unsettled
    // ------------------------------------------------------------------

    [Fact]
    public void AGiveWhoseResultCouldNotBeWrittenSaysSoAndIsUnsettled()
    {
        Scene scene = Start();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        Assert.Equal(HandoverOutcome.Uncertain, scene.Give(ledger, out string message, FailingOn(scene, call: 2)));

        // Not "he takes the axe" followed by an unrecorded axe after a reload.
        Assert.Contains("could not be written down", message);
        Assert.Contains("cf_settle resolve give-1 his", message);
        Assert.DoesNotContain(scene.Journal.Entries, e => e.Kind == JournalEntryKind.ToolHandoverFinished);
        Assert.Equal(ToolHoldingState.Uncertain, StateIn(ledger, Give1));
        AssertLedgerIsTheRecord(ledger, scene.Journal);

        // It did move. The world is saved like that and the session ends.
        Assert.Single(scene.WorkerTools.Items);
        scene.Process.SaveWorld();
        scene.Restart();

        ReplayResult replay = scene.Journal.Replay();
        Assert.True(replay.Tools.HasUncertainHandover(Worker));
        Assert.True(ToolResolution.TryRecord(Give1, workerHasIt: true, replay.Tools, scene.Journal, scene.Save, out _));
        Assert.True(scene.Journal.Replay().Tools.TryGetHeld(Worker, ToolKind.Axe, out _));
    }

    [Fact]
    public void AReturnWhoseResultCouldNotBeWrittenSaysSoAndIsUnsettled()
    {
        Scene scene = HandedOver();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        Assert.Equal(HandoverOutcome.Uncertain, scene.Return(ledger, out string message, FailingOn(scene, call: 2)));

        Assert.Contains("could not be written down", message);
        Assert.Contains("cf_settle resolve give-1 mine", message);
        JournalEntry intent = Assert.Single(scene.Journal.Entries, e => e.Kind == JournalEntryKind.ToolReturned);
        Assert.True(intent.ReturnIntent);
        Assert.Equal(ToolHoldingState.Uncertain, StateIn(ledger, Give1));
        AssertLedgerIsTheRecord(ledger, scene.Journal);

        Assert.Single(scene.PlayerTools.Items);
        scene.Process.SaveWorld();
        scene.Restart();

        ReplayResult replay = scene.Journal.Replay();
        Assert.True(replay.Tools.HasUncertainHandover(Worker));
        Assert.True(ToolResolution.TryRecord(Give1, workerHasIt: false, replay.Tools, scene.Journal, scene.Save, out _));
        Assert.Equal(ToolHoldingState.Returned, StateIn(scene.Journal.Replay().Tools, Give1));
    }

    [Fact]
    public void AGiveCloseThatCouldNotBeWrittenAsksAnHonestQuestion()
    {
        Scene scene = Start();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        scene.WorkerTools.RefuseAdd = true;
        Assert.Equal(HandoverOutcome.Refused, scene.Give(ledger, out string message, FailingOn(scene, call: 2)));

        Assert.Contains("nothing was taken from you", message);
        Assert.Contains("cf_settle resolve give-1 mine", message);
        Assert.Single(scene.PlayerTools.Items);
        Assert.Empty(scene.WorkerTools.Items);

        // The record holds an intention with no result, so the ledger says
        // unsettled too, not "no holding".
        Assert.Equal(ToolHoldingState.Uncertain, StateIn(ledger, Give1));
        AssertLedgerIsTheRecord(ledger, scene.Journal);

        // The answer the message gives is accepted in this session.
        Assert.True(ToolResolution.TryRecord(
            Give1, workerHasIt: false, scene.Journal.Replay().Tools, scene.Journal, scene.Save, out _));
        Assert.Equal(ToolHoldingState.Returned, StateIn(scene.Journal.Replay().Tools, Give1));
    }

    [Fact]
    public void AReturnCloseThatCouldNotBeWrittenAsksAnHonestQuestion()
    {
        Scene scene = HandedOver();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        scene.PlayerTools.RefuseAdd = true;
        Assert.Equal(HandoverOutcome.Refused, scene.Return(ledger, out string message, FailingOn(scene, call: 2)));

        Assert.Contains("he is still holding it", message);
        Assert.Contains("cf_settle resolve give-1 his", message);
        Assert.Single(scene.WorkerTools.Items);
        Assert.Empty(scene.PlayerTools.Items);
        Assert.Equal(ToolHoldingState.Uncertain, StateIn(ledger, Give1));
        AssertLedgerIsTheRecord(ledger, scene.Journal);

        Assert.True(ToolResolution.TryRecord(
            Give1, workerHasIt: true, scene.Journal.Replay().Tools, scene.Journal, scene.Save, out _));
        Assert.Equal(ToolHoldingState.Held, StateIn(scene.Journal.Replay().Tools, Give1));
    }

    // ------------------------------------------------------------------
    // 5. What the handover writes itself says so
    // ------------------------------------------------------------------

    [Fact]
    public void WhenHeCannotTakeItTheHandoverClosesItsOwnAttemptAndItCanSimplyRunAgain()
    {
        Scene scene = Start();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        scene.WorkerTools.RefuseAdd = true;
        Assert.Equal(HandoverOutcome.Refused, scene.Give(ledger, out string message));
        Assert.Contains("Nothing was taken from you", message);
        AssertLedgerIsTheRecord(ledger, scene.Journal);

        // Who wrote the close survives the file.
        SettlementJournal fromDisk = scene.Disk.Store.Load(CustodyProcess.Scope).Journal;
        JournalEntry close = Assert.Single(fromDisk.Entries, e => e.Kind == JournalEntryKind.ToolResolvedToPlayer);
        Assert.True(close.WrittenByHandover);

        // Nothing moved, so there is nothing to settle and nobody is asked.
        ReplayResult replay = fromDisk.Replay();
        Assert.False(replay.Tools.TryGet(Give1, out _));
        Assert.DoesNotContain(replay.Repairs, line => line.Contains("request \"give-1\""));

        scene.WorkerTools.RefuseAdd = false;
        ToolLedger again = scene.Journal.Replay().Tools;
        Assert.Equal(HandoverOutcome.Given, scene.Give(again, out _));
        Assert.Equal(ToolHoldingState.Held, StateIn(scene.Journal.Replay().Tools, Give1));
        AssertLedgerIsTheRecord(again, scene.Journal);
    }

    [Fact]
    public void WhenYouCannotTakeItBackHeKeepsItAndTheRecordSaysSo()
    {
        Scene scene = HandedOver();
        ToolLedger ledger = scene.Journal.Replay().Tools;

        scene.PlayerTools.RefuseAdd = true;
        Assert.Equal(HandoverOutcome.Refused, scene.Return(ledger, out string message));
        Assert.Contains("He is still holding it", message);
        AssertLedgerIsTheRecord(ledger, scene.Journal);
        Assert.Equal(ToolHoldingState.Held, StateIn(scene.Journal.Replay().Tools, Give1));
        Assert.Single(scene.WorkerTools.Items);
        Assert.Empty(scene.PlayerTools.Items);

        JournalEntry close = Assert.Single(scene.Journal.Entries, e => e.Kind == JournalEntryKind.ToolResolvedToWorker);
        Assert.True(close.WrittenByHandover);

        scene.PlayerTools.RefuseAdd = false;
        ToolLedger again = scene.Journal.Replay().Tools;
        Assert.Equal(HandoverOutcome.Given, scene.Return(again, out _));
        Assert.Equal(ToolHoldingState.Returned, StateIn(scene.Journal.Replay().Tools, Give1));
        AssertLedgerIsTheRecord(again, scene.Journal);
        Assert.Single(scene.PlayerTools.Items);
        Assert.Empty(scene.WorkerTools.Items);
    }

    [Fact]
    public void AHandoverAuthoredCloseIsNeverReadAsAPersonsAnswer()
    {
        // A person saying "mine" about an interrupted give settles a holding.
        SettlementJournal answered = Journal(
            (JournalEntryKind.ToolHandoverStarted, false, false),
            (JournalEntryKind.ToolResolvedToPlayer, false, false));
        Assert.Equal(ToolHoldingState.Returned, StateIn(answered.Replay().Tools, Give1));

        // The handover's own close means nothing moved: no holding at all.
        SettlementJournal closed = Journal(
            (JournalEntryKind.ToolHandoverStarted, false, false),
            (JournalEntryKind.ToolResolvedToPlayer, false, true));
        Assert.Null(StateIn(closed.Replay().Tools, Give1));

        // Each close only closes the kind of attempt that writes it: "he still
        // has it" belongs to a return and is no answer about a give...
        SettlementJournal giveClosedAsReturn = Journal(
            (JournalEntryKind.ToolHandoverStarted, false, false),
            (JournalEntryKind.ToolResolvedToWorker, false, true));
        Assert.Equal(ToolHoldingState.Uncertain, StateIn(giveClosedAsReturn.Replay().Tools, Give1));

        // ...and "you still have it" belongs to a give and is no answer about a
        // return.
        SettlementJournal returnClosedAsGive = Journal(
            (JournalEntryKind.ToolHandoverStarted, false, false),
            (JournalEntryKind.ToolHandoverFinished, false, false),
            (JournalEntryKind.ToolReturned, true, false),
            (JournalEntryKind.ToolResolvedToPlayer, false, true));
        Assert.Equal(ToolHoldingState.Uncertain, StateIn(returnClosedAsGive.Replay().Tools, Give1));

        // And the flags cannot be put where they would mean nothing.
        Assert.Throws<ArgumentException>(() => answered.Append(
            JournalEntryKind.ToolHandoverFinished, default, Give1, worker: Worker, tool: Axe, writtenByHandover: true));
        Assert.Throws<ArgumentException>(() => answered.Append(
            JournalEntryKind.ToolHandoverStarted, default, Give1, worker: Worker, tool: Axe, returnIntent: true));
    }

    private static SettlementJournal Journal(params (JournalEntryKind Kind, bool ReturnIntent, bool ByHandover)[] rows)
    {
        var journal = new SettlementJournal(CustodyProcess.Scope);
        foreach ((JournalEntryKind kind, bool returnIntent, bool byHandover) in rows)
        {
            journal.Append(
                kind, default, Give1, worker: Worker, tool: Axe,
                returnIntent: returnIntent, writtenByHandover: byHandover);
        }

        return journal;
    }

    // ------------------------------------------------------------------
    // 3. Which tool he reaches for follows the record
    // ------------------------------------------------------------------

    [Fact]
    public void WhichAxeHeReachesForIsTheOneHandedOverFirstWhateverItsIdAndWhenItFinished()
    {
        // "give-9" first appears first; ordinal order puts "give-10" first, and
        // so does the order the two finished in.
        var nine = new RequestId("give-9");
        var ten = new RequestId("give-10");
        var bronze = Axe;
        var iron = new ToolSpecimen(ToolKind.Axe, "$item_axe_iron", 1, 100f, 3);

        var journal = new SettlementJournal(CustodyProcess.Scope);
        journal.Append(JournalEntryKind.ToolHandoverStarted, default, nine, worker: Worker, tool: bronze);
        journal.Append(JournalEntryKind.ToolHandoverStarted, default, ten, worker: Worker, tool: iron);
        journal.Append(JournalEntryKind.ToolHandoverFinished, default, ten, worker: Worker, tool: iron);
        journal.Append(JournalEntryKind.ToolHandoverFinished, default, nine, worker: Worker, tool: bronze);

        Assert.True(journal.Replay().Tools.TryGetHeld(Worker, ToolKind.Axe, out ToolHolding first));
        Assert.Equal(nine, first.Transaction);

        var store = new JournalStore(Path.Combine(_root, "order"));
        Assert.True(store.Save(journal).Saved);
        ToolLedger reloaded = store.Load(CustodyProcess.Scope).Journal.Replay().Tools;
        Assert.Equal(new[] { "give-9", "give-10" }, reloaded.Holdings.Select(h => h.Transaction.Value));
        Assert.True(reloaded.TryGetHeld(Worker, ToolKind.Axe, out ToolHolding afterReload));
        Assert.Equal(nine, afterReload.Transaction);

        journal.Append(JournalEntryKind.ToolReturned, default, nine, worker: Worker, tool: bronze, returnIntent: true);
        journal.Append(JournalEntryKind.ToolReturned, default, nine, worker: Worker, tool: bronze);
        Assert.True(journal.Replay().Tools.TryGetHeld(Worker, ToolKind.Axe, out ToolHolding next));
        Assert.Equal(ten, next.Transaction);
    }

    [Fact]
    public void ManyHandoversKeepTheOrderTheyFirstAppearedInTheRecord()
    {
        const int Count = 40;
        var appeared = new List<string>();
        var journal = new SettlementJournal(CustodyProcess.Scope);
        for (int index = 0; index < Count; index++)
        {
            string id = "t-" + ((index * 17) % Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
            appeared.Add(id);
            journal.Append(JournalEntryKind.ToolHandoverStarted, default, new RequestId(id), worker: Worker, tool: Axe);
        }

        for (int index = Count - 1; index >= 0; index--)
        {
            journal.Append(JournalEntryKind.ToolHandoverFinished, default, new RequestId(appeared[index]), worker: Worker, tool: Axe);
        }

        Assert.Equal(appeared, journal.Replay().Tools.Holdings.Select(h => h.Transaction.Value));
    }

    // ------------------------------------------------------------------
    // 4. A repair comes from a holding's own state, once
    // ------------------------------------------------------------------

    [Fact]
    public void EveryUnsettledHandoverIsAskedAboutExactlyOnceAndNothingSettledIs()
    {
        var gunnar = new WorkerId("gunnar");
        var journal = new SettlementJournal(CustodyProcess.Scope);

        void Row(JournalEntryKind kind, string id, WorkerId? who = null, bool returnIntent = false) =>
            journal.Append(kind, default, new RequestId(id), worker: who ?? Worker, tool: Axe, returnIntent: returnIntent);

        Row(JournalEntryKind.ToolHandoverStarted, "give-interrupted");

        Row(JournalEntryKind.ToolHandoverStarted, "held");
        Row(JournalEntryKind.ToolHandoverFinished, "held");

        Row(JournalEntryKind.ToolHandoverStarted, "return-interrupted");
        Row(JournalEntryKind.ToolHandoverFinished, "return-interrupted");
        Row(JournalEntryKind.ToolReturned, "return-interrupted", returnIntent: true);

        Row(JournalEntryKind.ToolHandoverStarted, "returned");
        Row(JournalEntryKind.ToolHandoverFinished, "returned");
        Row(JournalEntryKind.ToolReturned, "returned", returnIntent: true);
        Row(JournalEntryKind.ToolReturned, "returned");

        Row(JournalEntryKind.ToolHandoverStarted, "someone-else", gunnar);
        Row(JournalEntryKind.ToolHandoverStarted, "second-for-thorstein");

        ReplayResult replay = journal.Replay();

        foreach (string unsettled in new[] { "give-interrupted", "return-interrupted", "someone-else", "second-for-thorstein" })
        {
            Assert.Single(replay.Repairs, line => line.Contains("request \"" + unsettled + "\""));
        }

        foreach (string settled in new[] { "held", "returned" })
        {
            Assert.DoesNotContain(replay.Repairs, line => line.Contains("request \"" + settled + "\""));
        }

        Assert.Equal(4, replay.Tools.CountIn(ToolHoldingState.Uncertain));
        Assert.Contains(replay.Repairs, line => line.Contains("request \"return-interrupted\"") && line.Contains("was being given back"));
        Assert.True(replay.Tools.HasUncertainHandover(gunnar));
    }
}
