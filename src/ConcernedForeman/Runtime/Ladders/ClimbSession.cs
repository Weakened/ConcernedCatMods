using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>What a climb is doing, for anything that has to draw or sound it.</summary>
internal enum ClimbPhase
{
    /// <summary>Nobody is on a ladder.</summary>
    None = 0,

    /// <summary>The body is being moved onto the ladder's line and turned into
    /// it. Never a snap and never a teleport: it takes
    /// <see cref="ClimbLimits.AlignSeconds"/>.</summary>
    Aligning = 1,

    OnTheLadder = 2,

    /// <summary>The climb is over and the character has been handed back.</summary>
    Ended = 3,
}

/// <summary>The one thing the presentation layer reads (CF-LAD-003 drives the
/// pose and the sound from this and nothing else).
///
/// It is a value, published once per frame, so the pose never has to reach into
/// the controller and can never keep a climb alive by holding a reference.</summary>
internal readonly struct ClimbTelemetry
{
    internal ClimbTelemetry(
        ClimbPhase phase,
        ClimbMotion motion,
        float progress,
        float height,
        float velocity,
        float animationSpeed,
        int rung,
        float alignFraction,
        ClimbHeading facing)
    {
        Phase = phase;
        Motion = motion;
        Progress = progress;
        Height = height;
        Velocity = velocity;
        AnimationSpeed = animationSpeed;
        Rung = rung;
        AlignFraction = alignFraction;
        Facing = facing;
    }

    public ClimbPhase Phase { get; }

    public ClimbMotion Motion { get; }

    /// <summary>Metres above the foot of the run.</summary>
    public float Progress { get; }

    /// <summary>The whole run's height, so a fraction is available without
    /// anybody re-measuring the ladder.</summary>
    public float Height { get; }

    /// <summary>Metres per second, signed: up is positive.</summary>
    public float Velocity { get; }

    /// <summary>1 is the tuned climb speed upwards, -1 the same downwards, 0 a
    /// rest. What an animator speed parameter takes directly.</summary>
    public float AnimationSpeed { get; }

    /// <summary>Which rung the climber is at, counted from the foot of the whole
    /// run so it rises monotonically across a stack and a contact sound does not
    /// fire twice at a join.</summary>
    public int Rung { get; }

    /// <summary>0 at the moment of mounting, 1 once the body is on the ladder's
    /// line. A pose can blend in over it.</summary>
    public float AlignFraction { get; }

    /// <summary>The way the body is turned: into the ladder.</summary>
    public ClimbHeading Facing { get; }

    /// <summary>Nobody is climbing. Publishing this is also how the pose and the
    /// sound are told to stop, which is why every ending path publishes it.</summary>
    public static ClimbTelemetry Idle => default;

    public bool IsClimbing => Phase == ClimbPhase.Aligning || Phase == ClimbPhase.OnTheLadder;
}

/// <summary>Everything one frame of a climb needs, read from the world by the
/// runtime and handed to the decision layer, which never looks at the world
/// itself.</summary>
internal readonly struct ClimbFrame
{
    public ClimbFrame(
        float deltaSeconds,
        in ClimbConditions conditions,
        float climbInput,
        bool letGoRequested,
        in TopLanding topLanding)
    {
        DeltaSeconds = deltaSeconds;
        Conditions = conditions;
        ClimbInput = climbInput;
        LetGoRequested = letGoRequested;
        TopLanding = topLanding;
    }

    public float DeltaSeconds { get; }

    public ClimbConditions Conditions { get; }

    /// <summary>The character's intent along the ladder, -1 down to 1 up.</summary>
    public float ClimbInput { get; }

    /// <summary>The player asked to let go (jumped).</summary>
    public bool LetGoRequested { get; }

    /// <summary>What the runtime found at the top this frame, or
    /// <see cref="Ladders.TopLanding.None"/> when it did not look (it only looks
    /// near the top).</summary>
    public TopLanding TopLanding { get; }
}

