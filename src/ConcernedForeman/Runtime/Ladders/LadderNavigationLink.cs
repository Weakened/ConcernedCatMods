using System;
using System.Globalization;
using TheConcernedCat.Ladders;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>Where a ladder run becomes something a walker can plan through
/// (CF-LAD-005, `docs/mods/concerned-foreman/LADDERS.md` decision L6).
///
/// <b>Why this file is in the product and not in a shared area.</b> It is the
/// only place that knows both the ladder domain (<c>src/Shared/Ladders</c>) and
/// the worker traversal seam (<c>src/Shared/Workers/Traversal</c>), and those two
/// may not know each other: a shared area is adopted by one
/// <c>&lt;Compile Include&gt;</c> line at a time, and Concerned Teamster takes
/// the worker area without the ladder one. A reference between them would
/// silently make that false and break a product that never asked for ladders. So
/// the join happens here, in the product that adopts both.
///
/// <b>The endpoints are the climb's own exits.</b> The bottom of a link is where
/// <see cref="ClimbExit.OffTheBottom"/> puts a climber, and the top is where
/// <see cref="ClimbExit.OverTheTop"/> puts one. They are not computed a second
/// time from the geometry: a link that promised a walker somewhere the climb does
/// not actually deliver is the kind of drift that ends with a body inside a
/// wall.
///
/// <b>A ladder with nothing to step onto is not published.</b> If the runtime's
/// probe finds no standing space at the head, <see cref="ClimbExit.OverTheTop"/>
/// returns nothing and so does this: a ladder that ends under a roof is a fine
/// thing for a player to hang on and a useless thing for a walker to plan
/// through.
///
/// Nothing here touches the game. It takes measurements the runtime made and
/// returns a value; the survey that makes those measurements belongs to the
/// climb workstream.</summary>
internal static class LadderNavigationLink
{
    /// <summary>The traversal limits that match a given climb.
    ///
    /// The two numbers the layers share travel in this direction only — from the
    /// ladder domain, which tunes them, into the worker layer, which merely
    /// compares them against a walk. Nothing flows back.</summary>
    public static TraversalLimits LimitsFor(ClimbLimits climb)
    {
        if (climb == null)
        {
            throw new ArgumentNullException(nameof(climb));
        }

        return new TraversalLimits
        {
            TraversalSpeedMetresPerSecond = climb.EffectiveClimbSpeed,
            MinHeightMetres = LadderGeometry.MinimumClimbableHeight,
        }.Validate();
    }

    /// <summary>The capabilities a Foreman worker runtime starts with.
    ///
    /// Thorstein is granted ladder traversal, and it does him no good at all
    /// while <paramref name="npcClimbingEnabled"/> is false — which is the
    /// default and stays the default until gate G5 is passed. Both gates are
    /// real: the grant says <i>which</i> workers would climb, the setting says
    /// <i>whether</i> any of them does. Gunnar is not granted anything here;
    /// Foreman does not hand out capabilities on another product's behalf.
    /// </summary>
    public static TraversalCapabilities CapabilitiesFor(bool npcClimbingEnabled)
    {
        var capabilities = new TraversalCapabilities(npcClimbingEnabled);
        capabilities.Grant(WorkerKey.Thorstein, TraversalCapability.Ladders);
        return capabilities;
    }

    /// <summary>Turns a climbable run into a link, or refuses.
    ///
    /// <paramref name="topLanding"/> is what the runtime's probe found at the
    /// head of the run. It is passed in rather than assumed because only the
    /// loaded world knows whether there is floor up there, and a link is a
    /// promise that there is.</summary>
    public static bool TryDescribe(
        LadderRun run,
        in TopLanding topLanding,
        ClimbLimits climb,
        TraversalLimits limits,
        out TraversalLink link)
    {
        if (climb == null)
        {
            throw new ArgumentNullException(nameof(climb));
        }

        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        link = default;
        if (run == null || !run.AsOne.IsClimbable)
        {
            return false;
        }

        ClimbExitPlan? top = ClimbExit.OverTheTop(run.AsOne, topLanding, climb);
        if (!top.HasValue || !top.Value.PlaceCharacter)
        {
            return false;
        }

        ClimbExitPlan bottom = ClimbExit.OffTheBottom(run.AsOne, climb);

        return TraversalLink.TryCreate(
            IdFor(run),
            TraversalLinkKind.Ladder,
            ToWorkPoint(bottom.Landing),
            ToWorkPoint(top.Value.Landing),
            TraversalCapability.Ladders,
            limits,
            out link);
    }

    /// <summary>Describe the run and publish it in one step. Returns the
    /// generation the network gave it, or zero when the run could not be
    /// described.</summary>
    public static int Publish(
        TraversalLinkNetwork network,
        LadderRun run,
        in TopLanding topLanding,
        ClimbLimits climb,
        TraversalLimits limits)
    {
        if (network == null)
        {
            throw new ArgumentNullException(nameof(network));
        }

        return TryDescribe(run, topLanding, climb, limits, out TraversalLink link) ? network.Publish(link) : 0;
    }

    /// <summary>The run is gone: destroyed, unloaded, or no longer one run
    /// because a piece in the middle of it broke.</summary>
    public static bool Retire(TraversalLinkNetwork network, LadderRun run)
    {
        if (network == null)
        {
            throw new ArgumentNullException(nameof(network));
        }

        return run != null && network.Retire(IdFor(run));
    }

    /// <summary>A run's id: where its foot is and which way it faces, to the
    /// centimetre.
    ///
    /// <b>Deliberately not a ZDO id.</b> The game renumbers those every time a
    /// world loads, which is the same reason a worker's identity is never one.
    /// A position is stable for as long as the ladder stands, and a ladder
    /// rebuilt in exactly the same place is, for a walker's purposes, the same
    /// way up — the network's generation counter is what tells the two apart for
    /// anyone already climbing.</summary>
    public static string IdFor(LadderRun run)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        LadderGeometry whole = run.AsOne;
        return "ladder:" + Centimetres(whole.Bottom.X) + "," + Centimetres(whole.Bottom.Y) + "," +
            Centimetres(whole.Bottom.Z) + ":" + Centimetres(whole.StandingSide.X) + "," +
            Centimetres(whole.StandingSide.Z);
    }

    /// <summary>How a finished traversal reads in the ladder domain's own words,
    /// so the climb controller and the worker runtime end a climb the same way
    /// rather than each inventing a vocabulary.</summary>
    public static ClimbExitKind ToClimbExit(TraversalExitKind kind, TraversalDirection direction)
    {
        switch (kind)
        {
            case TraversalExitKind.Arrived:
                return direction == TraversalDirection.Down ? ClimbExitKind.Bottom : ClimbExitKind.Top;
            case TraversalExitKind.TurnedBack:
                return direction == TraversalDirection.Down ? ClimbExitKind.Top : ClimbExitKind.Bottom;
            case TraversalExitKind.LetGo:
                return ClimbExitKind.JumpedOff;
            default:
                // Interrupted, and the unspecified value nobody means to send:
                // the character falls from where it is. Never a placement.
                return ClimbExitKind.LetGo;
        }
    }

    private static WorkPoint ToWorkPoint(ClimbPoint point) => new WorkPoint(point.X, point.Y, point.Z);

    private static string Centimetres(float value) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? "x"
            : Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
