using System;

namespace TheConcernedCat.Ladders;

/// <summary>Why a character may not start climbing. Every refusal has a reason
/// a player can be told; none is ever <see cref="Unspecified"/>.</summary>
internal enum MountRefusal
{
    Unspecified = 0,

    /// <summary>The piece is not a ladder anyone can climb: too short, or not
    /// measurable.</summary>
    NotClimbable = 1,

    /// <summary>Too far in front of the rungs.</summary>
    TooFar = 2,

    /// <summary>Beside the ladder rather than at it.</summary>
    OffToTheSide = 3,

    /// <summary>Behind the ladder: the wall side, where there are no rungs.</summary>
    WrongSide = 4,

    /// <summary>Above the head or below the foot of the ladder by more than a
    /// step.</summary>
    OutOfSpan = 5,

    /// <summary>Walking past it, not into it.</summary>
    LookingAway = 6,

    /// <summary>The character cannot climb at all right now: dead, attached to
    /// something, swimming, riding, or a worker with no climbing in its
    /// capabilities.</summary>
    CharacterBusy = 7,

    /// <summary>Ladders are switched off.</summary>
    Disabled = 8,

    /// <summary>Already on a ladder.</summary>
    AlreadyClimbing = 9,
}

/// <summary>What the character is doing when it asks to climb: the parts of its
/// state the decision needs, with no engine type in sight.</summary>
internal readonly struct ClimberState
{
    public ClimberState(ClimbPoint feet, ClimbHeading looking, bool canClimbNow, bool alreadyClimbing)
    {
        Feet = feet;
        Looking = looking;
        CanClimbNow = canClimbNow;
        AlreadyClimbing = alreadyClimbing;
    }

    /// <summary>Where the character's feet are.</summary>
    public ClimbPoint Feet { get; }

    /// <summary>Which way the character is turned.</summary>
    public ClimbHeading Looking { get; }

    /// <summary>False while dead, attached to a chair or bed, swimming, riding,
    /// teleporting, or otherwise not in a state to grab a ladder.</summary>
    public bool CanClimbNow { get; }

    public bool AlreadyClimbing { get; }
}

/// <summary>The answer: climb from here, or not, and why.</summary>
internal readonly struct MountDecision
{
    private MountDecision(bool allowed, MountRefusal refusal, float progress, ClimbPoint position, ClimbHeading facing)
    {
        Allowed = allowed;
        Refusal = refusal;
        Progress = progress;
        Position = position;
        Facing = facing;
    }

    public bool Allowed { get; }

    public MountRefusal Refusal { get; }

    /// <summary>Where on the ladder the climb starts, in metres above its
    /// foot. A character stepping on from a platform starts at the top.</summary>
    public float Progress { get; }

    /// <summary>Where the body belongs at that progress. The runtime moves the
    /// character there over <see cref="ClimbLimits.AlignSeconds"/>, never in one
    /// frame.</summary>
    public ClimbPoint Position { get; }

    /// <summary>Which way the body is turned while climbing: into the ladder.</summary>
    public ClimbHeading Facing { get; }

    public static MountDecision Allow(float progress, ClimbPoint position, ClimbHeading facing) =>
        new MountDecision(true, MountRefusal.Unspecified, progress, position, facing);

    public static MountDecision Refuse(MountRefusal refusal)
    {
        if (refusal == MountRefusal.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal), "A refusal needs a reason.");
        }

        return new MountDecision(false, refusal, 0f, default, ClimbHeading.None);
    }
}

/// <summary>Whether a character may start climbing a ladder, and where on it.
///
/// Deliberately forgiving about position and strict about intent: walking into
/// a ladder climbs it, walking past one does not.</summary>
internal static class LadderMount
{
    public static MountDecision Decide(
        in LadderGeometry ladder,
        in ClimberState climber,
        ClimbLimits limits,
        bool laddersEnabled = true,
        bool byDeliberateUse = false)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (!laddersEnabled)
        {
            return MountDecision.Refuse(MountRefusal.Disabled);
        }

        if (climber.AlreadyClimbing)
        {
            return MountDecision.Refuse(MountRefusal.AlreadyClimbing);
        }

        if (!climber.CanClimbNow)
        {
            return MountDecision.Refuse(MountRefusal.CharacterBusy);
        }

        if (!ladder.IsClimbable)
        {
            return MountDecision.Refuse(MountRefusal.NotClimbable);
        }

        if (!climber.Feet.IsFinite)
        {
            return MountDecision.Refuse(MountRefusal.TooFar);
        }

        float above = climber.Feet.Y - ladder.Top.Y;
        float below = ladder.Bottom.Y - climber.Feet.Y;
        if (above > limits.MountHeightMarginMetres || below > limits.MountHeightMarginMetres)
        {
            return MountDecision.Refuse(MountRefusal.OutOfSpan);
        }

        float reach = ladder.ReachFrom(climber.Feet);
        if (reach < -0.05f)
        {
            // Behind the rungs: the wall the ladder is fixed to. Stepping on
            // from there would put the climber inside the structure.
            return MountDecision.Refuse(MountRefusal.WrongSide);
        }

        if (reach > limits.MountReachMetres)
        {
            return MountDecision.Refuse(MountRefusal.TooFar);
        }

        if (ladder.SidewaysOffsetOf(climber.Feet) > (ladder.Width * 0.5f) + limits.MountSideMarginMetres)
        {
            return MountDecision.Refuse(MountRefusal.OffToTheSide);
        }

        // Intent. Pressing Use is intent enough; walking needs the body turned
        // towards the ladder, so that passing one never grabs it.
        if (!byDeliberateUse)
        {
            ClimbHeading towardsLadder = ladder.FacingWhileClimbing;
            if (climber.Looking.Agreement(towardsLadder) < limits.MountFacingAgreement)
            {
                return MountDecision.Refuse(MountRefusal.LookingAway);
            }
        }

        float progress = ladder.ProgressOf(climber.Feet);
        return MountDecision.Allow(
            progress,
            ladder.ClimbPositionAt(progress, limits.BodyOffsetMetres),
            ladder.FacingWhileClimbing);
    }

    /// <summary>The player-facing sentence for a refusal. One per value, never
    /// a bare enum name in front of a person.</summary>
    public static string Explain(MountRefusal refusal)
    {
        switch (refusal)
        {
            case MountRefusal.NotClimbable:
                return "That is too short to climb; just step up.";
            case MountRefusal.TooFar:
                return "Stand closer to the ladder.";
            case MountRefusal.OffToTheSide:
                return "Stand in front of the ladder.";
            case MountRefusal.WrongSide:
                return "The rungs are on the other side.";
            case MountRefusal.OutOfSpan:
                return "The ladder does not reach here.";
            case MountRefusal.LookingAway:
                return "Face the ladder to climb it.";
            case MountRefusal.CharacterBusy:
                return "Not while you are doing that.";
            case MountRefusal.Disabled:
                return "Ladder climbing is switched off.";
            case MountRefusal.AlreadyClimbing:
                return "You are already on a ladder.";
            default:
                return "You cannot climb that.";
        }
    }
}
