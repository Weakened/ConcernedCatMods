using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The manual half of Gunnar's collection (#381 item 4): a haul from
/// the chest the player chose to take from to the chest they chose to put
/// things in, with a cargo filter they named. He works out a manifest first -
/// what can move, on-foot capacity, the number of tours and the loading order
/// (<see cref="HaulManifestPlanner"/>) - and runs each tour as provision,
/// travel, unload, reconcile (<see cref="ChestHaul"/>), both game-free and
/// tested.
///
/// <b>Nothing new reaches the world.</b> Every take is the pinned
/// <see cref="GunnarTakePort"/>'s (D20) and every unload the pinned
/// <see cref="GunnarDepositPort"/>'s (D15), each behind its own gate re-asked
/// here in the frame of the move and then the port's own permit mint (the
/// player's mark on that exact chest, ownership, in-use, ward, privacy, reach).
/// Movement is his own body walking through the vanilla motor - the own-body
/// pathfinding D14.2 allows and D20 named for this leaf - and nothing else.
///
/// <b>What it does not do</b>, each a boundary rather than an omission: no cart
/// (a cart round for a chest-to-chest haul needs its own owner decision - D22
/// loads only what a tour picked, and nothing unloads a cart); no chest he was
/// not pointed at; no resumption after a reload (nothing is written ahead
/// because nothing resumes: what he holds stays in his persisted inventory and
/// goes back through <c>ct_collect deposit &lt;item&gt;</c>).
///
/// <b>Off by default.</b> <c>Workers/GunnarChestHaulEnabled</c>, which also
/// needs collection and the chest take; the switches and the work-authority
/// rule are re-asked every frame while a haul runs.</summary>
internal sealed partial class GunnarCollectionRuntime
{
    private ChestHaul? _haul;
    private ZDOID _haulBody = ZDOID.None;
    private string _lastHaul = "none this session";
    private readonly HashSet<string> _haulCargo = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The three switches - master and collection, the take's, the
    /// haul's own. An unreadable setting is not an opted-in one.</summary>
    private bool HaulOptedIn()
    {
        try
        {
            return TakeOptedIn() && _settings.GunnarChestHaulEnabled.Value;
        }
        catch
        {
            return false;
        }
    }

    private WorkAuthorityVerdict HaulAuthority() =>
        WorkAuthorityPolicy.Evaluate(GunnarWorkAuthority.ReadWorldFacts(HaulOptedIn()));

    /// <summary><c>ct_collect haul plan &lt;cargo...&gt;</c> works the manifest
    /// out and says it; <c>ct_collect haul &lt;cargo...&gt;</c> works it out and
    /// starts. Cargo is one to four exact item names, each optionally
    /// <c>:count</c>; without a count, all of it the source holds now.</summary>
    private string Haul(string[]? args)
    {
        bool planOnly = args != null && args.Length > 1 &&
            string.Equals(args[1], "plan", StringComparison.OrdinalIgnoreCase);
        HaulPlanRefusal parsed = HaulCargoFilter.TryParse(
            args, planOnly ? 2 : 1, out List<KeyValuePair<string, int?>> lines, out string which);
        if (parsed != HaulPlanRefusal.None)
        {
            return HaulManifestPlanner.Describe(parsed, which);
        }

        Humanoid? worker = _worker();
        NpcWorldEpoch world = CurrentEpoch();
        var named = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Count; index++)
        {
            named.Add(lines[index].Key);
        }

        IReadOnlyList<KeyValuePair<string, int>> carrying =
            _deposit.Carrying(worker, named.Contains, out bool carriedReadable);

        var request = new ChestHaulRequest(
            collectionEnabled: OptedIn(),
            takeEnabled: TakeOptedIn(),
            haulEnabled: HaulOptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: HaulAuthority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            somethingInFlight: TourRunning || _ordered || _port.Phase == PickPhase.Gathering,
            interruptedTourOpen: _tourPlan.BlocksOrders,
            cartAssigned: CartHeld(),
            carriedReadable: carriedReadable,
            carryingCargo: carrying.Count > 0);

