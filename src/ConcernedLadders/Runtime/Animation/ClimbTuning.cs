using System;
using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>Every presentation value in one place (owner correction 2026-10-26). Metres and degrees. The gait, body
/// and bend values come from the Blender source (AnimationSources/LadderGait.blend, exported to GeneratedGait); the
/// fit values below are measured in game and edited here. A development harness may change them at runtime and bump <see cref="Version"/>,
/// which re-fits the current climb; shipped builds never do. See AnimationSources/README.md.</summary>
internal static class ClimbTuning
{
    internal static GaitShape Gait = GeneratedGait.Shape();

    /// <summary>Pelvis height below the rig's standing pelvis estimate, and how far it leans in toward the rungs.</summary>
    internal static float PelvisDrop = GeneratedGait.PelvisDrop;
    internal static float PelvisToward = GeneratedGait.PelvisToward;

    /// <summary>Torso pitch toward the ladder while climbing and while sliding; side tilt per unit of sway.</summary>
    internal static float Lean = GeneratedGait.Lean;
    internal static float SlideLean = GeneratedGait.SlideLean;
    internal static float SwayTilt = GeneratedGait.SwayTilt;
    internal static float SwayShift = GeneratedGait.SwayShift;

    /// <summary>Hand grip on a rung: the palm centre sits this far above the rung's centre and this far toward the
    /// climber from the rung's centre plane; hands sit this far either side of the centreline.</summary>
    internal static float HandAbove = 0.055f;
    internal static float HandOut = 0.025f;
    internal static float HandSpan = 0.19f;

    /// <summary>Fingers' angle from vertical toward the ladder (0 = fingers up, 90 = fingers level into the ladder).</summary>
    internal static float HandFingerPitch = 70f;

    /// <summary>+1 or -1: which way the humanoid hand goal's up (the back of the hand) faces.</summary>
    internal static float HandNormalSign = 1f;

    /// <summary>Palm centre from the wrist joint: along the fingers, and toward the palm side.</summary>
    internal static float HandPalmAlong = 0.07f;
    internal static float HandPalmDepth = -0.01f;

    /// <summary>Ankle joint from the ball of the foot: behind it toward the heel, and above the sole.</summary>
    internal static float FootBallForward = 0.165f;
    internal static float FootAnkleHeight = 0.146f;

    /// <summary>On a round rung the palm centre sits this many degrees from the rung's top toward the climber.</summary>
    internal static float HandWrapDegrees = 35f;

    /// <summary>Half the palm's thickness: palm patch (bone centre) to skin. The gap measure's zero for hands.</summary>
    internal static float PalmThickness = 0.02f;

    /// <summary>Distance from the toe-base joint down to the sole surface (measurement definition of the sole patch).</summary>
    internal static float SoleThickness = 0.03f;

    /// <summary>Sole contact: the ball of the foot sits this far above the rung centre and this far toward the
    /// climber from the rung's centre plane; feet sit this far either side of the centreline.</summary>
    internal static float FootAbove = 0.045f;
    internal static float FootOut = 0.0f;
    internal static float FootSpan = 0.128f;

    /// <summary>Elbow and knee bend hints, relative to the shoulder and hip.</summary>
    internal static float ElbowOut = GeneratedGait.ElbowOut;
    internal static float ElbowSide = GeneratedGait.ElbowSide;
    internal static float ElbowDown = GeneratedGait.ElbowDown;
    internal static float KneeToward = GeneratedGait.KneeToward;
    internal static float KneeSide = GeneratedGait.KneeSide;
    internal static float KneeDown = GeneratedGait.KneeDown;

    /// <summary>Near the top: a hand whose rung lies more than RailFromBelow below its shoulder blends (over RailBlend)
    /// onto the rail, RailBelowShoulder under the shoulder, RailSide from the centreline, RailOut in front of the rail
    /// centre plane. ExitFold is the torso's extra forward pitch at the middle of a top finish.</summary>
    internal static float RailFromBelow = 0.42f;
    internal static float RailBlend = 0.12f;
    internal static float RailBelowShoulder = 0.3f;
    internal static float RailSide = 0.34f;
    internal static float RailOut = 0.08f;
    internal static float ExitFold = 28f;


    /// <summary>Authored transitions (mount, catch, top entry, top/side exit, bottom step-off), from the Blender source.</summary>
    internal static TransitionAction[] Transitions = GeneratedGait.Transitions();

    internal static TransitionAction Action(TransitionKind kind)
    {
        foreach (TransitionAction action in Transitions) { if (action.Kind == kind) { return action; } }
        foreach (TransitionAction action in DefaultTransitions.All()) { if (action.Kind == kind) { return action; } }
        throw new InvalidOperationException("No authored transition " + kind + ".");
    }

    /// <summary>How much of the native pose's hip yaw is removed so the hips face the ladder square (1 = fully).</summary>
    internal static float SquareHips = 1f;

    /// <summary>How much of the native chest twist against the hips is removed (1 = shoulders square to the rungs).</summary>
    internal static float SquareChest = 1f;

    /// <summary>Climbing up, how far the knee hint moves to the middle of the leg pushed KneeAhead toward the rungs.</summary>
    internal static float KneeMidAscent = 1f;
    internal static float KneeAhead = 0.35f;

    /// <summary>On a top exit or entry the palms press on the floor this far past the ladder's plane.</summary>
    internal static float LipOnto = 0.18f;

    /// <summary>On a top exit the feet step onto the floor this much further past the palms.</summary>
    internal static float FootOnto = 0.2f;

    /// <summary>Over the top: from this far below the run's top the climber begins to crouch (pelvis down and toward
    /// the ladder) and fold forward, reaching the full amount TopApproachSpan higher.</summary>
    internal static float TopApproachFrom = 1.15f;
    internal static float TopApproachSpan = 0.5f;
    internal static float TopApproachDrop = 0.34f;
    internal static float TopApproachToward = 0.12f;
    internal static float TopApproachLean = 22f;

    /// <summary>A contact needing more than FadeFrom of its chain fades out toward ReachLimit instead of popping.</summary>
    internal static float FadeFrom = 0.96f;

    /// <summary>A contact needing more than this fraction of the measured chain is skipped and counted; never stretched.</summary>
    internal const float ReachLimit = 1.01f;

    /// <summary>Development diagnostics: log each change of the ladder snap decision (off in normal play).</summary>
    internal static bool LogSnapDecisions = false;

    /// <summary>Development diagnostics: write one CSV row per limb per frame to BepInEx/ladder_contact_trace.csv
    /// (bounded; off in normal play).</summary>
    internal static bool TraceContacts = false;

    /// <summary>Development diagnostics: log every landing query of a dismount, step-off, top exit or top entry and
    /// why it was accepted or refused (off in normal play).</summary>
    internal static bool LogLandingQueries = false;

    internal static int Version { get; private set; }

    /// <summary>Re-fit the current climb after values changed (development harness only).</summary>
    internal static void Bump() => Version++;
}
