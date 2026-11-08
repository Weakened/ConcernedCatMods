using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using static Shared.Settlement.Tests.CustodyFixtures;

namespace Shared.Settlement.Tests;

/// <summary>#282 review blocker: the take of a traced drop must name the place
/// the settlement record booked that drop at, through the real custody core,
/// or every take is refused and nothing a pick or a fell dropped ever reaches
/// Thorstein. Tested against the real ledger rather than a fake pickup port,
/// because the fake is where the mismatch hid.</summary>
public sealed class PickupTakeLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cc-pickup-take", Guid.NewGuid().ToString("N"));

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

    private CustodyProcess Picked(SpawnedDrop[] drops, PickupOutcome outcome = PickupOutcome.Picked)
    {
        var world = new FakeWorld();
        var disk = new FaultyDisk(world, new JournalStore(Path.Combine(_root, Guid.NewGuid().ToString("N"))));
        CustodyProcess process = CustodyProcess.Start(world, disk);
        Assert.True(process.Core.RecordAccepted(Definition(stone: 10, wood: 10), out string refused), refused);
        RequestId? pickup = process.Core.BeginPickup(Order, Source(), out string why);
        Assert.True(pickup.HasValue, why);
        Assert.True(process.Core.FinishPickup(pickup!.Value, new PickupResult(outcome, drops, "traced"), out why), why);
        return process;
    }

    private static TransferIntent Take(CustodyProcess process, CustodyLocation from, SpawnedDrop drop) =>
        new TransferIntent(
            CustodyIds.ForTransfer(Order, process.Core.Ledger), Order, from, WorkerAt(), drop.Item, drop.Count,
            process.Core.Ledger.Revision);

    [Fact]
    public void ATracedDropIsTakenFromWhereThePickBookedIt()
    {
        var drop = new SpawnedDrop("1:777", Wood, 1);
        CustodyProcess process = Picked(new[] { drop });

        CustodyLocation from = PickupTakes.GroundOf(drop, Source());
        Assert.Equal(1, process.Core.Ledger.HoldingAt(Order, from, Wood));
        TransferIntent take = Take(process, from, drop);
        Assert.Equal(TransferOutcome.Unspecified, process.Core.BeginTransfer(take, out string why));
        Assert.True(
            process.Core.FinishTransfer(new TransferReceipt(take.Request, TransferOutcome.Completed, 1, from, "taken"), out why),
            why);

        Assert.Equal(0, process.Core.Ledger.HoldingAt(Order, from, Wood));
        Assert.Equal(1, process.Core.Ledger.ProgressFor(Definition(stone: 10, wood: 10), CollectedResource.Wood)!.Carried);
    }

    [Fact]
    public void TheSourcesOwnNameHoldsNothingSoATakeNamingItIsRefused()
    {
        // The defect this pins: the port named the source, not the drop.
        var drop = new SpawnedDrop("1:778", Wood, 1);
        CustodyProcess process = Picked(new[] { drop });

        var wrong = new CustodyLocation(CustodyPlace.SourceGround, Source().ToString(), Source().WorldLoadEpoch);
        Assert.Equal(0, process.Core.Ledger.HoldingAt(Order, wrong, Wood));
        Assert.Equal(TransferOutcome.Refused, process.Core.BeginTransfer(Take(process, wrong, drop), out _));
    }

    [Fact]
    public void EveryDropOfAFellIsTakenSeparatelyAndAFellWithNoWoodRoundTripsCreditingNothing()
    {
        var first = new SpawnedDrop("1:801", Wood, 1);
        var second = new SpawnedDrop("1:802", Wood, 1);
        CustodyProcess process = Picked(new[] { first, second });
        foreach (SpawnedDrop drop in new[] { first, second })
        {
            CustodyLocation from = PickupTakes.GroundOf(drop, Source());
            TransferIntent take = Take(process, from, drop);
            Assert.Equal(TransferOutcome.Unspecified, process.Core.BeginTransfer(take, out string why));
            Assert.True(
                process.Core.FinishTransfer(new TransferReceipt(take.Request, TransferOutcome.Completed, 1, from, "taken"), out why),
                why);
        }

        Assert.Equal(2, process.Core.Ledger.ProgressFor(Definition(stone: 10, wood: 10), CollectedResource.Wood)!.Carried);

        // A fell whose drops were none of the order's: a Picked result with no
        // drops is written, read back and credits nothing.
        CustodyProcess empty = Picked(Array.Empty<SpawnedDrop>());
        Assert.Equal(0, empty.Core.Ledger.ProgressFor(Definition(stone: 10, wood: 10), CollectedResource.Wood)!.OnGround);
        empty.SaveWorld();
        CustodyProcess reloaded = empty.Restart();
        Assert.False(reloaded.Ledger.HasUncertainTransfer(Order));
        Assert.Equal(0, reloaded.Ledger.ProgressFor(Definition(stone: 10, wood: 10), CollectedResource.Wood)!.OnGround);

        // A hit that left the tree standing is a Refused pick: nothing either.
        CustodyProcess struck = Picked(Array.Empty<SpawnedDrop>(), PickupOutcome.Refused);
        struck.SaveWorld();
        Assert.Equal(0, struck.Restart().Ledger.ProgressFor(Definition(stone: 10, wood: 10), CollectedResource.Wood)!.OnGround);
    }
}
