using System;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>The Steward's body: a real networked creature that walks where it
/// is told, stops, and does nothing else at all.
///
/// <b>Why this derives from <c>BaseAI</c> and not <c>MonsterAI</c>.</b>
/// <c>BaseAI.UpdateAI</c> contains no wandering, alerting or threat logic to
/// switch off. It is an ownership gate plus housekeeping — takeoff/landing, a
/// jump timer, a random-move <i>timer</i> that nothing here reads, health
/// regeneration and a time-since-hurt counter — and it returns false when this
/// peer is not the owner. Every piece of vanilla's own thinking lives one level
/// down, in <c>MonsterAI.UpdateAI</c> and <c>AnimalAI.UpdateAI</c>, which call
/// <c>base.UpdateAI</c> as a gate and then do their own work. So this class does
/// not suppress vanilla behaviour; it never inherits any.
///
/// <b>The four traps this class exists to avoid</b>, each of which silently
/// breaks a naive implementation (full evidence in
/// <c>docs/mods/concerned-foreman/WORKER_ACTOR_SPIKE.md</c>, re-confirmed
/// against 1.0.14 for #340):
///
/// <list type="number">
/// <item>The shared driver, <c>MonoUpdatersExtra.UpdateAI</c>, iterates
/// <c>BaseAI.Instances</c> with <b>no try/catch</b>, at a fixed 20 Hz. One
/// exception escaping this method aborts every creature later in the list for
/// that tick and the rest of <c>FixedUpdate</c> with it. So
/// <see cref="UpdateAI"/> catches everything, latches, and goes inert.</item>
/// <item><c>BaseAI.MoveTo</c> returns <c>true</c> for <i>stopped</i>, not
/// <i>arrived</i> — including when <c>FindPath</c> failed and when the path ran
/// out. Arrival is decided here, from distance.</item>
/// <item>Several vanilla behaviours live <b>outside</b> <c>UpdateAI</c> and are
/// untouched by overriding it: a repeating idle sound armed in <c>Awake</c>,
/// three registered RPCs, and three <c>MessageHud.MessageAll</c> broadcasts.
/// <see cref="SilenceWhatUpdateAiDoesNotReach"/> deals with each.</item>
/// <item><c>BaseAI.HavePath</c> reads like a cheap question and is not:
/// <c>Pathfinding.HavePath</c> forwards straight to <c>GetPath</c> with
/// <c>requireFullPath: true</c>, so asking it every tick would be a full path
/// search twenty times a second. The planner is told about paths through
/// <c>FoundPath()</c>, a field read of the last result.</item>
/// </list>
///
/// <b>Nothing in this class writes a position, a velocity or a force.</b>
/// Movement is <c>MoveTo</c> and <c>StopMoving</c>, vanilla's own motor, and
/// there is deliberately no member here that would let a caller do otherwise.
/// </summary>
internal sealed class StewardWorkerAI : BaseAI
{
    private readonly WorkerMovementPlanner _planner = new(WorkerMovementBudget.Default);

    /// <summary>How many ticks a site check is reused for. The hazard check
    /// casts about and walks the burning-area list, so running it at the full
    /// 20 Hz would be an unbudgeted per-tick cost. Ten ticks is half a second —
    /// far faster than a fire spreads or a tide moves, and twenty times
    /// cheaper.</summary>
    private const int SiteCheckIntervalTicks = 10;

    private StewardSitePolicy _policy = StewardSitePolicy.Refusing;
    private bool _faulted;
    private bool _loggedFault;
    private int _ticksUntilSiteCheck;
    private bool _goalInLoadedGround;
    private bool _goalIsHazardous = true;

    /// <summary>Set by the runtime. When it answers false he refuses to act and
    /// says why — he does not idle silently, because a silent Steward and a
    /// refusing one look identical to a player and only one of them is a bug.
    /// </summary>
    internal Func<bool> AuthorityGate { get; set; } = () => false;

    internal Action<string>? DebugLog { get; set; }

    /// <summary>Errors, reported whatever the diagnostic settings say. A latched
    /// fault makes him permanently inert this session, which is exactly the
    /// thing a player needs told.</summary>
    internal Action<string>? ErrorLog { get; set; }

    /// <summary>Called once per owned tick, before movement is planned, so the
    /// upkeep loop's mutations run inside his own simulation step and a goal it
    /// sets is planned in the same tick. Anything that escapes it latches this
    /// body like any other fault.</summary>
    internal Action<float>? WorkTick { get; set; }

    internal bool IsFaulted => _faulted;

    /// <summary>The view is valid and owned by this process.</summary>
    internal bool IsOwnedAndValid => m_nview != null && m_nview.IsValid() && m_nview.IsOwner();

