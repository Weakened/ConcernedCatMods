using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.Settlement.Custody;
using UnityEngine;
using static ConcernedForeman.Tests.ForemanFixtures;

namespace ConcernedForeman.Tests;

/// <summary>The ports, and the transfer path that actually ships.
///
/// Review R2's M4: `CustodyExecutorTests` pins the executor's two-step
/// Add/Remove branch, and no shipped port can reach it — `EngineInventoryPort.Add`
/// refuses on purpose, so every real transfer goes through `MoveFrom`. These
/// tests drive the real executor over the real ports over a vanilla inventory
/// modelled on 1.0.14, so the ordering, the classification and the refusals that
/// ship are the ones under test.</summary>
public sealed class EngineInventoryPortTests : IDisposable
{
    private readonly List<string> _trace = new List<string>();

    public EngineInventoryPortTests()
    {
        Inventory.Trace = _trace;
        PrivateArea.Access = true;
        Player.m_localPlayer = null;
        Game.instance = new Game();

        // The item database is the game's, and each test describes its own
        // items: a stack size from one test must not follow into the next.
        ObjectDB.instance = new ObjectDB();
    }

    public void Dispose()
    {
        Inventory.Trace = null;
        Player.m_localPlayer = null;
    }

    private static WorkerInventoryPort WorkerPort(WorkerBody body, float carryWeight = 1000f) =>
        new WorkerInventoryPort(body, () => carryWeight);

    private static ContainerInventoryPort ChestPort(Container chest, float reach = 3f, Vector3? worker = null) =>
        new ContainerInventoryPort(chest, KeyOf(chest), () => worker ?? Vector3.zero, reach);

    // ------------------------------------------------------------------
    // A port never creates anything
    // ------------------------------------------------------------------

    [Fact]
    public void APortRefusesToAddBecauseItHasNothingToAdd()
    {
        Container chest = Chest();
        ContainerInventoryPort port = ChestPort(chest);

        // The executor's two-step branch cannot run against a shipped port, and
        // this is why: an item made from a prefab would be a minted stack that
        // never existed, and would lose what the real one carried.
        Assert.Equal(0, port.Add(Stone, 5));
        Assert.Empty(chest.Bag.GetAllItems());
        Assert.Empty(_trace);
    }

    // ------------------------------------------------------------------
    // The path that ships: add here before removing there
    // ------------------------------------------------------------------

