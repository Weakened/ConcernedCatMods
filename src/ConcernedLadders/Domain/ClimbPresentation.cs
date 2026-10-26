using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>The authored rung shape of every Concerned Ladders section, in section-local metres. The piece
/// builder makes its rung boxes from these values, and presentation fits palms and soles to them.</summary>
internal static class RungShape
{
    internal const float HalfWidth = 0.31f;
    internal const float HalfHeight = 0.04f;
    internal const float HalfDepth = 0.06f;

    /// <summary>Distance of the rung centre plane from the section origin along the outward axis.</summary>
    internal const float Plane = 0.16f;

    /// <summary>Rungs per 2 m section; rung i's centre is (i + 0.5) × pitch above the section base.</summary>
    internal const int PerSection = 6;
}

/// <summary>What accepted gameplay is doing, for presentation only.</summary>
internal enum PresentedMotion
{
    Hold,
    Climb,
    Fast,
    Slide,
    GroundMount,
    AirCatch,
    TopFinish,
    StepOff,
}

/// <summary>Why an attachment ended. Delivered once, synchronously, before any departure impulse is applied.</summary>
internal enum ClimbEnd
{
    None,
    Jump,
    Drop,
    Landed,
    Death,
    Teleport,
    Destroyed,
    Disabled,
    External,
    Fault,
}

/// <summary>Body parts the game's own animation currently owns. Owner decision 2026-10-26: with the building tool
/// out only the hammer (right) arm is native, for its hold and every swing, while the body, legs and left hand keep
/// the climbing pose; an interact (door, hatch) owns both arms and the upper body. Presentation must release its own
/// influence on these parts while set, from the first frame.</summary>
[Flags]
internal enum NativeActionParts
{
    None = 0,
    LeftArm = 1,
    RightArm = 2,
    UpperBody = 4,
    Head = 8,
}

/// <summary>One accepted motor step, published after the motor ran it (owner revision 2026-10-26, agreed with
/// Codex). Read-only for presentation: nothing here may be used to move the body, read input or decide gameplay.
///
/// <para>Continuity: <see cref="Session"/> changes on every accepted attachment (bind afresh).
/// <see cref="Discontinuity"/> changes inside a session only for a real correction (a face change or an anchor
/// correction): re-fit contacts and do not interpolate across it. <see cref="GeometryRevision"/> changes when the
/// connected run was rebuilt (a section added or removed) without moving the climber: phase and planted world
/// contacts stay valid; only the available rung span changed.</para>
///
/// <para>Travel: <see cref="Travel"/> is the cumulative signed vertical distance the body completed while
/// climbing in this session (+ up). Mount, catch, top-finish and step-off motion and corrections are excluded.
/// <see cref="Sample"/> increases by one per published frame; consume each sample once.</para></summary>
internal readonly struct ClimbPresentationFrame
{
    internal ClimbPresentationFrame(int session, int discontinuity, int geometryRevision, long sample, float sampleTime,
        PresentedMotion motion, ClimbGait gait, float travel, float stepTravel, ClimbPoint feet, LadderGeometry run, LadderFace face,
        float rungPlane, float rungOrigin, int lastRung, float transition, ClimbPoint transitionFrom, ClimbPoint transitionTo,
        NativeActionParts nativeAction, bool hammerEquipped, bool menuHold)
    {
        Session = session;
        Discontinuity = discontinuity;
        GeometryRevision = geometryRevision;
        Sample = sample;
        SampleTime = sampleTime;
        Motion = motion;
        Gait = gait;
        Travel = travel;
        StepTravel = stepTravel;
        Feet = feet;
        Run = run;
        Face = face;
        RungPlane = rungPlane;
        RungOrigin = rungOrigin;
        LastRung = lastRung;
        Transition = transition;
        TransitionFrom = transitionFrom;
        TransitionTo = transitionTo;
        NativeAction = nativeAction;
        HammerEquipped = hammerEquipped;
        MenuHold = menuHold;
    }

    internal int Session { get; }
    internal int Discontinuity { get; }
    internal int GeometryRevision { get; }
    internal long Sample { get; }

    /// <summary>The physics time of the accepted step (Time.fixedTime).</summary>
    internal float SampleTime { get; }

    internal PresentedMotion Motion { get; }

    /// <summary>The gait the ledger accepted for this step (stamina, skill and blockage already resolved).</summary>
    internal ClimbGait Gait { get; }

    internal float Travel { get; }
    internal float StepTravel { get; }

    /// <summary>The body's physics position (capsule base) after the step.</summary>
    internal ClimbPoint Feet { get; }

    /// <summary>The connected run: Bottom/Top on the centreline, StandingSide is the front face's outward side.</summary>
    internal LadderGeometry Run { get; }

    /// <summary>Front climbs on the +StandingSide side, Rear on the opposite side.</summary>
    internal LadderFace Face { get; }

    /// <summary>Distance from the centreline to the rung centre plane along StandingSide (0.16 today).</summary>
    internal float RungPlane { get; }

    /// <summary>Height of rung 0's centre above Run.Bottom.Y; rung k is at Run.Bottom.Y + RungOrigin + k × pitch,
    /// unwrapped across section seams.</summary>
    internal float RungOrigin { get; }

    /// <summary>Index of the highest real rung on the current run; rungs 0..LastRung exist. Never fit a grip
    /// outside this span.</summary>
    internal int LastRung { get; }

    /// <summary>0..1 progress of an accepted GroundMount / AirCatch / TopFinish / StepOff move; -1 otherwise.
    /// The value is the motor's own route progress, not elapsed time.</summary>
    internal float Transition { get; }

    /// <summary>The accepted move's start and its validated end: for TopFinish/StepOff, the supported feet
    /// position the motor chose. Presentation may fit a sole there but never selects or validates a landing.</summary>
    internal ClimbPoint TransitionFrom { get; }
    internal ClimbPoint TransitionTo { get; }

    /// <summary>Body parts the game owns this step: RightArm while the building tool is out (its hold and every
    /// place/repair/remove swing); LeftArm | RightArm | UpperBody during an interact.</summary>
    internal NativeActionParts NativeAction { get; }

    /// <summary>Build mode with the hammer out, whether or not a swing is in flight.</summary>
    internal bool HammerEquipped { get; }

    /// <summary>A focused menu, console or inventory holds the climber; nothing is queued.</summary>
    internal bool MenuHold { get; }

    internal static float HalfWidth => RungShape.HalfWidth;
    internal static float HalfHeight => RungShape.HalfHeight;
    internal static float HalfDepth => RungShape.HalfDepth;

    /// <summary>Highest rung index whose centre lies on a run of the given height.</summary>
    internal static int LastRungOf(float runHeight, float rungOrigin, float pitch)
    {
        if (!ClimbPolicy.Finite(runHeight) || !ClimbPolicy.Finite(pitch) || pitch <= 0f || runHeight < rungOrigin) { return -1; }
        return (int)Math.Floor((runHeight - rungOrigin) / pitch + 1e-4f);
    }
}