/// <summary>What the runtime must do with the body after one frame of climbing.
///
/// <see cref="Restoration"/> is filled in on every ending step, whatever ended
/// it, because a path that forgets it is the defect this whole feature is most
/// afraid of (LADDERS.md L5).</summary>
internal readonly struct ClimbStep
{
    private ClimbStep(
        bool continues,
        ClimbPoint bodyTarget,
        ClimbHeading bodyFacing,
        float staminaUsed,
        bool hasExit,
        ClimbExitPlan exit,
        ClimbEndReason endReason,
        ClimbRestoration restoration,
        ClimbTelemetry telemetry)
    {
        Continues = continues;
        BodyTarget = bodyTarget;
        BodyFacing = bodyFacing;
        StaminaUsed = staminaUsed;
        HasExit = hasExit;
        Exit = exit;
        EndReason = endReason;
        Restoration = restoration;
        Telemetry = telemetry;
    }

    /// <summary>True while the climb owns the body.</summary>
    public bool Continues { get; }

    /// <summary>Where the body belongs at the end of this frame. Only meaningful
    /// while <see cref="Continues"/> is true.</summary>
    public ClimbPoint BodyTarget { get; }

    public ClimbHeading BodyFacing { get; }

    public float StaminaUsed { get; }

    /// <summary>True on the single frame the climb ends.</summary>
    public bool HasExit { get; }

    public ClimbExitPlan Exit { get; }

    /// <summary><see cref="ClimbEndReason.Unspecified"/> when the climber chose
    /// the ending (a step-off or a jump); a real reason when the world did.</summary>
    public ClimbEndReason EndReason { get; }

    public ClimbRestoration Restoration { get; }

    public ClimbTelemetry Telemetry { get; }

    internal static ClimbStep Continue(
        ClimbPoint bodyTarget,
        ClimbHeading facing,
        float staminaUsed,
        ClimbTelemetry telemetry) =>
        new ClimbStep(
            continues: true,
            bodyTarget,
            facing,
            staminaUsed,
            hasExit: false,
            default,
            ClimbEndReason.Unspecified,
            default,
            telemetry);

    internal static ClimbStep Ending(ClimbExitPlan exit, ClimbEndReason reason) =>
        new ClimbStep(
            continues: false,
            default,
            ClimbHeading.None,
            staminaUsed: 0f,
            hasExit: true,
            exit,
            reason,
            ClimbSafety.Restoration,
            ClimbTelemetry.Idle);

    /// <summary>The climb was already over when this step was asked for. The
    /// restoration is still complete, so applying it twice is harmless and a
    /// caller that lost track of the ending cannot leave a body half-held.</summary>
    internal static ClimbStep AlreadyOver(ClimbEndReason reason) =>
        new ClimbStep(
            continues: false,
            default,
            ClimbHeading.None,
            staminaUsed: 0f,
            hasExit: false,
            default,
            reason,
            ClimbSafety.Restoration,
            ClimbTelemetry.Idle);
}

/// <summary>Whether a character is allowed to grab a ladder again yet.
///
/// Without this, letting go halfway up a ladder drops the character straight
/// back through the mounting band and auto-mount catches them again, which is
/// the opposite of letting go. So a deliberate let-go has to leave the band
/// before walking into the ladder can mount it again, and every ending has a
/// short cooldown.</summary>
internal sealed class MountGate
{
    private float _cooldown;
    private bool _mustLeaveTheBand;

    public bool AllowsAutoMount => _cooldown <= 0f && !_mustLeaveTheBand;

    /// <summary>Pressing Use is a deliberate act and only waits out the
    /// cooldown: a player who meant to grab the ladder again may.</summary>
    public bool AllowsDeliberateMount => _cooldown <= 0f;

    public void Tick(float deltaSeconds)
    {
        if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
        {
            return;
        }

        if (_cooldown > 0f)
        {
            _cooldown = Math.Max(0f, _cooldown - deltaSeconds);
        }
    }

    /// <summary>A climb ended. A let-go of either kind has to clear the band
    /// before walking in mounts again; a step-off at either end does not,
    /// because the character is standing somewhere else by then.</summary>
    public void ClimbEnded(ClimbExitKind kind, float cooldownSeconds)
    {
        _cooldown = Math.Max(0f, cooldownSeconds);
        _mustLeaveTheBand = kind == ClimbExitKind.JumpedOff || kind == ClimbExitKind.LetGo;
    }

