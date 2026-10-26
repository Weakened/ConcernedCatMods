using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>Which physical face of a ladder a climber is on. The front face is the side the authored
/// outward direction points to; the rear face is the other side of the same rails.</summary>
internal enum LadderFace
{
    Front = 1,
    Rear = -1,
}

/// <summary>How a climber is moving this step.</summary>
internal enum ClimbGait
{
    Hold,
    Normal,
    Fast,
    Slide,
}

/// <summary>Why an admission, a climb step or an exit did not happen. Each one has its own message; only
/// <see cref="Building"/> may blame a building piece, and only a real other piece produces it.</summary>
internal enum ClimbRefusal
{
    None,
    OutOfReach,
    Unavailable,
    Building,
    OwnLadder,
    Terrain,
    Actor,
    Obstacle,
    Unchecked,
    NoLanding,
}

/// <summary>The kind of collider a clearance query hit, as the engine adapter classified it.</summary>
internal enum BlockerKind
{
    None,
    OwnLadder,
    Building,
    Terrain,
    Actor,
    Other,
    Overflow,
}

/// <summary>Owner-revision rules for reach, faces, air catches, speeds, stamina and skill. Game-free.</summary>
internal static class ClimbPolicy
{
    /// <summary>Maximum distance from the climber's chest to the nearest usable rung span.</summary>
    internal const float MaxReach = 1.5f;

    /// <summary>A catch may move the body at most this far sideways onto its lane.</summary>
    internal const float AirHorizontalLimit = 1.0f;

    /// <summary>A catch may move the body at most this far up or down; anything more is a hoist.</summary>
    internal const float AirVerticalLimit = 0.25f;

    /// <summary>Chest and grip heights as fractions of the capsule height above the feet.</summary>
    internal const float ChestFraction = 0.72f;
    internal const float GripFraction = 0.85f;

    /// <summary>Half the rung length: the usable span sideways from the centreline.</summary>
    internal const float RungHalfWidth = 0.31f;

    /// <summary>Lane clearance between the capsule and the rails' collider face.</summary>
    internal const float LaneGap = 0.06f;

    internal const float NormalSpeed = 1.8f;
    internal const float FastFactor = 1.35f;
    internal const float SlideFactor = 2.5f;
    internal const float FastSpeed = NormalSpeed * FastFactor;
    internal const float SlideSpeed = NormalSpeed * SlideFactor;
    internal const float FastStaminaPerSecond = 6f;
    internal const float SlideStaminaPerSecond = 2f;
    internal const float MaximumStaminaSaving = 0.25f;
    internal const float SlideUnlockLevel = 20f;
    internal const float SkillFactorPerMetre = 0.25f;
    internal const float RemountDelay = 0.4f;

    /// <summary>Horizontal departure speed of a powered jump off the rails, before the native jump skill bonus.</summary>
    internal const float JumpDepartureSpeed = 3.2f;

    /// <summary>An exhausted or encumbered release only clears the rails; ordinary gravity does the rest.</summary>
    internal const float ReleasePushSpeed = 1.0f;

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>The face whose side of the rails' collider the point is on. The collider spans
    /// <paramref name="backDepth"/>..<paramref name="frontDepth"/> along the outward direction; the
    /// midpoint of that box separates the two faces, not the authored origin.</summary>
    internal static LadderFace FaceOf(float depthAlongOutward, float backDepth, float frontDepth) =>
        depthAlongOutward >= (backDepth + frontDepth) * 0.5f ? LadderFace.Front : LadderFace.Rear;

    /// <summary>Signed distance along the authored outward direction from the run's line to the centre of
    /// a climber's capsule on <paramref name="face"/>. Each face is measured from its own collider surface:
    /// the box is not centred on the origin, so the rear lane is not the negated front lane.</summary>
    internal static float LaneDepth(LadderFace face, float radius, float backDepth, float frontDepth)
    {
        if (!Finite(radius) || radius < 0.05f || radius > 1f || !Finite(backDepth) || !Finite(frontDepth) ||
            backDepth > frontDepth || frontDepth - backDepth > 0.6f || Math.Abs(backDepth) > 0.5f || Math.Abs(frontDepth) > 0.5f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "The capsule and ladder faces must have bounded dimensions.");
        }

