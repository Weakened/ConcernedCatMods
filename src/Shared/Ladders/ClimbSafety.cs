using System;

namespace TheConcernedCat.Ladders;

/// <summary>Why a climb had to end without the climber choosing it. Each one is
/// a real thing that happens in a Valheim world, and each one ends the same
/// way: the character gets its own body back and falls if it must.</summary>
internal enum ClimbEndReason
{
    Unspecified = 0,

    /// <summary>The ladder piece was destroyed under the climber.</summary>
    LadderDestroyed = 1,

    /// <summary>The ladder is no longer loaded: the player walked, sailed or
    /// teleported out of its zone.</summary>
    LadderUnloaded = 2,

    /// <summary>The ladder moved. A piece on a ship, or something rebuilt.</summary>
    LadderMoved = 3,

    /// <summary>The character is too far from the ladder to be on it: pushed,
    /// thrown, knocked back or teleported.</summary>
    LostContact = 4,

    /// <summary>The climber died.</summary>
    CharacterDied = 5,

    /// <summary>The character is being teleported, or has attached itself to
    /// something else (a bed, a chair, a ship's rudder).</summary>
    CharacterTaken = 6,

    /// <summary>The world is going away: logout, world unload, or the game
    /// shutting down.</summary>
    WorldUnloading = 7,

    /// <summary>The plugin is stopping, or ladders were switched off while
    /// somebody was on one.</summary>
    RuntimeStopped = 8,

    /// <summary>The controller itself failed. The climb still ends cleanly:
    /// an exception inside a climb must not leave a player stuck.</summary>
    ControllerFault = 9,
}

/// <summary>The facts a climb is checked against, every frame, taken from the
/// world by the runtime.</summary>
internal readonly struct ClimbConditions
{
    public ClimbConditions(
        bool ladderAlive,
        bool ladderLoaded,
        bool ladderStill,
        bool characterAlive,
        bool characterFree,
        bool worldUp,
        bool runtimeRunning,
        float distanceFromLadder)
    {
        LadderAlive = ladderAlive;
        LadderLoaded = ladderLoaded;
        LadderStill = ladderStill;
        CharacterAlive = characterAlive;
        CharacterFree = characterFree;
        WorldUp = worldUp;
        RuntimeRunning = runtimeRunning;
        DistanceFromLadder = distanceFromLadder;
    }

    public bool LadderAlive { get; }

    public bool LadderLoaded { get; }

    /// <summary>False when the ladder's own position has changed since the
    /// climb started.</summary>
    public bool LadderStill { get; }

    public bool CharacterAlive { get; }

    /// <summary>False while the character is teleporting or attached to
    /// something else.</summary>
    public bool CharacterFree { get; }

    public bool WorldUp { get; }

    public bool RuntimeRunning { get; }

    /// <summary>How far the body is from where the climb holds it.</summary>
    public float DistanceFromLadder { get; }

    /// <summary>Everything is fine: the shape a passing frame has.</summary>
    public static ClimbConditions Fine(float distanceFromLadder = 0f) =>
        new ClimbConditions(true, true, true, true, true, true, true, distanceFromLadder);
}

/// <summary>The one rule that must never be got wrong: a climb ends the moment
/// anything it depends on stops being true, and the character is always handed
/// back whole (LADDERS.md decision L5).</summary>
internal static class ClimbSafety
{
    /// <summary>The reason this climb must end now, or
    /// <see cref="ClimbEndReason.Unspecified"/> to carry on.
    ///
    /// The order is deliberate: the reasons that make a character's position
    /// meaningless come first, so nothing tries to place a body that the world
    /// no longer holds.</summary>
    public static ClimbEndReason MustEnd(in ClimbConditions conditions, ClimbLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (!conditions.WorldUp)
        {
            return ClimbEndReason.WorldUnloading;
        }

        if (!conditions.RuntimeRunning)
        {
            return ClimbEndReason.RuntimeStopped;
        }

        if (!conditions.CharacterAlive)
        {
            return ClimbEndReason.CharacterDied;
        }

        if (!conditions.CharacterFree)
        {
            return ClimbEndReason.CharacterTaken;
        }

        if (!conditions.LadderAlive)
        {
            return ClimbEndReason.LadderDestroyed;
        }

        if (!conditions.LadderLoaded)
        {
            return ClimbEndReason.LadderUnloaded;
        }

        if (!conditions.LadderStill)
        {
            return ClimbEndReason.LadderMoved;
        }

        if (conditions.DistanceFromLadder > limits.LostContactMetres)
        {
            return ClimbEndReason.LostContact;
        }

        return ClimbEndReason.Unspecified;
    }

    /// <summary>Ending on any of these reasons lets the character go where it
    /// stands. None of them places a body: a killed, thrown or unloaded
    /// character is never moved by this feature.</summary>
    public static ClimbExitPlan Release(ClimbEndReason reason)
    {
        if (reason == ClimbEndReason.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "A climb does not end without a reason.");
        }

        return ClimbExit.LetGo(deliberate: false);
    }

    /// <summary>What the runtime must put back, whatever the reason. The list
    /// exists so that a new exit path cannot quietly forget one of them.</summary>
    public static ClimbRestoration Restoration => new ClimbRestoration(
        restoreGravity: true,
        restoreMotor: true,
        restoreCollisions: true,
        clearClimbPose: true,
        stopClimbSound: true);

    public static string Explain(ClimbEndReason reason)
    {
        switch (reason)
        {
            case ClimbEndReason.LadderDestroyed:
                return "The ladder broke.";
            case ClimbEndReason.LadderUnloaded:
                return "The ladder is too far away now.";
            case ClimbEndReason.LadderMoved:
                return "The ladder moved.";
            case ClimbEndReason.LostContact:
                return "You lost your grip.";
            case ClimbEndReason.CharacterDied:
                return "You died on the ladder.";
            case ClimbEndReason.CharacterTaken:
                return "Something else took hold of you.";
            case ClimbEndReason.WorldUnloading:
                return "The world is closing.";
            case ClimbEndReason.RuntimeStopped:
                return "Ladder climbing stopped.";
            case ClimbEndReason.ControllerFault:
                return "Climbing failed, and you were let go safely.";
            default:
                return "The climb ended.";
        }
    }
}

/// <summary>Everything a climb takes away from a character while it holds it,
/// and therefore everything it must give back.</summary>
internal readonly struct ClimbRestoration
{
    public ClimbRestoration(
        bool restoreGravity,
        bool restoreMotor,
        bool restoreCollisions,
        bool clearClimbPose,
        bool stopClimbSound)
    {
        RestoreGravity = restoreGravity;
        RestoreMotor = restoreMotor;
        RestoreCollisions = restoreCollisions;
        ClearClimbPose = clearClimbPose;
        StopClimbSound = stopClimbSound;
    }

    public bool RestoreGravity { get; }

    public bool RestoreMotor { get; }

    public bool RestoreCollisions { get; }

    public bool ClearClimbPose { get; }

    public bool StopClimbSound { get; }

    public bool IsComplete =>
        RestoreGravity && RestoreMotor && RestoreCollisions && ClearClimbPose && StopClimbSound;
}
