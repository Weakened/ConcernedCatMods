using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Felling;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>What one <c>cf_collect fell</c> did, for the player.</summary>
internal sealed class FellReport
{
    public FellReport(FellOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message;
    }

    public FellOutcome Outcome { get; }

    public string Message { get; }
}

/// <summary>D19 (owner decision 2026-10-31): Thorstein fells a small tree, one
/// hit at a time, with his own issued axe, inside an explicit work area.
///
/// <b>The single authorised world call.</b> <c>Destructible.Damage</c> on one of
/// the four allowlisted small trees, once per hit, here and nowhere else in
/// Foreman's runtime (the #400 audit pins it there; <c>TreeBase</c> and
/// <c>TreeLog</c> are refused in Foreman's runtime, decided but unused). The hit
/// is built from the axe's own item data along the lines of vanilla's melee hit
/// (damage, tool tier, item level, skill, the worker as attacker) but without the
/// attacker's skill factor, attack multipliers or status effects, so it is the
/// axe's plain damage. Vanilla then resolves resistances, the tool tier, health
/// and, at zero, its own <c>DropOnDestroyed</c> drops.
///
/// <b>Drops stay on the ground in this leaf.</b> Nothing is picked up, taken
/// into custody or counted toward an order: the wood lies where vanilla dropped
/// it, as it would after a player's swing. Taking it (same-call custody by
/// instance id, D19's second grant) belongs to the collection-loop leaf.
///
/// <b>Durability.</b> Vanilla wears a weapon on a hit only when the attacker is
/// a player (<c>Attack</c>: <c>if (m_weapon.m_shared.m_useDurability &amp;&amp;
/// m_character.IsPlayer())</c>, Valheim 1.0.21). Thorstein is not a player, so
/// vanilla's own rule wears nothing and this port writes nothing to the axe. An
/// axe that is already broken is refused.
///
/// <b>Fails closed.</b> The switch, work authority, an explicit area, ownership
/// by this process (so the game's damage handler runs in this call), a ward,
/// 15 m from any player and any player-built piece, his reach, and a usable axe
/// are all re-checked immediately before the hit.</summary>
internal sealed class WorldFellingPort
{
    private const float DropSearchMetres = 3f;

    private static readonly int[] AllowlistedHashes = BuildHashes();

    private readonly Func<bool> _enabled;
    private readonly Func<WorkAuthorityVerdict> _authority;
    private readonly Func<ForemanWorkerAI?> _body;
    private readonly IDesignationSite _site;
    private readonly float _reachMetres;
    private readonly Action<string> _log;
    private readonly HashSet<ItemDrop> _before = new HashSet<ItemDrop>();
    private readonly List<Piece> _pieces = new List<Piece>();

