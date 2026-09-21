using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Storage;
using TheConcernedCat.Settlement.Tools;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>Journal schema v3 (#316) and record integrity (#293): every custody
/// kind round-trips, older files still load, and a record that lost lines —
/// its tail on a line boundary, a row from the middle, a changed value — does
/// not load clean, goes read-only and is never written over.</summary>
public sealed class JournalSchemaTests : IDisposable
{
    private readonly string _root;
    private readonly JournalStore _store;

    private static readonly SettlementScope Scope = new(worldId: 9001, settlement: new SettlementId("schema-camp"));
    private static readonly Guid Load = new("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    public JournalSchemaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-journal-schema", Guid.NewGuid().ToString("N"));
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

    private static SettlementJournal EveryKind()
    {
        var journal = new SettlementJournal(Scope);
        double time = 100.25;
        var cart = new CustodyLocation(CustodyPlace.Cart, "1:77", Guid.NewGuid());
        var ground = new CustodyLocation(CustodyPlace.SourceGround, "1:900", Epoch);

        journal.AppendCustody(new CollectionAcceptedRow(Definition(stone: 20, wood: 30)), time++, Load);
        journal.AppendCustody(new CollectionTransitionRow(Order, CollectionOrderState.Accepted, CollectionOrderState.Surveying, CollectionAttentionReason.Unspecified), time++, Load);
        journal.AppendCustody(new PickupStartedRow(new RequestId("collect-1-pick-2"), Order, Source()), time++, Load);
        journal.AppendCustody(new PickupFinishedRow(new RequestId("collect-1-pick-2"), Order,
            new PickupResult(PickupOutcome.Picked, new[] { new SpawnedDrop("1:900", Stone, 1), new SpawnedDrop("1:901", Wood, 2) }, "traced\ttwo")), time++, Load);
        var intent = new TransferIntent(new RequestId("collect-1-t-4"), Order, ground, WorkerAt(), Stone, 1, 4);
        journal.AppendCustody(new TransferStartedRow(intent), time++, Load);
        journal.AppendCustody(new TransferFinishedRow(Order, new TransferReceipt(intent.Request, TransferOutcome.Partial, 1, ground, "a=b; 50% done*")), time++, Load);
        journal.AppendCustody(new TransferResolvedRow(new RequestId("collect-1-t-9"), Order, TransferSide.Destination, 3, "looked"), time++, Load);
        journal.AppendCustody(new CartBaselineRow(Order, "lease-1", "1:77", cart.WorldLoadEpoch, new[] { new ItemCount(Stone, 15), new ItemCount(new MaterialItem("Flint", 1, 0), 3) }), time++, Load);
        journal.AppendCustody(new LossRecordedRow(new RequestId("collect-1-lost-11"), Order, cart, Wood, 2, "the cart tipped"), time++, Load);
        journal.AppendCustody(new HandoverFinishedRow(new RequestId("collect-1-t-12"), Order, Stone, 4), time++, Load);
        journal.AppendCustody(new CollectionReboundRow(
            Order,
            new WorkScope(WorkScopeSource.DefaultCampCircle, new TheConcernedCat.Settlement.Worker.SitePoint(10f, 20f, 30f), 30f, "your bed", 4, Load),
            DeliveryTarget.ToContainer("1:43", Load, new TheConcernedCat.Settlement.Worker.SitePoint(12f, 20f, 31f))), time++, Load);
        journal.AppendCustody(new WorldSaveMarkerRow(7), time, Load);
        return journal;
    }

    [Fact]
    public void EveryCustodyKindRoundTripsExactly()
    {
        SettlementJournal written = EveryKind();
        Assert.True(_store.Save(written).Saved);

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);
        Assert.False(report.ReadOnly);
        Assert.Equal(TrailerVerdict.Intact, report.Trailer);
        Assert.Equal(JournalStore.SchemaVersion, report.SchemaVersion);
        Assert.Equal(written.Entries.Count, report.Journal.Entries.Count);

        for (int index = 0; index < written.Entries.Count; index++)
        {
            JournalEntry before = written.Entries[index];
            JournalEntry after = report.Journal.Entries[index];

            Assert.Equal(before.Kind, after.Kind);
            Assert.Equal(before.Sequence, after.Sequence);
            Assert.Equal(before.WorldTime, after.WorldTime);
            Assert.Equal(before.LoadEpoch, after.LoadEpoch);

            // The encoded fields are the round-trip proof: identical names,
            // values and order.
            Assert.Equal(
                CustodyRowCodec.Encode(before.Custody!).Encode().ToArray(),
                CustodyRowCodec.Encode(after.Custody!).Encode().ToArray());
        }

        var accepted = (CollectionAcceptedRow)report.Journal.Entries[0].Custody!;
        Assert.True(CollectionOrders.SameDefinition(Definition(stone: 20, wood: 30), accepted.Definition));

        var finished = (PickupFinishedRow)report.Journal.Entries[3].Custody!;
        Assert.Equal("traced\ttwo", finished.Result.Reason);
        Assert.Equal(2, finished.Result.Drops.Count);
    }

    [Fact]
    public void AFieldANewerBuildAddedIsKeptAndWrittenBack()
    {
        Assert.True(_store.Save(EveryKind()).Saved);
        string path = _store.ResolvePath(Scope);
        List<string> lines = File.ReadAllLines(path).ToList();
        int row = lines.FindIndex(line => line.StartsWith("c\t", StringComparison.Ordinal));
        lines[row] = lines[row] + "\tfuture.field=kept%09safe";
        lines.RemoveAt(lines.Count - 1);
        WriteSealed(path, lines);

        JournalStore.LoadReport report = _store.Load(Scope);
        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);

        report.Journal.AppendCustody(new WorldSaveMarkerRow(8), 999, Load);
        Assert.True(_store.Save(report.Journal).Saved);

        Assert.Contains(File.ReadAllLines(path), line => line.Contains("future.field=kept%09safe"));
    }

    [Fact]
    public void ARecordCutOnALineBoundaryDoesNotLoadClean()
    {
        // #293 item 1, the exact case: every surviving line parses, so without
        // the closing line this loaded as a shorter record written on purpose.
        SettlementJournal journal = EveryKind();
        Assert.True(_store.Save(journal).Saved);
        string path = _store.ResolvePath(Scope);
        string[] lines = File.ReadAllLines(path);

        for (int keep = 0; keep < lines.Length; keep++)
        {
            File.WriteAllLines(path, lines.Take(keep));
            string cut = File.ReadAllText(path);

            JournalStore.LoadReport report = _store.Load(Scope);

            Assert.True(report.ReadOnly, "cut to " + keep + " lines loaded writable");
            Assert.Equal(JournalLoadOutcome.Truncated, report.Outcome);
            Assert.Equal(TrailerVerdict.Missing, report.Trailer);
            Assert.NotNull(report.Notice);

            // Everything readable is kept...
            Assert.Equal(Math.Max(0, keep - 2), report.Journal.Entries.Count);

            // ...and nothing is ever written over it.
            report.Journal.AppendCustody(new WorldSaveMarkerRow(99), 5000, Load);
            Assert.False(_store.Save(report.Journal).Saved);
            Assert.Equal(cut, File.ReadAllText(path));
        }
    }

    [Fact]
    public void ARowMissingFromTheMiddleIsCaughtByTheClosingLine()
    {
        Assert.True(_store.Save(EveryKind()).Saved);
        string path = _store.ResolvePath(Scope);
        List<string> lines = File.ReadAllLines(path).ToList();
        lines.RemoveAt(4);
        File.WriteAllLines(path, lines);

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.IntegrityMismatch, report.Outcome);
        Assert.Equal(TrailerVerdict.Mismatch, report.Trailer);
        Assert.True(report.ReadOnly);
    }

    [Fact]
    public void AChangedValueThatStillParsesIsCaughtByTheChecksum()
    {
        Assert.True(_store.Save(EveryKind()).Saved);
        string path = _store.ResolvePath(Scope);
        List<string> lines = File.ReadAllLines(path).ToList();
        int row = lines.FindIndex(line => line.Contains("accepted=1"));
        lines[row] = lines[row].Replace("accepted=1", "accepted=5");
        File.WriteAllLines(path, lines);

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.IntegrityMismatch, report.Outcome);
        Assert.True(report.ReadOnly);
    }

    [Fact]
    public void AnythingAfterTheClosingLineIsDamage()
    {
        Assert.True(_store.Save(EveryKind()).Saved);
        string path = _store.ResolvePath(Scope);
        File.AppendAllLines(path, new[] { "c\t50\t20\t1\t" + Load.ToString("N") + "\tgeneration=9" });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.True(report.ReadOnly);
        Assert.Equal(TrailerVerdict.LinesAfterTrailer, report.Trailer);
    }

    [Fact]
    public void AHandRepairedRecordWithARecomputedClosingLineLoadsAgain()
    {
        // The repair guide's promise: the closing line is recomputable by hand.
        Assert.True(_store.Save(EveryKind()).Saved);
        string path = _store.ResolvePath(Scope);
        List<string> lines = File.ReadAllLines(path).ToList();
        lines.RemoveAt(lines.Count - 1);
        lines.RemoveAt(lines.Count - 1);
        WriteSealed(path, lines);

        JournalStore.LoadReport report = _store.Load(Scope);
        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);
    }

    [Fact]
    public void ASchemaTwoFileStillLoadsAndIsWrittenBackAsSchemaThree()
    {
        string path = _store.ResolvePath(Scope);
        File.WriteAllLines(path, new[]
        {
            "#\tsettlement journal v2",
            "v\t2\t" + Scope.ToStorageKey(),
            "e\t0\t0\tcottage-1\t\t0\t",
            "e\t1\t1\tcottage-1\tcottage-1-0\t0\tchest-a\tWood*20",
            "t\t2\t5\thandover-0\tthorstein\t1\t$item_axe_bronze\t2\t63.5\t2",
            "t\t3\t6\thandover-0\tthorstein\t1\t$item_axe_bronze\t2\t63.5\t2",
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);
        Assert.False(report.ReadOnly);
        Assert.Equal(2, report.SchemaVersion);
        Assert.Equal(TrailerVerdict.Unspecified, report.Trailer);
        Assert.Equal(4, report.Journal.Entries.Count);

        ReplayResult replay = report.Journal.Replay();
        Assert.Equal(20, replay.Ledger.Totals(TheConcernedCat.Settlement.Custody.ReservationState.Held)["Wood"]);
        Assert.True(replay.Tools.TryGetHeld(new WorkerId("thorstein"), ToolKind.Axe, out _));
        Assert.False(replay.NeedsRepair);

        report.Journal.Append(JournalEntryKind.OrderTransition, new OrderId("cottage-1"), transition: TheConcernedCat.Settlement.Orders.OrderTransition.Reserve);
        Assert.True(_store.Save(report.Journal).Saved);

        JournalStore.LoadReport again = _store.Load(Scope);
        Assert.Equal(3, again.SchemaVersion);
        Assert.Equal(TrailerVerdict.Intact, again.Trailer);
        Assert.Equal(5, again.Journal.Entries.Count);
        Assert.True(again.Journal.Replay().Tools.TryGetHeld(new WorkerId("thorstein"), ToolKind.Axe, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Legacy_request_bearing_transitions_without_material_payload_survive_v3_migration(int version)
    {
        string path = _store.ResolvePath(Scope);
        File.WriteAllLines(path, new[]
        {
            "#\tsettlement journal v" + version,
            "v\t" + version + "\t" + Scope.ToStorageKey(),
            "e\t0\t0\tcottage-1\t\t0\t",
            "e\t1\t0\tcottage-1\tcottage-1-0\t1\t",
            "e\t2\t1\tcottage-1\tcottage-1-0\t0\tchest-a\tWood*20",
            "e\t3\t0\tcottage-1\tcottage-1-0\t5\t",
            "e\t4\t2\tcottage-1\tcottage-1-0\t0\t",
        });

        JournalStore.LoadReport report = _store.Load(Scope);
        for (int pass = 0; pass < 2; pass++)
        {
            Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);
            Assert.Equal(0, report.SkippedLines);
            Assert.False(report.ReadOnly);
            Assert.Equal(5 + pass, report.Journal.Entries.Count);
            Assert.Equal(new RequestId("cottage-1-0"), report.Journal.Entries[1].Request);
            Assert.Equal(new RequestId("cottage-1-0"), report.Journal.Entries[3].Request);
            Assert.False(JournalEntryKinds.IsMaterialIntent(report.Journal.Entries[1]));
            Assert.False(JournalEntryKinds.IsMaterialIntent(report.Journal.Entries[3]));
            ReplayResult replay = report.Journal.Replay();
            Assert.False(replay.NeedsRepair);
            Assert.Equal(OrderState.Cancelled, replay.StateOf(new OrderId("cottage-1")));
            Assert.Equal(20, replay.Ledger.Totals(ReservationState.Refunded)["Wood"]);
            Assert.Empty(replay.Ledger.Totals(ReservationState.Held));

            if (pass == 0)
            {
                report.Journal.Append(JournalEntryKind.OrderTransition, new OrderId("cottage-1"),
                    new RequestId("cottage-1-0"), OrderTransition.Cancel);
                Assert.True(_store.Save(report.Journal).Saved);
                report = _store.Load(Scope);
                Assert.Equal(3, report.SchemaVersion);
                Assert.Equal(TrailerVerdict.Intact, report.Trailer);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Production_transition_intents_reject_empty_or_partial_material_payloads(int transition)
    {
        string path = _store.ResolvePath(Scope);
        foreach (string payload in new[]
        {
            "\t\t100\t" + Load.ToString("N"),
            "chest-a\t\t\t", // A partial payload is not a legacy empty transition.
            "\t" + Load.ToString("N") + "\t\t",
            "\t\t\t\tWood*20",
        })
        {
            WriteSealed(path, new List<string>
            {
                "#\tsettlement journal v3",
                "v\t3\t" + Scope.ToStorageKey(),
                "e\t0\t0\tcottage-1\tcottage-1-0\t" + transition + "\t" + payload,
            });
            JournalStore.LoadReport report = _store.Load(Scope);
            Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
            Assert.True(report.ReadOnly);
            Assert.Equal(1, report.SkippedLines);
            Assert.Empty(report.Journal.Entries);
        }

        Assert.Throws<ArgumentException>(() => new JournalEntry(0, JournalEntryKind.OrderTransition,
            new OrderId("cottage-1"), new RequestId("cottage-1-0"), (OrderTransition)transition,
            worldTime: 100, loadEpoch: Load));
    }

    [Theory]
    [InlineData("c\t0\t14\t100\t\trequest=a-1")]
    [InlineData("c\t0\t14\t\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\trequest=a-1")]
    [InlineData("c\t0\t3\t100\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\tgeneration=1")]
    [InlineData("c\t0\t20\t100\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\tgeneration=1\tgeneration=2")]
    [InlineData("c\t0\t20\t100\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\tgeneration")]
    [InlineData("c\t0\t11\t100\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\torder=collect-1\tfrom=1\tto=Surveying\treason=Unspecified")]
    [InlineData("c\t0\t15\t100\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\trequest=a-1\torder=collect-1\toutcome=Completed\taccepted=-1\trem.place=\trem.key=\trem.epoch=\tevidence=")]
    [InlineData("t\t0\t7\tgive-1\tthorstein\t1\t$item_axe\t1\t10\t0\t100\t0a1b2c3d4e5f4a6b8c7d9e0f1a2b3c4d\tstage=finished")]
    [InlineData("e\t0\t1\tcottage-1\tcottage-1-0\t0\t\t\t\t\t")]
    [InlineData("e\t0\t2\tcottage-1\t\t0\t\t\t\t\t")]
    public void ADamagedSchemaThreeRowIsDamageNeverACrashOrAGuess(string row)
    {
        string path = _store.ResolvePath(Scope);
        WriteSealed(path, new List<string>
        {
            "#\tsettlement journal v3",
            "v\t3\t" + Scope.ToStorageKey(),
            row,
        });

        JournalStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.Empty(report.Journal.Entries);
    }

    [Fact]
    public void ACustodyEntryCannotBeBuiltWithoutItsTimeItsLoadOrItsOrder()
    {
        var journal = new SettlementJournal(Scope);
        Assert.Throws<ArgumentException>(() => journal.AppendCustody(new WorldSaveMarkerRow(1), 1, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.AppendCustody(new WorldSaveMarkerRow(1), double.NaN, Load));
        Assert.Throws<ArgumentException>(() => new JournalEntry(0, JournalEntryKind.TransferStarted, Order, new RequestId("x-1")));
        Assert.Throws<ArgumentException>(() => journal.Append(JournalEntryKind.Refunded, Order));
        Assert.Throws<ArgumentException>(() => journal.Append(JournalEntryKind.Reserved, Order, new RequestId("x-2")));
        Assert.Throws<ArgumentException>(() => journal.Append(
            JournalEntryKind.OrderTransition, Order, worldTime: 5, loadEpoch: default));
    }

    [Fact]
    public void TheRegisterIsSealedTooAndACutRegisterGoesReadOnly()
    {
        var registers = new SettlementRegisterStore(_root);
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch("run-a");
        var site = new GrantingSite();
        register.Designate(TheConcernedCat.Settlement.Designations.DesignationRequest.Area(
            TheConcernedCat.Settlement.Designations.DesignationKind.SettlementArea, new TheConcernedCat.Settlement.Worker.SitePoint(0, 0, 0), 20f), site, true);
        register.Designate(TheConcernedCat.Settlement.Designations.DesignationRequest.Area(
            TheConcernedCat.Settlement.Designations.DesignationKind.HarvestArea, new TheConcernedCat.Settlement.Worker.SitePoint(40, 0, 0), 20f), site, true);
        Assert.True(registers.Save(register).Saved);

        string path = registers.ResolvePath(Scope);
        string[] lines = File.ReadAllLines(path);
        Assert.StartsWith("end\t", lines[lines.Length - 1]);

        File.WriteAllLines(path, lines.Take(lines.Length - 2));
        SettlementRegisterStore.LoadReport report = registers.Load(Scope);

        Assert.True(report.ReadOnly);
        Assert.Equal(RegisterLoadOutcome.Truncated, report.Outcome);
        Assert.False(registers.Save(report.Register).Saved);
    }

    private sealed class GrantingSite : TheConcernedCat.Settlement.Designations.IDesignationSite
    {
        public TheConcernedCat.Settlement.Designations.AreaAccess CheckAccess(TheConcernedCat.Settlement.Worker.SitePoint centre, float radius) =>
            TheConcernedCat.Settlement.Designations.AreaAccess.Granted;
    }

    /// <summary>Writes lines with a correct closing line, the way the repair
    /// guide's script does.</summary>
    private static void WriteSealed(string path, List<string> lines)
    {
        var accumulator = new RecordTrailer.Accumulator();
        var output = new List<string>();
        foreach (string line in lines)
        {
            output.Add(line);
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            accumulator.AddLine(line);
            string[] fields = line.Split('\t');
            if (fields[0] == "e" || fields[0] == "t" || fields[0] == "c")
            {
                accumulator.CountRow(long.TryParse(fields[1], out long sequence) && sequence >= 0 ? sequence : -1);
            }
        }

        output.Add(accumulator.Line);
        File.WriteAllLines(path, output);
    }
}