    [Fact]
    public void AWholeStackDepositAddsToTheChestBeforeItTakesFromHim()
    {
        CustodyCore core = Custody();
        WorkerBody body = Body();
        Carry(core, body.Inventory!, Stone, 10);
        Container chest = Chest();
        _trace.Clear();

        TransferReceipt receipt = core.Executor.Execute(
            Intent(core, WorkerAt(), ChestAt(), Stone, 10), WorkerPort(body), ChestPort(chest));

        Assert.Equal(TransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(10, receipt.Accepted);
        Assert.Equal(10, EngineInventoryPort.CountIn(chest.Bag, Stone));
        Assert.Equal(0, EngineInventoryPort.CountIn(body.Inventory!, Stone));

        // The ordering the whole design rests on, measured on the real
        // inventories: the chest has it before the worker loses it.
        int added = _trace.FindIndex(call => call.StartsWith("chest.add", StringComparison.Ordinal));
        int removed = _trace.FindIndex(call => call.StartsWith("worker.remove", StringComparison.Ordinal));
        Assert.True(added >= 0 && removed > added, string.Join(" | ", _trace));
    }

    [Fact]
    public void APartStackDepositAddsTheCloneBeforeItTakesFromTheStack()
    {
        // The only real partial path: the clone is added first and exactly what
        // arrived is taken off his stack. Inverting those two lines is the
        // regression review R2 traced to green with no test in its way.
        CustodyCore core = Custody();
        WorkerBody body = Body();
        Carry(core, body.Inventory!, Stone, 10);
        Container chest = Chest();
        _trace.Clear();

        TransferReceipt receipt = core.Executor.Execute(
            Intent(core, WorkerAt(), ChestAt(), Stone, 4), WorkerPort(body), ChestPort(chest));

        Assert.Equal(TransferOutcome.Completed, receipt.Outcome);
        Assert.Equal(4, EngineInventoryPort.CountIn(chest.Bag, Stone));
        Assert.Equal(6, EngineInventoryPort.CountIn(body.Inventory!, Stone));

        int added = _trace.FindIndex(call => call.StartsWith("chest.add", StringComparison.Ordinal));
        int removed = _trace.FindIndex(call => call.StartsWith("worker.remove", StringComparison.Ordinal));
        Assert.True(added >= 0 && removed > added, string.Join(" | ", _trace));
    }

    [Fact]
    public void ADepositThatOnlyPartlyFitsCreditsWhatArrivedAndLeavesTheRest()
    {
        CustodyCore core = Custody();
        WorkerBody body = Body();
        Carry(core, body.Inventory!, Stone, 10, maxStack: 5);
        Container chest = Chest(width: 1, height: 1);
        chest.Bag.AddItem(Stack(Stone, 3, maxStack: 5));

        TransferReceipt receipt = core.Executor.Execute(
            Intent(core, WorkerAt(), ChestAt(), Stone, 10), WorkerPort(body), ChestPort(chest));

        // One slot holding 3 of a 5-stack: two fit, eight stay with him, and the
        // record credits exactly what the counts showed.
        Assert.Equal(TransferOutcome.Partial, receipt.Outcome);
        Assert.Equal(2, receipt.Accepted);
        Assert.Equal(5, EngineInventoryPort.CountIn(chest.Bag, Stone));
        Assert.Equal(8, EngineInventoryPort.CountIn(body.Inventory!, Stone));
        Assert.Equal(8, core.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(2, core.Ledger.HoldingAt(Order, ChestAt(), Stone));
    }

    [Fact]
    public void AFullChestTakesNothingAndHeKeepsEverything()
    {
        CustodyCore core = Custody();
        WorkerBody body = Body();
        Carry(core, body.Inventory!, Stone, 10, maxStack: 5);
        Container chest = Chest(width: 1, height: 1);
        chest.Bag.AddItem(Stack(Stone, 5, maxStack: 5));

        TransferReceipt receipt = core.Executor.Execute(
            Intent(core, WorkerAt(), ChestAt(), Stone, 10), WorkerPort(body), ChestPort(chest));

        Assert.Equal(TransferOutcome.Refused, receipt.Outcome);
        Assert.Equal(10, EngineInventoryPort.CountIn(body.Inventory!, Stone));
        Assert.Equal(10, core.Ledger.HoldingAt(Order, WorkerAt(), Stone));
        Assert.Equal(5, EngineInventoryPort.CountIn(chest.Bag, Stone));
    }

    [Fact]
    public void AChangeTheBodyCouldNotStoreIsUncertainNeverCompleted()
    {
        // D9: the body writes its inventory in the same call as the change. If
        // that write fails, a receipt involving him is uncertain, never
        // completed -- the port says so by throwing at the end of the move.
        CustodyCore core = Custody();
        WorkerBody body = Body();
        Carry(core, body.Inventory!, Stone, 10);
        Container chest = Chest();

        // Everything is in order when the transfer is checked; his object stops
        // taking writes while the items are moving.
        Assert.True(WorkerPort(body).IsAvailable);
        body.View!.Zdo.FailWrites = true;

        TransferReceipt receipt = core.Executor.Execute(
            Intent(core, WorkerAt(), ChestAt(), Stone, 10), WorkerPort(body), ChestPort(chest));

        Assert.Equal(TransferOutcome.Uncertain, receipt.Outcome);
        Assert.Contains("could not be written to his body", receipt.Evidence);
        Assert.True(core.Ledger.HasUncertainTransfer(Order));

        // And a second attempt is refused rather than replayed.
        body.View.Zdo.FailWrites = false;
        Assert.Equal(
            TransferOutcome.Refused,
            core.Executor.Execute(Intent(core, WorkerAt(), ChestAt(), Stone, 10), WorkerPort(body), ChestPort(chest)).Outcome);
    }

    // ------------------------------------------------------------------
    // What the worker can take
    // ------------------------------------------------------------------

    [Fact]
    public void TheCarryBudgetAndTheRealFitBothLimitHim()
    {
        Prefab(Stone.PrefabName, maxStack: 50, weight: 2f);
        WorkerBody body = Body();
        WorkerInventoryPort port = WorkerPort(body, carryWeight: 10f);

        // 10 units of budget at 2.0 each.
        Assert.Equal(5, port.CanAccept(Stone, 100));

        body.Inventory!.AddItem(Stack(Stone, 3));
        Assert.Equal(2, port.CanAccept(Stone, 100));
    }

    [Fact]
    public void AnIssuedToolIsNotMaterialAndNeverCountsAgainstTheBudget()
    {
        Prefab(Stone.PrefabName, maxStack: 50, weight: 2f);
        WorkerBody body = Body();
        body.Inventory!.AddItem(Axe());
        WorkerInventoryPort port = WorkerPort(body, carryWeight: 10f);

        // The axe weighs 4, and none of it comes out of the material budget.
        Assert.Equal(5, port.CanAccept(Stone, 100));
        Assert.Equal(0, port.Count(Stone));
        Assert.False(EngineInventoryPort.Matches(body.Inventory!.GetAllItems()[0], Stone));
    }

    [Fact]
    public void ACheatedStackIsNotRoomForHonestMaterial()
    {
        // 1.0.14 merges only into a stack with the same cheated flag, so its
        // room is not room for gathered material.
        Container chest = Chest(width: 1, height: 1);
        ItemDrop.ItemData cheated = Stack(Stone, 1, maxStack: 20);
        cheated.m_cheated = true;
        chest.Bag.AddItem(cheated);

        Assert.Equal(0, ChestPort(chest).CanAccept(Stone, 5));
    }

    [Fact]
    public void AnItemOfAnotherQualityOrVariantIsAnotherItem()
    {
        Container chest = Chest();
        ItemDrop.ItemData other = Stack(Stone, 7);
        other.m_quality = 2;
        chest.Bag.AddItem(other);

        Assert.Equal(0, ChestPort(chest).Count(Stone));
        Assert.Equal(7, ChestPort(chest).Count(new MaterialItem(Stone.PrefabName, 2, 0)));
    }

    // ------------------------------------------------------------------
    // Refusals, one reason each
    // ------------------------------------------------------------------

    [Fact]
    public void AChestRefusesForOneStatedReasonAtATime()
    {
        Container chest = Chest();
        Assert.Null(ChestPort(chest).Unavailable);

        chest.Owner = false;
        Assert.Equal("not owned here", ChestPort(chest).Unavailable);
        chest.Owner = true;

        chest.InUse = true;
        Assert.Equal("in use", ChestPort(chest).Unavailable);
        chest.InUse = false;

        // A chest that is a cart's container is strictly in use while the cart
        // is attached: the delivery chest carve-out does not apply here.
        Vagon cart = Cart();
        chest.m_wagon = cart;
        cart.Attached = true;
        Assert.Equal("in use", ChestPort(chest).Unavailable);
        chest.m_wagon = null;

        chest.m_checkGuardStone = true;
        PrivateArea.Access = false;
        Assert.Equal("access denied", ChestPort(chest).Unavailable);
        PrivateArea.Access = true;
        chest.m_checkGuardStone = false;

        chest.m_privacy = Container.PrivacySetting.Private;
        chest.GetComponent<Piece>()!.Creator = 7L;
        Assert.Equal("access denied", ChestPort(chest).Unavailable);
        chest.GetComponent<Piece>()!.Creator = Game.instance!.GetPlayerProfile().GetPlayerID();
        Assert.Null(ChestPort(chest).Unavailable);

        chest.transform.position = new Vector3(50f, 0f, 0f);
        Assert.Equal("out of reach", ChestPort(chest).Unavailable);
    }

    [Fact]
    public void AChestThatIsNoLongerTheSameChestIsRefused()
    {
        Container chest = Chest();
        var port = new ContainerInventoryPort(chest, "0000000000000009:00000009", () => Vector3.zero, 3f);

        Assert.Equal("not the same chest", port.Unavailable);
    }

    [Fact]
    public void ACartRefusesWhenThePlayerHasItAndAllowsTheHaulerToHoldIt()
    {
        Vagon cart = Cart();
        var player = new GameObject("Player").Add(new Player());
        Player.m_localPlayer = player;
        var port = new CartInventoryPort(cart, _ => 0, () => Vector3.zero, 3f);

        Assert.Null(port.Unavailable);

        // Attached to the hauler: in use for vanilla, available here, which is
        // the ratified carve-out (CONTRACTS.md 5.2, C3).
        cart.Attached = true;
        cart.AttachedTo = null;
        Assert.True(cart.InUse());
        Assert.Null(port.Unavailable);

        // Attached to the local player: never.
        cart.AttachedTo = player;
        Assert.Equal("pulled by the player", port.Unavailable);
        cart.AttachedTo = null;

        // Somebody has it open.
        cart.m_container!.InUse = true;
        Assert.Equal("open", port.Unavailable);
    }

    [Fact]
    public void WithoutABaselineNothingInTheCartCanBeCountedOrTaken()
    {
        Vagon cart = Cart();
        cart.m_container!.Bag.AddItem(Stack(Stone, 12));

        var blind = new CartInventoryPort(cart, _ => null, () => Vector3.zero, 3f);
        Assert.Equal(0, blind.Count(Stone));

        var recorded = new CartInventoryPort(cart, _ => 5, () => Vector3.zero, 3f);
        Assert.Equal(7, recorded.Count(Stone));
    }

    [Fact]
    public void YouAreOnlyAPlaceWhileYouAreAliveAndWithinReach()
    {
        var player = new GameObject("Player").Add(new Player());
        Player.m_localPlayer = player;
        var port = new PlayerInventoryPort(player, () => Vector3.zero, 4f);

        Assert.True(port.IsAvailable);

        player.transform.position = new Vector3(0f, 0f, 9f);
        Assert.False(port.IsAvailable);

        player.transform.position = Vector3.zero;
        player.Dead = true;
        Assert.False(port.IsAvailable);
    }

    [Fact]
    public void AWorkerWithNoBodyNoOwnershipOrAnUnwrittenChangeIsNotAPlace()
    {
        WorkerBody body = Body();
        Assert.True(WorkerPort(body).IsAvailable);

        body.View!.Owner = false;
        body.Inventory!.AddItem(Stack(Stone, 1));
        Assert.False(body.LastChangePersisted);
        Assert.False(WorkerPort(body).IsAvailable);
    }
}
