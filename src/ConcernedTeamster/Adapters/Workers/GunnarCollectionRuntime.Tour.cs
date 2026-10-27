using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The automatic half of Gunnar's collection (#381): survey a bounded
/// area, plan the whole job in loads he can carry, walk each load along an
/// ordered route picking as he goes, take the load to the one chest the player
/// chose, measure it in, and the next load.
///
/// <b>Who decides what.</b> The survey and the eligibility rules are Teamster's
/// (<see cref="GunnarCollectionSurvey"/>, <see cref="GunnarTargetPredicate"/>);
/// how the job splits into loads and in what order each load is walked is the
/// shared runtime's job driver; that a load ends at the chest is
/// <see cref="CollectionTour"/>'s, game-free and tested. This file is the
/// glue: it reads the world, hands the tour its facts, and carries out its
/// orders through the same two ports the ordered pick and deposit already use.
///
/// <b>Nothing new reaches the world.</b> Every pick is the pinned
/// <see cref="GunnarCollectionPort"/>'s; every deposit is the pinned
/// <see cref="GunnarDepositPort"/>'s, with all nine D15 gates re-asked in the
/// frame of each move. Movement is his own body walking through the vanilla
/// motor - the pathfinding of his own body the Teamster carve-out allows - and
/// nothing else: no teleport, no portal, no cart.
///
/// <b>Off by default, and bounded.</b> The same switch, master switch, probe
/// and work-authority rule as the ordered pick, re-asked every frame while a tour
/// runs; one tour at a time; never while a cart is assigned to him.</summary>
internal sealed partial class GunnarCollectionRuntime
{
    private readonly GunnarCollectionSurvey _survey = new GunnarCollectionSurvey(CollectionLimits.Default.Validate());

    private CollectionArea? _area;
    private NpcWorldEpoch _areaEpoch;
    private int _areaRevision;
    private int _tourCounter;

    private CollectionTour? _tour;
    private GunnarTourPump? _tourPump;
    private bool _tourPicking;
    private string _tourPickYield = string.Empty;
    private string _tourPickSource = string.Empty;
    private float _tourLoadKilograms;
    private string _lastTour = "none this session";
    private ZDOID _tourBody = ZDOID.None;
    private float _lastDriveAt;

    /// <summary>How long a tour may go without its body's worker tick before it
    /// is ended: a body that died, unloaded or stopped being Gunnar's stops
    /// ticking, and the tour must not hold his job for ever.</summary>
    private const float TourBodySilenceSeconds = 5f;

    /// <summary>What one round of a tour may spend looking and planning: a full
    /// survey's worth of candidates in the snapshot and again in the partition,
    /// with room to spare, rather than exactly at the library's default.</summary>
    private static readonly int TourAllowance = (4 * CollectionLimits.Default.MostCandidatesPerSurvey) + 128;

    private Func<bool> _cartHeld = () => true;

    /// <summary>Whether a tour is running. The ordered pick and deposit refuse
    /// while one is.</summary>
    private bool TourRunning => _tour != null;

    /// <summary>Called from the hauling runtime's 20 Hz worker tick for the bound
    /// body. True while a tour owns the body's motor this tick - the hauling
    /// executor is not ticked then, so the two never steer him at once. False
    /// otherwise, and the executor ticks exactly as it always has.</summary>
    internal bool DriveWorker(TeamsterWorkerAI ai)
    {
        if (_tour == null || _faulted)
        {
            return false;
        }

        _lastDriveAt = Time.time;
        try
        {
            TickTour(ai);
        }
        catch (Exception exception)
        {
            _log?.LogError("Gunnar's collection tour faulted and was stopped: " + SafeFailure.Describe(exception));
            CancelTour("the tour faulted; the log says why");
            SafeHalt(ai);
        }

        return true;
    }

