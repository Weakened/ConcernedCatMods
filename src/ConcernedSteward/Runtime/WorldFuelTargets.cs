using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>The fires, over the real game.
///
/// <b>This is the only Steward code that can destroy a player's item, and every
/// line of it is written against the decompiled 1.0.14 build</b>
/// (<c>docs/mods/concerned-steward/VALHEIM_FIRE_API_AUDIT.md</c>). The four
/// findings it exists to honour:
///
/// <list type="number">
/// <item><b>Only <c>Fireplace.UseItem</c>.</b> It is the one member that
/// consumes a real item for the fuel it adds. <c>AddFuel</c> and <c>SetFuel</c>
/// write the fuel level with nothing taken out of anybody's inventory — they
/// are resource conjuring, and they appear nowhere in this product.
/// <c>Interact</c> is also declined: it would call <c>ClaimOwnership</c>, and
/// it toggles the fire off instead of fuelling it when the fire can be turned
/// off.</item>
/// <item><b>Its answer proves nothing.</b> <c>UseItem</c> returns <c>true</c>
/// for "cannot add more" exactly as it does for a unit accepted. The return
/// value is deliberately discarded and the outcome comes from measured deltas
/// — the same rule the settlement custody executor reaches for transfers.</item>
/// <item><b>Ownership is a precondition, not a detail.</b> With the fire's ZDO
/// owned by <i>nobody</i>, vanilla removes the wood from the worker's inventory
/// and then <c>RPC_AddFuel</c>'s own <c>IsOwner</c> gate discards the fuel: the
/// item is destroyed. Owned by another peer, the mutation routes away and
/// nothing local is observable. Both are refused before the call, and
/// <see cref="FeedOneUnit"/> checks again immediately before it.</item>
/// <item><b>The item identity comes from the fire.</b>
/// <c>m_fuelItem.m_itemData.m_shared.m_name</c> is what vanilla compares
/// against, so it is what is carried and counted. Nothing here contains the
/// word "wood".</item>
/// </list>
///
/// <b>Why the two reads bracket exactly one mutation.</b> Vanilla's routed RPC
/// runs <i>inline</i> when this process owns the object, and the two-second
/// fuel decay is an <c>InvokeRepeating</c> that cannot run in the middle of a
/// call on a single-threaded engine. So reading the fuel, calling
/// <c>UseItem</c>, and reading it again is one synchronous step with nothing
/// else in it.</summary>
internal sealed class WorldFuelTargets : IFuelTargetPort
{
    /// <summary>How tall a settlement may be and still have its fires found.
    ///
    /// <c>Piece.GetAllPiecesInRadius</c> measures in three dimensions while a
    /// settlement area is a circle on the ground, so asking it for the
    /// settlement's own radius would silently miss a fire at the top of a
    /// tower. The query is widened to the diagonal and the domain then applies
    /// the real, horizontal containment test — over-collect, then filter
    /// exactly, rather than under-collect and never know.</summary>
    private const float VerticalReachMetres = 32f;

    /// <summary>A hard cap on what one survey will carry back, so the cost of a
    /// scan cannot grow with somebody's base. The domain applies its own,
    /// smaller bound after sorting; this one only stops an enormous list being
    /// built in the first place.</summary>
    private const int MaxSurveyed = 512;

    /// <summary>How close he has to be standing to put wood on a fire.
    ///
    /// <b>Vanilla does not check this and it matters.</b> Decompiling
    /// <c>Fireplace</c> turns up no distance test anywhere in <c>UseItem</c> or
    /// <c>Interact</c>: the only thing that stops a player fuelling a hearth
    /// across their base is <c>Player.m_maxInteractDistance</c>, applied earlier,
    /// at the hover targeting a modded worker never goes through. So without a
    /// check here the Steward could stand at the chest and light every fire in
    /// the settlement from where he stands — legitimate by every other rule in
    /// this product, and obviously a cheat to anybody watching.
    ///
    /// Five metres is vanilla's own default for that field, so the Steward is
    /// held to the reach the game holds a player to. The upkeep loop already
    /// refuses to feed before it has walked there; this is the second check, in
    /// the one place that actually performs the mutation.</summary>
    private const float ReachMetres = 5f;

    private readonly Func<string> _epoch;
    private readonly Func<Humanoid?> _body;
    private readonly Func<Vector3, bool> _accessGranted;
    private readonly Action<string>? _log;

