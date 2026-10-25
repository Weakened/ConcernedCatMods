using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>Policies used by the real controller. No engine stand-ins are needed to test them.</summary>
internal static class LadderRules
{
    internal const float Speed = 1.8f;
    internal const float EndpointTolerance = 0.03f;
    internal const int MaximumSegments = 64;
    internal const int MaximumRegisteredSegments = 2048;
    internal const float MaximumFrameSeconds = 0.05f;
    internal const float RemountDelay = 0.35f;

    /// <summary>How far below the run's foot a supported player may stand and still mount. A wall on a
    /// slope stands on its uphill ground, so the open face of its ladder can be this high above the
    /// downhill side. The engine adapter still sweeps the whole rise; this is a step, not a jump.</summary>
    internal const float MaximumStepUpToFoot = 0.75f;

    /// <summary>How far in front of or behind the climb line a supported player may mount. The native
    /// hover prompt already shows at about 1.5 m, so a smaller band refused a mount the game offered.</summary>
    internal const float MountReach = 1.6f;

    /// <summary>How far beyond the rails' half width a side landing may be and still mount.</summary>
    internal const float MountSideMargin = 0.45f;

    /// <summary>Every transfer route first lifts the body this far, so its lateral leg clears the floor it starts on.</summary>
    internal const float RouteLift = 0.06f;

    /// <summary>The second mount lift, a natural step over uneven ground. Used only when the low route is blocked.</summary>
    internal const float StepLift = 0.3f;

    /// <summary>Descending within this height of the run's foot onto real support ends the climb.</summary>
    internal const float BottomExitBand = 0.5f;

    /// <summary>The lowest height the climb may rest at this frame. A body already stepped down below the
    /// foot (or mounted from ground below it) holds where it is rather than being pulled back up to the
    /// foot, but never lower than a step below it.</summary>
    internal static float LowestHold(float bottom, float bodyHeight) =>
        Finite(bodyHeight) ? Math.Min(bottom, Math.Max(bodyHeight, bottom - MaximumStepUpToFoot)) : bottom;

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static float FrameSeconds(float seconds) =>
        Finite(seconds) && seconds > 0f ? Math.Min(seconds, MaximumFrameSeconds) : 0f;

    internal static float Input(float input, bool overlayOpen) =>
        overlayOpen || !Finite(input) || Math.Abs(input) < 0.15f ? 0f : Math.Max(-1f, Math.Min(1f, input));

    internal static float BodyOffset(float radius, float frontFaceDepth)
    {
        if (!Finite(radius) || radius < 0.05f || radius > 1f || !Finite(frontFaceDepth) || frontFaceDepth < 0f || frontFaceDepth > 0.5f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "The capsule and ladder face must have bounded dimensions.");
        }