        ChestHaulRefusal refusal = ChestHaulGate.Evaluate(request);
        if (refusal != ChestHaulRefusal.None)
        {
            return ChestHaulGate.Describe(refusal, request.Authority);
        }

        // The two chests count only when chosen in this world load: a key from an
        // earlier load names whatever inherited that id, so it is offered as no
        // chest at all and the request gate says so.
        string sourceKey = _source != null && !world.IsUnknown && world.Matches(_sourceEpoch) ? _sourceKey : string.Empty;
        string destinationKey = _destination != null && !world.IsUnknown && world.Matches(_destinationEpoch)
            ? _destinationKey
            : string.Empty;
        bool sourceHere = sourceKey.Length != 0;
        var cargo = new List<HaulCargo>(lines.Count);
        for (int index = 0; index < lines.Count; index++)
        {
            string item = lines[index].Key;
            int? inSource = sourceHere ? SourceHolds(item) : 0;
            if (!inSource.HasValue)
            {
                return "The chest to take from could not be counted, so nothing was planned.";
            }

            cargo.Add(new HaulCargo(
                item, UnitKilogramsOf(item) ?? 0f, StackSizeOf(item) ?? 0, lines[index].Value, inSource.Value));
        }

        if (!OnFootCapacity(worker!, out CarryBudget carry, out int freeSlots))
        {
            return "How much Gunnar can carry could not be read, so nothing was planned.";
        }

        // The one answer to whether a manual haul may start: both chests named,
        // two different chests, the source marked for taking and the destination
        // for putting in (as recorded now - every move asks again), the filter
        // matching something, room somewhere. It also sets the loading order.
        // No cart is offered: a manual haul is on foot.
        HaulRequest admitted = HaulRequestGate.Check(
            UseOf(sourceHere ? Allowance(_source) : NpcContainerUse.Off),
            UseOf(destinationKey.Length != 0 ? Allowance(_destination) : NpcContainerUse.Off),
            sourceKey,
            destinationKey,
            HaulManifestPlanner.Offered(cargo),
            named,
            carry,
            CartCapacity.None);
        HaulManifest manifest = HaulManifestPlanner.Plan(
            admitted, cargo, new HaulCapacity(carry.FreeKilograms, freeSlots), CartCapacity.None);
        if (!manifest.IsPlanned)
        {
            return manifest.Describe();
        }

        string plan = "Haul manifest: " + manifest.Describe() + " On foot only: a cart round from chest to chest " +
            "is not authorized.";
        if (planOnly)
        {
            return plan + " Nothing moved; 'ct_collect haul' with the same cargo starts it.";
        }

        if (!_lease.TryAcquire(_identity()))
        {
            return "Gunnar is busy with another job, or his job authority is unavailable. Finish or cancel that job first.";
        }

        ZDOID body = BodyOf(worker);
        if (body == ZDOID.None)
        {
            _lease.Release();
            return "Gunnar's body has no network record this client can name, so no haul starts.";
        }

        Vector3 from = _source!.transform.position;
        Vector3 to = _destination!.transform.position;
        _haulCargo.Clear();
        foreach (string item in named)
        {
            _haulCargo.Add(item);
        }

