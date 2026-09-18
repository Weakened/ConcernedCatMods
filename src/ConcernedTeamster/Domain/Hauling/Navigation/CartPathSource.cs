using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>What one navmesh query answered.</summary>
internal enum CartPathStatus
{
    Unspecified = 0,

    /// <summary>A full path; the corners are filled in.</summary>
    Found = 1,

    /// <summary>No full path between the two points on the navmesh as it is
    /// built now. The game builds navmesh tiles when they are first asked for,
    /// so asking again a few seconds later can succeed.</summary>
    NotFound = 2,

    /// <summary>The navmesh could not be asked at all: no world, the member
    /// is missing on this game build, or the query failed.</summary>
    Unavailable = 3,
}

/// <summary>The port over the game's navmesh (agent B's adapter implements
/// it). A walker's path is only where a route starts: everything a loaded cart
/// needs beyond it is checked by <see cref="CartRouteEvaluator"/>.</summary>
internal interface ICartPathSource
{
    /// <summary>Clears <paramref name="corners"/> and fills it with a full
    /// path's corners, both ends included. The ends are where the navmesh put
    /// them, which may be a little way from the points asked for. Never throws.
    /// </summary>
    CartPathStatus FindPath(WorkPoint from, WorkPoint to, List<WorkPoint> corners);
}
