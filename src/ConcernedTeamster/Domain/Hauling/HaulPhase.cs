using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling;

/// <summary>Gunnar's cart lifecycle (CART-06). Zero is unspecified. Names are
/// part of the <c>concernedcat.haul/1</c> wire contract: renaming one is a
/// contract change.</summary>
internal enum HaulPhase
{
    Unspecified = 0,

    /// <summary>No cart leased.</summary>
    Unassigned = 1,

    /// <summary>A cart is leased; Gunnar is not hitched and has no leg.</summary>
    Ready = 2,

    /// <summary>Walking to the cart's handle.</summary>
    Approaching = 3,

    /// <summary>At the handle; attaching through the cart's own attach and
    /// verifying the joint.</summary>
    Hitching = 4,

    /// <summary>Hitched and walking the leg.</summary>
    Pulling = 5,

    /// <summary>Slowing to a stop at a safe point.</summary>
    Stopping = 6,

    /// <summary>Hitched, stopped, cart still: material may be transferred.
    /// </summary>
    Waiting = 7,

    /// <summary>Hitched and stopped while the consumer transfers material; the
    /// cart must not move.</summary>
    Unloading = 8,

    /// <summary>Releasing the joint on suitable ground.</summary>
    Detaching = 9,

    /// <summary>Holding: nothing progresses until resumed or cancelled.</summary>
    Paused = 10,

    /// <summary>Stopped with one actionable reason; waits for the player.
    /// </summary>
    NeedsAttention = 11,

    /// <summary>A bounded local recovery attempt after a stall.</summary>
    Recovering = 12,
}

/// <summary>The legal transitions of <see cref="HaulPhase"/>, and nothing else.
/// Anything not listed is refused. Whether the joint actually exists is a
/// runtime fact the executor checks; this table only guarantees that the order
/// of phases is one the executor was designed for - in particular, a hitched
/// phase never jumps straight to Ready or Unassigned without Detaching.
/// </summary>
internal static class HaulPhases
{
    private static readonly Dictionary<HaulPhase, HaulPhase[]> Legal = new Dictionary<HaulPhase, HaulPhase[]>
    {
        [HaulPhase.Unassigned] = new[] { HaulPhase.Ready },
        [HaulPhase.Ready] = new[] { HaulPhase.Approaching, HaulPhase.Unassigned, HaulPhase.Paused, HaulPhase.NeedsAttention },
        [HaulPhase.Approaching] = new[] { HaulPhase.Hitching, HaulPhase.Ready, HaulPhase.Paused, HaulPhase.NeedsAttention },
        [HaulPhase.Hitching] = new[] { HaulPhase.Pulling, HaulPhase.Approaching, HaulPhase.Detaching, HaulPhase.NeedsAttention },
        [HaulPhase.Pulling] = new[] { HaulPhase.Stopping, HaulPhase.Recovering, HaulPhase.Detaching, HaulPhase.NeedsAttention },
        [HaulPhase.Stopping] = new[] { HaulPhase.Waiting, HaulPhase.Detaching, HaulPhase.NeedsAttention },
        [HaulPhase.Waiting] = new[] { HaulPhase.Pulling, HaulPhase.Unloading, HaulPhase.Detaching, HaulPhase.Paused, HaulPhase.NeedsAttention },
        [HaulPhase.Unloading] = new[] { HaulPhase.Waiting, HaulPhase.Paused, HaulPhase.NeedsAttention },
        [HaulPhase.Detaching] = new[] { HaulPhase.Ready, HaulPhase.NeedsAttention },
        [HaulPhase.Recovering] = new[] { HaulPhase.Pulling, HaulPhase.Stopping, HaulPhase.Detaching, HaulPhase.NeedsAttention },
        [HaulPhase.Paused] = new[] { HaulPhase.Ready, HaulPhase.Approaching, HaulPhase.Waiting, HaulPhase.Detaching, HaulPhase.NeedsAttention },
        [HaulPhase.NeedsAttention] = new[] { HaulPhase.Ready, HaulPhase.Detaching, HaulPhase.Unassigned },
    };

    public static bool CanTransition(HaulPhase from, HaulPhase to)
    {
        return from != to &&
            Legal.TryGetValue(from, out HaulPhase[]? targets) &&
            Array.IndexOf(targets, to) >= 0;
    }

    /// <summary>Phases in which the executor may hold a joint to the cart.
    /// Leaving any of them for Ready or Unassigned goes through Detaching.
    /// </summary>
    public static bool MayHoldJoint(HaulPhase phase)
    {
        switch (phase)
        {
            case HaulPhase.Hitching:
            case HaulPhase.Pulling:
            case HaulPhase.Stopping:
            case HaulPhase.Waiting:
            case HaulPhase.Unloading:
            case HaulPhase.Recovering:
            case HaulPhase.Detaching:
            case HaulPhase.Paused:
            case HaulPhase.NeedsAttention:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Phases in which the cart must not be moved by anyone's request:
    /// a transfer may be in progress.</summary>
    public static bool ForbidsMotion(HaulPhase phase) => phase == HaulPhase.Unloading;

    /// <summary>Every phase that is a real state, for exhaustive tests.</summary>
    public static IEnumerable<HaulPhase> All()
    {
        foreach (HaulPhase phase in Enum.GetValues(typeof(HaulPhase)))
        {
            if (phase != HaulPhase.Unspecified)
            {
                yield return phase;
            }
        }
    }
}
