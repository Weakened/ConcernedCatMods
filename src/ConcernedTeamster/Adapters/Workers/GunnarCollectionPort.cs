using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Why a pick was refused before anything was attempted. Zero means it
/// was not.</summary>
internal enum PickRefusal
{
    /// <summary>Nothing refused.</summary>
    None = 0,

    /// <summary>The player has not opted in. Nothing in this file runs for
    /// somebody who installed Concerned Teamster for its telemetry.</summary>
    FeatureOff = 1,

    /// <summary>The start-up probe could not verify every game member this file
    /// uses, so none of them is called.</summary>
    SeamUnavailable = 2,

    /// <summary>There is no local player. The game's own pick path dereferences
    /// it and would throw.</summary>
    NoLocalPlayer = 3,

    /// <summary>No worker body to pick with, or it is not alive.</summary>
    NoWorker = 4,

    /// <summary>The source has no valid network object.</summary>
    ViewInvalid = 5,

    /// <summary>This client does not own the source. It is never claimed.
    /// </summary>
    NotOwnedHere = 6,

    /// <summary>Already picked, disabled, or not something that can be picked
    /// at all.</summary>
    NotPickableNow = 7,

    /// <summary>The source is one the game refuses to pick out of tar, and that
    /// path speaks to the character. Refused rather than entered.</summary>
    TarPrevents = 8,

    /// <summary>Something about the source could not be read. Unknown refuses.
    /// </summary>
    Unreadable = 9,

    /// <summary>A pick is already in flight. One body picks one thing.</summary>
    Busy = 10,
}

/// <summary>Where a started pick has got to.</summary>
internal enum PickPhase
{
    /// <summary>Nothing in flight.</summary>
    Idle = 0,

    /// <summary>The source has been picked and the drops it made are being
    /// gathered.</summary>
    Gathering = 1,

    /// <summary>Finished. <see cref="PickProgress.Taken"/> is what was
    /// <b>measured</b> into his inventory.</summary>
    Done = 2,

    /// <summary>The window closed with nothing gathered. The source may still
    /// have been picked, so nothing is assumed about it.</summary>
    Lost = 3,
}

/// <summary>What a started pick has done so far.</summary>
internal readonly struct PickProgress
{
    public PickProgress(PickPhase phase, int taken, string detail)
    {
        Phase = phase;
        Taken = taken;
        Detail = detail ?? string.Empty;
    }

    public PickPhase Phase { get; }

    /// <summary>Units actually in his inventory, counted from the stacks that
    /// went in - never the number the source was expected to give.</summary>
    public int Taken { get; }

    public string Detail { get; }
}

/// <summary>The one file in Concerned Teamster allowed to make Gunnar act on a
/// resource (#381, under the owner's carve-out of 2026-09-19).
///
/// <b>Why this file exists at all, and why it is only this file.</b> Teamster
/// shipped as an observational mod, and the #313 worker-runtime scope audit is
/// what makes that claim true rather than stated: it bans an interaction and an
/// RPC send from Gunnar's runtime outright. Picking a stone up is
/// <c>Pickable.Interact</c>, which sends one. The owner granted a scoped
/// carve-out for exactly that, and the carve-out is confined to this file by the
/// validator - which is the difference between an allowance and an exemption.
/// Nothing else in this product may spell these calls, now or later.
///
/// <b>What it is allowed to do.</b> Pick a source Gunnar was planned onto, and
/// gather the drops it makes into his own inventory. That is all. It moves no
/// cart, writes no mass, no force, no velocity, no position, no ownership and no
/// mod data into a vanilla object.
///
/// <b>Fail-closed, everywhere.</b> Every refusal below is a fact that could not
/// be established, not a fact that was established as false. In particular this
/// port <b>requires that this client already owns the source</b>. The game's own
/// pick routes an RPC to the owner and its handler drops the items there, so a
/// source owned elsewhere would spill its contents on another machine and count
/// as nothing here. Requiring ownership we already hold is the same rule the
/// cart seam follows, and it is why nothing here ever takes ownership.
///
/// <b>Two calls, one window.</b> The game's pick does not hand anything back: it
/// routes an RPC whose handler drops items on the ground. So a pick is started
/// and then gathered over a bounded window, and what is reported is what was
/// <b>measured</b> into his inventory. A window that closes with nothing is
/// <see cref="PickPhase.Lost"/> and asserts nothing about the source, because
/// the pick may well have happened.</summary>
internal sealed class GunnarCollectionPort
{
    /// <summary>How long to keep gathering after a pick. The drop arrives on the
    /// owner's next routed-RPC turn, which is the following frame in the only
    /// configuration this runtime is allowed to run in (host, no peers); two
    /// seconds is that with room for a slow frame, and short enough that a lost
    /// drop is noticed rather than waited on.</summary>
    internal const float GatherWindowSeconds = 2f;

