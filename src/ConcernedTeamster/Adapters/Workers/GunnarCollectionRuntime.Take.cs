using System;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar takes a named item from a chest the player chose and marked
/// <c>Take</c> or <c>Both</c> (#381, owner decision 2026-10-31, `DECISIONS.md`
/// <b>D20</b>).
///
/// <b>Two explicit acts, never an inference.</b> The player points at the chest
/// (<c>ct_collect source</c>), which must already carry their mark, and then
/// names the one item (<c>ct_collect take &lt;item&gt; [count]</c>). There is no
/// nearest chest, no "everything", and no wildcard.
///
/// <b>Every gate is re-asked in the frame of the move.</b> This file asks the
/// switches, the world, the work-authority rule, the probe, his body and its
/// record, what else is in flight, an open tour plan, the source, the item and
/// the carry limit (<see cref="TakeOrderGate"/>); the pinned
/// <see cref="GunnarTakePort"/> then asks the container itself - the mark,
/// ownership, in-use, ward, privacy and reach - through the shared permit mint,
/// in the same call that moves anything.
///
/// <b>Nothing here moves him.</b> He has to be standing next to the chest, the
/// same as an ordered deposit.</summary>
internal sealed partial class GunnarCollectionRuntime
{
    private readonly GunnarTakePort _take = new GunnarTakePort();

    /// <summary>The chest the player chose to take from, and the world load they
    /// chose it in. <b>There is no fallback.</b></summary>
    private Container? _source;
    private string _sourceKey = string.Empty;
    private NpcWorldEpoch _sourceEpoch;

    /// <summary>Teamster's master switch, the collection switch and the take's
    /// own switch, all three. An unreadable setting is not an opted-in one.
    /// </summary>
    private bool TakeOptedIn()
    {
        try
        {
            return OptedIn() && _settings.GunnarChestTakeEnabled.Value;
        }
        catch
        {
            return false;
        }
    }

    private WorkAuthorityVerdict TakeAuthority() =>
        WorkAuthorityPolicy.Evaluate(GunnarWorkAuthority.ReadWorldFacts(TakeOptedIn()));

    /// <summary>Chooses the chest the player is looking at as the one Gunnar
    /// takes from. The mark is checked here so a player finds out now, and again
    /// at the moment of every move, because they can change it in between.
    /// </summary>
    private string DesignateSource()
    {
        if (TourRunning)
        {
            return "He is on a tour. Stop it first (ct_collect stop).";
        }

        Container? chest = PointedChest();
        if (chest == null)
        {
            return "Look at the chest he should take from, then run this again. He never looks for the " +
                "nearest one.";
        }

        string key = GunnarDepositPort.KeyOf(chest);
        if (key.Length == 0)
        {
            return "That chest has no network record this client can name, so it cannot be chosen.";
        }

        NpcContainerUse allowed = Allowance(chest);
        if ((allowed & NpcContainerUse.Take) != NpcContainerUse.Take)
        {
            return TakeSentences.Describe(
                allowed == NpcContainerUse.Off
                    ? NpcContainerRefusal.NotEnabled
                    : NpcContainerRefusal.UseNotAllowed);
        }

        _source = chest;
        _sourceKey = key;
        _sourceEpoch = CurrentEpoch();
        return "He will take from that chest when you name the item (ct_collect take <item> [count]). The " +
            "mark on it is re-checked every time he actually takes something.";
    }

