using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Custody;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>What the world holds of one worker identity's bodies, loaded or
/// not.</summary>
internal sealed class WorkerBodyCensus
{
    internal WorkerBodyCensus(string key, IReadOnlyList<ZDO> bodies, int unidentified)
    {
        Key = key;
        Bodies = bodies;
        Unidentified = unidentified;
    }

    public string Key { get; }

    /// <summary>Every saved body carrying this identity.</summary>
    public IReadOnlyList<ZDO> Bodies { get; }

    /// <summary>Worker bodies with no identity at all: spawned by a build that
    /// did not stamp one. Reported; never adopted and never destroyed.</summary>
    public int Unidentified { get; }

    public bool IsMissing => Bodies.Count == 0;

    public bool IsDuplicated => Bodies.Count > 1;
}

/// <summary>Finding the objects custody names, in this process, for this world
/// load only.
///
/// Every key here is an in-session network id: renumbered on every load, and
/// likely to name some other object after one (DESIGNATION_AND_RECRUITMENT.md
/// §2). A key is only ever resolved with the epoch it was issued in already
/// checked by the caller, and only against objects that are loaded now —
/// never by position and never to "the nearest".
///
/// Loaded objects are found by scanning the scene's instances and comparing
/// the id's two numbers, rather than by constructing a <c>ZDOID</c>: that
/// constructor registers an unknown user id in a game-wide table as a side
/// effect, and a stale or mistyped key must not be able to change game state.
/// </summary>
internal static class WorldCustodyObjects
{
    /// <summary>A delivery chest by its <c>SettlementTargets.TryIdentify</c> key
    /// (<c>x16 user:x8 id</c>).</summary>
    internal static Container? FindContainer(string key)
    {
        if (!TryParseHex(key, out long user, out uint id))
        {
            return null;
        }

        ZNetView? view = FindLoaded(user, id);
        if (view == null)
        {
            return null;
        }

        Container container = view.GetComponent<Container>() ?? view.GetComponentInChildren<Container>();
        return container != null && string.Equals(SettlementTargets.TryIdentify(container), key, StringComparison.Ordinal)
            ? container
            : null;
    }

    /// <summary>A cart by Teamster's session key: <c>ZDOID.ToString()</c>,
    /// decimal <c>user:id</c>.</summary>
    internal static Vagon? FindCart(string sessionKey)
    {
        if (!TryParseDecimal(sessionKey, out long user, out uint id))
        {
            return null;
        }

        ZNetView? view = FindLoaded(user, id);
        return view == null ? null : view.GetComponent<Vagon>();
    }

    internal static ZNetView? FindLoaded(long user, uint id)
    {
        ZNetScene scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }

        foreach (KeyValuePair<ZDO, ZNetView> instance in scene.m_instances)
        {
            ZDO zdo = instance.Key;
            if (zdo != null && zdo.m_uid.ID == id && zdo.m_uid.UserID == user && instance.Value != null)
            {
                return instance.Value;
            }
        }

        return null;
    }

    /// <summary>Every saved worker body in the world, loaded or not, grouped by
    /// the identity it carries.</summary>
    internal static WorkerBodyCensus Census(string key)
    {
        var bodies = new List<ZDO>();
        int unidentified = 0;
        ZDOMan manager = ZDOMan.instance;
        if (manager == null)
        {
            return new WorkerBodyCensus(key, bodies, 0);
        }

        var all = new List<ZDO>();
        int index = 0;
        int guard = 0;
        while (!manager.GetAllZDOsWithPrefabIterative(ForemanWorkerPrefab.PrefabName, all, ref index) && guard++ < 100000)
        {
        }

        // The iterative scan's last call adds sector 0 again (1.0.12), so the
        // same object can appear twice; a body counted twice would read as a
        // duplicate identity.
        var distinct = new HashSet<ZDO>();
        foreach (ZDO zdo in all)
        {
            if (zdo == null || !distinct.Add(zdo))
            {
                continue;
            }

            string stored = zdo.GetString(WorkerBody.KeyField, string.Empty);
            if (stored.Length == 0)
            {
                unidentified++;
            }
            else if (string.Equals(stored, key, StringComparison.Ordinal))
            {
                bodies.Add(zdo);
            }
        }

        return new WorkerBodyCensus(key, bodies, unidentified);
    }

    /// <summary>Counts a saved body's carried material straight from its
    /// stored inventory, without the body being loaded. Null when it cannot be
    /// read.</summary>
    internal static int? CountStored(ZDO body, MaterialItem item)
    {
        try
        {
            byte[]? stored = body.GetByteArray(WorkerBody.InventoryField, null);
            if (stored == null || stored.Length == 0)
            {
                return 0;
            }

            var inventory = new Inventory("tcc-census", null, 8, 4);
            inventory.Load(new ZPackage(stored));
            return EngineInventoryPort.CountIn(inventory, item);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryParseHex(string key, out long user, out uint id)
    {
        user = 0;
        id = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        int colon = key.IndexOf(':');
        return colon > 0
            && ulong.TryParse(key.Substring(0, colon), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong unsignedUser)
            && uint.TryParse(key.Substring(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id)
            && Assign(unchecked((long)unsignedUser), out user);
    }

    private static bool TryParseDecimal(string key, out long user, out uint id)
    {
        user = 0;
        id = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        int colon = key.IndexOf(':');
        return colon > 0
            && long.TryParse(key.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out user)
            && uint.TryParse(key.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static bool Assign(long value, out long target)
    {
        target = value;
        return true;
    }
}