        return radius + frontFaceDepth + 0.06f;
    }

    internal static float Advance(float height, float input, float seconds, float bottom, float top)
    {
        if (!Finite(height) || !Finite(bottom) || !Finite(top) || top < bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The climb span must be finite and ordered.");
        }

        return Math.Max(bottom, Math.Min(top, height + Input(input, false) * Speed * FrameSeconds(seconds)));
    }

    /// <summary>Mounts start from a supported surface. Grabbing a ladder cannot erase an existing fall.</summary>
    internal static bool CanMount(
        in LadderGeometry run, ClimbPoint feet, bool supported, float verticalSpeed,
        float recordedFallHeight, bool owned, bool free, bool overlayOpen, float cooldown)
    {
        if (!run.IsClimbable || !feet.IsFinite || !supported || !owned || !free || overlayOpen ||
            !Finite(verticalSpeed) || verticalSpeed < -0.5f || !Finite(recordedFallHeight) ||
            recordedFallHeight - feet.Y > 0.75f || !Finite(cooldown) || cooldown > 0f)
        {
            return false;
        }

        // A supported intermediate landing may be on either side. The engine adapter must still prove
        // a swept route onto the open face; this decision never licenses moving through the wall.
        return feet.Y >= run.Bottom.Y - MaximumStepUpToFoot && feet.Y <= run.Top.Y + 0.5f &&
               Math.Abs(run.ReachFrom(feet)) <= MountReach &&
               run.SidewaysOffsetOf(feet) <= run.Width * 0.5f + MountSideMargin;
    }

    /// <summary>True when a player standing in front of the ladder, at a height it could be mounted from,
    /// is refused only for being too far out or too far to the side. The game's own prompt shows from
    /// further away than the mount band, so this case is told to step closer.</summary>
    internal static bool TooFarToMount(in LadderGeometry run, ClimbPoint feet)
    {
        if (!run.IsClimbable || !feet.IsFinite || feet.Y < run.Bottom.Y - MaximumStepUpToFoot || feet.Y > run.Top.Y + 0.5f)
        {
            return false;
        }

        float reach = run.ReachFrom(feet);
        return reach > 0f && (reach > MountReach || run.SidewaysOffsetOf(feet) > run.Width * 0.5f + MountSideMargin);
    }

    internal static bool Joins(in LadderGeometry below, in LadderGeometry above) =>
        below.IsClimbable && above.IsClimbable &&
        Math.Abs(below.Top.Y - above.Bottom.Y) <= EndpointTolerance &&
        below.Top.HorizontalDistanceTo(above.Bottom) <= EndpointTolerance &&
        below.StandingSide.Agreement(above.StandingSide) >= 0.999f;

    /// <summary>Exact connected endpoints, across the complete loaded registry rather than a mount-radius sphere.</summary>
    internal static bool TryBuildRun(
        IReadOnlyList<LadderSample> samples, int anchorId, out List<int> orderedIds, out LadderGeometry run)
    {
        orderedIds = new List<int>();
        run = default;
        if (samples.Count == 0 || samples.Count > MaximumRegisteredSegments)
        {
            return false;
        }

        int anchor = -1;
        var uniqueIds = new HashSet<int>();
        for (int index = 0; index < samples.Count; index++)
        {
            if (!uniqueIds.Add(samples[index].Id) || !samples[index].Geometry.IsClimbable)
            {
                return false;
            }

            if (samples[index].Id == anchorId)
            {
                anchor = index;
            }
        }

        if (anchor < 0)
        {
            return false;
        }

        var chain = new List<LadderSample> { samples[anchor] };
        var seen = new HashSet<int> { anchorId };
        foreach (bool upwards in new[] { false, true })
        {
            while (true)
            {
                LadderSample endpoint = upwards ? chain[chain.Count - 1] : chain[0];
                int found = -1;
                for (int index = 0; index < samples.Count; index++)
                {
                    LadderSample candidate = samples[index];
                    if (seen.Contains(candidate.Id))
                    {
                        continue;
                    }

                    bool sameLine = candidate.Geometry.Bottom.HorizontalDistanceTo(endpoint.Geometry.Bottom) <= EndpointTolerance &&
                                    candidate.Geometry.StandingSide.Agreement(endpoint.Geometry.StandingSide) >= 0.999f;
                    bool overlaps = candidate.Geometry.Bottom.Y < endpoint.Geometry.Top.Y - EndpointTolerance &&
                                    candidate.Geometry.Top.Y > endpoint.Geometry.Bottom.Y + EndpointTolerance;
                    if (sameLine && overlaps) { return false; }

                    bool joins = upwards ? Joins(endpoint.Geometry, candidate.Geometry) : Joins(candidate.Geometry, endpoint.Geometry);
                    if (!joins)
                    {
                        continue;
                    }

                    // Two pieces occupying the same connection are ambiguous, never a random choice.
                    if (found >= 0)
                    {
                        return false;
                    }

                    found = index;
                }

                if (found < 0)
                {
                    break;
                }

                if (chain.Count >= MaximumSegments)
                {
                    return false;
                }

                LadderSample next = samples[found];
                if (next.Geometry.Bottom.HorizontalDistanceTo(samples[anchor].Geometry.Bottom) > EndpointTolerance)
                {
                    return false; // Small per-joint errors must not accumulate into a different climb line.
                }
                seen.Add(next.Id);
                if (upwards) { chain.Add(next); } else { chain.Insert(0, next); }
            }
        }

        LadderGeometry first = chain[0].Geometry;
        LadderGeometry last = chain[chain.Count - 1].Geometry;
        if (!LadderGeometry.TryMeasure(first.Bottom, last.Top, first.StandingSide, first.Width, first.RungPitch, out run))
        {
            return false;
        }

        foreach (LadderSample sample in chain) { orderedIds.Add(sample.Id); }
        return true;
    }

    internal static bool MustRelease(bool enabled, bool owned, bool alive, bool free, bool worldPresent,
        bool segmentsPresent, bool segmentsUnmoved, float drift) =>
        !enabled || !owned || !alive || !free || !worldPresent || !segmentsPresent || !segmentsUnmoved ||
        !Finite(drift) || drift > 1.5f;

    /// <summary>A release the ladder did not cause leaves velocity, fall reference and pose to whatever
    /// moved the body: lost authority, a menu or attach, an unloaded world, death, a kinematic body, or a
    /// displacement the motor did not make.</summary>
    internal static bool IsExternalRelease(bool owned, bool alive, bool free, bool worldPresent, bool kinematic, float drift) =>
        !owned || !alive || !free || !worldPresent || kinematic || !Finite(drift) || drift > 1.5f;
}

internal readonly struct LadderSample
{
    internal LadderSample(int id, LadderGeometry geometry) { Id = id; Geometry = geometry; }
    internal int Id { get; }
    internal LadderGeometry Geometry { get; }
}
