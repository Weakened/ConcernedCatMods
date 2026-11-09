using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's cart-assisted collection tour (#381, owner decision
/// 2026-10-31, `DECISIONS.md` <b>D22</b>): the same survey, plan and walking as
/// the on-foot tour, with every load taken to the cart the player assigned him
/// instead of to a chest, and put into its container through the pinned
/// <see cref="GunnarCartLoadPort"/>, measured.
///
/// <b>The cart stands still.</b> He fills a cart parked inside the area,
/// unhitched, with the hauling runtime doing nothing with it. He never hitches
/// it during a tour, so the tour needs nothing of D4's pulling; hauling the full
/// cart to a chest is the player's existing <c>ct_haul go</c> once the tour is
/// over, through the hauling executor exactly as before. What is in the cart
/// stays in the cart: nothing here takes anything out of it.
///
/// <b>Only what this tour picked.</b> He starts empty-handed of collected
/// material, and a load offers at most what this tour has picked and not yet
/// loaded, so nothing he carried before - a stone from an earlier session, what
/// a chest take gave him - goes into the cart.
///
/// <b>Every gate, every frame and every move.</b> The four switches, the world,
/// the work-authority rule, the probe, his body and its record are asked at the
/// start, every frame, every tick and in the frame of every load; the cart is
/// watched for the same things (still assigned, still the same cart, resolved,
/// idle, unhitched, where it stood); and the port asks the cart itself through
/// the shared permit mint in the call that moves anything.</summary>
internal sealed partial class GunnarCollectionRuntime
{
    private readonly GunnarCartLoadPort _cartLoad = new GunnarCartLoadPort();

    /// <summary>What this tour has picked and not yet loaded, per material.
    /// Only these units are ever offered to the cart.</summary>
    private readonly Dictionary<string, int> _tourCartPicked = new Dictionary<string, int>(StringComparer.Ordinal);

    private readonly List<KeyValuePair<string, int>> _tourCartOffer = new List<KeyValuePair<string, int>>();

    private Func<AssignedCartFacts> _assignedCart = () => default;
    private Func<Vagon?> _assignedVagon = () => null;
    private Func<Vagon?, NpcContainerUse> _cartAllowance = _ => NpcContainerUse.Off;

    /// <summary>The cart this tour fills, fixed at its start: the lease's key,
    /// the cart's own network id, where it stood, and the world load. Empty for
    /// a chest tour and between tours.</summary>
    private CartKey _tourCart;
    private string _tourCartId = string.Empty;
    private WorkPoint _tourCartAt;
    private NpcWorldEpoch _tourCartEpoch;

    /// <summary>Whether the running tour fills a cart.</summary>
    private bool CartTourRunning => _tour != null && !_tourCart.IsEmpty;

    /// <summary>Connects the hauling runtime's view of the cart assigned to
    /// him. Unconnected, there is no cart: the tour refuses.</summary>
    internal void ConnectCart(
        Func<AssignedCartFacts> assignedCart,
        Func<Vagon?> assignedVagon,
        Func<Vagon?, NpcContainerUse> allowance)
    {
        _assignedCart = assignedCart ?? throw new ArgumentNullException(nameof(assignedCart));
        _assignedVagon = assignedVagon ?? throw new ArgumentNullException(nameof(assignedVagon));
        _cartAllowance = allowance ?? throw new ArgumentNullException(nameof(allowance));
    }

    /// <summary>Teamster's master switch, collection, hauling and the cart
    /// tour's own switch, all four. An unreadable setting is not an opted-in
    /// one.</summary>
    private bool CartTourOptedIn()
    {
        try
        {
            return OptedIn() && _settings.GunnarHaulingEnabled.Value && _settings.GunnarCartTourEnabled.Value;
        }
        catch
        {
            return false;
        }
    }

    private WorkAuthorityVerdict CartTourAuthority() =>
        WorkAuthorityPolicy.Evaluate(GunnarWorkAuthority.ReadWorldFacts(CartTourOptedIn()));

    private AssignedCartFacts AssignedCartNow()
    {
        try
        {
            return _assignedCart();
        }
        catch (Exception)
        {
            // Not knowing is no cart: a load never goes into a cart on a guess.
            return default;
        }
    }

    private CartTourRefusal WatchTourCart() => CartTourGate.Watch(AssignedCartNow(), _tourCart, _tourCartAt);

