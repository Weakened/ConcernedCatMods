using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>A worker body's mind: a real networked creature that walks where it
/// is told, stops, and does nothing else at all.
///
/// <b>Why this derives from <c>BaseAI</c> and not <c>MonsterAI</c>.</b>
/// <c>BaseAI.UpdateAI</c> contains no wandering, alerting or threat logic to
/// switch off. It is an ownership gate plus housekeeping - takeoff and landing,
/// a jump timer, a random-move <i>timer</i> that nothing here reads, health
/// regeneration and a time-since-hurt counter - and it returns false when this
/// peer is not the owner. Every piece of vanilla's own thinking lives one level
/// down, in <c>MonsterAI</c> and <c>AnimalAI</c>, which call
/// <c>base.UpdateAI</c> as a gate and then do their own work. So this class
/// does not suppress vanilla behaviour; it never inherits any.
///
/// <b>The four traps this class exists to avoid</b>, each read from the
/// installed game and each of which silently breaks a naive implementation:
///
/// <list type="number">
/// <item>The shared driver iterates every AI instance with <b>no try/catch</b>,
/// at a fixed 20 Hz. One exception escaping this method aborts every creature
/// later in the list for that tick, and the rest of the physics step with it.
/// So <see cref="UpdateAI"/> catches everything, latches, and goes
/// inert.</item>
/// <item>Vanilla's own move returns true for <i>stopped</i>, not
/// <i>arrived</i> - including when the path search failed and when the path ran
/// out. Its return value is discarded here and arrival is the caller's
/// decision, from distance.</item>
/// <item>Several vanilla behaviours live <b>outside</b> <c>UpdateAI</c> and are
/// untouched by overriding it: a repeating idle sound armed in <c>Awake</c>,
/// three registered RPCs, and three broadcasts to every player on the server.
/// <see cref="SilenceWhatUpdateAiDoesNotReach"/> deals with each.</item>
/// <item>The question "do we have a path" reads like a cheap field and is a
/// full path search. Only the field read is ever asked.</item>
/// </list>
///
/// <b>What changed when three minds became one</b>, because two of the
/// differences are behaviour and not style. The cart puller's mind did not turn
/// hunting off, and now does - it never ran the code that reads the flag, so
/// this is a stricter statement of what it already was rather than a change to
/// what it does. And neither of the other two exposed the steer, face and halt
/// primitives, which they did not need; gaining them costs nothing, because
/// nothing calls a method it does not call.
///
/// <b>The planner is injected, not baked in.</b> Two of the three minds owned a
/// movement planner as a field, which is why one planner exists twice and why
/// neither could be exercised without a live creature. Here the mind offers
/// <see cref="INpcBodyMotor"/> and a per-tick callback, and whoever is planning
/// calls the primitives. Nothing in this file knows what a cart is, what a
/// shelter is, or why anybody is walking anywhere.</summary>
internal sealed class NpcBodyMind : BaseAI, INpcBodyMotor
{
    private static readonly List<NpcBodyMind> LiveMinds = new List<NpcBodyMind>();

    private bool _faulted;
    private bool _loggedFault;
    private bool _motorCommanded;
    private bool _identityRead;
    private NpcIdentity _identity;

    /// <summary>Every enabled body of every role, bound or not. A runtime
    /// filters it by its own prefab first and its own identity second; the two
    /// together are what stop two roles that share a key prefix from adopting
    /// each other's bodies.</summary>
    internal static IReadOnlyList<NpcBodyMind> Live => LiveMinds;

    /// <summary>Errors, reported whatever the diagnostic settings say. A
    /// latched fault makes this body permanently inert, which is exactly the
    /// thing a player needs told - routing it through a debug channel would
    /// hide it behind an option that is off by default.</summary>
    internal static Action<string>? ErrorLog { get; set; }

    /// <summary>Called once per owned tick, before anything else, so a
    /// runtime's mutations run inside this body's own simulation step and a
    /// goal it sets is acted on in the same tick. Anything that escapes it
    /// latches this body like any other fault.</summary>
    internal Action<NpcBodyMind, float>? WorkTick { get; set; }

    /// <summary>Verbose per-tick logging. Off unless the player asked.</summary>
    internal Action<string>? DebugLog { get; set; }

    public bool IsFaulted => _faulted;

    public bool MotorCommanded => _motorCommanded;

    public bool IsOwnedAndValid => m_nview != null && m_nview.IsValid() && m_nview.IsOwner();

    public bool HasFoundPath => FoundPath();

    /// <summary>Who this body is, from its own network object. Read once and
    /// remembered: it never changes for a body, and re-reading it every tick
    /// would be a network-object read twenty times a second for an answer that
    /// cannot have moved.</summary>
    internal NpcIdentity Identity
    {
        get
        {
            if (_identityRead)
            {
                return _identity;
            }

            _identity = ReadIdentity();
            return _identity;
        }
    }

    /// <summary>Tells a freshly built body who it is, so the first tick after a
    /// spawn does not have to go back to the network object for something the
    /// spawner already knows.</summary>
    internal void RememberIdentity(NpcIdentity identity)
    {
        _identity = identity;
        _identityRead = true;
    }

