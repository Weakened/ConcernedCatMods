using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Felling;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>What one <c>ct_collect fell</c> did, for the player and the ledger.
/// </summary>
internal sealed class GunnarFellReport
{
    public GunnarFellReport(FellOutcome outcome, int woodTaken, bool uncertainTake, string message)
    {
        Outcome = outcome;
        WoodTaken = woodTaken;
        UncertainTake = uncertainTake;
        Message = message ?? string.Empty;
    }

    public FellOutcome Outcome { get; }

    /// <summary>Units of Wood that measurably arrived in him from this fell.
    /// </summary>
    public int WoodTaken { get; }

    /// <summary>A take after the fell did not add up; nothing more was taken.
    /// </summary>
    public bool UncertainTake { get; }

    public string Message { get; }
}

/// <summary>Gunnar's half of `DECISIONS.md` D19 (#381 item 5): he fells one of
/// the four small trees, one hit per explicit <c>ct_collect fell</c>, with an axe
/// he carries in his own inventory, inside the area the player set.
///
/// <b>The single authorised world call.</b> <c>Destructible.Damage</c>, once per
/// hit, in <see cref="StrikeOnce"/>, here and nowhere else in Teamster or the
/// shared trees it compiles in. The #313 worker-runtime scope audit pins it
/// verbatim and once; <c>D19 Gunnar felling audit</c> pins that its receiver is
/// the <c>Destructible</c> the strike is handed, that the strike is reached only
/// from <see cref="TryFellOne"/> behind the shared site rule, and that
/// <see cref="TryFellOne"/> is reached only from <c>ct_collect fell</c>. It
/// is the only <c>.Damage(</c> in the product, and it can take health from
/// nothing but a small tree: no character, his own body included, is a
/// <c>Destructible</c>.
///
/// <b>The scope is Thorstein's, as the lead default pending owner
/// confirmation.</b> The owner granted felling on #381 without setting Gunnar's
/// scope, so this is D19 as Foreman built it: the four allowlisted prefabs; the
/// axe's plain damage, built from its own item data with Gunnar as attacker and
/// no skill factor, multiplier or status effect; inside an explicit work area
/// (his only one is the area set with <c>ct_collect area</c>); never within 15 m
/// of a player or a player-built piece; never where a ward denies the player, in a
/// dungeon or in a world location; owned by this process, so vanilla's handler runs
/// in this call and spawns its own drops in it. TreeBase and TreeLog stay unused.
///
/// <b>The axe is his.</b> It is read from his own inventory - put there by the
/// player through D20's chest take - and never built, cloned or equipped here.
/// Vanilla wears a weapon on a hit only when the attacker is a player, so this
/// writes nothing to it; a broken axe is refused.
///
/// <b>The Wood is taken through the already-pinned pickup.</b> On a fell, the
/// item drops that appeared beside the tree during the hit are handed, by
/// instance, in the same call, to <see cref="GunnarCollectionPort.TakeFelled"/>,
/// which takes only Wood owned here, whole drops that fit his carry limit,
/// through the collection port's one <c>Humanoid.Pickup</c>, and counts only
/// what arrived. Resin, fir cones and Wood beyond the limit lie on the ground.
///
/// <b>Nothing here moves him.</b> He has to be standing at the tree.</summary>
internal sealed class GunnarFellingPort
{
    /// <summary>How far from the tree a drop may land and still be the hit's.
    /// Foreman's figure.</summary>
    internal const float DropSearchMetres = 3f;

    /// <summary>How far from the tree a ward could stand and still cover it: a
    /// vanilla guard stone reaches 32 m. Every zone touching the square of this
    /// half-side around the tree must be loaded before the ward is asked, or the
    /// answer is a refusal.</summary>
    private const float WardReachMetres = 32f;

    private static readonly int[] AllowlistedHashes = BuildHashes();

    private readonly HashSet<ItemDrop> _before = new HashSet<ItemDrop>();
    private readonly List<ItemDrop> _spawned = new List<ItemDrop>();
    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly List<ZNetView> _scene = new List<ZNetView>();

