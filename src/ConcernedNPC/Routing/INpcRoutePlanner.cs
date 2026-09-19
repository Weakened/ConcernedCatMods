using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Routing;

/// <summary>Getting an NPC from where it is to where it needs to be, on foot.
///
/// <b>What it guarantees.</b> Four things.
///
/// <i>It never throws for a world reason.</i> Every refusal is a
/// <see cref="RouteVerdict"/>. Route planning runs inside a driver that does not
/// catch, and an exception from it stops the NPC completely, so a planner that
/// cannot read the ground answers a verdict and lets the caller decide.
///
/// <i>It is bounded.</i> Planning spends a budget and stops. A spent budget
/// answers <see cref="RouteVerdict.BudgetExhausted"/>, which means ask again -
/// never that no route exists, and never a reason to stop a job.
///
/// <i>It learns from being wrong.</i> The navigation mesh believes in gaps a
/// body does not fit through. The only evidence is having tried, so a walk that
/// failed is refused again for a while - for the destination that failed, and
/// for the place the NPC was stopped <b>approached the same way</b>. Not the
/// place alone: an NPC that refused everywhere near where it once got stuck
/// cannot walk round the other side of the building, and walking round the other
/// side is the whole point.
///
/// <i>It plans one body.</i> This is an NPC walking. It knows nothing about a
/// trailing load, a hitch, a turning circle or a corridor width, and it must
/// never learn: cart routing lives in the product whose safety audit confines
/// cart attachment, mass writes and world writes to one folder, and moving it
/// here would move a shipped safety property out of the code that is audited for
/// it.</summary>
internal interface INpcRoutePlanner
{
    /// <summary>Plans a walk, or says why not.</summary>
    /// <param name="request">Where from, where to, how close counts.</param>
    /// <param name="now">The caller's own clock, in seconds. Passed in rather
    /// than read, so a test can advance time without waiting for it.</param>
    RoutePlan Plan(in RouteRequest request, float now);

    /// <summary>The next waypoint to walk to, given where the NPC has got to, or
    /// null when the route is finished or no longer followable.</summary>
    RouteGoal? NextGoal(in RoutePlan plan, NpcPoint at);

    /// <summary>Remembers that this walk did not work: where it was going, where
    /// the NPC was stopped, and which way it was heading. Both places and the
    /// heading, because "I cannot get through there, going that way" is the
    /// lesson, and "nowhere near there" is not.</summary>
    void RememberSetback(NpcPoint destination, NpcPoint stoppedAt, NpcPoint headingTowards, float now);
}
