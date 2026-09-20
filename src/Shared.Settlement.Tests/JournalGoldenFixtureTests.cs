using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Storage;

namespace Shared.Settlement.Tests;

/// <summary>A settlement journal exactly as a shipped build wrote it, pinned
/// byte for byte and loaded back.
///
/// <b>Why a literal and not a round trip.</b> Every other test of this format
/// writes a file with today's code and reads it with today's code, so the pair
/// can drift together and stay green while every file on a player's disk stops
/// loading. This one carries the bytes. They were produced by the shipped
/// writer and then frozen: if a row tag, a field name, a value, the header, the
/// scope token or the closing line's checksum ever changes, this fails - and
/// the failure is the point, because a change here is a change to files that
/// exist on real machines.
///
/// <b>What it guards, and for whom.</b> The Concerned NPC program moves the
/// custody ledger, the transfer executor and the reconciler into a shared
/// runtime. The acceptance criterion for that whole program is that a
/// pre-refactor data directory, dropped in unchanged, still works with no
/// migration code having run. Schema 3 and every literal in it are therefore
/// frozen, and this is where that is enforced rather than intended.
///
/// <b>If this test fails</b>, the question is not how to update the literal. It
/// is whether the change was meant to break every existing settlement record;
/// and if it was, it needs its own issue, a schema the loader understands, and
/// a migration - none of which this program is doing.</summary>
public sealed class JournalGoldenFixtureTests : IDisposable
{
    private static readonly SettlementScope Scope =
        new(worldId: 4242, settlement: new SettlementId("golden-camp"));

    private static readonly Guid Load = new("11111111-2222-4333-8444-555555555555");

    private static readonly Guid Epoch = new("5c2c6d7e-2a57-4a44-8d8e-3f3d9b5ac001");

    /// <summary>The file, line for line, as the shipped writer produced it.
    /// Lines are joined with a carriage return and a line feed, which is also
    /// part of the format.</summary>
    private static readonly string[] Golden =
    {
        "#\tsettlement journal v3",
        "v\t3\t0000000000001092.golden-camp",
        "c\t0\t10\t100\t11111111222243338444555555555555\torder=collect-1\tworker=thorstein\tquotas=2\tquota0.resource=Stone\tquota0.n=20\tquota1.resource=Wood\tquota1.n=30\tscope.source=DefaultCampCircle\tscope.x=10\tscope.y=20\tscope.z=30\tscope.r=30\tscope.anchor=your bed\tscope.rev=3\tscope.epoch=5c2c6d7e2a574a448d8e3f3d9b5ac001\tdelivery.kind=Container\tdelivery.key=1:42\tdelivery.epoch=5c2c6d7e2a574a448d8e3f3d9b5ac001\tdelivery.x=12\tdelivery.y=20\tdelivery.z=30\tparticipation=Solo\tissuer=Tester",
        "c\t1\t12\t101\t11111111222243338444555555555555\trequest=collect-1-pick-2\torder=collect-1\tsource.prefab=Pickable_Stone\tsource.id=1:500\tsource.epoch=5c2c6d7e2a574a448d8e3f3d9b5ac001\tsource.x=11\tsource.y=20\tsource.z=31",
        "c\t2\t13\t102\t11111111222243338444555555555555\trequest=collect-1-pick-2\torder=collect-1\toutcome=Picked\treason=traced\tdrops=1\tdrop0.id=1:900\tdrop0.n=4\tdrop0.prefab=Stone\tdrop0.q=1\tdrop0.v=0",
        "c\t3\t14\t103\t11111111222243338444555555555555\trequest=collect-1-t-4\torder=collect-1\tfrom.place=SourceGround\tfrom.key=1:900\tfrom.epoch=5c2c6d7e2a574a448d8e3f3d9b5ac001\tto.place=Worker\tto.key=foreman/thorstein\tto.epoch=\titem.prefab=Stone\titem.q=1\titem.v=0\tcount=4\trev=3",
        "c\t4\t15\t104\t11111111222243338444555555555555\trequest=collect-1-t-4\torder=collect-1\toutcome=Completed\taccepted=4\trem.place=SourceGround\trem.key=1:900\trem.epoch=5c2c6d7e2a574a448d8e3f3d9b5ac001\tevidence=moved",
        "c\t5\t20\t105\t11111111222243338444555555555555\tgeneration=1",
        "end\t6\t5\t04049bdd69729fb1",
    };

    private readonly string _root;

