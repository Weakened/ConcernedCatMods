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

    /// <summary>This source was picked and the world has not said so yet.
    ///
    /// <b>The refusal that stops material being minted.</b> The game's pick
    /// raises two routed messages: the first drops the items, and a second is
    /// what finally marks the source picked. Between them the source still
    /// reports that it can be picked and the game's own guard is still open, so
    /// picking it again yields a second full load out of nothing. Nothing about
    /// the world changes in that window, so the only thing that can refuse is a
    /// record of what we just did.</summary>
    AwaitingConfirmation = 11,

    /// <summary>Gunnar's own body has no network record. Vanilla's take
    /// <b>destroys the item and answers true</b> for a character in that state,
    /// so every gathered unit would be deleted and counted as taken. Refused.
    /// </summary>
    WorkerHasNoRecord = 12,

    /// <summary>There is too much lying here to account for. The overlap query
    /// has a ceiling; a place that reaches it cannot be enumerated, so what was
    /// already on the ground cannot be told from what this pick made.</summary>
    PlaceTooCrowded = 13,
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
/// the pick may well have happened.
///
/// <b>Who calls it.</b> <see cref="GunnarCollectionRuntime"/>, behind
/// <c>Workers/GunnarCollectionEnabled</c> (off by default) and the shared
/// work-authority rule. The two lifecycle verbs are not routed by that runtime
/// directly: it reports what happened to <see cref="CollectionLifecycle"/>,
/// which is game-free and decides which verb an event is, so the pairing that
/// must never be swapped is decided somewhere a test can reach.</summary>
internal sealed class GunnarCollectionPort : IPickLifecycle
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
    ///
    /// <b>Why this is not sixty-four any more.</b> Saturating the buffer is a
    /// refusal, and the query it bounds used to run against <i>every</i> layer:
    /// terrain, pieces, characters, triggers. Four metres of any built-up base
    /// exceeds sixty-four of those, so the exceptional refusal was going to be
    /// the ordinary outcome and the port would simply never work. The query is
    /// now masked to the layers a dropped item can be on - see
    /// <see cref="DropMask"/> - and the ceiling doubled on top of that, so
    /// saturation means what it says: this many <i>items</i> lying within four
    /// metres, which is a pile, not a base.</summary>
    internal const int MostCollidersPerGather = 128;

    /// <summary>The layers a dropped item can be on. <c>ItemDrop</c> prefabs
    /// carry the game's <c>item</c> layer, which is in the installed game's own
    /// layer table - the same table <c>audit-teamster-navigation-api.ps1</c>
    /// already reads that name out of.
    ///
    /// <b>Inclusive within that layer, on purpose.</b> This query seeds "what
    /// was already lying here", and a drop missing from that seed is a drop
    /// this pick would later credit as its own - so triggers are queried too,
    /// and a mask that resolves to nothing falls back to every layer rather
    /// than to an empty seed. What it deliberately does <i>not</i> do is add
    /// layers no drop is on: every collider admitted for nothing is one closer
    /// to the ceiling, and the ceiling is a refusal.</summary>
    private static readonly string[] DropLayers = { "item" };

    private readonly Collider[] _hits = new Collider[MostCollidersPerGather];
    private int _dropMask;
    private readonly HashSet<int> _before = new HashSet<int>();
    private readonly List<GameObject> _found = new List<GameObject>();

    private readonly PickAccounting _accounting = new PickAccounting();

    private Humanoid? _worker;
    private string _sourceKey = string.Empty;
    private Pickable? _source;
    private Vector3 _at;
    private string _expectedItem = string.Empty;
    private int _expectedUnits;
    private float _deadline;

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

        // Gunnar's OWN record, not the source's. Vanilla's take destroys the
        // item and answers true when the taking character has no network
        // record, so a body in that state would delete everything it gathered
        // and report it as taken. The source's view was always checked; this is
        // the same discipline applied to the other half of the transaction.
        if (!HasNetworkRecord(worker))
        {
            return PickRefusal.WorkerHasNoRecord;
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
            // The world says it is picked, which is the confirmation the
            // accounting was waiting for. Forgetting it here is what keeps the
            // record from growing for the life of a session.
            _accounting.Confirmed(KeyOf(source));
            return PickRefusal.NotPickableNow;
        }

        string key = KeyOf(source);
        if (key.Length == 0)
        {
            return PickRefusal.Unreadable;
        }

        switch (_accounting.MayBegin(key, expectedUnits, nowSeconds))
        {
            case PickGuard.None:
                break;
            case PickGuard.AwaitingConfirmation:
                return PickRefusal.AwaitingConfirmation;
            case PickGuard.Busy:
                return PickRefusal.Busy;
            default:
                return PickRefusal.Unreadable;
        }

        _at = source.transform.position;

        // Everything already lying here is not this pick's. Recorded BEFORE the
        // pick, so a stack a player dropped beside the stone can never be
        // counted as something Gunnar produced - and refused outright when the
        // query saturates, because a place too dense to enumerate is one where
        // that sentence stops being true.
        _before.Clear();
        if (!Scan(_found))
        {
            return PickRefusal.PlaceTooCrowded;
        }

        for (int index = 0; index < _found.Count; index++)
        {
            _before.Add(_found[index].GetInstanceID());
        }

        _worker = worker;
        _source = source;
        _sourceKey = key;
        _expectedItem = expectedItemPrefab;
        _expectedUnits = expectedUnits;
        _deadline = nowSeconds + GatherWindowSeconds;

        // THE CARVE-OUT. The one interaction this product is permitted to make,
        // on a source this client already owns, with Gunnar's own body as the
        // character. He is not a Player, so the game's skill, statistic and
        // bonus-yield branches do not run: he gets what the source gives and
        // nothing extra.
        source.Interact(_worker, repeat: false, alt: false);

        // Recorded only now, after the interaction actually happened, so the
        // record and the world agree about what was done.
        _accounting.Began(key, expectedUnits, nowSeconds);
        Phase = PickPhase.Gathering;
        return PickRefusal.None;
    }

    /// <summary>Gathers what the pick dropped. Called every frame while a pick
    /// is in flight.</summary>
    public PickProgress Poll(float nowSeconds)
    {
        if (Phase != PickPhase.Gathering)
        {
            // Zero, not the last pick's total. A finished pick hands its count
            // back once, through the Poll that finished it; leaving it readable
            // is how a caller credits units it never gathered.
            return new PickProgress(Phase, 0, string.Empty);
        }

        if (_worker == null || _worker.IsDead() || !HasNetworkRecord(_worker))
        {
            return Finish(PickPhase.Lost, "the worker went away while the drop was in the air");
        }

        ConfirmIfThePickLanded();

        if (!Scan(_found))
        {
            // Too much arrived to enumerate. What is already counted stands;
            // nothing more is credited, because from here on what was here
            // before cannot be told from what this pick made.
            return Finish(
                _accounting.Taken > 0 ? PickPhase.Done : PickPhase.Lost,
                "too much is lying here to tell this pick's drops from everything else");
        }

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

            // The game drops each unit of the main yield as its own stack of
            // one, so anything bigger arriving inside the window is somebody
            // else's - most likely a player emptying their inventory beside
            // him. The accounting refuses it, and refuses anything past what
            // this pick expected, so a pick can be short but never generous.
            if (!_accounting.MayCredit(item.m_itemData.m_stack))
            {
                continue;
            }

            // Vanilla's own take, so weight, stacking and the pickup delay are
            // the game's arithmetic and not ours. Taken only after the
            // accounting has agreed to count it, and uncounted again if the
            // take refuses: counting something still lying there is how a
            // shortfall becomes invisible.
            if (_worker.Pickup(dropped, autoequip: false, autoPickupDelay: false))
            {
                _before.Add(dropped.GetInstanceID());
            }
            else
            {
                _accounting.Uncredit();
            }
        }

        if (_accounting.IsComplete)
        {
            return Finish(PickPhase.Done, string.Empty);
        }

        if (nowSeconds >= _deadline)
        {
            // What was measured is what is reported, whether or not it is what
            // the source was expected to give. Nothing is made up to close the
            // gap in either direction.
            int gathered = _accounting.Taken;
            return gathered > 0
                ? Finish(PickPhase.Done, "the window closed with " + gathered + " of " + _expectedUnits)
                : Finish(PickPhase.Lost, "nothing this pick made reached him inside the window");
        }

        return new PickProgress(PickPhase.Gathering, _accounting.Taken, string.Empty);
    }

    /// <summary>Forgets a pick in flight, because <b>the job</b> it belonged to
    /// has gone. Takes nothing and asserts nothing.
    ///
    /// <b>What it deliberately keeps.</b> The unconfirmed record. A cancelled
    /// job is an ordinary caller event and says nothing about whether the
    /// source has settled - it still exists, and inside the settle window
    /// neither it nor the game's own guard refuses a second pick. An earlier
    /// version answered a job ending with a world-scoped reset, and
    /// <c>Begin - Interact - Forget - Begin</c> on the same source minted a
    /// second full yield. A world going away is <see cref="ForgetWorld"/>, and
    /// it is the only thing that may drop that record.</summary>
    public void Forget()
    {
        // Free, and it is what bounds the record: if the world has already said
        // the source is picked, the entry goes now rather than sitting until
        // the horizon retires it.
        ConfirmIfThePickLanded();
        Release();
        _accounting.ForgetJob();
    }

    /// <summary>Forgets a pick in flight <b>and</b> every unconfirmed source,
    /// because the world they named has gone. The sources do not exist in the
    /// next world, and a record that outlived them would refuse picks of
    /// whatever inherited their ids.</summary>
    public void ForgetWorld()
    {
        Release();
        _accounting.ForgetWorld();
    }

    private void Release()
    {
        Phase = PickPhase.Idle;
        _worker = null;
        _source = null;
        _sourceKey = string.Empty;
        _before.Clear();
        _found.Clear();
        _expectedUnits = 0;
        _expectedItem = string.Empty;
    }

    // THE IDLE GESTURE IS NOT HERE, AND THAT IS THE POINT.
    //
    // An earlier draft of this file played one on Gunnar's own body, on the
    // reading that the owner's carve-out covered it. It does not. The
    // authorization that exists is one token - the interaction below - in this
    // one file; the animation trigger and the tree damage were relayed before
    // the verification the owner required, and that verification cut the
    // allowance to what loose pickup actually needs. The validator implements
    // exactly that, and its own comment says the other two would each need
    // their own owner decision.
    //
    // So the gesture's state machine stays where it is - Domain/Collection's
    // CartUpkeepIdle, which is game-free and proves it changes nothing - and
    // the one call that would make it visible waits for a decision rather than
    // for somebody to notice it went in. It is one line when it comes.

    private PickProgress Finish(PickPhase phase, string detail)
    {
        ConfirmIfThePickLanded();
        Phase = phase;
        _worker = null;
        _source = null;
        _sourceKey = string.Empty;
        _before.Clear();
        _found.Clear();

        // The count is handed back here and cleared with it, so a later read
        // sees a finished pick rather than a live total.
        return new PickProgress(phase, _accounting.Finish(), detail);
    }

    /// <summary>Drops the source from the unconfirmed record once the world
    /// says it is picked. Until it does, that record is the only thing standing
    /// between one source and two yields.</summary>
    private void ConfirmIfThePickLanded()
    {
        if (_source != null && _sourceKey.Length != 0 && !_source.CanBePicked())
        {
            _accounting.Confirmed(_sourceKey);
        }
    }

    /// <summary>Whether a character has a network record of its own. Vanilla's
    /// take destroys the item and answers <b>true</b> without one.</summary>
    private static bool HasNetworkRecord(Humanoid worker)
    {
        ZNetView view = worker.m_nview;
        return view != null && view.IsValid() && view.GetZDO() != null;
    }

    /// <summary>A source's identity for the life of this world load: the
    /// network record's own id, never a position. Two stones a metre apart
    /// would share a rounded place, and one of them would be refused for the
    /// other's pick.</summary>
    private static string KeyOf(Pickable source)
    {
        ZNetView view = source.m_nview;
        if (view == null || !view.IsValid())
        {
            return string.Empty;
        }

        ZDO record = view.GetZDO();
        return record == null ? string.Empty : record.m_uid.ToString();
    }

    /// <summary>Everything lying within the gather radius, bounded. No
    /// reflection, no engine-wide search: one overlap query with a ceiling.
    /// </summary>
    /// <summary>Everything lying within the gather radius. Answers <b>false</b>
    /// when the query saturated, which is not the same as finding a lot: a
    /// saturated query has silently dropped colliders, so the set it produced
    /// is not the set that is there. The first version treated the ceiling as a
    /// clamp, which reads as a bound and is not one - above it a thing already
    /// on the ground can fall outside the seed and be credited later.</summary>
    private bool Scan(List<GameObject> into)
    {
        into.Clear();
        int count = Physics.OverlapSphereNonAlloc(
            _at,
            GatherRadiusMetres,
            _hits,
            DropMask,
            QueryTriggerInteraction.Collide);
        if (count >= _hits.Length)
        {
            return false;
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

        return true;
    }

    /// <summary>The mask the gather query runs against, resolved once.
    ///
    /// Falls back to every layer if the game names nothing this file knows -
    /// a version that renamed its item layer must not quietly hand back an
    /// empty seed, because an empty seed is what lets a pick credit something
    /// that was already on the ground.</summary>
    private int DropMask
    {
        get
        {
            if (_dropMask == 0)
            {
                _dropMask = LayerMask.GetMask(DropLayers);
                if (_dropMask == 0)
                {
                    _dropMask = ~0;
                }
            }

            return _dropMask;
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
