using System;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>A settlement worker: a real networked creature that walks where it
/// is told, stops, and does nothing else at all.
///
/// <b>Why this derives from <c>BaseAI</c> and not <c>MonsterAI</c>.</b> The
/// authority ADR expected to <i>switch off</i> vanilla behaviour by overriding
/// <c>UpdateAI</c>. Reading the installed 1.0.12 assembly shows that is not how
/// this build is shaped, and the real shape is better: <c>BaseAI.UpdateAI</c>
/// contains no wandering, alerting or threat logic to switch off. It is a
/// four-line ownership gate plus housekeeping — takeoff/landing, a jump timer, a
/// random-move <i>timer</i> that nothing here reads, health regeneration and a
/// time-since-hurt counter — and it returns false when this peer is not the
/// owner. Every piece of vanilla's own thinking lives one level down, in
/// <c>MonsterAI.UpdateAI</c> and <c>AnimalAI.UpdateAI</c>, which call
/// <c>base.UpdateAI</c> as a gate and then do their own work.
///
/// So this class does not suppress vanilla behaviour; it never inherits any.
/// The full evidence, and the parts that are <i>not</i> silenced by this choice,
/// are in <c>docs/mods/concerned-foreman/WORKER_ACTOR_SPIKE.md</c>.
///
/// <b>The three traps this class exists to avoid.</b>
///
/// <list type="number">
/// <item>The shared driver, <c>MonoUpdatersExtra.UpdateAI</c>, iterates
/// <c>BaseAI.Instances</c> with <b>no try/catch</b>. One exception escaping this
/// method aborts every creature later in the list for that tick, and the rest of
/// <c>MonoUpdaters.FixedUpdate</c> with it. So <see cref="UpdateAI"/> catches
/// everything, latches, and goes inert.</item>
/// <item><c>BaseAI.MoveTo</c> returns <c>true</c> for <i>stopped</i>, not for
/// <i>arrived</i> — it returns true when the point is close, when
/// <c>FindPath</c> failed, and when the path ran out. Treating that as arrival
/// would report a failed path as a completed order. Arrival is decided here,
/// from distance, by the planner.</item>
/// <item>Several vanilla behaviours live <b>outside</b> <c>UpdateAI</c> and are
/// untouched by overriding it: a repeating idle sound armed in <c>Awake</c>,
/// three registered RPCs, and three <c>MessageHud.MessageAll</c> broadcasts.
/// <see cref="SilenceWhatUpdateAiDoesNotReach"/> deals with each.</item>
/// <item><c>BaseAI.HavePath</c> reads like a cheap question and is not:
/// <c>Pathfinding.HavePath</c> forwards straight to <c>GetPath</c> with
/// <c>requireFullPath: true</c>, so asking it every tick would be a full path
/// search twenty times a second — an unbudgeted cost inside a leaf whose whole
/// claim is that its costs are bounded. The planner is told about paths through
/// <c>FoundPath()</c>, which is a field read of the last result.</item>
/// </list></summary>
internal sealed class ForemanWorkerAI : BaseAI
{
    private readonly WorkerMovementPlanner _planner = new(WorkerMovementBudget.Default);

    /// <summary>How many ticks a site check is reused for. The hazard check
    /// casts a ray and walks the burning-area list, so running it at the full
    /// 20 Hz would be an unbudgeted per-tick cost hiding behind a leaf that
    /// claims its costs are bounded. Ten ticks is half a second — far faster
    /// than a fire spreads or a tide moves, and twenty times cheaper.</summary>
    private const int SiteCheckIntervalTicks = 10;

    private WorkerSitePolicy _policy = WorkerSitePolicy.Refusing;
    private bool _faulted;
    private bool _loggedFault;
    private WorkerDeferralReason _lastReportedDeferral = WorkerDeferralReason.None;
    private int _ticksUntilSiteCheck;
    private bool _goalInLoadedGround;
    private bool _goalIsHazardous = true;

    /// <summary>Set by the runtime that spawned this worker. When it answers
    /// false the worker refuses to act and says why — it does not idle
    /// silently, because a silent worker and a refusing worker look identical
    /// to a player and only one of them is a bug.</summary>
    internal Func<bool> AuthorityGate { get; set; } = () => false;

    /// <summary>Verbose per-decision logging. Off unless the player asked.</summary>
    internal Action<string>? DebugLog { get; set; }

    /// <summary>Errors, reported whatever the diagnostic settings say. A latched
    /// fault makes this worker permanently inert, which is exactly the thing a
    /// player needs told — routing it through <see cref="DebugLog"/> would hide
    /// it behind an option that is off by default.</summary>
    internal Action<string>? ErrorLog { get; set; }

    /// <summary>Told once, when the worker gives up on a goal.</summary>
    internal Action<WorkerDeferralReason>? OnDeferred { get; set; }

    /// <summary>Set by the collection runtime: true while a job holds this
    /// worker's actor mode (ARCH-02). While it does, only the job's own goal
    /// calls move him; the console's goto and stop are refused, so two owners
    /// never steer one body. Anything the gate throws counts as held.</summary>
    internal Func<bool>? IsHeldByJob { get; set; }