    /// <summary>The character is not in a position to mount: it has left the
    /// band, so walking back into the ladder may grab it again.</summary>
    public void OutOfTheBand() => _mustLeaveTheBand = false;

    public void Reset()
    {
        _cooldown = 0f;
        _mustLeaveTheBand = false;
    }
}

/// <summary>One character's climb, from the decision to mount to the frame the
/// body is handed back. Game-free on purpose: every rule that decides where a
/// climber is, when the climb ends and what has to be given back is tested
/// without the game, and the Valheim side only reads the world and moves a
/// body.</summary>
internal sealed class ClimbSession
{
    private readonly LadderRun _run;
    private readonly ClimbLimits _limits;
    private readonly ClimbTrack _track;
    private readonly ClimbPoint _mountedFrom;
    private readonly ClimbHeading _facedFrom;
    private float _aligned;

    private ClimbSession(
        LadderRun run,
        ClimbLimits limits,
        float startProgress,
        ClimbPoint mountedFrom,
        ClimbHeading facedFrom)
    {
        _run = run;
        _limits = limits;
        _track = new ClimbTrack(run.Height, limits, startProgress);
        _mountedFrom = mountedFrom;
        _facedFrom = facedFrom.IsKnown ? facedFrom : run.AsOne.FacingWhileClimbing;
        Phase = limits.AlignSeconds > 0f ? ClimbPhase.Aligning : ClimbPhase.OnTheLadder;
    }

    public ClimbPhase Phase { get; private set; }

    public ClimbEndReason EndReason { get; private set; } = ClimbEndReason.Unspecified;

    /// <summary>The run being climbed, so the runtime can watch its pieces.</summary>
    public LadderRun Run => _run;

    public float Progress => _track.Progress;

    /// <summary>Where the body belongs right now, with no step taken. The
    /// runtime uses it to measure how far the body has drifted from the climb,
    /// which is what <see cref="ClimbConditions.DistanceFromLadder"/> is.</summary>
    public ClimbPoint HeldAt => _run.AsOne.ClimbPositionAt(_track.Progress, _limits.BodyOffsetMetres);

    /// <summary>Whether the top landing is worth probing this frame. The probe
    /// costs a raycast, so it only runs near the top, where the answer is
    /// used.</summary>
    public bool NearTheTop(float withinMetres) => _track.Progress >= _run.Height - Math.Max(0f, withinMetres);

    /// <summary>Whether a character may climb this run from where it stands, and
    /// if so, the climb. Every refusal carries the domain's reason, so a player
    /// is always told why rather than left wondering.</summary>
    public static bool TryStart(
        LadderRun run,
        in ClimberState climber,
        ClimbLimits limits,
        bool laddersEnabled,
        bool byDeliberateUse,
        out ClimbSession session,
        out MountRefusal refusal)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        session = null!;
        MountDecision decision = LadderMount.Decide(run.AsOne, climber, limits, laddersEnabled, byDeliberateUse);
        if (!decision.Allowed)
        {
            refusal = decision.Refusal;
            return false;
        }

