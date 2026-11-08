using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>The runtime's one occupancy question (CL-004 #485, slice 3): would the lid's pose at this hinge angle overlap
/// a character, a dynamic body or another building piece? The lid runtime answers it with one bounded,
/// non-allocating physics overlap of the lid's own box at that angle, shrunk by a small skin so that surface contact
/// (something resting on the closed lid, or the lid resting against it) is not intrusion; tests answer it with a
/// script. Generic callers
/// (<see cref="HatchMotion.Step{TProbe}"/>) take it as a constrained type parameter, so a class probe is never boxed
/// and nothing is allocated per frame.</summary>
internal interface IHatchOccupancy
{
    /// <summary>True when the lid's box at <paramref name="angleDegrees"/> would overlap anything it must not move into.</summary>
    bool Occupied(float angleDegrees);
}

/// <summary>The lid's motion and its obstruction policy (CL-004 #485). Game-free: the runtime adapter feeds it the
/// native door's authoritative state and one occupancy answer per frame, and applies the returned progress to the
/// lid's transform and collider together, so motion and collision cannot disagree.
///
/// <para>State. The native <c>Door</c> component on the clone owns the open/closed state: its own <c>UseDoor</c> RPC
/// writes the ZDO, every client's <c>Door.UpdateState</c> reads it, and its animator's <c>state</c> integer follows.
/// The hatch reads that same ZDO integer (slice 3: <c>Runtime/TrapdoorLid</c>) and nothing else. Native doors open to +1 or -1 depending on which side the user
/// stands; a hatch has one swing direction, so both signs mean open.</para>
///
/// <para>Obstruction. The lid never moves into anything that is not fixed world or the hatch itself. Each frame the
/// runtime divides the frame's step into sub-steps of at most <see cref="MaximumAngleStepDegrees"/> and asks, in
/// order, whether the lid's pose at each would overlap a character, a dynamic body (a dropped item, a cart, a boat)
/// or another building piece; at the first that would, the lid holds at the last clear sub-step and tries again next
/// frame. So a lid with someone (or something, or a piece built on it) on it stays closed until it is clear, a lid
/// closing over a head waits. The guarantee is that no two checked poses are more than
/// <see cref="MaximumAngleStepDegrees"/> apart, so anything that spans more than that much of the sweep is always
/// seen; a body thinner than that (a small falling item) could pass between two checked poses, while anything resting
/// on the lid or standing in the opening spans far more. Nothing it checks is launched, pushed through a wall or shut in. The native state (and so the hover text) still says what was asked
/// for. Each client moves its own copy of the lid against the positions it sees; remote characters are
/// interpolated, so two clients can briefly disagree about when a held lid resumes, but each player's own client,
/// which is authoritative for that player's collision, never moves the lid into them.</para>
///
/// <para>Progress is the runtime's own float, and the runtime must keep its last finite value and never pass NaN. An
/// infinity reads as its end of the swing; NaN reads as the closed pose without any occupancy check.</para></summary>
internal static class HatchMotion
{
    /// <summary>Shortest and longest swing the policy accepts; a donor outside this range is a census error.</summary>
    internal const float MinimumSwingSeconds = 0.1f;
    internal const float MaximumSwingSeconds = 5f;

    /// <summary>Longest frame step applied at once, seconds; a longer hitch is simply slower that frame.</summary>
    internal const float MaximumStepSeconds = 0.05f;

    /// <summary>Largest hinge turn between two poses the runtime checks. At 5 degrees the lid's free edge (1.58 m out)
    /// moves at most 0.14 m between checks, less than any character capsule's diameter.</summary>
    internal const float MaximumAngleStepDegrees = 5f;

    /// <summary>The steepest slope of <see cref="Ease"/> (at progress one half): angle per unit progress is at most
    /// this times the open angle.</summary>
    internal const float MaximumEaseSlope = 1.5f;