    /// <summary>Where the walk stands, decided by distance and never by
    /// <c>MoveTo</c>'s return value (trap 2).</summary>
    internal WorkerWalkStatus WalkStatus
    {
        get
        {
            if (!_planner.HasGoal)
            {
                return WorkerWalkStatus.Idle;
            }

            if (_planner.IsDeferred)
            {
                return WorkerWalkStatus.Deferred;
            }

            WorkerGoal goal = _planner.Goal;
            return ToSitePoint(transform.position).HorizontalDistanceTo(goal.Point) <= goal.ArrivalTolerance
                ? WorkerWalkStatus.Arrived
                : WorkerWalkStatus.Walking;
        }
    }

    internal WorkerDeferralReason DeferredReason => _planner.DeferredReason;

    /// <summary>Total path requests authorised, so a diagnostic command can
    /// state the measured cost rather than claim a budget.</summary>
    internal long TotalPathRequests => _planner.TotalPathRequests;

    // Public rather than protected because the build compiles against the
    // publicized assemblies, where BaseAI.Awake is public. The real assembly
    // declares it protected virtual; widening accessibility in an override is
    // permitted by the runtime, and is how every mod that subclasses a
    // publicized type binds to it.
    public override void Awake()
    {
        base.Awake();
        SilenceWhatUpdateAiDoesNotReach();
    }

    internal void UseSitePolicy(StewardSitePolicy policy)
    {
        _policy = policy ?? StewardSitePolicy.Refusing;
    }

    /// <summary>Send him somewhere. Re-stating the goal he is already walking to
    /// changes nothing, so a loop that repeats itself every tick cannot reset
    /// the path budget into a spin.</summary>
    internal bool SetGoal(Vector3 point, float arrivalTolerance)
    {
        if (_faulted || !(arrivalTolerance > 0f) || float.IsInfinity(arrivalTolerance))
        {
            return false;
        }

        var goal = new WorkerGoal(ToSitePoint(point), arrivalTolerance);
        if (_planner.HasGoal && !_planner.IsDeferred && _planner.Goal.Equals(goal))
        {
            return true;
        }

        _planner.AssignGoal(goal);

        // A new place: never inherit the last one's answers.
        _ticksUntilSiteCheck = 0;
        _goalInLoadedGround = false;
        _goalIsHazardous = true;
        return true;
    }