    /// <summary>One hit on the eligible small tree nearest Gunnar inside the
    /// area. Every D19 site rule is re-measured here, in the frame of the hit.
    /// </summary>
    internal GunnarFellReport TryFellOne(
        Humanoid? worker,
        CollectionArea? area,
        bool areaChosen,
        Func<bool> enabled,
        Func<WorkAuthorityVerdict> authority,
        float reachMetres,
        GunnarCollectionPort pickup,
        Func<Humanoid, int?> woodRoom,
        Action<string> log)
    {
        if (!enabled())
        {
            return Refused(FellOrderGate.DescribeSite(FellSiteRefusal.FeatureOff));
        }

        if (worker == null || worker.IsDead() || !HasNetworkRecord(worker))
        {
            return Refused("Gunnar is not here, is down, or has no network record");
        }

        if (area == null || !areaChosen)
        {
            return Refused(FellOrderGate.DescribeSite(FellSiteRefusal.NoExplicitArea));
        }

        Vector3 workerAt = worker.transform.position;
        if (!TryFindTree(area, workerAt, out Destructible? target, out ZNetView? view, out SmallTreeKind? kind, out string none))
        {
            return Refused(none);
        }

        Vector3 treeAt = target!.transform.position;
        if (Character.InInterior(treeAt))
        {
            return Refused("that tree is inside a dungeon");
        }

        LocationStanding location = TeamsterLocationSense.At(treeAt);
        if (location != LocationStanding.Outside)
        {
            return Refused(location == LocationStanding.Inside
                ? "that tree is inside a world location, which he leaves alone"
                : "whether that tree is inside a world location could not be read, so he leaves it alone");
        }

        ItemDrop.ItemData? axe = BestAxe(worker);
        FellSiteFacts site = new FellSiteFacts(
            featureEnabled: enabled(),
            authorityGranted: authority() == WorkAuthorityVerdict.Granted,
            explicitArea: areaChosen,
            insideArea: area.Contains(new CollectionPoint(treeAt.x, treeAt.y, treeAt.z)),
            ownedHere: view!.IsOwner(),
            wardAllows: WardAllows(treeAt),
            nearestPlayerMetres: PlayerClearanceMetres(treeAt),
            nearestBuiltPieceMetres: PieceClearanceMetres(treeAt),
            workerDistanceMetres: Utils.DistanceXZ(workerAt, treeAt),
            hasUsableAxe: axe != null,
            axeToolTier: axe?.m_shared.m_toolTier ?? -1,
            treeMinToolTier: kind!.MinToolTier);
        FellSiteRefusal refusal = SmallTreeFelling.CheckSite(site, reachMetres);
        if (refusal != FellSiteRefusal.Allowed)
        {
            return Refused(FellOrderGate.DescribeSite(refusal) +
                (refusal == FellSiteRefusal.WorkerOutOfReach
                    ? " (the nearest small tree in the area is " + site.WorkerDistanceMetres.ToString("0.#") + " m from him)"
                    : string.Empty));
        }

        _spawned.Clear();
        try
        {
            FellOutcome outcome = StrikeOnce(target, view, kind, axe!, worker, workerAt, _spawned, out float healthBefore, out float healthAfter, out bool gone, out bool threw);
            switch (outcome)
            {
                case FellOutcome.Felled:
                    int? room = woodRoom(worker);
                    FelledTake taken = room.HasValue
                        ? pickup.TakeFelled(worker, _spawned, room.Value)
                        : new FelledTake(0, _spawned.Count, uncertain: false);
                    log("Gunnar's felling: he felled a " + kind.Prefab + "; " + _spawned.Count + " drop(s), " +
                        taken.Taken + " Wood taken" + (taken.Uncertain ? " (a take did not add up)" : string.Empty) + ".");
                    return new GunnarFellReport(
                        outcome, taken.Taken, taken.Uncertain,
                        "Gunnar felled a small " + Pretty(kind) + ". He took " + taken.Taken + " Wood" +
                        (room.HasValue ? string.Empty : " (what he can carry could not be read, so he took none)") +
                        (taken.Uncertain
                            ? "; one take did not add up, so he stopped there and nothing more is assumed - see the log"
                            : string.Empty) +
                        (taken.LeftOnGround > 0 ? "; " + taken.LeftOnGround + " item(s) lie on the ground for you." : "."));
                case FellOutcome.Hit:
                    return new GunnarFellReport(
                        outcome, 0, false,
                        "Gunnar struck a small " + Pretty(kind) + "; it still stands (" +
                        Math.Max(0f, healthAfter).ToString("0") + " left). Run ct_collect fell again.");
                case FellOutcome.NoEffect:
                    return new GunnarFellReport(outcome, 0, false, "His axe did nothing to that tree; nothing changed.");
                default:
                    log("Gunnar's felling: an uncertain hit on " + kind.Prefab + " (before " + healthBefore + ", gone " + gone +
                        ", after " + healthAfter + ", drops " + _spawned.Count + ", threw " + threw + ").");
                    return new GunnarFellReport(
                        FellOutcome.Uncertain, 0, false,
                        "The hit's result could not be told for certain; nothing was taken or counted. See the log.");
            }
        }
        finally
        {
            _spawned.Clear();
        }
    }