    private readonly List<Piece> _pieces = new List<Piece>();

    internal WorldFuelTargets(
        Func<string> epoch,
        Func<Humanoid?> body,
        Func<Vector3, bool>? accessGranted = null,
        Action<string>? log = null)
    {
        _epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
        _body = body ?? throw new ArgumentNullException(nameof(body));

        // Default to vanilla's own ward check, and to refusing when it throws:
        // an unestablished permission is not a permission.
        _accessGranted = accessGranted ?? (point =>
        {
            try
            {
                return PrivateArea.CheckAccess(point, 0f, false, false);
            }
            catch (Exception)
            {
                return false;
            }
        });

        _log = log;
    }

    /// <summary>Every fire inside the marked settlement, as it is right now.
    ///
    /// Found through <c>Piece.GetAllPiecesInRadius</c>, which walks the pieces
    /// a player has built rather than the whole world. That is what keeps this
    /// from being the global scan #340 forbids, and the centre it is measured
    /// from is the <b>designated</b> settlement centre — a fixed point — never
    /// the player, whose movement would otherwise reshuffle the Steward's queue
    /// every time they crossed their own base.</summary>
    public IReadOnlyList<FuelTargetObservation> Survey(SitePoint settlementCentre, float settlementRadius)
    {
        var found = new List<FuelTargetObservation>();
        if (!(settlementRadius > 0f))
        {
            return found;
        }

        string epoch = SafeEpoch();
        if (epoch.Length == 0)
        {
            // No identity space means no key can be told from a stale one, so
            // nothing resolves. Returning an empty survey is that answer.
            return found;
        }

        _pieces.Clear();
        try
        {
            float queryRadius = (float)Math.Sqrt(
                ((double)settlementRadius * settlementRadius) +
                ((double)VerticalReachMetres * VerticalReachMetres));
            Piece.GetAllPiecesInRadius(ToVector3(settlementCentre), queryRadius, _pieces);
        }
        catch (Exception exception)
        {
            _log?.Invoke("The Steward could not list the settlement's pieces: " + SafeFailure.Describe(exception));
            return found;
        }

        int examined = 0;
        foreach (Piece piece in _pieces)
        {
            if (examined >= MaxSurveyed)
            {
                break;
            }

            if (piece == null)
            {
                continue;
            }

            // Fireplace.Awake reads its Piece off its own GameObject, so the two
            // are always on the same object. Looking in children would find a
            // fire belonging to a different piece.
            Fireplace? fireplace = piece.GetComponent<Fireplace>();
            if (fireplace == null)
            {
                continue;
            }

            examined++;
            if (TryObserve(fireplace, epoch, out FuelTargetObservation observation))
            {
                found.Add(observation);
            }
        }

        _pieces.Clear();
        return found;
    }

    public bool TryObserve(FuelTargetKey key, out FuelTargetObservation observation)
    {
        observation = default;
        string epoch = SafeEpoch();
        if (epoch.Length == 0 || !key.IsFrom(epoch))
        {
            return false;
        }

        Fireplace? fireplace = StewardIdentity.FindFireplace(key.Value);
        return fireplace != null && TryObserve(fireplace, epoch, out observation);
    }

    /// <summary>Puts one unit of this fire's own fuel item into it, and reports
    /// what was measured on both sides of the call.</summary>
    public FeedMeasurement FeedOneUnit(FuelTargetKey key, IItemStorePort carrier)
    {
        string epoch = SafeEpoch();
        if (epoch.Length == 0 || !key.IsFrom(epoch))
        {
            return Nothing("that fire was identified in an earlier session, so the Steward left it alone");
        }

        Fireplace? fireplace = StewardIdentity.FindFireplace(key.Value);
        if (fireplace == null)
        {
            return Nothing("that fire is no longer here");
        }

        ZNetView? view = fireplace.GetComponent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            return Nothing("that fire is not a live object any more");
        }

        // The audit's section 3, checked again at the last possible moment: the
        // scan and the revalidation both said this was ours, and the walk
        // between them was seconds long.
        if (!view.IsOwner())
        {
            return Nothing(
                "that fire stopped being this session's to touch, and adding fuel to it would " +
                "have destroyed the wood without lighting anything");
        }

        Humanoid? body = SafeBody();
        if (body == null)
        {
            return Nothing("the Steward is not here to put anything on the fire");
        }

