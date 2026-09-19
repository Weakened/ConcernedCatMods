using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Every body of one role's prefab that this world has saved, grouped
/// by the identity it carries - the walk, where <see cref="NpcBodyTally"/> is
/// the verdict.
///
/// <b>Filtered by prefab and then by key, in that order, and never the other
/// way round.</b> The world index is asked for one prefab, so a role only ever
/// sees its own bodies; the identity inside them is read second, with that
/// role's own key name. Two shipped roles share a key prefix today, so a scan
/// that matched on the identity first would hand one role the other's bodies -
/// and the runtime that received them would count two, report the identity
/// duplicated, and refuse to work for the rest of the session. The prefab is
/// what separates them before the key is ever read.
///
/// <b>Incremental, because a world index is not small.</b> The walk is resumed
/// across frames rather than run in one call, which is the cart runtime's shape
/// rather than the settlement runtime's; the settlement one spun the whole
/// index inside a guarded loop and stalled a frame on a large world. The
/// synchronous form is still here for a caller that genuinely has to have the
/// answer now, and it is bounded.
///
/// <b>Both duplicate guards are kept, because they do different jobs.</b> The
/// game's own iterative scan adds the first sector a second time on its
/// terminating call, so the same object arrives twice and a body counted twice
/// reads as a duplicated identity. One shipped census dropped repeats by object
/// reference and the other by network id, and they are not interchangeable: the
/// reference set is what stops one object being <i>examined</i> twice, which is
/// what the repeated sector actually causes and is the only guard that works
/// for a body carrying no identity at all; the id set is what survives across
/// the many calls an incremental walk takes, and what makes forgetting one
/// retired body possible without re-running the whole scan.
///
/// <b>Nothing here destroys anything.</b> A duplicate is reported and left
/// standing; an unidentified body is counted and never adopted. Deciding which
/// of two bodies carrying one identity should go is a person's decision, and
/// getting it wrong means a player loses whatever was inside.</summary>
internal sealed class NpcWorldBodies
{
    private readonly NpcBodySetup _setup;
    private readonly Dictionary<string, HashSet<ZDOID>> _byIdentity =
        new Dictionary<string, HashSet<ZDOID>>(StringComparer.Ordinal);

    private readonly HashSet<ZDOID> _unidentified = new HashSet<ZDOID>();
    private readonly HashSet<ZDO> _seen = new HashSet<ZDO>();
    private readonly List<ZDO> _buffer = new List<ZDO>();

    private int _index;

    internal NpcWorldBodies(NpcBodySetup setup)
    {
        _setup = setup;
    }

    /// <summary>The walk over this world's saved objects has finished. Until it
    /// has, nothing may be built.</summary>
    internal bool IsComplete { get; private set; }

    /// <summary>Bodies of this prefab carrying no identity at all. Reported so
    /// a player can be told there is a stranger in their world; never adopted.
    /// </summary>
    internal int Unidentified => _unidentified.Count;

    /// <summary>The prefab this census walks.</summary>
    internal string PrefabName => _setup.Contract.PrefabName;

    /// <summary>Forgets everything and starts again. Called when a world is
    /// loaded: every network id in the previous one was renumbered, so a single
    /// id carried across would name some other object.</summary>
    internal void Restart()
    {
        _byIdentity.Clear();
        _unidentified.Clear();
        _seen.Clear();
        _buffer.Clear();
        _index = 0;
        IsComplete = false;
    }

    /// <summary>One step of the walk. Returns whether it has finished.
    ///
    /// Safe to call after it has finished - it does nothing - and safe to call
    /// with no world up, in which case it does nothing and stays unfinished,
    /// which is the refusing answer.</summary>
    internal bool Advance()
    {
        if (IsComplete)
        {
            return true;
        }

        ZDOMan manager = ZDOMan.instance;
        if (manager == null)
        {
            return false;
        }

        _buffer.Clear();
        bool done = manager.GetAllZDOsWithPrefabIterative(_setup.Contract.PrefabName, _buffer, ref _index);
        for (int index = 0; index < _buffer.Count; index++)
        {
            Examine(_buffer[index]);
        }

        _buffer.Clear();
        if (done)
        {
            IsComplete = true;
        }

        return IsComplete;
    }