    private static GunnarFellReport Refused(string why)
    {
        return new GunnarFellReport(FellOutcome.Unspecified, 0, false, "Not felled: " + why + ".");
    }

    /// <summary>The one hit (D19), after <see cref="TryFellOne"/> has
    /// re-measured every site rule in the same call. It lands exactly one
    /// <c>Destructible.Damage</c> built from his own axe and reports what the
    /// game did; <paramref name="spawned"/> receives every item drop that
    /// appeared within <see cref="DropSearchMetres"/> of the tree during the
    /// call. This method takes, guards and changes none of them.</summary>
    internal FellOutcome StrikeOnce(
        Destructible tree, ZNetView view, SmallTreeKind kind, ItemDrop.ItemData axe, Humanoid worker, Vector3 workerAt,
        List<ItemDrop> spawned, out float healthBefore, out float healthAfter, out bool gone, out bool threw)
    {
        Vector3 treeAt = tree.transform.position;
        HitData hit = HitFrom(axe, worker, workerAt, treeAt);
        healthBefore = HealthOf(tree, view);

        _before.Clear();
        foreach (ItemDrop existing in ItemDrop.s_instances)
        {
            if (existing != null)
            {
                _before.Add(existing);
            }
        }

        threw = false;
        try
        {
            tree.Damage(hit);
        }
        catch (Exception)
        {
            threw = true;
        }

        foreach (ItemDrop candidate in ItemDrop.s_instances)
        {
            if (candidate != null && !_before.Contains(candidate) &&
                Utils.DistanceXZ(candidate.transform.position, treeAt) <= DropSearchMetres)
            {
                spawned.Add(candidate);
            }
        }

        _before.Clear();
        gone = tree == null || view == null || !view.IsValid();
        healthAfter = gone ? 0f : HealthOf(tree!, view!);
        return SmallTreeFelling.Read(healthBefore, gone, healthAfter, spawned.Count, threw);
    }

    private static string Pretty(SmallTreeKind kind)
    {
        return kind.Prefab.StartsWith("Beech", StringComparison.Ordinal) ? "beech" : "fir";
    }