    // Public rather than protected because the build compiles against the
    // publicized assemblies, where these members are public. The real assembly
    // declares them protected virtual; widening accessibility in an override is
    // permitted by the runtime, and is how every mod that subclasses a
    // publicized type binds to it.
    public override void Awake()
    {
        base.Awake();
        SilenceWhatUpdateAiDoesNotReach();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (!LiveMinds.Contains(this))
        {
            LiveMinds.Add(this);
        }
    }

    public override void OnDisable()
    {
        LiveMinds.Remove(this);
        base.OnDisable();
    }

    public override bool UpdateAI(float dt)
    {
        // Trap 1. Nothing may escape this method into the shared driver.
        if (_faulted)
        {
            return false;
        }

        try
        {
            // The ownership gate: false means this peer does not own the body,
            // or the view is invalid. It carries no wandering, alerting or
            // threat behaviour - see the class comment.
            if (!base.UpdateAI(dt))
            {
                return false;
            }

            WorkTick?.Invoke(this, dt);
            return true;
        }
        catch (Exception exception)
        {
            _faulted = true;
            TryHaltQuietly();
            if (!_loggedFault)
            {
                _loggedFault = true;
                try
                {
                    ErrorLog?.Invoke(
                        "An NPC body faulted and is now inert; it will not act again this session. " + exception);
                }
                catch
                {
                    // Already faulting; nothing may escape into the game's loop.
                }
            }

            return false;
        }
    }

    public bool TryFindPath(Vector3 point) => Pathfinding.instance != null && FindPath(point);

    public void WalkTo(Vector3 point, float arrivalRadiusMetres)
    {
        if (Pathfinding.instance == null)
        {
            return;
        }

        m_character.SetRun(false);
        m_character.SetWalk(true);
        _motorCommanded = true;

        // Trap 2: the return value means "stopped", not "arrived", so it is
        // deliberately discarded here and arrival is the caller's decision.
        MoveTo(Time.fixedDeltaTime, point, arrivalRadiusMetres, run: false);
    }

    public void SteerToward(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            Halt();
            return;
        }

        m_character.SetRun(false);
        m_character.SetWalk(true);
        _motorCommanded = true;
        MoveTowards(direction.normalized, run: false);
    }

    public void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            LookTowards(direction.normalized);
        }
    }

    public void Halt()
    {
        _motorCommanded = false;
        StopMoving();
    }

    private NpcIdentity ReadIdentity()
    {
        _identityRead = true;

        NpcBody? body = GetComponent<NpcBody>();
        if (body != null && body.IsLoaded)
        {
            return body.Identity;
        }

        string prefab = NpcBodyContracts.PrefabNameOf(gameObject == null ? string.Empty : gameObject.name);
        if (!NpcBodyContracts.TryFind(prefab, out NpcBodySetup setup))
        {
            // Nobody registered this prefab, so this library has no key to read
            // and will not guess one. An empty identity binds to nothing.
            _identityRead = false;
            return default;
        }

        ZNetView view = GetComponent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            // The object is not up yet. Ask again next time rather than
            // remembering "nobody".
            _identityRead = false;
            return default;
        }

        string stored = view.GetZDO().GetString(setup.Fields.Key, string.Empty);
        return NpcIdentity.TryParse(stored, out NpcIdentity identity) ? identity : default;
    }

    private void TryHaltQuietly()
    {
        try
        {
            Halt();
        }
        catch
        {
            // Already faulting; a second failure here must not escape either.
        }
    }

    /// <summary>Turns off every vanilla behaviour that <c>UpdateAI</c> does not
    /// reach. Each line corresponds to a specific call site in the installed
    /// build.</summary>
    private void SilenceWhatUpdateAiDoesNotReach()
    {
        // BaseAI.Awake arms a repeating idle sound. It fires on Unity's own
        // schedule, never through UpdateAI, so overriding UpdateAI does not
        // stop it. Cancel it, and empty the effect as well so a future prefab
        // with a sound configured cannot resurrect it.
        CancelInvoke("DoIdleSound");
        m_idleSound = new EffectList();
        m_idleSoundChance = 0f;

        // Being alerted early-outs entirely when this flag is false. That one
        // flag neutralises the whole alert path at its source - the animator
        // flag, the alerted effect, the boss counter and the broadcast - which
        // is better than overriding the method, because the RPC behind it is
        // private and non-virtual and would still reach it.
        m_canBeAlerted = false;

        // Three broadcasts to every player on the server, one each on spawn, on
        // death and on being alerted, all guarded by a non-empty string. A
        // worker has no business announcing itself to a whole server.
        m_spawnMessage = string.Empty;
        m_deathMessage = string.Empty;
        m_alertedMessage = string.Empty;

        // Not a threat and not a target-seeker. These are the fields vanilla's
        // own sensing reads; a body that never runs MonsterAI does not use
        // them, and leaving them at creature defaults would mislead anyone
        // reading this later.
        m_viewRange = 0f;
        m_hearRange = 0f;
        SetHuntPlayer(hunt: false);

        // The pathfinding agent a person walks as. Already the default; stated
        // because every movement budget written against this assumes it.
        m_pathAgentType = Pathfinding.AgentType.Humanoid;
    }
}