        return face == LadderFace.Front ? frontDepth + radius + LaneGap : backDepth - radius - LaneGap;
    }

    /// <summary>Distance from the chest to the nearest point of the usable rung span: the rectangle in
    /// the rails' mid-plane between the lowest and highest rung, a rung's half length either side.</summary>
    internal static float ChestReach(in LadderGeometry run, ClimbPoint chest, float planeDepth, float firstRungHeight)
    {
        if (!run.IsClimbable || !chest.IsFinite || !Finite(planeDepth) || !Finite(firstRungHeight))
        {
            return float.PositiveInfinity;
        }

        float depth = run.ReachFrom(chest) - planeDepth;
        float sideways = Math.Max(0f, run.SidewaysOffsetOf(chest) - RungHalfWidth);
        float low = run.Bottom.Y + firstRungHeight;
        float high = run.Top.Y - firstRungHeight;
        float vertical = chest.Y < low ? low - chest.Y : chest.Y > high ? chest.Y - high : 0f;
        return (float)Math.Sqrt(depth * depth + sideways * sideways + vertical * vertical);
    }

    internal static bool InReach(float reach) => Finite(reach) && reach <= MaxReach;

    /// <summary>The lowest feet height at which the hands still hold the lowest rung.</summary>
    internal static float LowestHang(float bottom, float firstRungHeight, float gripHeight) =>
        bottom + firstRungHeight - gripHeight;

    /// <summary>The feet height a fresh grab holds at: where the body already is, inside the span the hands
    /// can hold. Never raised to the foot of the run (that would be a hoist).</summary>
    internal static float GrabHeight(float feet, float lowestHang, float top) => Math.Max(lowestHang, Math.Min(top, feet));

    /// <summary>An airborne catch may only nudge the body onto its lane; a larger adjustment is out of reach.</summary>
    internal static bool AirAlignmentAllowed(float horizontal, float vertical) =>
        Finite(horizontal) && Finite(vertical) && horizontal <= AirHorizontalLimit && Math.Abs(vertical) <= AirVerticalLimit;

    /// <summary>Admission gates that do not depend on geometry. Being airborne is not one of them.</summary>
    internal static bool MayAdmit(bool owned, bool free, bool overlayOpen, float cooldown) =>
        owned && free && !overlayOpen && Finite(cooldown) && cooldown <= 0f;

    internal static float StaminaSaving(float learnedLevel) =>
        Finite(learnedLevel) ? MaximumStaminaSaving * Math.Max(0f, Math.Min(100f, learnedLevel)) / 100f : 0f;

    internal static bool SlideUnlocked(float learnedLevel) => Finite(learnedLevel) && learnedLevel >= SlideUnlockLevel;

    internal static float SpeedOf(ClimbGait gait) => gait switch
    {
        ClimbGait.Normal => NormalSpeed,
        ClimbGait.Fast => FastSpeed,
        ClimbGait.Slide => SlideSpeed,
        _ => 0f,
    };

    /// <summary>Stamina per metre of eligible travel in a gait, after the learned-level saving.</summary>
    internal static float StaminaPerMetre(ClimbGait gait, float learnedLevel)
    {
        float rate = gait == ClimbGait.Fast ? FastStaminaPerSecond : gait == ClimbGait.Slide ? SlideStaminaPerSecond : 0f;
        return rate <= 0f ? 0f : rate / SpeedOf(gait) * (1f - StaminaSaving(learnedLevel));
    }

    /// <summary>The gait for this step. Run is a request: without the stamina for the step it falls back to
    /// a normal climb (never a forced fall), and slide needs the current learned level.</summary>
    internal static ClimbGait SelectGait(float input, bool run, float learnedLevel, float stamina, float seconds)
    {
        if (!Finite(input) || input == 0f) { return ClimbGait.Hold; }
        if (!run || !Finite(stamina) || !Finite(seconds) || seconds <= 0f) { return ClimbGait.Normal; }
        ClimbGait wanted = input < 0f && SlideUnlocked(learnedLevel) ? ClimbGait.Slide : ClimbGait.Fast;
        float budget = StaminaPerMetre(wanted, learnedLevel) * SpeedOf(wanted) * Math.Abs(input) * seconds;
        return stamina > budget ? wanted : ClimbGait.Normal;
    }

    /// <summary>Climb speed for an analog input in a gait; a slide always runs at full speed downward.</summary>
    internal static float StepSpeed(ClimbGait gait, float input) =>
        gait == ClimbGait.Slide ? -SlideSpeed : gait == ClimbGait.Hold ? 0f : SpeedOf(gait) * input;

    /// <summary>The horizontal departure direction for a jump: the camera-relative movement input, or the
    /// face's outward direction when there is none. A direction straight into the rails is not a departure.</summary>
    internal static void DepartureDirection(float moveX, float moveZ, float outwardX, float outwardZ, out float x, out float z)
    {
        float length = Finite(moveX) && Finite(moveZ) ? (float)Math.Sqrt(moveX * moveX + moveZ * moveZ) : 0f;
        if (length > 0.15f)
        {
            x = moveX / length;
            z = moveZ / length;
            if (x * outwardX + z * outwardZ > -0.5f) { return; }
        }

        x = outwardX;
        z = outwardZ;
    }
}