    /// <summary>Why the running tour must stop because of a cart, or empty. A
    /// cart tour stops when its cart stops being the parked cart it began with;
    /// a chest tour stops when any cart is assigned to him.</summary>
    private string TourCartStop()
    {
        if (_tour == null)
        {
            return string.Empty;
        }

        if (!CartTourRunning)
        {
            return CartHeld() ? "a cart was assigned to him while he was on the tour" : string.Empty;
        }

        CartTourRefusal watch = WatchTourCart();
        return watch == CartTourRefusal.None ? string.Empty : CartTourGate.Describe(watch, CartTourAuthority());
    }

    private void ForgetTourCart()
    {
        _tourCart = default;
        _tourCartId = string.Empty;
        _tourCartAt = default;
        _tourCartEpoch = NpcWorldEpoch.Unknown;
        _tourCartPicked.Clear();
        _tourCartOffer.Clear();
    }

    /// <summary>A pick of this cart tour reached him: those units, and only
    /// those, may go into the cart.</summary>
    private void NoteCartTourPick(string material, int units)
    {
        if (!CartTourRunning || string.IsNullOrEmpty(material) || units <= 0)
        {
            return;
        }

        _tourCartPicked.TryGetValue(material, out int already);
        _tourCartPicked[material] = already + units;
    }

    /// <summary>What he carries, capped per material at what this tour picked
    /// and has not loaded yet. Reused list, so a trip allocates nothing here.
    /// </summary>
    private IReadOnlyList<KeyValuePair<string, int>> PickedByThisTour(IReadOnlyList<KeyValuePair<string, int>> carried)
    {
        _tourCartOffer.Clear();
        for (int index = 0; index < carried.Count; index++)
        {
            KeyValuePair<string, int> material = carried[index];
            if (!_tourCartPicked.TryGetValue(material.Key, out int picked) || picked <= 0 || material.Value <= 0)
            {
                continue;
            }

            _tourCartOffer.Add(new KeyValuePair<string, int>(material.Key, material.Value < picked ? material.Value : picked));
        }

        return _tourCartOffer;
    }

    // ------------------------------------------------------------------
    // ct_collect start cart
    // ------------------------------------------------------------------

    private string StartCartTour()
    {
        Humanoid? worker = _worker();
        NpcWorldEpoch world = CurrentEpoch();
        bool areaThisWorld = _area != null && !world.IsUnknown && world.Matches(_areaEpoch);
        AssignedCartFacts cart = AssignedCartNow();
        bool inside = areaThisWorld && cart.Resolved
            && _area!.Contains(new CollectionPoint(cart.Position.X, cart.Position.Y, cart.Position.Z));
        IReadOnlyList<KeyValuePair<string, int>> carrying =
            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable);
        int capacity = worker != null ? CapacityFor(worker, 2, out _tourLoadKilograms) : 0;

        var request = new CartTourRequest(
            cartTourEnabled: CartTourOptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: CartTourAuthority(),
            seamAvailable: _seamAvailable(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            workerPresent: worker != null && !worker.IsDead(),
            somethingInFlight: TourRunning || _ordered || _port.Phase == PickPhase.Gathering,
            cart: cart,
            areaSetThisWorld: areaThisWorld,
            cartInsideArea: inside,
            carriedIsReadable: readable,
            carryingCollected: carrying.Count > 0,
            capacityUnits: capacity);
        CartTourRefusal refusal = CartTourGate.EvaluateStart(request);
        if (refusal != CartTourRefusal.None)
        {
            return CartTourGate.Describe(refusal, request.Authority);
        }

        Vagon? assigned = AssignedVagonNow();
        string cartId = AssignedCartContainer.LiveKeyOf(assigned);
        if (assigned == null || cartId.Length == 0
            || !string.Equals(cartId, cart.Cart.SessionId, StringComparison.Ordinal)
            || AssignedCartContainer.BedOf(assigned) == null)
        {
            return "The cart assigned to him could not be read as a cart with a container, so no tour starts.";
        }

        WorkPoint standAt = cart.StandAt;
        string? refused = BeginTour(
            worker, world, capacity, "cart:" + cart.Cart, new Vector3(standAt.X, standAt.Y, standAt.Z),
            "the cart assigned to him", CollectionTourLimits.AtTheCart(_limits), refuseIfCarrying: true,
            out _, out string planSaid);
        if (refused != null)
        {
            return refused;
        }

        _tourCart = cart.Cart;
        _tourCartId = cartId;
        _tourCartAt = cart.Position;
        _tourCartEpoch = world;
        _tourCartPicked.Clear();
        return "He sets off: the whole area is surveyed and planned in loads of up to " +
            capacity.ToString(CultureInfo.InvariantCulture) + " unit(s), each walked in one round and put " +
            "into the cart assigned to him, standing at its handle. He never hitches it, never takes anything " +
            "out of it, and stops if it is hitched, opened, moved or given another job. 'ct_collect stop' ends " +
            "the tour; what he holds stays in him." + (planSaid.Length == 0 ? "" : " (" + planSaid + ".)");
    }

