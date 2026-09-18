using System;

namespace TheConcernedCat.Ladders;

/// <summary>How a climb ends.</summary>
internal enum ClimbExitKind
{
    Unspecified = 0,

    /// <summary>Off the foot of the ladder onto the ground, still standing.</summary>
    Bottom = 1,

    /// <summary>Over the edge at the top and onto the floor there. The one the
    /// brief cares most about, because reaching the top and being stuck is the
    /// failure everybody has met in some other game.</summary>
    Top = 2,

    /// <summary>Let go on purpose: a jump away from the ladder.</summary>
    JumpedOff = 3,

    /// <summary>Let go without choosing to: the ladder is gone, the character
    /// was thrown, killed or moved. The character falls normally from where it
    /// is.</summary>
    LetGo = 4,
}

/// <summary>Where the character ends up, and what the runtime has to restore.
/// Restoring is not optional on any path: whatever the reason, the character
/// gets its own motor, its gravity and its collisions back in the same frame
/// (LADDERS.md L5).</summary>
internal readonly struct ClimbExitPlan
{
    private ClimbExitPlan(ClimbExitKind kind, ClimbPoint landing, ClimbHeading facing, bool placeCharacter)
    {
        Kind = kind;
        Landing = landing;
        Facing = facing;
        PlaceCharacter = placeCharacter;
    }

    public ClimbExitKind Kind { get; }

    /// <summary>Where the character stands after the exit. Only meaningful when
    /// <see cref="PlaceCharacter"/> is true.</summary>
    public ClimbPoint Landing { get; }

    public ClimbHeading Facing { get; }

    /// <summary>Whether the runtime moves the character at all. A let-go never
    /// does: the character stays exactly where it is and falls, because moving
    /// a body that was just killed, thrown or unloaded is how mods teleport
    /// people into terrain.</summary>
    public bool PlaceCharacter { get; }

    public static ClimbExitPlan Step(ClimbExitKind kind, ClimbPoint landing, ClimbHeading facing)
    {
        if (kind != ClimbExitKind.Bottom && kind != ClimbExitKind.Top)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Only the two step-offs place a character.");
        }

        return new ClimbExitPlan(kind, landing, facing, placeCharacter: true);
    }

    public static ClimbExitPlan Release(ClimbExitKind kind) =>
        new ClimbExitPlan(kind, default, ClimbHeading.None, placeCharacter: false);
}

/// <summary>What the runtime found at the top of the ladder, this frame: is
/// there floor to step onto, and where. The domain never probes the world; it
/// only decides what to do with what the probe found.</summary>
internal readonly struct TopLanding
{
    public TopLanding(bool hasStandingSpace, ClimbPoint surface)
    {
        HasStandingSpace = hasStandingSpace;
        Surface = surface;
    }

    /// <summary>True when there is a surface at the top with room for the
    /// character to stand: no low ceiling, no wall in the way.</summary>
    public bool HasStandingSpace { get; }

    /// <summary>The point on that surface, level with the floor.</summary>
    public ClimbPoint Surface { get; }

    public static TopLanding None => new TopLanding(false, default);
}

/// <summary>Choosing the exit. Every climb ends through exactly one of these,
/// and the character is placed only on the two deliberate step-offs.</summary>
internal static class ClimbExit
{
    /// <summary>The climber is at the top and still pressing upwards.</summary>
    public static ClimbExitPlan? OverTheTop(in LadderGeometry ladder, in TopLanding landing, ClimbLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (!landing.HasStandingSpace || !landing.Surface.IsFinite)
        {
            // Nothing to step onto: the climber stays on the ladder rather than
            // being pushed into a wall or a roof. Not an error; a ladder may
            // simply end under something.
            return null;
        }

        ClimbHeading inwards = ladder.FacingWhileClimbing;
        ClimbPoint landingPoint = landing.Surface
            .Offset(inwards, limits.TopStepInMetres)
            .WithHeight(landing.Surface.Y + limits.TopStepUpMetres);
        return ClimbExitPlan.Step(ClimbExitKind.Top, landingPoint, inwards);
    }

    /// <summary>The climber has reached the foot and keeps pressing down.</summary>
    public static ClimbExitPlan OffTheBottom(in LadderGeometry ladder, ClimbLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        // Out into the open air the climber was already hanging in, at the
        // ladder's foot: the ground they walked in from, never inside it.
        ClimbPoint landing = ladder.Bottom.Offset(ladder.StandingSide, limits.BodyOffsetMetres);
        return ClimbExitPlan.Step(ClimbExitKind.Bottom, landing, ladder.StandingSide);
    }

    /// <summary>The player jumped, or something ended the climb. Both let go
    /// where the character already is.</summary>
    public static ClimbExitPlan LetGo(bool deliberate) =>
        ClimbExitPlan.Release(deliberate ? ClimbExitKind.JumpedOff : ClimbExitKind.LetGo);
}