    public JournalGoldenFixtureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-journal-golden", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void A_journal_a_shipped_build_wrote_still_loads_clean_and_means_the_same_thing()
    {
        var store = new JournalStore(_root);
        File.WriteAllText(store.ResolvePath(Scope), Text());

        JournalStore.LoadReport report = store.Load(Scope);

        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);
        Assert.False(report.ReadOnly, report.Notice);
        Assert.Equal(TrailerVerdict.Intact, report.Trailer);
        Assert.Equal(3, report.SchemaVersion);
        Assert.Equal(0, report.SkippedLines);

        SettlementJournal journal = report.Journal;
        Assert.Equal(6, journal.Entries.Count);

        // The rows, in order, with the values the file states - not merely a
        // count of them. A loader that silently dropped a field would still
        // produce six entries.
        Assert.IsType<CollectionAcceptedRow>(journal.Entries[0].Custody);
        Assert.IsType<PickupStartedRow>(journal.Entries[1].Custody);
        Assert.IsType<PickupFinishedRow>(journal.Entries[2].Custody);
        Assert.IsType<TransferStartedRow>(journal.Entries[3].Custody);
        Assert.IsType<TransferFinishedRow>(journal.Entries[4].Custody);
        Assert.IsType<WorldSaveMarkerRow>(journal.Entries[5].Custody);

        var accepted = (CollectionAcceptedRow)journal.Entries[0].Custody!;
        Assert.Equal("collect-1", accepted.Definition.Order.Value);
        Assert.Equal("thorstein", accepted.Definition.Worker.Value);
        Assert.Equal(2, accepted.Definition.Quotas.Count);
        Assert.Equal(20, accepted.Definition.Quotas[0].Requested);
        Assert.Equal(30, accepted.Definition.Quotas[1].Requested);
        Assert.Equal(Epoch, accepted.Definition.Scope.WorldLoadEpoch);
        Assert.Equal("1:42", accepted.Definition.Delivery.ContainerKey);

        var started = (TransferStartedRow)journal.Entries[3].Custody!;
        Assert.Equal("collect-1-t-4", started.Intent.Request.Value);
        Assert.Equal(CustodyPlace.SourceGround, started.Intent.From.Place);
        Assert.Equal(CustodyPlace.Worker, started.Intent.To.Place);
        Assert.Equal("foreman/thorstein", started.Intent.To.Key);
        Assert.Equal(4, started.Intent.Count);

        var finished = (TransferFinishedRow)journal.Entries[4].Custody!;
        Assert.Equal(TransferOutcome.Completed, finished.Receipt.Outcome);
        Assert.Equal(4, finished.Receipt.Accepted);

        var marker = (WorldSaveMarkerRow)journal.Entries[5].Custody!;
        Assert.Equal(1, marker.Generation);

        Assert.Equal(Load, journal.Entries[0].LoadEpoch);
        Assert.Equal(100d, journal.Entries[0].WorldTime!.Value, 3);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void A_newer_build_preserves_existing_rows_when_saving(string newline)
    {
        // Loading it is not enough if the next save writes a file an older
        // build could not read. Every line that was there before must still be
        // there with identical fields. The text writer uses native line endings;
        // both Windows and Unix source files must retain their rows.
        var store = new JournalStore(_root);
        string path = store.ResolvePath(Scope);
        File.WriteAllText(path, string.Join(newline, Golden) + newline);

        JournalStore.LoadReport report = store.Load(Scope);
        Assert.Equal(JournalLoadOutcome.Loaded, report.Outcome);

        report.Journal.AppendCustody(new WorldSaveMarkerRow(2), 200.0, Load);
        Assert.True(store.Save(report.Journal).Saved);

        string written = File.ReadAllText(path);
        string untouched = string.Join(Environment.NewLine, Golden.Take(Golden.Length - 1)) + Environment.NewLine;

        Assert.StartsWith(untouched, written, StringComparison.Ordinal);
        Assert.Contains("generation=2", written, StringComparison.Ordinal);

        // And it still loads, which is what an older build would be doing with
        // it next.
        Assert.Equal(JournalLoadOutcome.Loaded, store.Load(Scope).Outcome);
    }

    [Fact]
    public void The_schema_number_in_the_header_is_three()
    {
        // Named on its own, because bumping it is the one change that makes
        // every shipped build treat an existing file as unreadable, and it must
        // never happen as a side effect of a code move.
        Assert.Equal("#\tsettlement journal v3", Golden[0]);
        Assert.StartsWith("v\t3\t", Golden[1], StringComparison.Ordinal);
    }

    private static string Text() => string.Join("\r\n", Golden) + "\r\n";
}