        _haulBody = body;
        _lastDriveAt = Time.time;
        _haul = new ChestHaul(
            manifest,
            new CollectionPoint(from.x, from.y, from.z),
            new CollectionPoint(to.x, to.y, to.z),
            CollectionTourLimits.Default(_limits));
        _log?.LogInfo("Gunnar's collection: a haul from chest to chest started - " + manifest.Describe());
        return plan + " He sets off; 'ct_collect stop' ends it, and what he holds stays in him.";
    }

    /// <summary>Called from <see cref="DriveWorker"/> while a haul runs. True:
    /// the haul owns the body's motor this tick.</summary>
    private bool DriveHaul(TeamsterWorkerAI ai)
    {
        _lastDriveAt = Time.time;
        try
        {
            TickHaul(ai);
        }
        catch (Exception exception)
        {
            _log?.LogError("Gunnar's haul from chest to chest faulted and was stopped: " + SafeFailure.Describe(exception));
            CancelHaul("the haul faulted; the log says why");
            SafeHalt(ai);
        }

        return true;
    }

    private void TickHaul(TeamsterWorkerAI ai)
    {
        ChestHaul? haul = _haul;
        if (haul == null)
        {
            return;
        }

        string stop = HaulStop();
        Humanoid? worker = _worker();
        if (stop.Length == 0 && (worker == null || worker.IsDead() || BodyOf(ai) != _haulBody))
        {
            stop = "Gunnar's body is no longer the one the haul began with, or is not working";
        }

        if (stop.Length != 0)
        {
            CancelHaul(stop);
            SafeHalt(ai);
            return;
        }

        Vector3 position = ai.transform.position;
        HaulInstruction instruction = haul.Tick(new CollectionPoint(position.x, position.y, position.z), Time.time);
        switch (instruction.Order)
        {
            case HaulOrder.Walk:
                ai.WalkTo(
                    new Vector3(instruction.Target.X, instruction.Target.Y, instruction.Target.Z),
                    _limits.ArrivalToleranceMetres);
                break;
            case HaulOrder.Take:
                SafeHalt(ai);
                HaulLegEnd took = HaulTakeLeg(worker!, instruction.Item, instruction.Units, out int taken, out string tookSaid);
                haul.TakeEnded(took, taken, tookSaid);
                break;
            case HaulOrder.Unload:
                SafeHalt(ai);
                HaulLegEnd put = HaulUnloadLeg(worker!, instruction.Item, instruction.Units, out int moved, out string putSaid);
                haul.UnloadEnded(put, moved, putSaid);
                break;
            case HaulOrder.Reconcile:
                SafeHalt(ai);
                IReadOnlyList<KeyValuePair<string, int>> carried =
                    _deposit.Carrying(worker, _haulCargo.Contains, out bool readable);
                haul.Reconciled(readable ? carried : null);
                break;
            default:
                SafeHalt(ai);
                break;
        }

        if (haul.IsOver)
        {
            FinishHaul(haul);
        }
    }

    /// <summary>Provision: one item out of the source chest, through the pinned
    /// take port, behind D20's gate asked now. The haul is what is in flight, so
    /// it does not count against itself; anything else still does.</summary>
    private HaulLegEnd HaulTakeLeg(Humanoid worker, string cargo, int wanted, out int units, out string detail)
    {
        units = 0;
        float? unitKilograms = UnitKilogramsOf(cargo);
        int? fits = unitKilograms.HasValue ? UnitsThatFit(worker, unitKilograms.Value, wanted) : null;

        var request = new TakeRequest(
            collectionEnabled: OptedIn(),
            takeEnabled: HaulOptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: HaulAuthority(),
            seamAvailable: _seamAvailable(),
            workerPresent: !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            transferInFlight: _ordered || _tour != null || _port.Phase == PickPhase.Gathering,
            interruptedTourOpen: _tourPlan.BlocksOrders,
            sourceDesignated: _source != null && _sourceKey.Length != 0,
            cargo: cargo,
            cargoKnown: unitKilograms.HasValue,
            unitsWanted: wanted,
            unitsThatFit: fits);

        TakeRefusal refusal = TakeOrderGate.Evaluate(request);
        if (refusal != TakeRefusal.None)
        {
            detail = TakeSentences.Describe(refusal, request.Authority);
            return HaulLegEnd.Refused;
        }

        NpcWorldEpoch world = CurrentEpoch();
        if (world.IsUnknown || !world.Matches(_sourceEpoch))
        {
            ForgetSource("the world has been loaded again since you chose that chest");
            detail = "the chest to take from was chosen in a different world load";
            return HaulLegEnd.Refused;
        }

        TakeResult result = _take.TakeOne(
            _source,
            worker,
            _sourceKey,
            cargo,
            TakeOrderGate.UnitsToAsk(wanted, fits!.Value),
            _sourceEpoch,
            world,
            Allowance,
            WorkerPosition,
            _limits.PickupReachMetres,
            out NpcContainerRefusal why);

        // Refused and uncertain both acquire nothing in the ledger.
        _account.RecordTake(NextStep("haul-take"), result);
        detail = DescribeTake(cargo, result, why);
        switch (result.Outcome)
        {
            case TakeOutcome.Taken:
            case TakeOutcome.PartlyTaken:
                for (int index = 0; index < result.Moved.Count; index++)
                {
                    units += result.Moved[index].Value > 0 ? result.Moved[index].Value : 0;
                }

                return HaulLegEnd.Moved;
            case TakeOutcome.Uncertain:
                return HaulLegEnd.Uncertain;
            default:
                return HaulLegEnd.Refused;
        }
    }

    /// <summary>Unload: one item into the destination chest, through the pinned
    /// deposit port, behind D15's gate asked now - at most what this tour
    /// measured taking of it.</summary>
    private HaulLegEnd HaulUnloadLeg(Humanoid worker, string cargo, int wanted, out int units, out string detail)
    {
        units = 0;
        IReadOnlyList<KeyValuePair<string, int>> carried =
            _deposit.Carrying(worker, item => string.Equals(item, cargo, StringComparison.Ordinal), out bool readable);
        int holding = 0;
        for (int index = 0; index < carried.Count; index++)
        {
            holding += carried[index].Value;
        }

        var request = new DepositRequest(
            featureEnabled: HaulOptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: HaulAuthority(),
            seamAvailable: _seamAvailable(),
            workerPresent: !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            transferInFlight: _ordered || _tour != null || _port.Phase == PickPhase.Gathering,
            carryingSomething: holding > 0,
            destinationDesignated: _destination != null && _destinationKey.Length != 0,
            carriedIsReadable: readable,
            interruptedTourOpen: _tourPlan.BlocksOrders);

        DepositRefusal refusal = DepositOrderGate.Evaluate(request);
        if (refusal != DepositRefusal.None)
        {
            detail = DepositSentences.Describe(refusal, request.Authority);
            return refusal == DepositRefusal.Unreadable ? HaulLegEnd.Uncertain : HaulLegEnd.Refused;
        }

        int offered = holding < wanted ? holding : wanted;
        var said = new System.Text.StringBuilder();
        DepositTally tally = DepositLegs(
            worker, new[] { new KeyValuePair<string, int>(cargo, offered) }, said);
        detail = said.ToString();
        if (tally.Uncertain)
        {
            return HaulLegEnd.Uncertain;
        }

        units = tally.Moved;
        return tally.Moved > 0 ? HaulLegEnd.Moved : HaulLegEnd.Refused;
    }

    /// <summary>Why a running haul must stop now, or empty. Asked every tick
    /// and every frame.</summary>
    private string HaulStop()
    {
        if (_recordUnwritable() || _recordUnreadable())
        {
            return "Gunnar's body could not write down, or has not read, what he is carrying, so he is handed " +
                "nothing more; what he holds stays in him";
        }

        WorkAuthorityVerdict authority = HaulAuthority();
        if (authority != WorkAuthorityVerdict.Granted)
        {
            return WorkAuthorityPolicy.Describe(authority);
        }

        if (!_lease.IsHeld)
        {
            return "Gunnar no longer holds this job";
        }

        return CartHeld()
            ? "a cart was assigned to him, and a haul from chest to chest never walks him away from one"
            : string.Empty;
    }

    /// <summary>The haul runs on frames too: a body that stops ticking, a switch
    /// turned off, a peer connecting or a cart assigned ends it from here.
    /// </summary>
    private void WatchHaulFromFrame()
    {
        if (_haul == null)
        {
            return;
        }

        string stop = HaulStop();
        if (stop.Length != 0)
        {
            CancelHaul(stop);
        }
        else if (Time.time - _lastDriveAt > TourBodySilenceSeconds)
        {
            CancelHaul("Gunnar's body stopped working (it died, unloaded or is no longer the one bound)");
        }
    }

    private void FinishHaul(ChestHaul haul)
    {
        if (!ReferenceEquals(_haul, haul))
        {
            return;
        }

        _lastHaul = haul.Describe();
        _log?.LogInfo("Gunnar's haul from chest to chest " +
            (haul.Phase == ChestHaulPhase.Finished ? "finished" : "stopped") + ": " + _lastHaul);
        _haul = null;
        _haulBody = ZDOID.None;
        _haulCargo.Clear();
        _lease.Release();
    }

    /// <summary>Ends a running haul. What he holds stays in him; nothing is
    /// dropped, moved back or assumed to be anywhere else.</summary>
    private void CancelHaul(string why)
    {
        ChestHaul? haul = _haul;
        if (haul == null)
        {
            return;
        }

        try
        {
            haul.Cancel(why);
        }
        finally
        {
            FinishHaul(haul);
        }
    }

    /// <summary>The world went away: both chests' names mean nothing now, and
    /// the haul ends with them. Nothing is written as the world goes.</summary>
    private void DropHaulForWorld() => CancelHaul("the world went away");

    private string StopHaul()
    {
        if (_haul == null)
        {
            return "He is not on a haul.";
        }

        CancelHaul("you stopped it");
        return "Stopped. " + _lastHaul + ". Whatever he took and did not put in is still on him; " +
            "'ct_collect deposit <item>' at a chest you chose puts it down.";
    }

    private string HaulStatus() =>
        _haul == null
            ? "no haul running (last: " + _lastHaul + ")"
            : "hauling from chest to chest: " + _haul.Phase + ", tour " +
              (_haul.Tour + 1).ToString(CultureInfo.InvariantCulture) + ", " + _haul.Describe();

    /// <summary>How many of <paramref name="item"/> the chosen source chest
    /// holds, counted the way the take port counts (unequipped stacks of that
    /// prefab). A read for the manifest only; the take measures again.</summary>
    private int? SourceHolds(string item)
    {
        try
        {
            Inventory? inventory = _source == null ? null : _source.GetInventory();
            if (inventory == null)
            {
                return null;
            }

            int total = 0;
            foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
            {
                if (stack != null && stack.m_stack > 0 && !stack.m_equipped && stack.m_dropPrefab != null &&
                    string.Equals(stack.m_dropPrefab.name, item, StringComparison.Ordinal))
                {
                    total += stack.m_stack;
                }
            }

            return total;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>How many of an item share a slot, from the game's own item
    /// database, or <c>null</c>.</summary>
    private static int? StackSizeOf(string item)
    {
        try
        {
            ObjectDB database = ObjectDB.instance;
            GameObject? prefab = database == null ? null : database.GetItemPrefab(item);
            ItemDrop? drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
            int size = drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null
                ? 0
                : drop.m_itemData.m_shared.m_maxStackSize;
            return size > 0 ? size : (int?)null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The player's mark in this layer's words, flag by flag.</summary>
    private static ContainerUse UseOf(NpcContainerUse use) =>
        ((use & NpcContainerUse.Take) == NpcContainerUse.Take ? ContainerUse.Take : ContainerUse.Off) |
        ((use & NpcContainerUse.Deposit) == NpcContainerUse.Deposit ? ContainerUse.Deposit : ContainerUse.Off);

    /// <summary>One tour's room on foot: the weight left under D17's carry limit
    /// (a vanilla player's base plus Megingjord's bonus, less everything he
    /// carries) and his free slots. False for anything unreadable.</summary>
    private bool OnFootCapacity(Humanoid worker, out CarryBudget carry, out int freeSlots)
    {
        carry = default;
        freeSlots = 0;
        try
        {
            Player? player = Player.m_localPlayer;
            Inventory? inventory = worker.GetInventory();
            ObjectDB database = ObjectDB.instance;
            if (player == null || inventory == null || database == null)
            {
                return false;
            }

            float belt = 0f;
            GameObject? beltPrefab = database.GetItemPrefab("BeltStrength");
            ItemDrop? beltItem = beltPrefab == null ? null : beltPrefab.GetComponent<ItemDrop>();
            if (beltItem != null && beltItem.m_itemData.m_shared.m_equipStatusEffect is SE_Stats stats)
            {
                belt = stats.m_addMaxCarryWeight;
            }

            var facts = new CarryFacts(player.m_maxCarryWeight, belt, inventory.GetTotalWeight(), beltEquipped: belt > 0f);
            carry = CarryBudget.From(facts, _limits);
            freeSlots = inventory.GetEmptySlots();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
