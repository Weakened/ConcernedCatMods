using System;
using System.Collections.Generic;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's worker body's mind: a <c>BaseAI</c> that walks where the
/// haul executor tells it and does nothing else at all.
///
/// Copied from Concerned Foreman's proven worker pattern (products never share
/// game code, DECISIONS.md D2), for the same reasons, all read from the
/// installed 1.0.12 assembly (<c>docs/mods/concerned-foreman/WORKER_ACTOR_SPIKE.md</c>):
/// <list type="number">
/// <item><c>BaseAI.UpdateAI</c> holds no creature behaviour (wandering,
/// alerting and threat response live in <c>MonsterAI</c>), so deriving from
/// <c>BaseAI</c> inherits no vanilla mind to switch off.</item>
/// <item>The shared driver calls <c>UpdateAI</c> at 20 Hz with no try/catch, so
/// one escaping exception stalls every later creature: the tick catches,
/// latches and goes inert.</item>
/// <item><c>MoveTo</c> answers "stopped", not "arrived": arrival is decided by
/// the executor from distance.</item>
/// <item><c>HavePath</c> is a full path search: only <c>FoundPath()</c>, a field
/// read, is ever asked.</item>
/// </list>
/// Behaviours outside <c>UpdateAI</c> (idle sound, alert RPCs, broadcast
/// messages) are silenced in <see cref="Awake"/>.</summary>
internal sealed class TeamsterWorkerAI : BaseAI
{
    private static readonly List<TeamsterWorkerAI> LiveBodies = new List<TeamsterWorkerAI>();

    private bool _faulted;
    private bool _loggedFault;
    private bool _motorCommanded;
    private string? _identity;

    /// <summary>Every enabled worker body of this prefab, bound or not.</summary>
    internal static IReadOnlyList<TeamsterWorkerAI> Live => LiveBodies;

    /// <summary>Set by the runtime: the 20 Hz haul tick for a body. A body the
    /// runtime has not bound does nothing at all.</summary>
    internal static Action<TeamsterWorkerAI>? TickHandler { get; set; }

    /// <summary>Errors are reported whatever the diagnostics settings say: a
    /// worker that went inert is exactly what a player needs told.</summary>
    internal static Action<string>? ErrorLog { get; set; }

    internal bool IsFaulted => _faulted;

    internal bool MotorCommanded => _motorCommanded;

    internal bool HasFoundPath => FoundPath();

    // Public rather than protected: the build compiles against the publicized
    // game assembly, where BaseAI's protected members are public.
    public override void Awake()
    {
        base.Awake();
        SilenceWhatUpdateAiDoesNotReach();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (!LiveBodies.Contains(this))
        {
            LiveBodies.Add(this);
        }
    }

    public override void OnDisable()
    {
        LiveBodies.Remove(this);
        base.OnDisable();
    }

    public override bool UpdateAI(float dt)
    {
        if (_faulted)
        {
            return false;
        }

        try
        {
            // The ownership gate: false means this peer does not own the body.
            if (!base.UpdateAI(dt))
            {
                return false;
            }

            TickHandler?.Invoke(this);
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
                    ErrorLog?.Invoke("Gunnar's worker faulted and is now inert; he will not act again this session. " + SafeFailure.Describe(exception));
                }
                catch
                {
                    // Already faulting; nothing may escape into the game's loop.
                }
            }

            return false;
        }
    }

    /// <summary>The identity stored in this body's own network object, read
    /// once it is valid.</summary>
    internal string Identity
    {
        get
        {
            if (_identity == null)
            {
                ZNetView view = GetComponent<ZNetView>();
                if (view != null && view.IsValid())
                {
                    _identity = view.GetZDO().GetString("tcc.worker.key", string.Empty);
                }
            }

            return _identity ?? string.Empty;
        }
    }

    internal void RememberIdentity(string identity) => _identity = identity;

    /// <summary>Walks to a point with the body's own pathfinding, at walking
    /// pace.</summary>
    internal void WalkTo(Vector3 point, float radius)
    {
        m_character.SetRun(false);
        m_character.SetWalk(true);
        _motorCommanded = true;
        MoveTo(0.05f, point, radius, run: false);
    }

    /// <summary>Steers straight toward a point (the route planner vouched for
    /// the segment), at walking pace. Vanilla only moves a body roughly facing
    /// its direction.</summary>
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

    private void TryHaltQuietly()
    {
        try
        {
            Halt();
        }
        catch
        {
            // Already faulting; a second failure must not escape either.
        }
    }

    /// <summary>Each line matches a call site in the installed 1.0.12
    /// <c>BaseAI</c> that runs outside <c>UpdateAI</c>.</summary>
    private void SilenceWhatUpdateAiDoesNotReach()
    {
        CancelInvoke("DoIdleSound");
        m_idleSound = new EffectList();
        m_idleSoundChance = 0f;

        // SetAlerted early-outs on this flag, closing the alert RPC path too.
        m_canBeAlerted = false;

        m_spawnMessage = string.Empty;
        m_deathMessage = string.Empty;
        m_alertedMessage = string.Empty;

        m_viewRange = 0f;
        m_hearRange = 0f;
        m_pathAgentType = Pathfinding.AgentType.Humanoid;
    }
}