    private void TickTour(TeamsterWorkerAI ai)
    {
        CollectionTour? tour = _tour;
        if (tour == null)
        {
            return;
        }

        // The containment of GUNNAR_COLLECTION.md §6c: a body whose last change
        // did not reach its record, or that never read its record, is handed
        // nothing more. Asked by name, before anything else, so the sentence is
        // the true one.
        if (_recordUnwritable() || _recordUnreadable())
        {
            CancelTour("Gunnar's body could not write down, or has not read, what he is carrying, so he is " +
                "handed nothing more; what he holds stays in him");
            SafeHalt(ai);
            return;
        }

        Humanoid? worker = _worker();
        if (worker == null || worker.IsDead() || BodyOf(ai) != _tourBody)
        {
            CancelTour("Gunnar's body is no longer the one the tour began with, or is not working");
            SafeHalt(ai);
            return;
        }

        if (!_lease.IsHeld)
        {
            CancelTour("Gunnar no longer holds this collection job");
            SafeHalt(ai);
            return;
        }

        if (_tourPicking)
        {
            SafeHalt(ai);
            PollTourPick(tour);
            return;
        }

        Vector3 position = ai.transform.position;
        TourInstruction instruction = tour.Tick(new CollectionPoint(position.x, position.y, position.z), Time.time);
        switch (instruction.Order)
        {
            case TourOrder.Walk:
                ai.WalkTo(
                    new Vector3(instruction.Target.X, instruction.Target.Y, instruction.Target.Z),
                    _limits.ArrivalToleranceMetres);
                break;
            case TourOrder.Pick:
                SafeHalt(ai);
                BeginTourPick(tour, worker, instruction.Key);
                break;
            case TourOrder.Deposit:
                SafeHalt(ai);
                tour.DepositEnded(DepositForTour(worker, out string detail), detail);
                break;
            default:
                SafeHalt(ai);
                break;
        }

        if (tour.IsOver)
        {
            FinishTour(tour);
        }
    }

    private void BeginTourPick(CollectionTour tour, Humanoid worker, string key)
    {
        if (!_survey.TryResolve(key, out Pickable source, out string failure))
        {
            _log?.LogInfo("Gunnar's collection tour: skipped a source - " + failure + ".");
            tour.PickEnded(TourPickEnd.Refused, 0);
            return;
        }

        string yieldName = source.m_itemPrefab != null ? source.m_itemPrefab.name : string.Empty;
        string expected = CollectionOrderGate.PrefabNameOf(yieldName);
        int units = source.m_amount;
        PickRefusal refusal = _port.Begin(
            source,
            worker,
            featureEnabled: OptedIn(),
            seamAvailable: _seamAvailable(),
            expectedItemPrefab: expected,
            expectedUnits: units,
            nowSeconds: Time.time);

        switch (refusal)
        {
            case PickRefusal.None:
                _tourPicking = true;
                _tourPickYield = expected;
                _tourPickSource = CollectionOrderGate.PrefabNameOf(source.gameObject.name);
                return;
            case PickRefusal.FeatureOff:
            case PickRefusal.SeamUnavailable:
            case PickRefusal.NoLocalPlayer:
            case PickRefusal.NoWorker:
            case PickRefusal.WorkerHasNoRecord:
            case PickRefusal.Busy:
                // About Gunnar or the room, not the source: nothing further in
                // this tour could succeed.
                CancelTour("a pick was refused (" + refusal + "), so the tour stopped");
                return;
            default:
                // About this source: gone, owned elsewhere, awaiting the world's
                // confirmation, in tar, too crowded to count. Skipped, never
                // retried blind.
                tour.PickEnded(TourPickEnd.Refused, 0);
                return;
        }
    }

    private void PollTourPick(CollectionTour tour)
    {
        PickProgress progress = _port.Poll(Time.time);
        switch (progress.Phase)
        {
            case PickPhase.Gathering:
                return;
            case PickPhase.Done:
                _tourPicking = false;
                if (progress.Taken > 0 && _tourPickYield.Length != 0)
                {
                    _account.Record(NextStep("tour-pick"), _tourPickYield, progress.Taken, StopResult.Took());
                }

                tour.PickEnded(progress.Taken > 0 ? TourPickEnd.Took : TourPickEnd.NothingReached, progress.Taken);
                return;
            default:
                _tourPicking = false;
                _log?.LogInfo("Gunnar's collection tour: nothing reached him from " + _tourPickSource +
                    "; nothing is assumed about the source.");
                tour.PickEnded(TourPickEnd.NothingReached, 0);
                return;
        }
    }

    /// <summary>One trip's deposit: everything he collected, one material at a
    /// time, through the same path as <c>ct_collect deposit</c>. What decides
    /// "emptied" is a measurement of what he still holds afterwards, never the
    /// legs' own answers.</summary>
    private TourDepositEnd DepositForTour(Humanoid worker, out string detail)
    {
        IReadOnlyList<KeyValuePair<string, int>> carried =
            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable);