/// <summary>One bounded pending climb step paid once. The step is planned with its gait and distance,
/// the next physics tick observes how far the body really went, and settlement charges stamina and
/// accrues skill distance only for that eligible travel, never more than was planned.</summary>
internal sealed class ClimbLedger
{
    private ClimbGait _gait = ClimbGait.Hold;
    private float _planned;
    private float _start;
    private bool _pending;

    /// <summary>Unawarded normal/fast metres carried across mounts in one character/world session.</summary>
    internal float CarriedMetres { get; private set; }

    internal bool Pending => _pending;

    internal void Plan(ClimbGait gait, float startHeight, float plannedDistance)
    {
        if (!ClimbPolicy.Finite(startHeight) || !ClimbPolicy.Finite(plannedDistance) || plannedDistance < 0f)
        {
            Discard();
            return;
        }

        _gait = gait;
        _start = startHeight;
        _planned = Math.Min(plannedDistance, 1f);
        _pending = gait != ClimbGait.Hold && _planned > 0f;
    }

    /// <summary>Settles the pending step against the body's observed height. Returns the stamina to charge
    /// and the whole skill metres completed by this settlement.</summary>
    internal void Settle(float observedHeight, float learnedLevel, out float stamina, out int skillMetres)
    {
        stamina = 0f;
        skillMetres = 0;
        if (!_pending) { return; }
        _pending = false;
        if (!ClimbPolicy.Finite(observedHeight)) { return; }
        float travelled = Math.Min(Math.Abs(observedHeight - _start), _planned);
        if (travelled <= 0f) { return; }
        stamina = ClimbPolicy.StaminaPerMetre(_gait, learnedLevel) * travelled;
        if (_gait == ClimbGait.Normal || _gait == ClimbGait.Fast)
        {
            CarriedMetres += travelled;
            skillMetres = (int)Math.Floor(CarriedMetres);
            CarriedMetres -= skillMetres;
        }
    }

    /// <summary>Ownership loss or a takeover drops the unobserved step: nothing is charged for it.</summary>
    internal void Discard() { _pending = false; _planned = 0f; }

    /// <summary>Logout, character or world change clears the carried fraction too.</summary>
    internal void Reset() { Discard(); CarriedMetres = 0f; }
}