    /// <summary>How far from the source a drop may land and still be this
    /// pick's. The game spawns them at the source with a small offset per
    /// item.</summary>
    internal const float GatherRadiusMetres = 4f;

    /// <summary>The most colliders one gather may look at. A bound, not a
    /// target: an overlap query with no ceiling is a frame a player feels.
    /// </summary>
    internal const int MostCollidersPerGather = 64;

    private readonly Collider[] _hits = new Collider[MostCollidersPerGather];
    private readonly HashSet<int> _before = new HashSet<int>();
    private readonly List<GameObject> _found = new List<GameObject>();

    private Humanoid? _worker;
    private Vector3 _at;
    private string _expectedItem = string.Empty;
    private int _expectedUnits;
    private float _deadline;
    private int _taken;

    /// <summary>Where a started pick has got to.</summary>
    public PickPhase Phase { get; private set; } = PickPhase.Idle;

    /// <summary>Starts a pick, or says why it will not.
    ///
    /// Every check is made from this frame's own reads, and the first failure is
    /// the refusal - the same discipline the cart hitch follows, for the same
    /// reason: a precondition read a frame earlier is a precondition about a
    /// world that has moved.</summary>
    public PickRefusal Begin(
        Pickable? source,
        Humanoid? worker,
        bool featureEnabled,
        bool seamAvailable,
        string expectedItemPrefab,
        int expectedUnits,
        float nowSeconds)
    {
        if (Phase == PickPhase.Gathering)
        {
            return PickRefusal.Busy;
        }

        if (!featureEnabled)
        {
            return PickRefusal.FeatureOff;
        }

        if (!seamAvailable)
        {
            return PickRefusal.SeamUnavailable;
        }

        if (Player.m_localPlayer == null)
        {
            // The game's own pick handler dereferences the local player to place
            // its effect. Without one it throws, and a throw out of a worker
            // tick is how a runtime latches inert.
            return PickRefusal.NoLocalPlayer;
        }

        if (worker == null || worker.IsDead())
        {
            return PickRefusal.NoWorker;
        }

        if (source == null)
        {
            return PickRefusal.Unreadable;
        }

        ZNetView view = source.m_nview;
        if (view == null || !view.IsValid())
        {
            return PickRefusal.ViewInvalid;
        }

        if (!view.IsOwner())
        {
            return PickRefusal.NotOwnedHere;
        }

        if (source.m_tarPreventsPicking)
        {
            return PickRefusal.TarPrevents;
        }

        if (source.m_itemPrefab == null || expectedUnits <= 0 || string.IsNullOrEmpty(expectedItemPrefab))
        {
            return PickRefusal.Unreadable;
        }

        if (!source.CanBePicked())
        {
            return PickRefusal.NotPickableNow;
        }

        _worker = worker;
        _at = source.transform.position;
        _expectedItem = expectedItemPrefab;
        _expectedUnits = expectedUnits;
        _deadline = nowSeconds + GatherWindowSeconds;
        _taken = 0;

        // Everything already lying here is not this pick's. Recorded before the
        // pick, so a stack a player dropped beside the stone can never be
        // counted as something Gunnar produced.
        _before.Clear();
        Scan(_found);
        for (int index = 0; index < _found.Count; index++)
        {
            _before.Add(_found[index].GetInstanceID());
        }

        // THE CARVE-OUT. The one interaction this product is permitted to make,
        // on a source this client already owns, with Gunnar's own body as the
        // character. He is not a Player, so the game's skill, statistic and
        // bonus-yield branches do not run: he gets what the source gives and
        // nothing extra.
        source.Interact(_worker, repeat: false, alt: false);

        Phase = PickPhase.Gathering;
        return PickRefusal.None;
    }