    /// <summary>Called once per owned tick, before movement is planned, so the
    /// job's mutations (a pick, a deposit) run inside the worker's own
    /// simulation step and a goal it sets is planned in the same tick. The
    /// callee catches its own exceptions; anything that still escapes latches
    /// this worker like any other fault.</summary>
    internal Action<ForemanWorkerAI, float>? WorkTick { get; set; }

    internal bool IsDeferred => _planner.IsDeferred;

    /// <summary>The view is valid and owned by this process.</summary>
    internal bool IsOwnedAndValid => m_nview != null && m_nview.IsValid() && m_nview.IsOwner();

    /// <summary>Where the walk stands, decided by distance and never by
    /// <c>MoveTo</c>'s return value (trap 2): within the goal's tolerance on
    /// the ground plane is arrived, a planner deferral is deferred.</summary>
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

            // The goal owns this test, so this answer and the planner's own
            // Arrive cannot drift apart — which is what a second copy of the
            // comparison here would do the moment either changed (#334).
            return _planner.Goal.IsReachedFrom(ToSitePoint(transform.position))
                ? WorkerWalkStatus.Arrived
                : WorkerWalkStatus.Walking;
        }
    }

    internal WorkerDeferralReason DeferredReason => _planner.DeferredReason;

    /// <summary>Total path requests this worker has authorised — the "measured"
    /// half of #273's gate 5, readable by a diagnostic command.</summary>
    internal long TotalPathRequests => _planner.TotalPathRequests;

    /// <summary>True once this worker has gone permanently inert after throwing.
    /// It stays in <c>BaseAI.Instances</c> and keeps being ticked, but every
    /// tick returns immediately.</summary>
    internal bool IsFaulted => _faulted;

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

    /// <summary>Send the worker somewhere. Clears any previous deferral,
    /// because a new destination is a new question. Refused while a job holds
    /// the worker: the job's goal is not replaced from outside it.</summary>
    internal void SetGoal(Vector3 point, float arrivalTolerance = 2f)
    {
        if (RefuseWhileHeld("goto"))
        {
            return;
        }

        AssignGoal(point, arrivalTolerance);
    }

    /// <summary>Take the order away. The worker becomes inert — no movement, no
    /// planning, no path requests. Refused while a job holds the worker.</summary>
    internal void ClearGoal()
    {
        if (RefuseWhileHeld("stop"))
        {
            return;
        }

        ClearGoalNow();
    }

    /// <summary>The job's walk, for the motion adapter that has already
    /// checked the job holds the worker. Re-asking for the goal already being
    /// walked changes nothing, so a job that repeats itself cannot reset the
    /// path budget into a spin.</summary>
    internal bool SetJobGoal(Vector3 point, float arrivalTolerance)
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

        AssignGoal(point, arrivalTolerance);
        return true;
    }

    /// <summary>The job's stop, for the motion adapter.</summary>
    internal void ClearJobGoal() => ClearGoalNow();

    private void AssignGoal(Vector3 point, float arrivalTolerance)
    {
        _planner.AssignGoal(new WorkerGoal(ToSitePoint(point), arrivalTolerance));
        _lastReportedDeferral = WorkerDeferralReason.None;
        // A new goal is a new place: never inherit the last one's answers.
        _ticksUntilSiteCheck = 0;
        _goalInLoadedGround = false;
        _goalIsHazardous = true;
    }

    private void ClearGoalNow()
    {
        _planner.ClearGoal();
        _lastReportedDeferral = WorkerDeferralReason.None;
        StopMoving();
    }

    private bool RefuseWhileHeld(string what)
    {
        bool held;
        try
        {
            held = IsHeldByJob != null && IsHeldByJob();
        }
        catch
        {
            held = true;
        }

        if (held)
        {
            ErrorLog?.Invoke(
                "The worker is doing an ordered job, so \"" + what + "\" was not applied. " +
                "Pause or cancel the job first (cf_collect pause).");
        }

        return held;
    }

    /// <summary>Supplies the site checks. Defaults to
    /// <see cref="WorkerSitePolicy.Refusing"/> so a worker whose runtime forgot
    /// to wire this up refuses every goal rather than walking into unchecked
    /// ground.</summary>
    internal void UseSitePolicy(WorkerSitePolicy policy)
    {
        _policy = policy ?? WorkerSitePolicy.Refusing;
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
        catch (Exception ex)
        {
            _faulted = true;
            TryStopQuietly();
            if (!_loggedFault)
            {
                _loggedFault = true;
                ErrorLog?.Invoke(
                    "Worker faulted and is now inert; it will not act again this session. " + ex);
            }

            return false;
        }
    }

    private bool Tick(float dt)
    {
        // base.UpdateAI is the ownership gate: false means this peer does not
        // own the actor, or the view is invalid. It carries no wandering,
        // alerting or threat behaviour — see the class comment.
        if (!base.UpdateAI(dt))
        {
            return false;
        }

        WorkTick?.Invoke(this, dt);

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
        WorkerObservation observation = new(
            position: ToSitePoint(transform.position),
            hasPath: FoundPath(),
            hasAuthority: HasAuthority(),
            goalInLoadedGround: _goalInLoadedGround,
            goalIsHazardous: _goalIsHazardous);

        WorkerAction action = _planner.Decide(observation);

        switch (action.Kind)
        {
            case WorkerActionKind.RequestPath:
                // FindPath is vanilla's own, and it is already throttled: it
                // returns the cached result unless a second has passed, or five
                // if the target has barely moved. The planner's budget bounds
                // how often we ask; vanilla's throttle bounds how often the ask
                // reaches the pathfinder. Both are real, and the tighter one
                // wins.
                _planner.ReportPathOutcome(
                    Pathfinding.instance != null && FindPath(goal));
                break;

            case WorkerActionKind.Move:
                // Trap 2: the return value means "stopped", not "arrived", so
                // it is deliberately discarded. Arrival is the planner's call.
                if (Pathfinding.instance != null)
                {
                    MoveTo(dt, goal, _planner.Goal.ArrivalTolerance, run: false);
                }

                break;

            case WorkerActionKind.Arrive:
            case WorkerActionKind.Defer:
                StopMoving();
                break;

            case WorkerActionKind.WaitForBudget:
            case WorkerActionKind.Idle:
            default:
                break;
        }

        if (action.Kind == WorkerActionKind.Defer && _lastReportedDeferral != action.Reason)
        {
            _lastReportedDeferral = action.Reason;
            OnDeferred?.Invoke(action.Reason);
        }

        DebugLog?.Invoke(
            $"worker tick: {action}, requests this tick {_planner.PathRequestsThisTick}, " +
            $"attempts for goal {_planner.AttemptsForCurrentGoal}, total {_planner.TotalPathRequests}");

        return true;
    }

    /// <summary>Re-asks the site policy at most once every
    /// <see cref="SiteCheckIntervalTicks"/> ticks, holding the previous answer
    /// in between. The cached pair starts at "not loaded, hazardous", so the
    /// very first tick after a goal is set always asks — a worker never acts on
    /// an assumed-safe default it has not actually checked.</summary>
    private void RefreshSiteChecksIfDue(Vector3 goal)
    {
        if (_ticksUntilSiteCheck > 0)
        {
            _ticksUntilSiteCheck--;
            return;
        }

        // Re-armed one short: this tick is the checking tick, so counting a
        // further full interval would make the real period 11 ticks, not the
        // documented 10.
        _ticksUntilSiteCheck = SiteCheckIntervalTicks - 1;
        _goalInLoadedGround = _policy.IsInLoadedGround(goal);
        _goalIsHazardous = _policy.IsHazardous(goal);
    }

    /// <summary>True only when this peer owns the actor <i>and</i> the runtime
    /// that spawned it still says it may act. Any exception from the gate is a
    /// refusal, never a grant.</summary>
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

    /// <summary>Turns off every vanilla behaviour that <c>UpdateAI</c> does not
    /// reach. Each line corresponds to a specific call site in the installed
    /// 1.0.12 <c>BaseAI</c>, listed in the spike document.</summary>
    private void SilenceWhatUpdateAiDoesNotReach()
    {
        // BaseAI.Awake arms InvokeRepeating("DoIdleSound", ...). It fires on
        // Unity's own schedule, never through UpdateAI, so overriding UpdateAI
        // does not stop it. Cancel it, and empty the effect as well so a future
        // prefab with a sound configured cannot resurrect it.
        CancelInvoke("DoIdleSound");
        m_idleSound = new EffectList();
        m_idleSoundChance = 0f;

        // SetAlerted early-outs entirely when m_canBeAlerted is false. That one
        // flag neutralises the whole alert path at its source — the animator
        // flag, the alerted effect, the boss counter and the MessageAll — which
        // is better than overriding SetAlerted, because RPC_Alert is private and
        // non-virtual and would still reach it.
        m_canBeAlerted = false;

        // Three MessageHud.MessageAll broadcasts, one each in Awake, OnDeath and
        // SetAlerted, all guarded by a non-empty string. A settlement worker has
        // no business announcing itself to every player on the server.
        m_spawnMessage = string.Empty;
        m_deathMessage = string.Empty;
        m_alertedMessage = string.Empty;

        // Not a threat and not a target-seeker. These are the fields vanilla's
        // own sensing reads; a worker that never runs MonsterAI does not use
        // them, and leaving them at creature defaults would be misleading to
        // anyone reading this later.
        m_viewRange = 0f;
        m_hearRange = 0f;
        SetHuntPlayer(hunt: false);

        // The pathfinding agent a person walks as. Already the BaseAI default;
        // stated because the budget numbers in the spike document assume it.
        m_pathAgentType = Pathfinding.AgentType.Humanoid;
    }

    private static SitePoint ToSitePoint(Vector3 point) => new(point.x, point.y, point.z);

    private static Vector3 ToVector3(SitePoint point) => new(point.X, point.Y, point.Z);
}