        float reach = Vector3.Distance(body.transform.position, fireplace.transform.position);
        if (reach > ReachMetres)
        {
            return Nothing(
                "she is " + reach.ToString("0.#", CultureInfo.InvariantCulture) + " m from that " +
                "fire and only reaches " + ReachMetres.ToString("0.#", CultureInfo.InvariantCulture) +
                " m, the same as a player");
        }

        string fuelName = FuelNameOf(fireplace);
        if (fuelName.Length == 0)
        {
            return Nothing("that fire does not say what it burns");
        }

        ItemDrop.ItemData? item = FindCarried(body, fuelName);
        if (item == null)
        {
            return Nothing("the Steward is not carrying any " + fuelName);
        }

        int carriedBefore = SafeCount(carrier, fuelName);
        float fuelBefore = ReadFuel(view);
        if (carriedBefore < 0 || float.IsNaN(fuelBefore))
        {
            return new FeedMeasurement(
                FeedOutcome.Uncertain, carriedBefore, carriedBefore, fuelBefore, fuelBefore,
                "the Steward could not read what she was carrying or what the fire held, so she " +
                "did not touch it");
        }

        // The one mutation. Its answer is discarded on purpose: vanilla returns
        // true for a refusal as readily as for a unit accepted.
        Exception? fault = null;
        try
        {
            fireplace.UseItem(body, item);
        }
        catch (Exception exception)
        {
            fault = exception;
        }

        int carriedAfter = SafeCount(carrier, fuelName);
        float fuelAfter = ReadFuel(view);

