using System;

namespace TheConcernedCat.Ladders;

/// <summary>What the climb is doing after a step.</summary>
internal enum ClimbMotion
{
    Unspecified = 0,

    /// <summary>Holding still on the ladder.</summary>
    Resting = 1,

    Climbing = 2,

    Descending = 3,

    /// <summary>Climbing, but the ladder has run out at the top. The next thing
    /// is the top exit, if there is room; otherwise the climber waits here.</summary>
    PressingAgainstTheTop = 4,

    /// <summary>Descending with the foot of the ladder reached: the bottom exit
    /// follows.</summary>
    StandingOnTheGround = 5,
}

/// <summary>Where the climber is on the run, and how it got there. One step per
/// frame; every step is clamped to the ladder, so no input, however large or
/// however long the frame, can carry a climber past either end.</summary>
internal sealed class ClimbTrack
{
    private readonly float _span;
    private readonly ClimbLimits _limits;

    public ClimbTrack(float span, ClimbLimits limits, float startProgress)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (float.IsNaN(span) || span <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(span), "A run with no height is not climbable.");
        }

        _span = span;
        _limits = limits;
        Progress = Clamp(startProgress);
        Motion = ClimbMotion.Resting;
    }

    /// <summary>Metres above the foot of the run.</summary>
    public float Progress { get; private set; }

    public ClimbMotion Motion { get; private set; }

    /// <summary>Metres per second, signed: up is positive. What the pose and
    /// the animation follow, so the limbs move with the body.</summary>
    public float Velocity { get; private set; }

    public bool AtTop => Progress >= _span - 0.001f;

    public bool AtBottom => Progress <= 0.001f;

    /// <summary>One frame of climbing. <paramref name="input"/> is the
    /// character's intent along the ladder, -1 down to 1 up; anything smaller
    /// than a dead zone is a stop, so a resting climber does not creep.</summary>
    public ClimbMotion Step(float input, float deltaSeconds)
    {
        if (float.IsNaN(input) || float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
        {
            Velocity = 0f;
            Motion = ClimbMotion.Resting;
            return Motion;
        }

        float intent = Math.Min(Math.Max(input, -1f), 1f);
        if (Math.Abs(intent) < 0.1f)
        {
            Velocity = 0f;
            Motion = ClimbMotion.Resting;
            return Motion;
        }

        float before = Progress;
        Velocity = intent * _limits.EffectiveClimbSpeed;
        Progress = Clamp(Progress + (Velocity * deltaSeconds));

        // The velocity a watcher sees is the distance actually covered: at the
        // ends the climber stops dead, and the animation stops with them
        // instead of walking on the spot.
        float covered = Progress - before;
        Velocity = deltaSeconds > 0f ? covered / deltaSeconds : 0f;

        if (intent > 0f)
        {
            Motion = AtTop ? ClimbMotion.PressingAgainstTheTop : ClimbMotion.Climbing;
        }
        else
        {
            Motion = AtBottom ? ClimbMotion.StandingOnTheGround : ClimbMotion.Descending;
        }

        return Motion;
    }

    /// <summary>Put the climber at a known height, when the runtime has to
    /// re-derive the position (a stacked run gained a piece, a reload placed the
    /// body). Clamped like every other move.</summary>
    public void MoveTo(float progress)
    {
        Progress = Clamp(progress);
        Velocity = 0f;
        Motion = ClimbMotion.Resting;
    }

    /// <summary>How fast the climbing animation should play: 1 is the tuned
    /// climb speed, negative is downward. It follows the body, so nothing
    /// skates and nothing cycles on the spot.</summary>
    public float AnimationSpeed()
    {
        float nominal = _limits.EffectiveClimbSpeed;
        return nominal <= 0f ? 0f : Velocity / nominal;
    }

    /// <summary>Stamina drawn by this step, never negative.</summary>
    public float StaminaFor(float deltaSeconds)
    {
        if (_limits.StaminaPerSecond <= 0f || deltaSeconds <= 0f || Math.Abs(Velocity) < 0.01f)
        {
            return 0f;
        }

        return _limits.StaminaPerSecond * deltaSeconds;
    }

    private float Clamp(float progress)
    {
        if (float.IsNaN(progress))
        {
            return Progress;
        }

        return Math.Min(Math.Max(progress, 0f), _span);
    }
}