    /// <summary>Takes up to the asked-for count of one named item out of the
    /// chosen chest into Gunnar, through the pinned take port.</summary>
    private string TakeFromSource(string[]? args)
    {
        string cargo = args != null && args.Length > 1 ? args[1] ?? string.Empty : string.Empty;
        int wanted = int.MaxValue;
        if (args != null && args.Length > 2)
        {
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out wanted))
            {
                return "The count must be a whole number, for example ct_collect take Stone 20.";
            }
        }

        Humanoid? worker = _worker();
        bool cargoNamed = TakeOrderGate.IsCargoName(cargo);
        float? unitKilograms = cargoNamed ? UnitKilogramsOf(cargo) : null;
        int? fits = unitKilograms.HasValue && worker != null
            ? UnitsThatFit(worker, unitKilograms.Value, wanted)
            : null;

        var request = new TakeRequest(
            collectionEnabled: OptedIn(),
            takeEnabled: TakeOptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: TakeAuthority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            transferInFlight: _ordered || TourRunning || _port.Phase == PickPhase.Gathering,
            interruptedTourOpen: _tourPlan.BlocksOrders,
            sourceDesignated: _source != null && _sourceKey.Length != 0,
            cargo: cargo,
            cargoKnown: unitKilograms.HasValue,
            unitsWanted: wanted,
            unitsThatFit: fits);

        TakeRefusal refusal = TakeOrderGate.Evaluate(request);
        if (refusal != TakeRefusal.None)
        {
            return TakeSentences.Describe(refusal, request.Authority);
        }

        NpcWorldEpoch world = CurrentEpoch();
        if (world.IsUnknown || !world.Matches(_sourceEpoch))
        {
            // The chest was chosen in a world load that has ended, so the key
            // names whatever inherited that id this time. Refused and forgotten,
            // never resolved.
            ForgetSource("the world has been loaded again since you chose that chest");
            return "That chest was chosen in a different world load, so the name no longer means the same " +
                "object. Choose it again.";
        }

        if (!_lease.TryAcquire(_identity()))
        {
            return "Gunnar is busy with another job, or his job authority is unavailable. Finish or cancel " +
                "that job first.";
        }

        TakeResult result;
        NpcContainerRefusal why;
        try
        {
            result = _take.TakeOne(
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
                out why);
        }
        finally
        {
            _lease.Release();
        }

        // Refused and uncertain both acquire nothing: the chest still has it, or
        // nobody knows, and writing either down as fact is how material is
        // invented.
        _account.RecordTake(NextStep("take"), result);
        return DescribeTake(cargo, result, why);
    }

    private static string DescribeTake(string cargo, TakeResult result, NpcContainerRefusal why)
    {
        int moved = 0;
        for (int index = 0; index < result.Moved.Count; index++)
        {
            moved += result.Moved[index].Value > 0 ? result.Moved[index].Value : 0;
        }

        string count = moved.ToString(CultureInfo.InvariantCulture);
        switch (result.Outcome)
        {
            case TakeOutcome.Taken:
                return "He took " + count + " " + cargo + ".";
            case TakeOutcome.PartlyTaken:
                return "He took " + count + " " + cargo + "; the rest stays in the chest.";
            case TakeOutcome.Uncertain:
                return "Whether the " + cargo + " moved could not be established: " + result.Detail +
                    " Nothing is put right automatically - a person decides.";
            default:
                return why == NpcContainerRefusal.None
                    ? "No " + cargo + " was taken: " + result.Detail
                    : "No " + cargo + " was taken. " + TakeSentences.Describe(why);
        }
    }

    /// <summary>What one unit of an item weighs, from the game's own item
    /// database, or <c>null</c> when the game has no such item. A read, used for
    /// the carry limit; it builds nothing.</summary>
    private static float? UnitKilogramsOf(string cargo)
    {
        try
        {
            ObjectDB database = ObjectDB.instance;
            GameObject? prefab = database == null ? null : database.GetItemPrefab(cargo);
            ItemDrop? item = prefab == null ? null : prefab.GetComponent<ItemDrop>();
            if (item == null || item.m_itemData == null || item.m_itemData.m_shared == null)
            {
                return null;
            }

            float weight = item.m_itemData.m_shared.m_weight;
            return float.IsNaN(weight) || weight < 0f ? (float?)null : weight;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>How many units still fit under the carry limit: a vanilla
    /// player's base limit plus Megingjord's bonus (`DECISIONS.md` D17's load,
    /// whatever he wears), less everything he already carries. <c>null</c> when
    /// any of it could not be read, which the gate refuses.</summary>
    private int? UnitsThatFit(Humanoid worker, float unitKilograms, int wanted)
    {
        try
        {
            Player? player = Player.m_localPlayer;
            Inventory? inventory = worker.GetInventory();
            ObjectDB database = ObjectDB.instance;
            if (player == null || inventory == null || database == null)
            {
                return null;
            }

            float belt = 0f;
            GameObject? beltPrefab = database.GetItemPrefab("BeltStrength");
            ItemDrop? beltItem = beltPrefab == null ? null : beltPrefab.GetComponent<ItemDrop>();
            if (beltItem != null && beltItem.m_itemData.m_shared.m_equipStatusEffect is SE_Stats stats)
            {
                belt = stats.m_addMaxCarryWeight;
            }

            var facts = new CarryFacts(player.m_maxCarryWeight, belt, inventory.GetTotalWeight(), beltEquipped: belt > 0f);
            return CarryBudget.From(facts, _limits).HowManyFit(unitKilograms, wanted);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Forgets the source chest. Only when the name itself has stopped
    /// meaning anything - a chest that refused is still the chest the player
    /// chose.</summary>
    private void ForgetSource(string why)
    {
        _source = null;
        _sourceKey = string.Empty;
        _sourceEpoch = NpcWorldEpoch.Unknown;
        _log?.LogInfo("Gunnar's collection: the chest to take from was forgotten - " + why + ".");
    }
}