    /// <summary>The nearest eligible small tree to Gunnar inside the area. Only
    /// the four allowlisted prefab hashes are read.</summary>
    private bool TryFindTree(
        CollectionArea area, Vector3 workerAt, out Destructible? target, out ZNetView? found, out SmallTreeKind? kind,
        out string none)
    {
        target = null;
        found = null;
        kind = null;
        ZNetScene scene = ZNetScene.instance;
        if (scene == null)
        {
            none = "no world is loaded";
            return false;
        }

        _scene.Clear();
        _scene.AddRange(scene.m_instances.Values);
        float best = float.PositiveInfinity;
        int seen = 0;
        SmallTreeRefusal lastRefusal = SmallTreeRefusal.Unspecified;
        try
        {
            foreach (ZNetView candidate in _scene)
            {
                if (candidate == null || !candidate.IsValid() || Array.IndexOf(AllowlistedHashes, candidate.GetZDO().GetPrefab()) < 0)
                {
                    continue;
                }

                Vector3 at = candidate.transform.position;
                if (!area.Contains(new CollectionPoint(at.x, at.y, at.z)))
                {
                    continue;
                }

                seen++;
                SmallTreeFacts facts = ReadFacts(candidate, out Destructible? part, out SmallTreeKind? which);
                SmallTreeRefusal verdict = SmallTreeFelling.Classify(facts);
                if (verdict != SmallTreeRefusal.Eligible)
                {
                    lastRefusal = verdict;
                    continue;
                }

                float distance = Utils.DistanceXZ(workerAt, at);
                if (distance < best)
                {
                    best = distance;
                    target = part;
                    found = candidate;
                    kind = which;
                }
            }
        }
        finally
        {
            _scene.Clear();
        }

        if (target != null)
        {
            none = string.Empty;
            return true;
        }

        none = seen == 0
            ? "there is no small beech or small fir in the area you set"
            : "no small tree in the area may be felled: " + SmallTreeFelling.Describe(lastRefusal);
        return false;
    }

    private static SmallTreeFacts ReadFacts(ZNetView view, out Destructible? destructible, out SmallTreeKind? listed)
    {
        string prefab = Utils.GetPrefabName(view.gameObject);
        SmallTreeFelling.TryFind(prefab, out listed);
        Destructible[] parts = view.GetComponentsInChildren<Destructible>(true);
        destructible = parts.Length == 1 ? parts[0] : null;
        return new SmallTreeFacts(
            hasValidNetworkObject: view.IsValid(),
            networkPrefabName: prefab,
            destructibleCount: parts.Length,
            hasPlant: view.GetComponentInChildren<Plant>(true) != null,
            hasPiece: view.GetComponentInChildren<Piece>(true) != null,
            hasWearNTear: view.GetComponentInChildren<WearNTear>(true) != null,
            hasPickable: view.GetComponentInChildren<Pickable>(true) != null,
            minToolTier: destructible != null ? destructible.m_minToolTier : -1,
            health: destructible != null ? HealthOf(destructible, view) : 0f);
    }

    /// <summary>Health as vanilla's damage handler reads it, with the same
    /// default: base health plus the world-level bonus.</summary>
    private static float HealthOf(Destructible part, ZNetView view) =>
        view.IsValid()
            ? view.GetZDO().GetFloat(
                ZDOVars.s_health,
                part.m_health + (Game.m_worldLevel * part.m_health * (Game.instance != null ? Game.instance.m_worldLevelMineHPMultiplier : 0f)))
            : 0f;

    /// <summary>His own best usable axe, read from his own inventory: never a
    /// clone, never one conjured for the hit, never equipped here. An axe is an
    /// item whose chopping damage is its largest, the rule Foreman's issued-tool
    /// classifier uses; a building tool is not one.</summary>
    private static ItemDrop.ItemData? BestAxe(Humanoid worker)
    {
        Inventory? inventory = worker.GetInventory();
        if (inventory == null)
        {
            return null;
        }

        ItemDrop.ItemData? best = null;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            ItemDrop.ItemData.SharedData? shared = item?.m_shared;
            if (item == null || shared == null || shared.m_buildPieces != null)
            {
                continue;
            }

            HitData.DamageTypes damage = shared.m_damages;
            bool chops = damage.m_chop > 0f
                && damage.m_chop >= damage.m_damage
                && damage.m_chop >= damage.m_blunt
                && damage.m_chop >= damage.m_slash
                && damage.m_chop >= damage.m_pierce
                && damage.m_chop >= damage.m_pickaxe;
            bool usable = !shared.m_useDurability || item.m_durability > 0f;
            if (!chops || !usable)
            {
                continue;
            }

            if (best == null || shared.m_toolTier > best.m_shared.m_toolTier)
            {
                best = item;
            }
        }

