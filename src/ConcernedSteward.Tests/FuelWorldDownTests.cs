using System;
using System.IO;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Sunniva's run is one world load's (#376, third slice): when her
/// world goes down the run closes with it, what it still held is kept the way
/// she already keeps it - her pack in that world, her loss in that world's
/// record - and the next world starts a fresh ledger. Lead default 8, pending
/// owner confirmation (docs/mods/concerned-npc/ARCHITECTURE.md section 10).
/// </summary>
public sealed class FuelWorldDownTests
{
    private const string Wood = StewardFixture.Wood;
    private const long WorldA = 1111L;
    private const long WorldB = 2222L;

    [Fact]
    public void Without_the_close_fuel_she_carried_in_one_world_becomes_a_loss_in_the_next()
    {
        // The hazard this slice closes, shown on the loop as it stood before
        // it: nothing ends the run at world-down, so the next world's first
        // count of her pack is compared against the last world's ledger.
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(5);

        loop.Stop("The world went away.");
        loop.OnWorldLoaded(new FakeStore("the Steward's pack in world B"), Wood);

        Assert.Equal(5, loop.Custody.Unaccounted);
        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);
    }

    [Fact]
    public void Closed_at_world_down_the_next_world_starts_fresh_and_counts_nothing_of_the_last()
    {
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(5);
        loop.Custody.RecordBurned(1);
        var carry = new FuelWorldDownCarry();
        int writes = 0;

        loop.Stop("The world went away.");
        FuelWorldDownResult result = carry.Close(WorldA, loop.Custody, lossOnRecord: 0, () => { writes++; return true; });

        Assert.Equal(4, result.Run.Carried);
        Assert.Equal(5, result.Run.Withdrawn);
        Assert.Equal(1, result.Run.Burned);
        Assert.True(result.Run.Balanced);
        Assert.Equal(FuelWorldDownLoss.NoLoss, result.Loss);
        Assert.Equal(0, writes);
        Assert.Contains("stay in her pack in that world", result.Describe());

        AssertFresh(loop.Custody);

        loop.OnWorldLoaded(new FakeStore("the Steward's pack in world B"), Wood);
        Assert.Equal(0, loop.Custody.Unaccounted);
        Assert.Equal(0, loop.Custody.Withdrawn);
        Assert.False(loop.Custody.HasLoss);
        Assert.Equal(UpkeepPhase.Idle, loop.Phase);
    }

    [Fact]
    public void Her_pack_is_counted_again_when_its_own_world_loads_and_nothing_was_written_off()
    {
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(5);
        var carry = new FuelWorldDownCarry();
        loop.Stop("The world went away.");
        carry.Close(WorldA, loop.Custody, 0, () => true);

        loop.OnWorldLoaded(new FakeStore("the Steward's pack in world A", (Wood, 5)), Wood);

        Assert.Equal(5, loop.Custody.Carried);
        Assert.Equal(5, loop.Custody.Withdrawn);
        Assert.Equal(0, loop.Custody.Unaccounted);
        Assert.True(loop.Custody.Balances);
        Assert.Contains("put it back", loop.Explanation);
    }

    [Fact]
    public void A_loss_already_on_record_is_not_written_again_and_comes_back_only_to_its_world()
    {
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(3);
        loop.Custody.RecordLost(3);
        var carry = new FuelWorldDownCarry();
        int writes = 0;

        FuelWorldDownResult result = carry.Close(WorldA, loop.Custody, lossOnRecord: 3, () => { writes++; return true; });

        Assert.Equal(FuelWorldDownLoss.OnRecord, result.Loss);
        Assert.Equal(0, writes);
        AssertFresh(loop.Custody);
        Assert.Equal(0, carry.LossToRestore(WorldB, recorded: 0));
        Assert.Equal(3, carry.LossToRestore(WorldA, recorded: 3));
    }

    [Fact]
    public void A_loss_the_record_is_behind_on_is_written_once_before_the_ledger_closes()
    {
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(4);
        loop.Custody.RecordLost(3);
        var carry = new FuelWorldDownCarry();
        int writes = 0;
        int seenByTheWrite = -1;

        FuelWorldDownResult result = carry.Close(WorldA, loop.Custody, lossOnRecord: 1, () =>
        {
            writes++;
            seenByTheWrite = loop.Custody.Unaccounted;
            return true;
        });

        Assert.Equal(1, writes);
        Assert.Equal(3, seenByTheWrite);
        Assert.Equal(FuelWorldDownLoss.OnRecord, result.Loss);
        Assert.Equal(3, result.Run.Unaccounted);
        Assert.Equal(1, result.Run.Carried);
        Assert.Equal(0, carry.KeptFor(WorldA));
        AssertFresh(loop.Custody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_loss_its_record_could_not_take_is_kept_for_that_world_and_never_for_another(bool writeThrows)
    {
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(2);
        loop.Custody.RecordLost(2);
        var carry = new FuelWorldDownCarry();

        FuelWorldDownResult result = carry.Close(WorldA, loop.Custody, 0, () =>
        {
            if (writeThrows)
            {
                throw new IOException("disk full");
            }

            return false;
        });

        Assert.Equal(FuelWorldDownLoss.KeptForThatWorld, result.Loss);
        Assert.Contains("could NOT be written", result.Describe());
        Assert.Equal(2, carry.KeptFor(WorldA));
        AssertFresh(loop.Custody);

        // Another world: nothing of it.
        Assert.Equal(0, carry.LossToRestore(WorldB, recorded: 0));
        loop.OnWorldLoaded(new FakeStore("the Steward's pack in world B"), Wood);
        Assert.False(loop.Custody.HasLoss);

        // Its own world: restored, and she stops there until a person looks.
        loop.Stop("The world went away.");
        carry.Close(WorldB, loop.Custody, 0, () => true);
        int restore = carry.LossToRestore(WorldA, recorded: 0);
        Assert.Equal(2, restore);
        loop.Custody.RestoreLoss(restore);
        loop.OnWorldLoaded(new FakeStore("the Steward's pack in world A"), Wood);
        Assert.Equal(2, loop.Custody.Unaccounted);
        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);

        // Handed back once: the ledger holds it again from here.
        Assert.Equal(0, carry.KeptFor(WorldA));
        Assert.Equal(0, carry.LossToRestore(WorldA, recorded: 0));

        // Still not on record at the next world-down: kept again, not lost.
        FuelWorldDownResult again = carry.Close(WorldA, loop.Custody, 0, () => false);
        Assert.Equal(FuelWorldDownLoss.KeptForThatWorld, again.Loss);
        Assert.Equal(2, carry.KeptFor(WorldA));
    }

    [Fact]
    public void The_kept_loss_never_lowers_what_the_record_says()
    {
        var carry = new FuelWorldDownCarry();
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(2);
        loop.Custody.RecordLost(2);
        carry.Close(WorldA, loop.Custody, 0, () => false);

        Assert.Equal(5, carry.LossToRestore(WorldA, recorded: 5));
    }

    [Fact]
    public void A_loss_with_no_world_to_keep_it_against_is_said_and_not_attributed()
    {
        UpkeepLoop loop = NewLoop();
        loop.Custody.RecordWithdrawn(1);
        loop.Custody.RecordLost(1);
        var carry = new FuelWorldDownCarry();
        int writes = 0;

        FuelWorldDownResult result = carry.Close(0L, loop.Custody, 0, () => { writes++; return true; });

        Assert.Equal(FuelWorldDownLoss.InLogOnly, result.Loss);
        Assert.Equal(0, writes);
        Assert.Equal(0, carry.KeptFor(0L));
        Assert.Contains("the only record", result.Describe());
        AssertFresh(loop.Custody);
    }

    [Fact]
    public void The_close_is_unconditional_where_a_fresh_run_refuses()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(3);
        custody.RecordLost(1);
        custody.AcknowledgeLoss();
        custody.RecordLost(1);
        Assert.False(custody.TryReset());

        FuelRunAtWorldDown run = custody.CloseForWorld();

        Assert.Equal(1, run.Carried);
        Assert.Equal(1, run.Unaccounted);
        Assert.Equal(2, run.Withdrawn);
        Assert.True(run.Balanced);
        Assert.False(run.IsSettled);
        AssertFresh(custody);

        // The acknowledgement belonged to the closed run and goes with it.
        custody.RecordWithdrawn(1);
        Assert.Equal(1, custody.Withdrawn);
        Assert.True(custody.Balances);
    }

    [Fact]
    public void A_settled_run_says_nothing()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(2);
        custody.RecordBurned(1);
        custody.RecordReturned(1);
        var carry = new FuelWorldDownCarry();

        FuelWorldDownResult result = carry.Close(WorldA, custody, 0, () => throw new InvalidOperationException("not asked"));

        Assert.True(result.Run.IsSettled);
        Assert.Equal(FuelWorldDownLoss.NoLoss, result.Loss);
        Assert.Equal(string.Empty, result.Describe());
    }

    [Fact]
    public void The_runtime_closes_her_run_at_world_down_and_restores_what_it_kept_at_world_load()
    {
        // StewardRuntime reads Unity and is not compiled into these tests, so
        // its wiring is read: a helper with no production caller is not done.
        string source = File.ReadAllText(Path.Combine(ProductSources.Product, "Runtime", "StewardRuntime.cs"));

        string unloaded = Between(source, "internal void OnWorldUnloaded()", "_presentation.OnWorldUnloaded();");
        int stop = unloaded.IndexOf("_loop.Stop(\"The world went away.\");", StringComparison.Ordinal);
        int close = unloaded.IndexOf("CloseRunForWorld();", StringComparison.Ordinal);
        Assert.True(stop >= 0 && close > stop, "the run must be closed after the loop stops, before the world is forgotten");

        string loaded = Between(source, "internal void OnWorldLoaded()", "internal void OnWorldUnloaded()");
        Assert.Contains("_loadedScope = scope;", loaded);
        Assert.Contains("_lossOnRecord = report.RecordedLoss;", loaded);

        // The kept loss leaves the carry directly before it reaches the ledger.
        string restore = Between(loaded, "_recordedLoss = _worldDown.LossToRestore(scope.Value.WorldId, _recordedLoss);",
            "ScopeSnapshot recoveredScope");
        Assert.Contains("_loop.Custody.RestoreLoss(_recordedLoss);", restore);
        Assert.DoesNotContain("Restore(", restore.Replace("RestoreLoss(", string.Empty).Replace("LossToRestore(", string.Empty));

        string closeRun = Between(source, "private void CloseRunForWorld()", "private bool SaveRecord(");
        Assert.Contains("_worldDown.Close(", closeRun);
        Assert.Contains("SaveRecord(_journal.UnresolvedIntents, scope: scope)", closeRun);

        string save = Between(source, "private bool SaveRecord(", "private string CarryingName()");
        Assert.Contains("_lossOnRecord = loss;", save);
    }

    private static void AssertFresh(FuelCustody custody)
    {
        Assert.Equal(0, custody.Withdrawn);
        Assert.Equal(0, custody.Carried);
        Assert.Equal(0, custody.Burned);
        Assert.Equal(0, custody.Returned);
        Assert.Equal(0, custody.Unaccounted);
        Assert.Empty(custody.Ledger.Holdings);
        Assert.True(custody.Balances);
    }

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "missing: " + start);
        int to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "missing after " + start + ": " + end);
        return source.Substring(from, to - from);
    }

    private static UpkeepLoop NewLoop() =>
        new UpkeepLoop(UpkeepLimits.Default, new MemoryUpkeepJournal(), _ => { });
}