        // The same gate the ordered deposit asks - both switches, the world, the
        // work-authority rule, the probe, the body's record, a destination - in
        // the frame of the move. The tour itself is the thing in flight, so it
        // does not count against itself.
        var request = new DepositRequest(
            featureEnabled: OptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerPresent: !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            // The tour itself is what is moving material, so it does not count
            // against itself; anything else in flight still does.
            transferInFlight: _ordered || _port.Phase == PickPhase.Gathering,
            carryingSomething: carried.Count > 0,
            destinationDesignated: _destination != null && _destinationKey.Length != 0,
            carriedIsReadable: readable);
        DepositRefusal refusal = DepositOrderGate.Evaluate(request);
        switch (refusal)
        {
            case DepositRefusal.None:
                break;
            case DepositRefusal.NothingToDeposit:
                detail = string.Empty;
                return TourDepositEnd.Emptied;
            case DepositRefusal.Unreadable:
                detail = DepositSentences.Describe(refusal, request.Authority);
                return TourDepositEnd.Uncertain;
            default:
                detail = DepositSentences.Describe(refusal, request.Authority);
                return TourDepositEnd.Refused;
        }

        var said = new StringBuilder();
        DepositTally tally = DepositLegs(worker, carried, said);
        IReadOnlyList<KeyValuePair<string, int>> after =
            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readableAfter);
        detail = said.ToString();

        if (tally.Uncertain || !readableAfter)
        {
            return TourDepositEnd.Uncertain;
        }

        if (tally.Refused.Length != 0 && tally.Legs == 0)
        {
            detail = tally.Refused;
            return TourDepositEnd.Refused;
        }

        if (after.Count == 0)
        {
            return TourDepositEnd.Emptied;
        }

        // Something stayed in him. "Full" only when a leg measured the chest
        // taking part and no more; a leg the chest REFUSED (unmarked, opened,
        // warded between legs) is a refusal, and saying "full" would send the
        // player to the wrong fix.
        return tally.AnyPartial && !tally.AnyRefused ? TourDepositEnd.ChestFull : TourDepositEnd.Refused;
    }

    private void FinishTour(CollectionTour tour)
    {
        if (!ReferenceEquals(_tour, tour))
        {
            // Already finished by an earlier report in the same tick.
            return;
        }

        _lastTour = DescribeTour(tour);
        _log?.LogInfo("Gunnar's collection tour " + (tour.Phase == TourPhase.Finished ? "finished" : "stopped") +
            ": " + _lastTour);
        _tour = null;
        _tourPump = null;
        _tourPicking = false;
        try { _lifecycle.OrderEnded(); }
        finally { _lease.Release(); }
    }

    /// <summary>Ends a running tour for a reason that is not the world going
    /// away: the job verb, so an unconfirmed pick keeps its record.</summary>
    private void CancelTour(string why)
    {
        CollectionTour? tour = _tour;
        if (tour == null)
        {
            return;
        }

        try
        {
            tour.Cancel(why);
        }
        finally
        {
            FinishTour(tour);
        }
    }

    /// <summary>The world went away: every key the survey minted names nothing
    /// now, and the tour with it. The world verb has already run.</summary>
    private void DropTourForWorld()
    {
        CollectionTour? tour = _tour;
        _survey.Forget();
        if (tour == null)
        {
            return;
        }

        try
        {
            tour.Cancel("the world went away");
        }
        catch (Exception)
        {
            // The driver's world is gone with it; nothing more to give back.
        }

        _lastTour = DescribeTour(tour);
        _tour = null;
        _tourPump = null;
        _tourPicking = false;
        _lease.Release();
    }

    /// <summary>The tour runs on frames too: a body that stops ticking (died,
    /// unloaded, no longer bound) and a record that stops being trusted both end
    /// it from here, because neither produces another worker tick to end it from.
    /// </summary>
    private void WatchTourFromFrame()
    {
        if (_tour == null)
        {
            return;
        }

        if (_recordUnwritable() || _recordUnreadable())
        {
            CancelTour("Gunnar's body could not write down, or has not read, what he is carrying, so he is " +
                "handed nothing more; what he holds stays in him");
            return;
        }

        if (Time.time - _lastDriveAt > TourBodySilenceSeconds)
        {
            CancelTour("Gunnar's body stopped working (it died, unloaded or is no longer the one bound)");
        }
    }

    private static ZDOID BodyOf(Component? body)
    {
        ZNetView? view = body != null ? body.GetComponent<ZNetView>() : null;
        return view != null && view.IsValid() ? view.GetZDO().m_uid : ZDOID.None;
    }

    private static string DescribeTour(CollectionTour tour) =>
        tour.Picks.ToString(CultureInfo.InvariantCulture) + " source(s) picked over " +
        tour.Loads.ToString(CultureInfo.InvariantCulture) + " load(s), " +
        tour.Deposits.ToString(CultureInfo.InvariantCulture) + " trip(s) to the chest" +
        (tour.Reason.Length == 0 ? "" : "; " + tour.Reason) +
        (tour.CollectedSinceDeposit ? "; he is still carrying what he collected since the last trip" : "");

    private static void SafeHalt(TeamsterWorkerAI ai)
    {
        try
        {
            ai.Halt();
        }
        catch (Exception)
        {
            // Stopping is best-effort; the executor stops a body it finds moving.
        }
    }

    // ------------------------------------------------------------------
    // ct_collect area | start | stop
    // ------------------------------------------------------------------

    private string SetArea(string[]? args)
    {
        if (TourRunning)
        {
            return "He is on a tour of the area you set. Stop it first (ct_collect stop) - the area is never " +
                "changed under a tour that is working it.";
        }

        Player? player = Player.m_localPlayer;
        if (player == null)
        {
            return "No player is loaded.";
        }

        float radius = CollectionArea.DefaultRadiusMetres;
        if (args != null && args.Length > 1 &&
            !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius))
        {
            return "Usage: ct_collect area [radius in metres, " +
                CollectionArea.SmallestRadiusMetres.ToString("0", CultureInfo.InvariantCulture) + " to " +
                CollectionArea.LargestRadiusMetres.ToString("0", CultureInfo.InvariantCulture) + "].";
        }

        NpcWorldEpoch world = CurrentEpoch();
        if (world.IsUnknown)
        {
            return "No world is loaded.";
        }

        Vector3 at = player.transform.position;
        if (!CollectionArea.TryCreate(new CollectionPoint(at.x, at.y, at.z), radius, ++_areaRevision,
                out CollectionArea? area, out string said))
        {
            return "No area was set: " + said + ".";
        }

        _area = area;
        _areaEpoch = world;
        return "He will collect in " + said + ". He never looks further than this, and the area does " +
            "not move with anybody.";
    }

    private string StartTour()
    {
        Humanoid? worker = _worker();
        NpcWorldEpoch world = CurrentEpoch();
        int materials = 2;
        bool areaThisWorld = _area != null && !world.IsUnknown && world.Matches(_areaEpoch);
        bool chestThisWorld = _destination != null && _destinationKey.Length != 0 &&
            !world.IsUnknown && world.Matches(_destinationEpoch);
        NpcContainerUse allowed = chestThisWorld ? Allowance(_destination) : NpcContainerUse.Off;
        int capacity = worker != null ? CapacityFor(worker, materials, out _tourLoadKilograms) : 0;

        var request = new TourRequest(
            featureEnabled: OptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: Authority(),
            seamAvailable: _seamAvailable(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            workerPresent: worker != null && !worker.IsDead(),
            somethingInFlight: TourRunning || _ordered || _port.Phase == PickPhase.Gathering,
            cartAssigned: CartHeld(),
            areaSetThisWorld: areaThisWorld,
            destinationSetThisWorld: chestThisWorld,
            destinationAllowsDeposit: (allowed & NpcContainerUse.Deposit) == NpcContainerUse.Deposit,
            capacityUnits: capacity);

        TourRefusal refusal = CollectionTourGate.Evaluate(request);
        if (refusal != TourRefusal.None)
        {
            return CollectionTourGate.Describe(refusal, request.Authority);
        }

        if (!NpcIdentity.TryParse(WorkerKey.Gunnar.Value, out NpcIdentity identity))
        {
            return "Gunnar's identity could not be read, so no tour starts.";
        }

        if (!_lease.TryAcquire(_identity()))
        {
            return "Gunnar is busy with another job, or his job authority is unavailable. Finish or cancel that job first.";
        }

        GunnarTourPump? pump = null;
        try
        {
            CollectionArea area = _area!;
            var job = new GunnarCollectionJob(
                () => _survey.Look(area, _tourLoadKilograms),
                () => Array.Empty<SourceStock>(),
                key => _survey.Observe(key));
            NpcJobDriver StartJob() => NpcJobDriver.For(
                new NpcJobOrder(
                    identity,
                    "teamster-collect-" + (++_tourCounter).ToString(CultureInfo.InvariantCulture),
                    area,
                    world,
                    new NpcCarryCapacity(capacity),
                    new JobStepActions("take", "pick"),
                    allowance: TourAllowance),
                job,
                NpcRoleRegistry.Shared);
            pump = new GunnarTourPump(StartJob, () => job.LastLook);
            if (pump.Driver.Progress == NpcJobProgress.Stopped)
            {
                _lease.Release();
                return "The tour could not start: " + pump.Driver.Reason + ".";
            }

            IReadOnlyList<KeyValuePair<string, int>> carrying =
                _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable);
            if (!readable)
            {
                pump.Abandon();
                _lease.Release();
                return "What Gunnar is carrying could not be read, so no tour starts.";
            }

            Vector3 chest = _destination!.transform.position;
            _tourBody = BodyOf(worker);
            _lastDriveAt = Time.time;
            _tourPump = pump;
            _tour = new CollectionTour(
                _tourPump,
                CollectionTourLimits.Default(_limits),
                new CollectionPoint(chest.x, chest.y, chest.z),
                startWithDeposit: carrying.Count > 0);
            _tourPicking = false;
            return "He sets off: the whole area is surveyed and planned in loads of up to " +
                capacity.ToString(CultureInfo.InvariantCulture) + " unit(s), each walked in one round and " +
                "taken to the chest you chose in one trip" +
                (carrying.Count > 0 ? ", starting with what he is already carrying" : "") +
                ". 'ct_collect stop' ends it; what he holds stays in him.";
        }
        catch
        {
            // Every hold the start took is given back, the job's own included.
            try { pump?.Abandon(); }
            catch (Exception) { /* already failing */ }
            _tour = null;
            _tourPump = null;
            _lease.Release();
            throw;
        }
    }

    private string StopTour()
    {
        if (_tour == null)
        {
            return "He is not on a collection tour.";
        }

        CancelTour("you stopped it");
        return "Stopped. What he collected since the last trip to the chest is still on him; nothing was " +
            "dropped and nothing was assumed to be anywhere else.";
    }

    private string TourStatus() =>
        _tour == null
            ? "no tour running (last: " + _lastTour + ")"
            : "on a tour: " + _tour.Phase + ", " + DescribeTour(_tour) +
              (_area == null ? "" : "; " + _area);

    private bool CartHeld()
    {
        try
        {
            return _cartHeld();
        }
        catch (Exception)
        {
            // Not knowing means held: a tour never walks him off a cart on a guess.
            return true;
        }
    }

    /// <summary>How many units one load holds, from the game's own numbers: a
    /// vanilla player's base carry limit plus Megingjord's bonus, less whatever
    /// he holds that is not going in the chest; and his inventory's room. Zero
    /// for anything unreadable.</summary>
    private int CapacityFor(Humanoid worker, int materials, out float loadKilograms)
    {
        loadKilograms = 0f;
        try
        {
            Player? player = Player.m_localPlayer;
            Inventory? inventory = worker.GetInventory();
            ObjectDB database = ObjectDB.instance;
            if (player == null || inventory == null || database == null)
            {
                return 0;
            }

            float belt = 0f;
            ItemDrop? beltItem = Item(database, "BeltStrength");
            if (beltItem != null && beltItem.m_itemData.m_shared.m_equipStatusEffect is SE_Stats stats)
            {
                belt = stats.m_addMaxCarryWeight;
            }

            float heaviest = 0f;
            int smallestStack = int.MaxValue;
            foreach (string yield in new[] { GunnarCollectionAllowlist.StoneItemPrefab, GunnarCollectionAllowlist.WoodItemPrefab })
            {
                ItemDrop? item = Item(database, yield);
                if (item == null)
                {
                    return 0;
                }

                heaviest = Math.Max(heaviest, item.m_itemData.m_shared.m_weight);
                smallestStack = Math.Min(smallestStack, item.m_itemData.m_shared.m_maxStackSize);
            }

            float otherKilograms = 0f;
            int yieldSlots = 0;
            foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
            {
                string name = stack.m_dropPrefab != null ? stack.m_dropPrefab.name : string.Empty;
                if (GunnarCollectionAllowlist.IsCollectedYield(name))
                {
                    // Going in the chest before the first load, so not counted
                    // against it.
                    yieldSlots++;
                }
                else
                {
                    otherKilograms += stack.GetWeight();
                }
            }

            var facts = new CarryFacts(player.m_maxCarryWeight, belt, otherKilograms, beltEquipped: belt > 0f);
            loadKilograms = CarryBudget.From(facts, _limits).FreeKilograms;
            return CollectionCapacity.UnitsPerLoad(
                facts, _limits, heaviest, inventory.GetEmptySlots() + yieldSlots, smallestStack, materials);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static ItemDrop? Item(ObjectDB database, string prefab)
    {
        GameObject? found = database.GetItemPrefab(prefab);
        return found != null ? found.GetComponent<ItemDrop>() : null;
    }
}