        refusal = MountRefusal.Unspecified;
        session = new ClimbSession(run, limits, decision.Progress, climber.Feet, climber.Looking);
        return true;
    }

    /// <summary>One frame. Safety first, then the climber's own intent, then the
    /// motion: the order matters, because a frame in which the ladder has gone
    /// must never move a body along it.</summary>
    public ClimbStep Step(in ClimbFrame frame)
    {
        if (Phase == ClimbPhase.Ended)
        {
            return ClimbStep.AlreadyOver(EndReason);
        }

        ClimbEndReason reason = ClimbSafety.MustEnd(frame.Conditions, _limits);
        if (reason != ClimbEndReason.Unspecified)
        {
            return End(reason, ClimbSafety.Release(reason));
        }

        if (frame.LetGoRequested)
        {
            return End(ClimbEndReason.Unspecified, ClimbExit.LetGo(deliberate: true));
        }

        float delta = frame.DeltaSeconds;
        if (float.IsNaN(delta) || delta < 0f)
        {
            delta = 0f;
        }

        if (Phase == ClimbPhase.Aligning)
        {
            return Align(delta);
        }

        ClimbMotion motion = _track.Step(frame.ClimbInput, delta);
        if (motion == ClimbMotion.PressingAgainstTheTop)
        {
            ClimbExitPlan? overTheTop = ClimbExit.OverTheTop(_run.AsOne, frame.TopLanding, _limits);
            if (overTheTop.HasValue)
            {
                return End(ClimbEndReason.Unspecified, overTheTop.Value);
            }

            // No room up there. The climber waits on the ladder rather than
            // being pushed into a roof; a ladder may simply end under one.
        }
        else if (motion == ClimbMotion.StandingOnTheGround)
        {
            return End(ClimbEndReason.Unspecified, ClimbExit.OffTheBottom(_run.AsOne, _limits));
        }

        return ClimbStep.Continue(
            HeldAt,
            _run.AsOne.FacingWhileClimbing,
            _track.StaminaFor(delta),
            Telemetry(motion, 1f));
    }

    /// <summary>The controller itself failed. The climb ends anyway, and the
    /// character is handed back whole: an exception inside a climb is a bug, not
    /// a reason for a player to be stuck on a ladder.</summary>
    public ClimbStep Fault() => Stop(ClimbEndReason.ControllerFault);

    /// <summary>Ended from outside: the world is unloading, the plugin is
    /// stopping, or the setting was switched off under a climber.</summary>
    public ClimbStep Stop(ClimbEndReason reason)
    {
        if (reason == ClimbEndReason.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "A climb does not end without a reason.");
        }

        if (Phase == ClimbPhase.Ended)
        {
            return ClimbStep.AlreadyOver(EndReason);
        }

        return End(reason, ClimbSafety.Release(reason));
    }

    private ClimbStep Align(float delta)
    {
        _aligned += delta;
        float fraction = _limits.AlignSeconds <= 0f
            ? 1f
            : Math.Min(1f, Math.Max(0f, _aligned / _limits.AlignSeconds));
        if (fraction >= 1f)
        {
            Phase = ClimbPhase.OnTheLadder;
        }

        // Eased, so the body leaves its standing spot gently and arrives on the
        // ladder without a visible snap at either end.
        float eased = fraction * fraction * (3f - (2f * fraction));
        ClimbPoint target = Between(_mountedFrom, HeldAt, eased);
        ClimbHeading facing = Between(_facedFrom, _run.AsOne.FacingWhileClimbing, eased);
        return ClimbStep.Continue(target, facing, staminaUsed: 0f, Telemetry(ClimbMotion.Resting, fraction));
    }

    private ClimbStep End(ClimbEndReason reason, ClimbExitPlan exit)
    {
        Phase = ClimbPhase.Ended;
        EndReason = reason;
        return ClimbStep.Ending(exit, reason);
    }

    private ClimbTelemetry Telemetry(ClimbMotion motion, float alignFraction) =>
        new ClimbTelemetry(
            Phase,
            motion,
            _track.Progress,
            _run.Height,
            _track.Velocity,
            _track.AnimationSpeed(),
            _run.AsOne.RungAt(_track.Progress),
            alignFraction,
            _run.AsOne.FacingWhileClimbing);

    private static ClimbPoint Between(ClimbPoint from, ClimbPoint to, float fraction) =>
        new ClimbPoint(
            from.X + ((to.X - from.X) * fraction),
            from.Y + ((to.Y - from.Y) * fraction),
            from.Z + ((to.Z - from.Z) * fraction));

    private static ClimbHeading Between(ClimbHeading from, ClimbHeading to, float fraction)
    {
        if (!from.IsKnown)
        {
            return to;
        }

        if (!to.IsKnown)
        {
            return from;
        }

        ClimbHeading blended = ClimbHeading.FromXz(
            from.X + ((to.X - from.X) * fraction),
            from.Z + ((to.Z - from.Z) * fraction));

        // Exactly opposite headings cancel out to nothing halfway through. The
        // destination is the honest answer there: a body turning through a
        // half-circle is still turning towards the ladder.
        return blended.IsKnown ? blended : to;
    }
}