        return best;
    }

    /// <summary>A hit from this axe, built from the fields vanilla's melee attack
    /// copies from the weapon: tool tier, the item's own damage, item level and
    /// world level, its skill, Gunnar as attacker and, because he is not a
    /// player, the non-player hit type. Not applied: the attacker's skill
    /// factor, attack multipliers, status effects, skill raise and push.</summary>
    private static HitData HitFrom(ItemDrop.ItemData axe, Humanoid worker, Vector3 workerAt, Vector3 treeAt)
    {
        var made = new HitData();
        made.m_toolTier = (short)axe.m_shared.m_toolTier;
        made.m_damage.Add(axe.GetDamage());
        made.m_itemLevel = (short)axe.m_quality;
        made.m_itemWorldLevel = (byte)axe.m_worldLevel;
        made.m_skill = axe.m_shared.m_skillType;
        made.m_point = treeAt + Vector3.up;
        Vector3 direction = treeAt - workerAt;
        direction.y = 0f;
        made.m_dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : worker.transform.forward;
        made.SetAttacker(worker);
        made.m_hitType = HitData.HitType.EnemyHit;
        return made;
    }

    /// <summary>Whether no ward that covers the tree denies the local player,
    /// asked the way Foreman's designation site asks it for Thorstein:
    /// <c>wardCheck: true</c> refuses when ANY enabled ward covering the point
    /// denies access (vanilla's default, <c>wardCheck: false</c>, would allow
    /// it as soon as one covering ward grants access), and the question is not
    /// asked at all - it refuses - unless every zone a ward reaching the tree
    /// could stand in is loaded. <c>flash: false</c>: asking must not light a
    /// guard stone up.</summary>
    private static bool WardAllows(Vector3 at)
    {
        try
        {
            // Every zone that touches the square around the tree a ward's reach
            // spans: a 3 x 3 grid at -32, 0 and +32 m on both axes, corners
            // included. A zone is 64 m wide, so a 64 m side meets at most two
            // zones and both its ends land in one each - no zone touching the
            // square is stepped over (review of #381 item 5: an 8-point circle
            // missed the diagonal zone).
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null)
            {
                return false;
            }

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var corner = new Vector3(at.x + (dx * WardReachMetres), at.y, at.z + (dz * WardReachMetres));
                    if (!zones.IsZoneLoaded(corner))
                    {
                        return false;
                    }
                }
            }

            return PrivateArea.CheckAccess(at, 0f, flash: false, wardCheck: true);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static float PlayerClearanceMetres(Vector3 at)
    {
        float nearest = float.PositiveInfinity;
        foreach (Player player in Player.GetAllPlayers())
        {
            if (player != null)
            {
                nearest = Math.Min(nearest, Utils.DistanceXZ(player.transform.position, at));
            }
        }

        return nearest;
    }

    /// <summary>The flat distance to the nearest player-built piece. The game's
    /// search is a sphere, so it is four times wider and the judgement is the
    /// flat distance, which errs towards refusing.</summary>
    private float PieceClearanceMetres(Vector3 at)
    {
        _pieces.Clear();
        Piece.GetAllPiecesInRadius(at, SmallTreeFelling.ClearanceMetres * 4f, _pieces);
        float nearest = float.PositiveInfinity;
        foreach (Piece piece in _pieces)
        {
            if (piece != null && piece.IsPlacedByPlayer())
            {
                nearest = Math.Min(nearest, Utils.DistanceXZ(piece.transform.position, at));
            }
        }

        _pieces.Clear();
        return nearest;
    }

    private static bool HasNetworkRecord(Humanoid worker)
    {
        ZNetView record = worker.m_nview;
        return record != null && record.IsValid() && record.GetZDO() != null;
    }

    private static int[] BuildHashes()
    {
        var hashes = new int[SmallTreeFelling.Allowlist.Count];
        for (int index = 0; index < hashes.Length; index++)
        {
            hashes[index] = SmallTreeFelling.Allowlist[index].Prefab.GetStableHashCode();
        }

        return hashes;
    }
}