    internal void ClearGoal()
    {
        _planner.ClearGoal();
        StopMoving();
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
            return Tick(dt);
        }
        catch (Exception exception)
        {
            _faulted = true;
            TryStopQuietly();
            if (!_loggedFault)
            {
                _loggedFault = true;
                ErrorLog?.Invoke(
                    "The Steward faulted and is now inert; she will not act again this session. " +
                    SafeFailure.Describe(exception));
            }

            return false;
        }
    }

    private bool Tick(float dt)
    {
        // base.UpdateAI is the ownership gate: false means this peer does not
        // own the body, or the view is invalid. It carries no wandering,
        // alerting or threat behaviour — see the class comment.
        if (!base.UpdateAI(dt))
        {
            return false;
        }

        WorkTick?.Invoke(dt);

        _planner.BeginTick();

        if (!_planner.HasGoal)
        {
            // No order means nothing at all. Not even a stop call: there is
            // nothing to stop, and issuing one every tick would be twenty
            // pointless writes a second.
            return true;
        }

        Vector3 goal = ToVector3(_planner.Goal.Point);
        RefreshSiteChecksIfDue(goal);

        WorkerAction action = _planner.Decide(new WorkerObservation(
            position: ToSitePoint(transform.position),
            hasPath: FoundPath(),
            hasAuthority: HasAuthority(),
            goalInLoadedGround: _goalInLoadedGround,
            goalIsHazardous: _goalIsHazardous));

        switch (action.Kind)
        {
            case WorkerActionKind.RequestPath:
                // FindPath is vanilla's own and is already throttled: it returns
                // the cached result unless a second has passed, or five if the
                // target has barely moved. The planner bounds how often we ask;
                // vanilla's throttle bounds how often the ask reaches the
                // pathfinder. Both are real, and the tighter one wins.
                _planner.ReportPathOutcome(Pathfinding.instance != null && FindPath(goal));
                break;

            case WorkerActionKind.Move:
                // Trap 2: the return value means "stopped", not "arrived", so it
                // is deliberately discarded. Arrival is the planner's call.
                if (Pathfinding.instance != null)
                {
                    MoveTo(dt, goal, _planner.Goal.ArrivalTolerance, run: false);
                }

                break;

            case WorkerActionKind.Arrive:
            case WorkerActionKind.Defer:
                StopMoving();
                break;

            default:
                break;
        }

        DebugLog?.Invoke(
            "steward tick: " + action + ", requests this tick " + _planner.PathRequestsThisTick +
            ", attempts for goal " + _planner.AttemptsForCurrentGoal +
            ", total " + _planner.TotalPathRequests);

        return true;
    }

    /// <summary>Re-asks the site policy at most once every
    /// <see cref="SiteCheckIntervalTicks"/> ticks, holding the previous answer
    /// in between. The cached pair starts at "not loaded, hazardous", so the
    /// first tick after a goal is set always asks — he never acts on an
    /// assumed-safe default he has not actually checked.</summary>
    private void RefreshSiteChecksIfDue(Vector3 goal)
    {
        if (_ticksUntilSiteCheck > 0)
        {
            _ticksUntilSiteCheck--;
            return;
        }

        // Re-armed one short: this tick is the checking tick, so counting a
        // further full interval would make the real period 11 ticks.
        _ticksUntilSiteCheck = SiteCheckIntervalTicks - 1;
        _goalInLoadedGround = _policy.IsInLoadedGround(goal);
        _goalIsHazardous = _policy.IsHazardous(goal);
    }

    private bool HasAuthority()
    {
        try
        {
            return AuthorityGate();
        }
        catch
        {
            return false;
        }
    }

    private void TryStopQuietly()
    {
        try
        {
            StopMoving();
        }
        catch
        {
            // Already faulting; a second failure here must not escape either.
        }
    }

    /// <summary>Turns off every vanilla behaviour <c>UpdateAI</c> does not
    /// reach. Each line matches a specific call site in <c>BaseAI</c>.</summary>
    private void SilenceWhatUpdateAiDoesNotReach()
    {
        // BaseAI.Awake arms InvokeRepeating("DoIdleSound"). It fires on Unity's
        // own schedule, never through UpdateAI, so overriding UpdateAI does not
        // stop it. Cancel it, and empty the effect so a future prefab with a
        // sound configured cannot resurrect it. This is also the whole of
        // #340's "bounded audio": he makes none.
        CancelInvoke("DoIdleSound");
        m_idleSound = new EffectList();
        m_idleSoundChance = 0f;

        // SetAlerted early-outs entirely when m_canBeAlerted is false. That one
        // flag neutralises the alert path at its source — the animator flag, the
        // alerted effect, the boss counter and the MessageAll — which is better
        // than overriding SetAlerted, because RPC_Alert is private and
        // non-virtual and would still reach it.
        m_canBeAlerted = false;

        // Three MessageHud.MessageAll broadcasts, one each in Awake, OnDeath and
        // SetAlerted, all guarded by a non-empty string. A steward has no
        // business announcing himself to every player on the server.
        m_spawnMessage = string.Empty;
        m_deathMessage = string.Empty;
        m_alertedMessage = string.Empty;

        // Not a threat and not a target-seeker. These are the fields vanilla's
        // own sensing reads; he never runs MonsterAI, and leaving them at
        // creature defaults would mislead anyone reading this later.
        m_viewRange = 0f;
        m_hearRange = 0f;
        SetHuntPlayer(hunt: false);

        // The pathfinding agent a person walks as. Already the BaseAI default;
        // stated because the movement budget assumes it.
        m_pathAgentType = Pathfinding.AgentType.Humanoid;
    }

    private static SitePoint ToSitePoint(Vector3 point) => new(point.x, point.y, point.z);

    private static Vector3 ToVector3(SitePoint point) => new(point.X, point.Y, point.Z);
}

/// <summary>The upkeep loop's view of the Steward's legs.
///
/// A resolver rather than a reference, because the loop outlives any one body:
/// he can be unloaded when the player walks away and loaded again when they
/// come back, and a cached component would be a destroyed Unity object that
/// still answers to <c>!= null</c> in the wrong way. Every call re-asks.
///
/// The seam carries no way to write a position. That is the point of it
/// existing at all rather than the loop holding the AI directly.</summary>
internal sealed class StewardMotion : IStewardMotionPort
{
    private readonly Func<StewardWorkerAI?> _body;

    internal StewardMotion(Func<StewardWorkerAI?> body)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
    }

    private StewardWorkerAI? Body
    {
        get
        {
            try
            {
                StewardWorkerAI? ai = _body();
                return ai != null && !ai.IsFaulted && ai.IsOwnedAndValid ? ai : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public bool IsPresent => Body != null;

    public SitePoint Position
    {
        get
        {
            StewardWorkerAI? ai = Body;
            if (ai == null)
            {
                return default;
            }

            Vector3 at = ai.transform.position;
            return new SitePoint(at.x, at.y, at.z);
        }
    }

    public void WalkTo(SitePoint destination, float arrivalTolerance) =>
        Body?.SetGoal(new Vector3(destination.X, destination.Y, destination.Z), arrivalTolerance);

    public void Stop() => Body?.ClearGoal();

    public WorkerWalkStatus WalkStatus => Body?.WalkStatus ?? WorkerWalkStatus.Idle;

    public WorkerDeferralReason DeferredReason => Body?.DeferredReason ?? WorkerDeferralReason.None;
}
