using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Navigation;

/// <summary>The game's navmesh as a <see cref="ICartPathSource"/> (#314): a
/// local, read-only <c>Pathfinding.GetPath</c> query, the same one the game's
/// creatures use. No BaseAI, nothing networked, nothing written.
///
/// The agent is <c>HorseSize</c> - radius 0.8 m, height 2.5 m, step 0.3 m,
/// verified in <c>Pathfinding.SetupAgents</c> on Valheim 1.0.12 builds 25253764
/// and 25364265. No agent models a cart, so the choice is the closest body:
/// its radius is within 6 cm of the vanilla cart's measured half width (0.86 m),
/// its 2.5 m height clears a cart with Gunnar but not a low roof, and its step
/// is a walker's, not a troll's 0.6 m. The wider agents (TrollSize 1.0 m,
/// Abomination 1.5 m) are 5-7 m tall and step 0.6 m, which would shut the cart
/// out of every roofed yard for a margin the route checks already enforce. The
/// navmesh path is only where a route starts: <see cref="CartRouteEvaluator"/>
/// checks the cart's own width, clearance, turns, grade, footing and doors.
///
/// <c>requireFullPath</c> refuses partial paths. The game builds navmesh tiles
/// for an agent size only when asked, so the first query in a new place can come
/// back empty; the planner's query budget paces asking again.</summary>
internal sealed class NavmeshCartPathSource : ICartPathSource
{
    /// <summary>The chosen agent's radius, for the record.</summary>
    public const float AgentRadiusMetres = 0.8f;

    private readonly List<Vector3> _buffer = new List<Vector3>(64);

    public CartPathStatus FindPath(WorkPoint from, WorkPoint to, List<WorkPoint> corners)
    {
        corners.Clear();
        if (!NavigationCapability.Enabled || !from.IsFinite || !to.IsFinite)
        {
            return CartPathStatus.Unavailable;
        }

        try
        {
            return FindPathCore(from, to, corners);
        }
        catch (Exception)
        {
            corners.Clear();
            return CartPathStatus.Unavailable;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CartPathStatus FindPathCore(WorkPoint from, WorkPoint to, List<WorkPoint> corners)
    {
        Pathfinding pathfinding = Pathfinding.instance;
        if (pathfinding == null)
        {
            return CartPathStatus.Unavailable;
        }

        _buffer.Clear();
        bool found = pathfinding.GetPath(
            new Vector3(from.X, from.Y, from.Z),
            new Vector3(to.X, to.Y, to.Z),
            _buffer,
            Pathfinding.AgentType.HorseSize,
            requireFullPath: true);
        if (!found || _buffer.Count < 2)
        {
            return CartPathStatus.NotFound;
        }

        foreach (Vector3 corner in _buffer)
        {
            corners.Add(new WorkPoint(corner.x, corner.y, corner.z));
        }

        return CartPathStatus.Found;
    }
}