    public WorldFellingPort(
        Func<bool> enabled,
        Func<WorkAuthorityVerdict> authority,
        Func<ForemanWorkerAI?> body,
        IDesignationSite site,
        float reachMetres,
        Action<string> log)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _reachMetres = reachMetres;
        _log = log ?? (_ => { });
    }

    /// <summary>One hit on the eligible small tree nearest Thorstein inside
    /// <paramref name="scope"/>. <paramref name="explicitArea"/> is false for the
    /// default camp circle, which never authorises felling.</summary>
    public FellReport TryFellNearest(WorkScope scope, bool explicitArea)
    {
        if (!_enabled())
        {
            return Refused(SmallTreeFelling.Describe(FellSiteRefusal.FeatureOff));
        }

        ForemanWorkerAI? body = _body();
        Humanoid? humanoid = body != null ? body.GetComponent<Humanoid>() : null;
        if (body == null || humanoid == null || body.IsFaulted || !body.IsOwnedAndValid)
        {
            return Refused("Thorstein's body is not here, not owned here, or faulted");
        }

        Vector3 workerAt = body.transform.position;
        if (!TryFindNearest(scope, workerAt, out Destructible? tree, out ZNetView? view, out SmallTreeKind? kind, out string none))
        {
            return Refused(none);
        }

        Vector3 treeAt = tree!.transform.position;
        ItemDrop.ItemData? axe = BestAxe(humanoid);
        FellSiteFacts site = new FellSiteFacts(
            featureEnabled: _enabled(),
            authorityGranted: _authority() == WorkAuthorityVerdict.Granted,
            explicitArea: explicitArea,
            insideArea: scope.Contains(NaturalSourceClassifier.ToSitePoint(treeAt)),
            ownedHere: view!.IsOwner(),
            wardAllows: _site.CheckAccess(NaturalSourceClassifier.ToSitePoint(treeAt), 0f) == AreaAccess.Granted,
            nearestPlayerMetres: NearestPlayerMetres(treeAt),
            nearestBuiltPieceMetres: NearestPieceMetres(treeAt),
            workerDistanceMetres: Utils.DistanceXZ(workerAt, treeAt),
            hasUsableAxe: axe != null,
            axeToolTier: axe?.m_shared.m_toolTier ?? -1,
            treeMinToolTier: kind!.MinToolTier);
        FellSiteRefusal refusal = SmallTreeFelling.CheckSite(site, _reachMetres);
        if (refusal != FellSiteRefusal.Allowed)
        {
            return Refused(SmallTreeFelling.Describe(refusal) +
                (refusal == FellSiteRefusal.WorkerOutOfReach
                    ? " (the nearest small tree is " + site.WorkerDistanceMetres.ToString("0.#") + " m from him; walk him there with cf_worker goto)"
                    : string.Empty));
        }

        HitData hit = HitFrom(axe!, humanoid, workerAt, treeAt);
        float healthBefore = HealthOf(tree, view);

        _before.Clear();
        foreach (ItemDrop existing in ItemDrop.s_instances)
        {
            if (existing != null)
            {
                _before.Add(existing);
            }
        }

        bool threw = false;
        try
        {
            tree.Damage(hit);
        }
        catch (Exception exception)
        {
            threw = true;
            _log("Felling: the hit on " + kind.Prefab + " threw " + exception.GetType().Name + ".");
        }

        int newDrops = 0;
        foreach (ItemDrop candidate in ItemDrop.s_instances)
        {
            if (candidate != null && !_before.Contains(candidate) &&
                Utils.DistanceXZ(candidate.transform.position, treeAt) <= DropSearchMetres)
            {
                newDrops++;
            }
        }

        _before.Clear();
        bool gone = tree == null || view == null || !view.IsValid();
        float healthAfter = gone ? 0f : HealthOf(tree!, view!);
        FellOutcome outcome = SmallTreeFelling.Read(healthBefore, gone, healthAfter, newDrops, threw);
        switch (outcome)
        {
            case FellOutcome.Felled:
                _log("Felling: Thorstein felled a " + kind.Prefab + "; " + newDrops + " drop(s) left on the ground.");
                return new FellReport(outcome, "Thorstein felled a small " + Pretty(kind) + ". What it dropped (" + newDrops +
                    " item(s)) lies on the ground for you; he does not pick it up yet.");
            case FellOutcome.Hit:
                return new FellReport(outcome, "Thorstein struck a small " + Pretty(kind) + "; it still stands (" +
                    Math.Max(0f, healthAfter).ToString("0") + " left). Run cf_collect fell again.");
            case FellOutcome.NoEffect:
                return new FellReport(outcome, "His axe did nothing to that tree; nothing changed.");
            default:
                _log("Felling: an uncertain hit on " + kind.Prefab + " (before " + healthBefore + ", gone " + gone +
                    ", after " + healthAfter + ", drops " + newDrops + ", threw " + threw + ").");
                return new FellReport(FellOutcome.Uncertain, "The hit's result could not be told for certain; nothing is counted. See the log.");
        }
    }

    private static FellReport Refused(string why) => new FellReport(FellOutcome.Unspecified, "Not felled: " + why + ".");

    private static string Pretty(SmallTreeKind kind) =>
        kind.Prefab.StartsWith("Beech", StringComparison.Ordinal) ? "beech" : "fir";

    /// <summary>The nearest eligible small tree to the worker inside the scope's
    /// circle and boundary. Only the four allowlisted prefab hashes are read.
    /// </summary>
    private static bool TryFindNearest(
        WorkScope scope, Vector3 workerAt, out Destructible? tree, out ZNetView? view, out SmallTreeKind? kind, out string none)
    {
        tree = null;
        view = null;
        kind = null;
        ZNetScene scene = ZNetScene.instance;
        if (scene == null)
        {
            none = "no world is loaded";
            return false;
        }

        float best = float.PositiveInfinity;
        int seen = 0;
        SmallTreeRefusal lastRefusal = SmallTreeRefusal.Unspecified;
        foreach (ZNetView candidate in scene.m_instances.Values)
        {
            if (candidate == null || !candidate.IsValid() || Array.IndexOf(AllowlistedHashes, candidate.GetZDO().GetPrefab()) < 0)
            {
                continue;
            }

            Vector3 at = candidate.transform.position;
            if (!scope.Contains(NaturalSourceClassifier.ToSitePoint(at)))
            {
                continue;
            }

            seen++;
            SmallTreeFacts facts = ReadFacts(candidate, out Destructible? destructible, out SmallTreeKind? found);
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
                tree = destructible;
                view = candidate;
                kind = found;
            }
        }

        if (tree != null)
        {
            none = string.Empty;
            return true;
        }

        none = seen == 0
            ? "there is no small beech or small fir in this work area"
            : "no small tree here may be felled: " + SmallTreeFelling.Describe(lastRefusal);
        return false;
    }

    private static SmallTreeFacts ReadFacts(ZNetView view, out Destructible? destructible, out SmallTreeKind? kind)
    {
        string prefab = Utils.GetPrefabName(view.gameObject);
        SmallTreeFelling.TryFind(prefab, out kind);
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
    private static float HealthOf(Destructible tree, ZNetView view) =>
        view.IsValid()
            ? view.GetZDO().GetFloat(
                ZDOVars.s_health,
                tree.m_health + (Game.m_worldLevel * tree.m_health * (Game.instance != null ? Game.instance.m_worldLevelMineHPMultiplier : 0f)))
            : 0f;

    /// <summary>His own best usable axe, read from his own inventory: the
    /// issued tool, never a clone and never one conjured for the hit.</summary>
    private static ItemDrop.ItemData? BestAxe(Humanoid humanoid)
    {
        ItemDrop.ItemData? best = null;
        foreach (ItemDrop.ItemData item in humanoid.GetInventory().GetAllItems())
        {
            if (TheConcernedCat.ConcernedForeman.Runtime.Settlement.ToolClassifier.Classify(item) != ToolKind.Axe || !TheConcernedCat.ConcernedForeman.Runtime.Settlement.ToolClassifier.IsStillUsable(item))
            {
                continue;
            }

            if (best == null || item.m_shared.m_toolTier > best.m_shared.m_toolTier)
            {
                best = item;
            }
        }

        return best;
    }

    /// <summary>A hit from this axe, built from the fields vanilla's melee
    /// attack copies from the weapon (<c>Attack</c> in Valheim 1.0.21): tool
    /// tier, the item's own damage, item level and world level, its skill, the
    /// worker as attacker and, because he is not a player, the non-player hit
    /// type. Not applied: the attacker's skill factor, attack multipliers,
    /// status effects, skill raise and push.</summary>
    private static HitData HitFrom(ItemDrop.ItemData axe, Humanoid worker, Vector3 workerAt, Vector3 treeAt)
    {
        var hit = new HitData();
        hit.m_toolTier = (short)axe.m_shared.m_toolTier;
        hit.m_damage.Add(axe.GetDamage());
        hit.m_itemLevel = (short)axe.m_quality;
        hit.m_itemWorldLevel = (byte)axe.m_worldLevel;
        hit.m_skill = axe.m_shared.m_skillType;
        hit.m_point = treeAt + Vector3.up;
        Vector3 direction = treeAt - workerAt;
        direction.y = 0f;
        hit.m_dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : worker.transform.forward;
        hit.SetAttacker(worker);
        hit.m_hitType = HitData.HitType.EnemyHit;
        return hit;
    }

    private static float NearestPlayerMetres(Vector3 at)
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
    /// search is a 3D sphere, so a piece 14 m away across a slope could sit
    /// outside a 15 m sphere; the search is therefore four times wider and the
    /// judgement is the flat distance, which errs towards refusing.</summary>
    private float NearestPieceMetres(Vector3 at)
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
