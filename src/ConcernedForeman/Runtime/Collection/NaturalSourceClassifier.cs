using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>Reads the facts of CONTRACTS.md §6 from a live object, so the
/// game-free <see cref="NaturalSourcePredicate"/> can decide. Nothing here
/// decides anything: every read is a field or component the installed 1.0.12
/// build has (<c>scripts/audit-foreman-collection-api.ps1</c>), and every
/// comparison lives in the tested predicate.
///
/// <b>Identity is the network prefab hash</b> (<c>ZDO.GetPrefab()</c>), what
/// the world save stores and what <c>ZNetScene</c> instantiates, never the
/// GameObject's name: a location's embedded <c>Pickable_Stone (3)</c> has the
/// same hash, and a renamed clone has a different one (PICKUP_SEAM_AUDIT.md
/// §4.3).</summary>
internal static class NaturalSourceClassifier
{
    internal static readonly int LooseStoneHash = NaturalSourceAllowlist.LooseStonePrefab.GetStableHashCode();

    internal static readonly int BranchHash = NaturalSourceAllowlist.BranchPrefab.GetStableHashCode();

    internal static bool IsAllowlistedHash(int prefabHash) => prefabHash == LooseStoneHash || prefabHash == BranchHash;

    /// <summary>The in-session id text of a network object, the same form
    /// <c>SettlementTargets</c> uses for chests. Valid for this world load
    /// only: every load hands out new ids.</summary>
    internal static string FormatId(ZDOID id) =>
        unchecked((ulong)id.UserID).ToString("x16", CultureInfo.InvariantCulture) + ":" +
        id.ID.ToString("x8", CultureInfo.InvariantCulture);

    internal static SitePoint ToSitePoint(Vector3 point) => SitePoints.ToSitePoint(point);

    internal static Vector3 ToVector3(SitePoint point) => SitePoints.ToVector3(point);

    /// <summary>Describes a candidate network object: its key for this world
    /// load, its facts, and what one pick of it gives under the world's drop
    /// scaling. False when it is not a candidate at all (no valid object, not an
    /// allowlisted prefab hash).</summary>
    internal static bool TryDescribe(
        ZNetView? view, Guid worldLoadEpoch, out SourceKey key, out NaturalSourceFacts facts, out int expectedYield)
    {
        key = default;
        facts = null!;
        expectedYield = 0;

        if (view == null || !view.IsValid() || worldLoadEpoch == Guid.Empty)
        {
            return false;
        }

        ZDO zdo = view.GetZDO();
        int hash = zdo.GetPrefab();
        string? prefab = hash == LooseStoneHash ? NaturalSourceAllowlist.LooseStonePrefab
            : hash == BranchHash ? NaturalSourceAllowlist.BranchPrefab
            : null;
        if (prefab == null)
        {
            return false;
        }

        facts = ReadFacts(view, prefab, out Pickable? pickable);
        key = new SourceKey(prefab, FormatId(zdo.m_uid), worldLoadEpoch, ToSitePoint(view.transform.position));
        expectedYield = ExpectedYield(pickable);
        return true;
    }

    /// <summary>The C1–C5 facts of one object.</summary>
    internal static NaturalSourceFacts ReadFacts(ZNetView view, string? networkPrefabName, out Pickable? pickable)
    {
        pickable = null;
        GameObject root = view.gameObject;
        ZDO? zdo = view.IsValid() ? view.GetZDO() : null;

        Pickable[] pickables = root.GetComponentsInChildren<Pickable>(includeInactive: true);
        pickable = pickables.Length > 0 ? pickables[0] : null;

        GameObject? yieldPrefab = pickable != null ? pickable.m_itemPrefab : null;
        ItemDrop? yieldDrop = yieldPrefab != null ? yieldPrefab.GetComponent<ItemDrop>() : null;

        return new NaturalSourceFacts(
            hasValidNetworkObject: zdo != null,
            networkPrefabName: networkPrefabName,
            pickableCount: pickables.Length,
            forbiddenComponents: ForbiddenComponentsIn(root),
            rootTag: root.tag,
            yieldHasItemDrop: yieldDrop != null,
            yieldPrefabName: yieldPrefab != null ? Utils.GetPrefabName(yieldPrefab) : null,
            yieldSharedName: yieldDrop != null ? yieldDrop.m_itemData?.m_shared?.m_name : null,
            amount: pickable != null ? pickable.m_amount : 0,
            minAmountScaled: pickable != null ? pickable.m_minAmountScaled : 0,
            dontScale: pickable != null && pickable.m_dontScale,
            extraDropsEmpty: pickable == null || pickable.m_extraDrops == null || pickable.m_extraDrops.IsEmpty(),
            aggravateRange: pickable != null ? pickable.m_aggravateRange : 0f,
            respawnTimeMinutes: pickable != null ? pickable.m_respawnTimeMinutes : 0f,
            hasHideWhenPicked: pickable != null && pickable.m_hideWhenPicked != null,
            creator: zdo != null ? zdo.GetLong(ZDOVars.s_creator, 0L) : 0L,
            picked: pickable != null && pickable.GetPicked(),
            enabled: pickable != null ? pickable.GetEnabled : 0,
            canBePicked: pickable != null && pickable.CanBePicked());
    }

