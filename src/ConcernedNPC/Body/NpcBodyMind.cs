using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;
using TheConcernedCat.Diagnostics;

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
/// shelter is, or why anybody is walking anywhere.
///
/// <b>Why this class does not itself implement <see cref="INpcBodyMotor"/>.</b>
/// It used to, with every driving verb public, so holding a mind was the same
/// thing as being allowed to drive it - and the library handed every role's
/// minds to every consumer in one static list. The verbs are internal now and
/// the motor is a separate object handed out by <see cref="TryDrive"/> against
/// the <see cref="BodyLease"/> the arbiter granted. An explicit interface
/// implementation would not have done: a cast reaches one. There is no motor
/// without a lease, and no way to cast to one either.</summary>
public sealed class NpcBodyMind : BaseAI
{
    private static readonly List<NpcBodyMind> LiveMinds = new List<NpcBodyMind>();

    private bool _faulted;
    private bool _loggedFault;
    private bool _motorCommanded;
    private bool _identityRead;
    private NpcIdentity _identity;

    /// <summary>Every enabled body of every role, bound or not.
    ///
    /// <b>Internal.</b> Its own doc used to say "a runtime filters it by its
    /// own prefab first and its own identity second" - advice delivered across
    /// an assembly boundary, guarding the one rule the arbiter exists to
    /// enforce, and a consumer that ignored it could pick another product's
    /// worker out of this list and drive him. A consumer asks
    /// <see cref="LiveFor"/>, which does the prefab half itself, and
    /// <see cref="TryDrive"/>, which does the identity half against a
    /// lease.</summary>
    internal static IReadOnlyList<NpcBodyMind> Live => LiveMinds;

    /// <summary>The enabled minds of one role's prefab, and nobody else's.
    ///
    /// A contract that is not the one its prefab was registered under names no
    /// role, and a role that does not exist has no bodies.</summary>
    public static IReadOnlyList<NpcBodyMind> LiveFor(NpcBodyContract contract)
    {
        if (!NpcBodyContracts.IsRegistered(contract))
        {
            return Array.Empty<NpcBodyMind>();
        }

        var mine = new List<NpcBodyMind>();
        foreach (NpcBodyMind mind in LiveMinds)
        {
            if (mind != null && string.Equals(mind.PrefabName, contract.PrefabName, StringComparison.Ordinal))
            {
                mine.Add(mind);
            }
        }

        return mine;
    }

    /// <summary>Errors, reported whatever the diagnostic settings say. A
    /// latched fault makes this body permanently inert, which is exactly the
    /// thing a player needs told - routing it through a debug channel would
    /// hide it behind an option that is off by default.
    ///
    /// <b>An event, not a settable property.</b> With several products loading
    /// this library, a property means whichever one assigns it last takes the
    /// error channel away from the others, silently, and an inert body becomes
    /// an inert and unreported one. <c>=</c> no longer compiles; <c>+=</c> and
    /// <c>-=</c> are the operations, and every subscriber is raised
    /// independently so one throwing handler costs only itself.</summary>
    public static event Action<string>? ErrorLog;

    /// <summary>Called once per owned tick, before anything else, so a
    /// runtime's mutations run inside this body's own simulation step and a
    /// goal it sets is acted on in the same tick. Anything that escapes it
    /// latches this body like any other fault.</summary>
    public Action<NpcBodyMind, float>? WorkTick { get; set; }

    /// <summary>Verbose per-tick logging. Off unless the player asked.</summary>
    public Action<string>? DebugLog { get; set; }

    public bool IsFaulted => _faulted;

    public bool MotorCommanded => _motorCommanded;

    public bool IsOwnedAndValid => m_nview != null && m_nview.IsValid() && m_nview.IsOwner();

    public bool HasFoundPath => FoundPath();

    /// <summary>The prefab this body is an instance of, read off the object's
    /// own name - the one fact the host cannot have failed to set, and the same
    /// one <see cref="ReadIdentity"/> uses.</summary>
    internal string PrefabName =>
        NpcBodyContracts.PrefabNameOf(gameObject == null ? string.Empty : gameObject.name);