    /// <summary>How much the runtime shrinks the lid's box on every side for its occupancy query, metres: surface
    /// contact is not intrusion, so a body resting on a closed lid does not read as inside it (the ladders'
    /// <c>LadderClearance</c> uses a skin for the same reason). The lid can therefore come at most this close past a
    /// surface it touches before the next checked pose sees it: a body resting on the lid is seen by the first
    /// sub-step anywhere the 5-degree turn lifts the lid by more than this, which is everywhere beyond about 0.12 m
    /// from the hinge, and by the next sub-step there.</summary>
    internal const float QuerySkinMetres = 0.01f;

    /// <summary>Hard ceiling on sub-steps in one frame (a 0.1 s swing over a full 0.05 s step needs 14).</summary>
    internal const int MaximumSubSteps = 32;

    /// <summary>The native door state integer means open when it is not zero, whatever its sign.</summary>
    internal static bool IsOpenState(int nativeState) => nativeState != 0;

    /// <summary>Ease-in-out over the swing: progress 0..1 to the fraction of the open angle.</summary>
    internal static float Ease(float progress)
    {
        float p = Clamp01(progress);
        return p * p * (3f - 2f * p);
    }

    /// <summary>The hinge angle for a progress, degrees.</summary>
    internal static float Angle(float progress) => HatchGeometry.OpenAngleDegrees * Ease(progress);

    /// <summary>Where the lid would be after this frame if nothing were in the way. Linear in time, so a full swing
    /// takes exactly <paramref name="swingSeconds"/>; a reversal continues from the current progress.</summary>
    internal static float Proposed(float progress, bool open, float seconds, float swingSeconds)
    {
        float current = Clamp01(progress);
        float step = StepSeconds(seconds) / ClampSwing(swingSeconds);
        return open ? Math.Min(1f, current + step) : Math.Max(0f, current - step);
    }

    /// <summary>How many equal sub-steps the move from <paramref name="progress"/> to <paramref name="proposed"/> needs so
    /// no two consecutive checked poses differ by more than <see cref="MaximumAngleStepDegrees"/>. Zero when there is
    /// no move.</summary>
    internal static int SubSteps(float progress, float proposed)
    {
        float delta = Math.Abs(Clamp01(proposed) - Clamp01(progress));
        if (delta <= 0f) { return 0; }
        double worstDegrees = delta * MaximumEaseSlope * HatchGeometry.OpenAngleDegrees;
        return Math.Max(1, Math.Min(MaximumSubSteps, (int)Math.Ceiling(worstDegrees / MaximumAngleStepDegrees - 1e-6)));
    }

    /// <summary>The progress of sub-step <paramref name="step"/> (1-based) of <paramref name="count"/> on the way from
    /// <paramref name="progress"/> to <paramref name="proposed"/>; the last is the proposal itself.</summary>
    internal static float SubStep(float progress, float proposed, int step, int count)
    {
        float from = Clamp01(progress), to = Clamp01(proposed);
        if (count <= 0 || step >= count) { return to; }
        if (step <= 0) { return from; }
        return from + (to - from) * step / count;
    }

    /// <summary>The progress after one checked sub-step: the proposal, unless the lid's pose there would overlap
    /// something it must not move into, in which case the lid holds at its current progress.</summary>
    internal static float Advance(float progress, float proposed, bool proposedPoseOccupied)
    {
        float current = Clamp01(progress);
        return proposedPoseOccupied ? current : Clamp01(proposed);
    }

    /// <summary>One frame of the lid (slice 3): propose this frame's move, split it into <see cref="SubSteps"/> checked
    /// poses, ask <paramref name="probe"/> about each in order, and stop at the first occupied one, returning the last
    /// clear progress. So the returned progress is either the starting progress (nothing moved) or a pose the probe
    /// reported clear; the lid is never put at a pose that was not checked. No move asks nothing. Allocation-free.</summary>
    internal static float Step<TProbe>(float progress, bool open, float seconds, float swingSeconds, TProbe probe)
        where TProbe : IHatchOccupancy
    {
        float current = Clamp01(progress);
        float proposed = Proposed(current, open, seconds, swingSeconds);
        int count = SubSteps(current, proposed);
        float reached = current;
        for (int step = 1; step <= count; ++step)
        {
            float next = SubStep(current, proposed, step, count);
            bool occupied = probe.Occupied(Angle(next));
            reached = Advance(reached, next, occupied);
            if (occupied) { break; }
        }

        return reached;
    }