    /// <summary>Runs the walk to the end now, for a caller that cannot wait a
    /// frame. Bounded: a world index that never terminates leaves the census
    /// unfinished, which refuses, rather than hanging the game.</summary>
    internal bool RunToCompletion(int maximumSteps = 100000)
    {
        int steps = 0;
        while (!Advance() && steps++ < maximumSteps)
        {
        }

        return IsComplete;
    }

    /// <summary>Drops one saved body this census knows about, because it is
    /// gone. Without it, retiring a body would leave the census reporting a
    /// body that no longer exists and refusing to build a replacement for the
    /// rest of the session.</summary>
    internal void Forget(ZDOID id)
    {
        _unidentified.Remove(id);
        foreach (KeyValuePair<string, HashSet<ZDOID>> group in _byIdentity)
        {
            group.Value.Remove(id);
        }
    }

    /// <summary>Drops every saved body the caller says is gone.
    /// <paramref name="stillExists"/> is the caller's, because asking the world
    /// whether an object still exists is a game call and this type has exactly
    /// one already.</summary>
    internal void Prune(Func<ZDOID, bool> stillExists)
    {
        if (stillExists == null)
        {
            return;
        }

        _unidentified.RemoveWhere(id => !stillExists(id));
        foreach (KeyValuePair<string, HashSet<ZDOID>> group in _byIdentity)
        {
            group.Value.RemoveWhere(id => !stillExists(id));
        }
    }

    /// <summary>How many saved bodies carry one identity.</summary>
    internal int SavedBodiesFor(NpcIdentity identity) =>
        !identity.IsEmpty && _byIdentity.TryGetValue(identity.Value, out HashSet<ZDOID>? bodies)
            ? bodies.Count
            : 0;

    /// <summary>The verdict for one identity, given what is loaded here now.
    /// </summary>
    /// <param name="identity">Whose bodies to count.</param>
    /// <param name="loadedBodies">Bodies of this prefab, carrying this
    /// identity, loaded in this scene now. Counted by the caller because it is
    /// the one holding the live list.</param>
    /// <param name="loadedIsFaulted">The single loaded body's mind has
    /// latched.</param>
    internal NpcBodyTally TallyFor(NpcIdentity identity, int loadedBodies, bool loadedIsFaulted) =>
        NpcBodyTally.Of(identity, IsComplete, SavedBodiesFor(identity), loadedBodies, Unidentified, loadedIsFaulted);

    /// <summary>Reads what a saved body carries without loading it, so a
    /// runtime can tell a player what is inside a body in ground they have not
    /// walked to. Null when the stored package cannot be read; empty when there
    /// is none.
    ///
    /// The inventory returned is a throwaway, not the body's: writing to it
    /// changes nothing anywhere, which is the whole point of being able to ask
    /// this question at all.</summary>
    internal Inventory? TryReadStoredInventory(ZDO body)
    {
        try
        {
            if (body == null || _setup.Keeps != NpcBodyKeeps.IdentityAndInventory)
            {
                return null;
            }

            var inventory = new Inventory("npc-census", null, 8, 4);
            byte[]? stored = body.GetByteArray(_setup.Fields.Inventory, null);
            if (stored != null && stored.Length > 0)
            {
                inventory.Load(new ZPackage(stored));
            }

            return inventory;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Examine(ZDO? record)
    {
        // The game's terminating call repeats the first sector, so the same
        // object arrives twice; counted twice, one body reads as two.
        if (record == null || !_seen.Add(record))
        {
            return;
        }

        string stored = record.GetString(_setup.Fields.Key, string.Empty);
        if (stored.Length == 0)
        {
            _unidentified.Add(record.m_uid);
            return;
        }

        if (!_byIdentity.TryGetValue(stored, out HashSet<ZDOID>? bodies))
        {
            bodies = new HashSet<ZDOID>();
            _byIdentity.Add(stored, bodies);
        }

        // Grouped by the identity text exactly as it is stored, never by a
        // parsed one. A body stamped with something this build cannot parse is
        // grouped under what it actually says - so it is neither silently
        // adopted into a well-formed identity's count nor thrown in with the
        // unidentified, and a runtime asking about its own identity gets an
        // answer about its own bodies.
        bodies.Add(record.m_uid);
    }
}