    /// <summary>Who this body is, from its own network object. Read once and
    /// remembered: it never changes for a body, and re-reading it every tick
    /// would be a network-object read twenty times a second for an answer that
    /// cannot have moved.</summary>
    public NpcIdentity Identity
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
    /// spawner already knows.
    ///
    /// <b>Internal.</b> The only caller that legitimately knows an identity
    /// before the object does is the factory that just stamped it. A role that
    /// could set this could make a body answer with an identity its own network
    /// object does not carry, and the census - which reads the object - would
    /// disagree with every runtime that reads the mind.</summary>
    internal void RememberIdentity(NpcIdentity identity)
    {
        _identity = identity;
        _identityRead = true;
    }

    /// <summary>Hands back the motor for this body, to a caller that can show
    /// the lease the arbiter granted for it.
    ///
    /// <b>Why the lease is a parameter and not a courtesy.</b> The same reason
    /// <see cref="NpcBodyBuildGate"/> takes one. The arbiter hands a lease out
    /// on a grant and only on a grant, so a role that was refused - or that is
    /// holding somebody else's body - has nothing to pass, and driving a body
    /// it does not hold stops being a discipline and becomes a refusal it can
    /// read. The motor that comes back is not this object: it is a separate
    /// one that re-asks the lease on every command, because a lease that
    /// expires while a runtime is walking a body is exactly the case a check
    /// taken once at the start cannot see.
    ///
    /// The reads a role legitimately does without holding anything -
    /// <see cref="IsFaulted"/>, <see cref="Identity"/> - stay on this class.
    /// Nothing that moves the body does.</summary>
    /// <param name="lease">The permission the arbiter granted for this
    /// identity. Null is a refusal, not a default.</param>
    /// <param name="motor">The motor, or null on a refusal.</param>
    /// <param name="reason">Why it was refused, in words a role author can act
    /// on. Empty on success.</param>
    public bool TryDrive(BodyLease? lease, out INpcBodyMotor? motor, out string reason)
    {
        motor = null;

        if (lease == null)
        {
            reason = "no lease was supplied, so nothing granted permission to drive this body";
            return false;
        }

        if (lease.Kind != NpcBodyKind.Worker)
        {
            reason = "the lease permits a " + lease.Kind + " body and this is a worker body's mind";
            return false;
        }

        NpcIdentity identity = Identity;
        if (identity.IsEmpty)
        {
            // Not "no", but "not yet": the object may not be up. A remembered
            // refusal here would outlive its reason.
            reason = "this body has not read its identity yet, so nothing can be said about who may drive it";
            return false;
        }

        if (!lease.Identity.Equals(identity))
        {
            reason = "the lease is for " + lease.Identity + " and this body is " + identity;
            return false;
        }

        // LAST, and nothing may be added below it, for the reason the build
        // gate asks it last: every refusal above is a fact that was already
        // true on entry, and this is the one whose answer can have changed
        // since the claim was taken.
        if (!lease.IsActive)
        {
            reason = "the lease for " + lease.Identity
                + " is no longer held, so permission to drive this body has gone";
            return false;
        }

        motor = new NpcLeasedMotor(this, lease);
        reason = string.Empty;
        return true;
    }

    /// <summary>Says one thing to every subscriber, each on its own, so a
    /// product whose log handler throws does not cost another product the
    /// report - and so nothing thrown in a handler escapes into the game's
    /// shared AI driver.</summary>
    internal static void RaiseErrorLog(string message)
    {
        Delegate[]? handlers = ErrorLog?.GetInvocationList();
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<string>)handler)(message);
            }
            catch (Exception)
            {
                // Reporting a failure must never become one.
            }
        }
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
                    RaiseErrorLog(
                        "An NPC body faulted and is now inert; it will not act again this session. " + SafeFailure.Describe(exception));
                }
                catch
                {
                    // Already faulting; nothing may escape into the game's loop.
                }
            }

            return false;
        }
    }

    internal bool TryFindPath(Vector3 point) => Pathfinding.instance != null && FindPath(point);

    internal void WalkTo(Vector3 point, float arrivalRadiusMetres)
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

    internal void SteerToward(Vector3 point)
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

    internal void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            LookTowards(direction.normalized);
        }
    }

    internal void Halt()
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