    /// <summary>Gathers what the pick dropped. Called every frame while a pick
    /// is in flight.</summary>
    public PickProgress Poll(float nowSeconds)
    {
        if (Phase != PickPhase.Gathering)
        {
            return new PickProgress(Phase, _taken, string.Empty);
        }

        if (_worker == null || _worker.IsDead())
        {
            return Finish(PickPhase.Lost, "the worker went away while the drop was in the air");
        }

        Scan(_found);
        for (int index = 0; index < _found.Count; index++)
        {
            GameObject dropped = _found[index];
            if (_before.Contains(dropped.GetInstanceID()))
            {
                continue;
            }

            ItemDrop item = dropped.GetComponent<ItemDrop>();
            if (item == null || item.m_itemData == null)
            {
                continue;
            }

            string name = ItemPrefabName(item);
            if (!string.Equals(name, _expectedItem, System.StringComparison.Ordinal))
            {
                // Not what this pick was for. Left exactly where it is: a thing
                // he was not planned onto is not his to take.
                continue;
            }

            int stack = item.m_itemData.m_stack;
            if (stack <= 0)
            {
                continue;
            }

            // Vanilla's own take, so weight, stacking and the pickup delay are
            // the game's arithmetic and not ours.
            if (_worker.Pickup(dropped, autoequip: false, autoPickupDelay: false))
            {
                _taken += stack;
                _before.Add(dropped.GetInstanceID());
            }
        }

        if (_taken >= _expectedUnits)
        {
            return Finish(PickPhase.Done, string.Empty);
        }

        if (nowSeconds >= _deadline)
        {
            // What was measured is what is reported, whether or not it is what
            // the source was expected to give. Nothing is made up to close the
            // gap in either direction.
            return _taken > 0
                ? Finish(PickPhase.Done, "the window closed with " + _taken + " of " + _expectedUnits)
                : Finish(PickPhase.Lost, "nothing this pick made reached him inside the window");
        }

        return new PickProgress(PickPhase.Gathering, _taken, string.Empty);
    }

    /// <summary>Forgets a pick in flight, because the world or the job it
    /// belonged to has gone. Takes nothing and asserts nothing.</summary>
    public void Forget()
    {
        Phase = PickPhase.Idle;
        _worker = null;
        _before.Clear();
        _found.Clear();
        _taken = 0;
        _expectedUnits = 0;
        _expectedItem = string.Empty;
    }

    /// <summary>Plays one idle gesture on Gunnar's own body.
    ///
    /// <b>Cosmetic, and structurally so.</b> This is a transient animation
    /// trigger on his own character and nothing else: it writes no state, saves
    /// nothing, and touches no other object in the world. It is in this file
    /// because it reaches the game through the same replicated path the
    /// carve-out covers, not because it does anything to a cart.
    ///
    /// The trigger is vanilla's own <c>interact</c> gesture rather than a
    /// bespoke hammer swing, deliberately: it is a parameter every humanoid
    /// animator in the game already has, where an invented name would warn on
    /// every call and animate nothing. Whether it reads as "he is fiddling with
    /// his cart" is an in-game question for the owner, and it is one line to
    /// change because nothing about it is durable.</summary>
    public static void PlayIdleGesture(Humanoid? worker, bool featureEnabled, bool seamAvailable)
    {
        if (!featureEnabled || !seamAvailable || worker == null || worker.IsDead())
        {
            return;
        }

        ZSyncAnimation animation = worker.m_zanim;
        if (animation == null)
        {
            return;
        }

        animation.SetTrigger(GunnarHaulingDefaults.IdleGestureTrigger);
    }

    private PickProgress Finish(PickPhase phase, string detail)
    {
        Phase = phase;
        _worker = null;
        _before.Clear();
        _found.Clear();
        return new PickProgress(phase, _taken, detail);
    }

    /// <summary>Everything lying within the gather radius, bounded. No
    /// reflection, no engine-wide search: one overlap query with a ceiling.
    /// </summary>
    private void Scan(List<GameObject> into)
    {
        into.Clear();
        int count = Physics.OverlapSphereNonAlloc(_at, GatherRadiusMetres, _hits);
        if (count > _hits.Length)
        {
            count = _hits.Length;
        }

        for (int index = 0; index < count; index++)
        {
            Collider hit = _hits[index];
            if (hit == null)
            {
                continue;
            }

            ItemDrop item = hit.GetComponentInParent<ItemDrop>();
            if (item != null)
            {
                into.Add(item.gameObject);
            }
        }
    }

    /// <summary>The prefab name of what a dropped item is, with the clone suffix
    /// the host adds removed - the same reading the game's own helper does.
    /// </summary>
    private static string ItemPrefabName(ItemDrop item)
    {
        string name = item.gameObject.name;
        int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        return clone < 0 ? name : name.Substring(0, clone);
    }
}
