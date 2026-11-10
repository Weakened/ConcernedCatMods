using System;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's collection account is one world load's (#376, third
/// slice): it closes when its world goes down, what it still held is handed
/// back as evidence classified fail-closed, and the next world starts a fresh
/// one. Lead default 8, pending owner confirmation
/// (docs/mods/concerned-npc/ARCHITECTURE.md section 10).</summary>
public sealed class GunnarWorldDownTests
{
    private static CollectionAccount AccountHolding(int onHisBack, int inCart, int delivered = 0)
    {
        var account = new CollectionAccount();
        int step = 0;
        if (onHisBack + delivered > 0)
        {
            Assert.Equal(CargoOutcome.Recorded,
                account.Record("pick#" + step++, "Stone", onHisBack + delivered, StopResult.Took()));
        }

        if (inCart > 0)
        {
            Assert.Equal(CargoOutcome.Recorded,
                account.Record("pick#" + step++, "Wood", inCart, StopResult.Took(CargoPlace.InCart)));
        }

        if (delivered > 0)
        {
            account.RecordDeposit("deposit#" + step++, new DepositResult(
                DepositOutcome.PartlyDeposited, new[] { new System.Collections.Generic.KeyValuePair<string, int>("Stone", delivered) }));
            Assert.Equal(onHisBack, account.Ledger.At("Stone", CargoPlace.Carried));
            Assert.Equal(delivered, account.Ledger.At("Stone", CargoPlace.Delivered));
        }

        Assert.True(account.Ledger.IsConserved);
        return account;
    }

    [Fact]
    public void Closing_takes_what_is_unsettled_and_leaves_a_fresh_account()
    {
        CollectionAccount account = AccountHolding(onHisBack: 5, inCart: 3, delivered: 2);
        Assert.True(account.HoldsAnything);

        WorldDownCustody closed = account.CloseForWorld(BodyRecordTrust.Trusted);

        Assert.False(closed.IsSettled);
        Assert.Equal(10, closed.Acquired);
        Assert.True(closed.Conserved);
        Assert.Equal(5, closed.UnitsStanding(WorldDownStanding.InHisBodyRecord));
        Assert.Equal(3, closed.UnitsStanding(WorldDownStanding.InTheCart));
        Assert.Equal(0, closed.Unresolved);
        Assert.Contains(closed.Holdings, h => h.Item == "Stone" && h.Units == 5 && h.Place == CargoPlace.Carried);
        Assert.Contains(closed.Holdings, h => h.Item == "Wood" && h.Units == 3 && h.Place == CargoPlace.InCart);

        // Delivered material is history: it is not handed on as unsettled.
        Assert.DoesNotContain(closed.Holdings, h => h.Place == CargoPlace.Delivered);

        // The account is the next world's now, and holds nothing of this one.
        Assert.True(account.IsFresh);
        Assert.False(account.HoldsAnything);
        Assert.False(account.HoldsOnHisBack);
        Assert.Equal(0, account.Ledger.Acquired);
        Assert.Equal(0, account.Ledger.TotalEverywhere);
        Assert.Equal(0, account.Serviced);
        Assert.True(account.Ledger.IsConserved);
        Assert.Empty(account.Load());
    }

    [Theory]
    [InlineData((int)BodyRecordTrust.Unknown)]
    [InlineData((int)BodyRecordTrust.Untrusted)]
    [InlineData(99)]
    public void Carried_material_no_current_body_record_was_seen_holding_is_unresolved(int trustValue)
    {
        var trust = (BodyRecordTrust)trustValue;
        CollectionAccount account = AccountHolding(onHisBack: 4, inCart: 2);

        WorldDownCustody closed = account.CloseForWorld(trust);

        Assert.Equal(4, closed.Unresolved);
        Assert.Equal(0, closed.UnitsStanding(WorldDownStanding.InHisBodyRecord));
        // The cart is a vanilla container that saves with the world, whatever
        // his record says.
        Assert.Equal(2, closed.UnitsStanding(WorldDownStanding.InTheCart));
        Assert.True(account.IsFresh);
    }

    [Fact]
    public void Only_a_record_seen_current_vouches_for_what_he_carries()
    {
        foreach (BodyRecordTrust trust in Enum.GetValues(typeof(BodyRecordTrust)).Cast<BodyRecordTrust>())
        {
            Assert.Equal(
                trust == BodyRecordTrust.Trusted ? WorldDownStanding.InHisBodyRecord : WorldDownStanding.Unresolved,
                WorldDownPolicy.ForCarried(trust));
        }

        Assert.Equal(WorldDownStanding.InTheCart, WorldDownPolicy.ForCart());
        Assert.Equal(0, (int)default(BodyRecordTrust));
        Assert.Equal(BodyRecordTrust.Unknown, default(BodyRecordTrust));
    }

    [Fact]
    public void Nothing_one_world_recorded_is_the_next_worlds()
    {
        CollectionAccount account = AccountHolding(onHisBack: 6, inCart: 0);
        account.CloseForWorld(BodyRecordTrust.Trusted);

        // The next world: a deposit and a death that measured six stone each
        // find nothing of the last world's on his back, so the ledger moves
        // nothing rather than settling another world's units here.
        account.RecordDeposit("next#0", new DepositResult(
            DepositOutcome.Deposited, new[] { new System.Collections.Generic.KeyValuePair<string, int>("Stone", 6) }));
        var death = new DeathDropReport(DeathDropVerdict.Drop);
        death.Add(DeathDropLeg.Classify("Stone", asked: 6, before: 6, after: 0, returned: true, threw: false));
        account.RecordDeath("next#1", death);

        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.DroppedOnDeath));
        Assert.Equal(0, account.Ledger.Acquired);
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_settled_account_closes_with_nothing_to_write_and_closing_twice_is_harmless()
    {
        CollectionAccount account = AccountHolding(onHisBack: 0, inCart: 0, delivered: 3);
        WorldDownCustody closed = account.CloseForWorld(BodyRecordTrust.Unknown);

        Assert.True(closed.IsSettled);
        Assert.Equal(3, closed.Acquired);
        Assert.Equal(string.Empty, WorldDownRows.Serialize(DateTime.UtcNow, "gunnar", closed, "the world went away"));

        WorldDownCustody again = account.CloseForWorld(BodyRecordTrust.Unknown);
        Assert.True(again.IsSettled);
        Assert.Equal(0, again.Acquired);
    }

    [Fact]
    public void The_watch_latches_while_the_world_is_up_and_fails_closed_without_a_body()
    {
        var watch = new CollectionWorldWatch();
        watch.ObserveWorld(null);
        watch.ObserveWorld(0L);
        Assert.Null(watch.WorldUid);
        watch.ObserveWorld(4242L);
        watch.ObserveWorld(9999L);
        Assert.Equal(4242L, watch.WorldUid);

        // No body seen yet: unknown, read as untrusted.
        Assert.Equal(BodyRecordTrust.Unknown, watch.Trust);
        watch.ObserveBody(bodyBound: false, recordUnwritable: false, recordUnreadable: false);
        Assert.Equal(BodyRecordTrust.Unknown, watch.Trust);

        watch.ObserveBody(bodyBound: true, recordUnwritable: false, recordUnreadable: false);
        Assert.Equal(BodyRecordTrust.Trusted, watch.Trust);

        // The body unloading (or the world tearing down, where the flags read
        // "no body") changes nothing: the last body seen is what the world holds.
        watch.ObserveBody(bodyBound: false, recordUnwritable: false, recordUnreadable: false);
        Assert.Equal(BodyRecordTrust.Trusted, watch.Trust);

        // A record that fell behind is untrusted whatever came before.
        watch.ObserveBody(bodyBound: false, recordUnwritable: true, recordUnreadable: false);
        Assert.Equal(BodyRecordTrust.Untrusted, watch.Trust);
        watch.ObserveBody(bodyBound: false, recordUnwritable: false, recordUnreadable: false);
        Assert.Equal(BodyRecordTrust.Untrusted, watch.Trust);
        watch.ObserveBody(bodyBound: true, recordUnwritable: false, recordUnreadable: true);
        Assert.Equal(BodyRecordTrust.Untrusted, watch.Trust);

        CollectionAccount account = AccountHolding(onHisBack: 2, inCart: 0);
        WorldDownCustody closed = watch.Close(account, out long? world);

        Assert.Equal(4242L, world);
        Assert.Equal(2, closed.Unresolved);
        Assert.Equal(BodyRecordTrust.Untrusted, closed.Trust);

        // The next world starts with nothing of this one's.
        Assert.Null(watch.WorldUid);
        Assert.Equal(BodyRecordTrust.Unknown, watch.Trust);
        Assert.True(account.IsFresh);
    }

    [Fact]
    public void The_world_down_rows_name_only_what_is_unresolved_one_line_each()
    {
        CollectionAccount account = AccountHolding(onHisBack: 4, inCart: 2);
        account.Record("pick#9", "Flint\tShard", 1, StopResult.Took());
        WorldDownCustody closed = account.CloseForWorld(BodyRecordTrust.Untrusted);

        string rows = WorldDownRows.Serialize(
            new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), "gunnar", closed, "the world\nwent away");
        string[] lines = rows.TrimEnd('\n').Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("world-down\t2026-11-10T12:00:00Z\tgunnar\tUntrusted\t*\t5\t*\t", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("holding\t", StringComparison.Ordinal)
            && line.Contains("\tStone\t4\tCarried\tunresolved", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\tFlint Shard\t1\tCarried\tunresolved", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("\tWood\t", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.Equal(7, line.Count(character => character == '\t')));

        string file = WorldDownRows.Append(null, rows);
        Assert.StartsWith(WorldDownRows.Header, file);
        string more = WorldDownRows.Append(file.TrimEnd('\n'), rows);
        Assert.StartsWith(file, more);
        Assert.EndsWith(rows, more);
    }

    [Fact]
    public void Holds_agrees_with_the_holding_view_in_every_place()
    {
        CollectionAccount account = AccountHolding(onHisBack: 3, inCart: 2, delivered: 1);
        foreach (CargoPlace place in Enum.GetValues(typeof(CargoPlace)).Cast<CargoPlace>())
        {
            Assert.Equal(account.Ledger.Holding(place).Count > 0, account.Ledger.Holds(place));
        }

        Assert.True(account.HoldsOnHisBack);
        account.RecordDeposit("d#0", new DepositResult(
            DepositOutcome.Deposited, new[] { new System.Collections.Generic.KeyValuePair<string, int>("Stone", 3) }));
        Assert.False(account.HoldsOnHisBack);
        Assert.True(account.HoldsAnything);
    }

    [Fact]
    public void The_runtime_closes_the_account_at_the_down_edge_at_teardown_and_when_faulted()
    {
        // GunnarCollectionRuntime is a MonoBehaviour no test here can load, so
        // its wiring is read: a helper with no production caller is not done.
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ConcernedTeamster", "Adapters", "Workers", "GunnarCollectionRuntime.cs"));

        string downEdge = Between(source, "if (_lifecycle.ObserveWorld(worldUp) == PickForget.World)", "if (worldUp)");
        Assert.Contains("CloseAccountForWorld(\"the world went away\");", downEdge);

        string teardown = Between(source, "private void TearDown(string why)", "private void WatchAccountWorld()");
        Assert.Contains("CloseAccountForWorld(why);", teardown);

        string update = Between(source, "private void Update()", "private void OnDestroy()");
        Assert.Contains("CloseAccountForWorld(\"the world went away\");", update);

        string frame = Between(source, "private void FrameTick()", "private void EndOrder(string why)");
        Assert.Contains("WatchAccountWorld();", frame);

        string watch = Between(source, "private void WatchAccountWorld()", "private void CloseAccountForWorld(string why)");
        Assert.Contains("_worldWatch.ObserveBody(_worker() != null, _recordUnwritable(), _recordUnreadable());", watch);

        string close = Between(source, "private void CloseAccountForWorld(string why)", "private bool OptedIn()");
        Assert.Contains("_worldWatch.Close(_account, out world)", close);
        Assert.Contains("_worldDowns.TryAppend(world, rows, out kept);", close);
    }

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "missing: " + start);
        int to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "missing after " + start + ": " + end);
        return source.Substring(from, to - from);
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root could not be found.");
    }
}

/// <summary>The account is asked every frame whether anything is on his back;
/// that question must not allocate.</summary>
[Collection(ExactAllocationCollection.Name)]
public sealed class GunnarWorldDownAllocationTests
{
    [Fact]
    public void Asking_whether_he_holds_anything_allocates_nothing()
    {
        var account = new CollectionAccount();
        account.Record("pick#0", "Stone", 3, StopResult.Took());
        account.Record("pick#1", "Wood", 2, StopResult.Took(CargoPlace.InCart));
        bool held = false;
        _ = account.HoldsOnHisBack;
        _ = account.HoldsAnything;

        long allocated = ExactAllocations.Measure(() =>
        {
            for (int index = 0; index < 1000; index++)
            {
                held |= account.HoldsOnHisBack;
                held |= account.HoldsAnything;
            }
        });

        Assert.True(held);
        Assert.Equal(0, allocated);
    }
}