    /// <summary>What <c>Pickable.RPC_Pick</c> will spawn for a non-player
    /// picker, computed by the game's own scaling: the same expression the RPC
    /// evaluates, with the player-only bonus at zero. 0 when it cannot be read.
    /// </summary>
    internal static int ExpectedYield(Pickable? pickable)
    {
        if (pickable == null || pickable.m_itemPrefab == null || Game.instance == null)
        {
            return 0;
        }

        return pickable.m_dontScale
            ? pickable.m_amount
            : Mathf.Max(pickable.m_minAmountScaled, Game.instance.ScaleDrops(pickable.m_itemPrefab, pickable.m_amount));
    }

    /// <summary>The expected yield of one pick of a vanilla source prefab, for
    /// acceptance: the registered prefab's configuration under this world's
    /// resource rate. 0 when the prefab or the game is not there.</summary>
    internal static int ExpectedYieldOfPrefab(string prefabName)
    {
        ZNetScene scene = ZNetScene.instance;
        GameObject? prefab = scene != null ? scene.GetPrefab(prefabName) : null;
        return prefab != null ? ExpectedYield(prefab.GetComponent<Pickable>()) : 0;
    }

    private static IReadOnlyList<string> ForbiddenComponentsIn(GameObject root)
    {
        List<string>? found = null;
        void Check<T>(string name) where T : Component
        {
            if (root.GetComponentInChildren<T>(includeInactive: true) != null)
            {
                (found ??= new List<string>()).Add(name);
            }
        }

        Check<Piece>("Piece");
        Check<WearNTear>("WearNTear");
        Check<Plant>("Plant");
        Check<Destructible>("Destructible");
        Check<ItemDrop>("ItemDrop");
        Check<Container>("Container");
        Check<ItemStand>("ItemStand");
        Check<Procreation>("Procreation");
        Check<Character>("Character");
        Check<PickableItem>("PickableItem");
        return found ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}

/// <summary>The sources the latest survey saw, by key, for this world load. A
/// key is resolved back to its live object only after its identity is proved
/// again: same prefab hash, same in-session id, still valid. A key from another
/// world load resolves to nothing.
///
/// Views, not ZDOs, are kept: the game pools ZDO objects, so a remembered ZDO
/// could later belong to a different object; a destroyed view is simply null.
/// </summary>
internal sealed class SourceDirectory
{
    private readonly Dictionary<SourceKey, ZNetView> _views = new Dictionary<SourceKey, ZNetView>();

    public Guid WorldLoadEpoch { get; private set; }

    public int Count => _views.Count;

    public void Reset(Guid worldLoadEpoch)
    {
        _views.Clear();
        WorldLoadEpoch = worldLoadEpoch;
    }

    public void Remember(SourceKey key, ZNetView view)
    {
        if (key.WorldLoadEpoch == WorldLoadEpoch && view != null)
        {
            _views[key] = view;
        }
    }

    public bool TryResolve(SourceKey key, out ZNetView view, out Pickable pickable, out string failure)
    {
        view = null!;
        pickable = null!;

        if (key.IsEmpty || key.WorldLoadEpoch != WorldLoadEpoch || WorldLoadEpoch == Guid.Empty)
        {
            failure = "the source was seen in another world load";
            return false;
        }

        if (!_views.TryGetValue(key, out ZNetView? found) || found == null || !found.IsValid())
        {
            _views.Remove(key);
            failure = "the source is gone";
            return false;
        }

        ZDO zdo = found.GetZDO();
        if (zdo.GetPrefab() != key.PrefabName.GetStableHashCode() ||
            !string.Equals(NaturalSourceClassifier.FormatId(zdo.m_uid), key.SessionId, StringComparison.Ordinal))
        {
            _views.Remove(key);
            failure = "the object at that key is no longer the source that was surveyed";
            return false;
        }

        Pickable? component = found.GetComponent<Pickable>();
        if (component == null)
        {
            failure = "the source has no pickable part";
            return false;
        }

        view = found;
        pickable = component;
        failure = string.Empty;
        return true;
    }
}