    private Vagon? AssignedVagonNow()
    {
        try
        {
            return _assignedVagon();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>One material, one permit, one measured leg, through the pinned
    /// cart-load port, with every D22 gate on the cart re-asked by the port in
    /// the frame of the move. An uncertain leg stops the run where it stands.
    /// </summary>
    private DepositTally CartLoadLegs(
        Humanoid? worker, IReadOnlyList<KeyValuePair<string, int>> offered, StringBuilder said)
    {
        NpcWorldEpoch world = CurrentEpoch();
        if (world.IsUnknown || !world.Matches(_tourCartEpoch))
        {
            string why = "The world has been loaded again since the tour began, so the cart's name no longer " +
                "means the same object.";
            said.Append(why);
            return new DepositTally(0, 0, false, why);
        }

        // Copied first: the legs below change what he carries, and the offer list
        // is rebuilt from that.
        var legsToRun = new KeyValuePair<string, int>[offered.Count];
        for (int index = 0; index < offered.Count; index++)
        {
            legsToRun[index] = offered[index];
        }

        int legs = 0;
        int moved = 0;
        bool anyPartial = false;
        bool anyRefused = false;
        for (int index = 0; index < legsToRun.Length; index++)
        {
            KeyValuePair<string, int> material = legsToRun[index];
            DepositResult result = _cartLoad.LoadOne(
                worker,
                AssignedVagonNow(),
                _tourCartId,
                material.Key,
                material.Value,
                _tourCartEpoch,
                world,
                _cartAllowance,
                WorkerPosition,
                new Vector3(_tourCartAt.X, _tourCartAt.Y, _tourCartAt.Z),
                _limits.PickupReachMetres,
                CartTourGate.MostDriftMetres,
                out NpcContainerRefusal why);

            _account.RecordCartLoad(NextStep("cart-load"), result);
            legs++;
            anyPartial |= result.Outcome == DepositOutcome.PartlyDeposited;
            anyRefused |= result.Outcome == DepositOutcome.Refused;
            if (result.Outcome == DepositOutcome.Deposited || result.Outcome == DepositOutcome.PartlyDeposited)
            {
                for (int part = 0; part < result.Moved.Count; part++)
                {
                    int units = result.Moved[part].Value > 0 ? result.Moved[part].Value : 0;
                    moved += units;
                    SpendCartTourPick(result.Moved[part].Key, units);
                }
            }

            said.Append(legs == 1 ? string.Empty : " ").Append(DescribeLoad(material.Key, result, why));

            if (result.Outcome == DepositOutcome.Uncertain)
            {
                said.Append(" Nothing after this was attempted, and nothing is put right automatically - a " +
                    "person decides.");
                return new DepositTally(legs, moved, true, string.Empty, anyPartial, anyRefused);
            }
        }

        return new DepositTally(legs, moved, false, string.Empty, anyPartial, anyRefused);
    }

    private void SpendCartTourPick(string material, int units)
    {
        if (units <= 0 || !_tourCartPicked.TryGetValue(material, out int picked))
        {
            return;
        }

        int left = picked - units;
        if (left > 0)
        {
            _tourCartPicked[material] = left;
        }
        else
        {
            _tourCartPicked.Remove(material);
        }
    }

    private static string DescribeLoad(string material, DepositResult result, NpcContainerRefusal why)
    {
        switch (result.Outcome)
        {
            case DepositOutcome.Deposited:
                return "All the " + material + " went into the cart.";
            case DepositOutcome.PartlyDeposited:
                return Moved(result) + " " + material + " went into the cart; he keeps the rest.";
            case DepositOutcome.Uncertain:
                return "Whether the " + material + " went into the cart could not be established: " + result.Detail;
            default:
                return why == NpcContainerRefusal.None
                    ? "No " + material + " went into the cart: " + result.Detail
                    : "No " + material + " went into the cart. " + CartLoadSentences.Describe(why);
        }
    }
}