    /// <summary>Where a lid starts the first time its runtime reads the native state: when a piece is placed, loaded
    /// with its zone, or seen by a late joiner. The stored state's end of the swing, which is what every other client
    /// already shows, if that pose is clear. If it is not (someone stands where a loaded-closed lid would lie), the
    /// other end, if that is clear: the checked motion of <see cref="Step{TProbe}"/> then carries the lid to the stored
    /// state once the way is clear, so it is never put into whoever is there. When both ends are occupied, which
    /// needs something both in the opening and where the upright lid stands, it takes the clear pose nearest the stored
    /// end, scanning at <see cref="MaximumAngleStepDegrees"/>; only if no pose of the swing is clear does it take the
    /// stored end anyway: the lid has to be somewhere, and that is the pose the network agrees on. Contact is not
    /// occupancy (the runtime's query has a skin), so a closed lid with something resting on it loads closed.
    /// Asks at most <see cref="SettleScanPoses"/> + 1 times.</summary>
    internal static float Settle<TProbe>(bool open, TProbe probe)
        where TProbe : IHatchOccupancy
    {
        float stored = open ? 1f : 0f;
        if (!probe.Occupied(Angle(stored))) { return stored; }
        float other = 1f - stored;
        if (!probe.Occupied(Angle(other))) { return other; }
        for (int pose = 1; pose < SettleScanPoses; ++pose)
        {
            float angle = HatchGeometry.OpenAngleDegrees * pose / SettleScanPoses;
            float candidate = ProgressForAngle(open ? HatchGeometry.OpenAngleDegrees - angle : angle);
            if (!probe.Occupied(Angle(candidate))) { return candidate; }
        }

        return stored;
    }

    /// <summary>The poses <see cref="Settle{TProbe}"/> scans across the swing when both ends are occupied: one per
    /// <see cref="MaximumAngleStepDegrees"/>.</summary>
    internal const int SettleScanPoses = (int)(HatchGeometry.OpenAngleDegrees / MaximumAngleStepDegrees);

    /// <summary>The progress whose <see cref="Angle"/> is <paramref name="angleDegrees"/>: the inverse of the ease, by
    /// bisection (the ease is monotonic). Only for the one-off settle scan, never per frame.</summary>
    internal static float ProgressForAngle(float angleDegrees)
    {
        float target = Math.Max(0f, Math.Min(HatchGeometry.OpenAngleDegrees, float.IsNaN(angleDegrees) ? 0f : angleDegrees));
        float low = 0f, high = 1f;
        for (int iteration = 0; iteration < 40; ++iteration)
        {
            float middle = (low + high) * 0.5f;
            if (Angle(middle) < target) { low = middle; } else { high = middle; }
        }

        return (low + high) * 0.5f;
    }

    /// <summary>True while the lid has somewhere to go.</summary>
    internal static bool IsMoving(float progress, bool open)
    {
        float p = Clamp01(progress);
        return open ? p < 1f : p > 0f;
    }

    /// <summary>The opening is a passage only while the lid is fully open and staying open: a climber may go up
    /// through it, and the ladder's top exit may route through it. A lid asked to close is not, even before it moves
    /// (it would hold against the climber anyway, but a route must not be committed through it).</summary>
    internal static bool IsPassage(float progress, bool open) => open && Clamp01(progress) >= 1f;

    /// <summary>The lid is a floor only while closed and staying closed. A moving, held or open lid is never a
    /// landing the top exit may choose; it must find a real floor instead.</summary>
    internal static bool IsLanding(float progress, bool open) => !open && Clamp01(progress) <= 0f;

    private static float StepSeconds(float seconds) =>
        float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f ? 0f : Math.Min(seconds, MaximumStepSeconds);

    private static float ClampSwing(float swingSeconds) =>
        float.IsNaN(swingSeconds) || float.IsInfinity(swingSeconds)
            ? MaximumSwingSeconds
            : Math.Max(MinimumSwingSeconds, Math.Min(MaximumSwingSeconds, swingSeconds));

    private static float Clamp01(float value) =>
        float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
}
