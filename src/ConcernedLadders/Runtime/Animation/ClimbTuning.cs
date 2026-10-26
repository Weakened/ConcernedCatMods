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
    internal static float HandAbove = 0.035f;
    internal static float HandOut = 0.045f;
    internal static float HandSpan = 0.19f;

    /// <summary>Fingers' angle from vertical toward the ladder (0 = fingers up, 90 = fingers level into the ladder).</summary>
    internal static float HandFingerPitch = 70f;

    /// <summary>+1 or -1: which way the humanoid hand goal's up (the back of the hand) faces.</summary>
    internal static float HandNormalSign = 1f;

    /// <summary>Palm centre from the wrist joint: along the fingers, and toward the palm side.</summary>
    internal static float HandPalmAlong = 0.07f;
    internal static float HandPalmDepth = 0.02f;

    /// <summary>Ankle joint from the ball of the foot: behind it toward the heel, and above the sole.</summary>
    internal static float FootBallForward = 0.15f;
    internal static float FootAnkleHeight = 0.09f;

    /// <summary>Sole contact: the ball of the foot sits this far above the rung centre and this far toward the
    /// climber from the rung's centre plane; feet sit this far either side of the centreline.</summary>
    internal static float FootAbove = 0.045f;
    internal static float FootOut = 0.0f;
    internal static float FootSpan = 0.14f;

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


    /// <summary>Attaching: seconds after the attachment each limb (LH, RH, LF, RF) starts taking its contact, how long
    /// each takes, and how long the body takes to settle into the climbing posture; a ground mount and a midair catch.</summary>
    internal static float[] MountEntryDelay = { 0.14f, 0.0f, 0.26f, 0.40f };
    internal static float MountEntryLimb = 0.28f;
    internal static float MountEntryBody = 0.55f;
    internal static float[] CatchEntryDelay = { 0.0f, 0.04f, 0.12f, 0.18f };
    internal static float CatchEntryLimb = 0.14f;
    internal static float CatchEntryBody = 0.25f;

    /// <summary>A contact needing more than FadeFrom of its chain fades out toward ReachLimit instead of popping.</summary>
    internal static float FadeFrom = 0.96f;

    /// <summary>A contact needing more than this fraction of the measured chain is skipped and counted; never stretched.</summary>
    internal const float ReachLimit = 1.01f;

    /// <summary>Development diagnostics: log each change of the ladder snap decision (off in normal play).</summary>
    internal static bool LogSnapDecisions = false;

    internal static int Version { get; private set; }

    /// <summary>Re-fit the current climb after values changed (development harness only).</summary>
    internal static void Bump() => Version++;
}