        return Classify(
            fireplace, fuelName, carriedBefore, carriedAfter, fuelBefore, fuelAfter, fault);
    }

    // ------------------------------------------------------------------

    private static FeedMeasurement Classify(
        Fireplace fireplace,
        string fuelName,
        int carriedBefore,
        int carriedAfter,
        float fuelBefore,
        float fuelAfter,
        Exception? fault)
    {
        string numbers = string.Format(
            CultureInfo.InvariantCulture,
            " (she held {0} {1} and now holds {2}; the fire was at {3} and is at {4})",
            carriedBefore, fuelName, Text(carriedAfter),
            FuelMath.Describe(fuelBefore, fireplace.m_maxFuel),
            float.IsNaN(fuelAfter) ? "?" : FuelMath.Describe(fuelAfter, fireplace.m_maxFuel));

        if (fault != null)
        {
            return new FeedMeasurement(
                FeedOutcome.Uncertain, carriedBefore, carriedAfter, fuelBefore, fuelAfter,
                "adding fuel threw " + SafeFailure.Brief(fault) + numbers);
        }

        if (carriedAfter < 0 || float.IsNaN(fuelAfter))
        {
            return new FeedMeasurement(
                FeedOutcome.Uncertain, carriedBefore, carriedAfter, fuelBefore, fuelAfter,
                "the Steward could not read the result of putting wood on the fire" + numbers);
        }

        int spent = carriedBefore - carriedAfter;
        float gained = fuelAfter - fuelBefore;
        bool reallyGained = gained > FuelMath.Epsilon;

        if (spent == 1 && reallyGained)
        {
            return new FeedMeasurement(
                FeedOutcome.Accepted, carriedBefore, carriedAfter, fuelBefore, fuelAfter,
                "the fire took one " + fuelName + numbers);
        }

        if (spent == 0 && !reallyGained)
        {
            return new FeedMeasurement(
                FeedOutcome.Declined, carriedBefore, carriedAfter, fuelBefore, fuelAfter,
                "the fire would take no more" + numbers);
        }

        if (spent == 1 && !reallyGained)
        {
            // The destructive case. It should be unreachable — the owner check
            // above is there to make it so — and it is still measured for,
            // because the one thing worse than losing a player's wood is losing
            // it silently.
            return new FeedMeasurement(
                FeedOutcome.Lost, carriedBefore, carriedAfter, fuelBefore, fuelAfter,
                "one " + fuelName + " left the Steward's hands and the fire did not light by it" +
                numbers);
        }

        return new FeedMeasurement(
            FeedOutcome.Uncertain, carriedBefore, carriedAfter, fuelBefore, fuelAfter,
            "putting wood on the fire did not add up" + numbers);
    }

    private bool TryObserve(Fireplace fireplace, string epoch, out FuelTargetObservation observation)
    {
        observation = default;

        string? key = StewardIdentity.TryIdentify(fireplace);
        if (key == null)
        {
            return false;
        }

        ZNetView? view = fireplace.GetComponent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            return false;
        }

        Vector3 position = fireplace.transform.position;
        float fuel = ReadFuel(view);
        if (float.IsNaN(fuel))
        {
            return false;
        }

        observation = new FuelTargetObservation(
            new FuelTargetKey(key, epoch),
            new SitePoint(position.x, position.y, position.z),
            FuelNameOf(fireplace),
            fuel,
            fireplace.m_maxFuel,
            fireplace.m_canRefill,
            fireplace.m_infiniteFuel,
            view.IsOwner(),
            SafeAccess(position),
            fireplace.m_secPerFuel,
            ReadIsLit(view));
        return true;
    }

    /// <summary>The fuel level, straight out of the object's own data.
    ///
    /// A read and only a read: <c>ZDO.GetFloat</c> takes no lock, claims no
    /// ownership and changes nothing. NaN means it could not be read, which
    /// every caller treats as "do not act".</summary>
    private static float ReadFuel(ZNetView view)
    {
        try
        {
            ZDO zdo = view.GetZDO();
            return zdo == null ? float.NaN : zdo.GetFloat(ZDOVars.s_fuel, 0f);
        }
        catch (Exception)
        {
            return float.NaN;
        }
    }

    /// <summary>Whether the piece is switched on, straight out of its own data.
    ///
    /// <c>Fireplace.IsBurning</c> would be the obvious call and it is the wrong
    /// one here: it also answers false for a fire that is blocked, wet or out of
    /// fuel, and "out of fuel" is precisely the fire a round exists to serve. So
    /// the one thing that is actually asked is vanilla's own on/off state —
    /// <c>ZDOVars.s_state</c>, where 1 is on and 2 is off, defaulting to on for
    /// the pieces that cannot be turned off at all.
    ///
    /// A read and only a read. Unreadable counts as on, which keeps a fire that
    /// is genuinely burning in the round rather than dropping it for a reason
    /// nobody could establish.</summary>
    private static bool ReadIsLit(ZNetView view)
    {
        try
        {
            ZDO zdo = view.GetZDO();
            return zdo == null || zdo.GetInt(ZDOVars.s_state, 1) == 1;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string FuelNameOf(Fireplace fireplace)
    {
        try
        {
            ItemDrop? fuelItem = fireplace.m_fuelItem;
            if (fuelItem == null || fuelItem.m_itemData == null || fuelItem.m_itemData.m_shared == null)
            {
                return string.Empty;
            }

            return fuelItem.m_itemData.m_shared.m_name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>One stack of the fire's fuel item out of the Steward's own
    /// inventory, matched the way vanilla matches — <c>m_shared.m_name</c> and
    /// nothing else.
    ///
    /// Not <c>Inventory.GetItem(name)</c>: that filters on
    /// <c>m_worldLevel</c> as well, so on a world whose level has been raised it
    /// would answer "none" for a stack <c>UseItem</c> would happily burn. Two
    /// different predicates for the same question is how a count and a mutation
    /// stop describing the same items.</summary>
    private static ItemDrop.ItemData? FindCarried(Humanoid body, string fuelName)
    {
        try
        {
            Inventory? inventory = body.GetInventory();
            if (inventory == null)
            {
                return null;
            }

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item != null && item.m_shared != null && item.m_stack > 0
                    && string.Equals(item.m_shared.m_name, fuelName, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FeedMeasurement Nothing(string because) =>
        new FeedMeasurement(FeedOutcome.Declined, 0, 0, 0f, 0f, "The Steward left it: " + because + ".");

    private static int SafeCount(IItemStorePort carrier, string fuelName)
    {
        try
        {
            return carrier == null ? -1 : carrier.Count(fuelName);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private bool SafeAccess(Vector3 point)
    {
        try
        {
            return _accessGranted(point);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Humanoid? SafeBody()
    {
        try
        {
            return _body();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string SafeEpoch()
    {
        try
        {
            return _epoch() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static Vector3 ToVector3(SitePoint point) => new Vector3(point.X, point.Y, point.Z);

    private static string Text(int value) =>
        value < 0 ? "?" : value.ToString(CultureInfo.InvariantCulture);
}
