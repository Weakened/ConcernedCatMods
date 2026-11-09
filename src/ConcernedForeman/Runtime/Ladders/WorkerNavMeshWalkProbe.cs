using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>#334: "can he walk from here to there", answered by vanilla's own
/// navigation mesh and verified in height, for the route search.
///
/// <b>Why it must be verified in height.</b> <c>Pathfinding.GetPath</c> snaps
/// each end onto the mesh first, searching up to twelve metres around the
/// start, so a path "from the foot of the ladder to the upper floor" can quietly
/// start and end on the ground floor and still return true. A walk edge is
/// therefore reachable only when the path's own first and last corners are
/// within <see cref="EndpointHorizontalMetres"/> across and
/// <see cref="EndpointVerticalMetres"/> up or down of the exact points asked
/// about - under a storey, so a different floor is never a match. Anything else
/// is blocked, never "close enough".
///
/// <b>Unknown is not permission.</b> The mesh is built on demand around a point
/// and asynchronously, so a fresh area answers no path before it answers the
/// truth. A failed search where either end has no mesh yet is
/// <see cref="WorkerWalkEvidenceKind.Unknown"/> for up to
/// <see cref="SettleSeconds"/> per point, then blocked; the search waits on an
/// unknown and never turns it into a walk. Where both ends are on the mesh, a
/// failed full-path search is blocked at once.
///
/// <b>Bounded.</b> One <c>GetPath</c> per call at most; a question asked again
/// within <see cref="RetrySeconds"/> of an unknown answer is answered unknown
/// without searching again, and the search asks at most one question per worker
/// tick. <c>HavePath</c> is never used (it is a full search wearing an
/// accessor's name).</summary>
internal sealed class WorkerNavMeshWalkProbe : IWorkerWalkProbe
{
    internal const float EndpointHorizontalMetres = 1.0f;
    internal const float EndpointVerticalMetres = 0.6f;
    internal const float SettleSeconds = 6f;
    internal const float RetrySeconds = 0.25f;

    private readonly Func<float> _now;
    private readonly List<Vector3> _path = new List<Vector3>(64);
    private readonly Dictionary<WorkPoint, float> _unbuiltSince = new Dictionary<WorkPoint, float>();
    private WorkPoint _lastFrom;
    private WorkPoint _lastTo;
    private float _lastUnknownAt = float.NegativeInfinity;

    internal WorkerNavMeshWalkProbe(Func<float> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    public WorkerWalkEvidence Check(WorkPoint from, WorkPoint to)
    {
        float now = _now();
        Pathfinding pathfinding = Pathfinding.instance;
        ZoneSystem zones = ZoneSystem.instance;
        if (pathfinding == null || zones == null || !from.IsFinite || !to.IsFinite)
        {
            return Unknown(from, to, now);
        }

        if (from.Equals(_lastFrom) && to.Equals(_lastTo) && now - _lastUnknownAt < RetrySeconds && now >= _lastUnknownAt)
        {
            return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Unknown);
        }

        Vector3 start = V(from);
        Vector3 end = V(to);
        if (!zones.IsZoneLoaded(start) || !zones.IsZoneLoaded(end))
        {
            return Unknown(from, to, now);
        }

        bool found = pathfinding.GetPath(
            start, end, _path, Pathfinding.AgentType.Humanoid, requireFullPath: true, cleanup: false);
        try
        {
            if (found && _path.Count > 0)
            {
                if (!Near(_path[0], from) || !Near(_path[_path.Count - 1], to))
                {
                    // The mesh found a walk between two OTHER points - another
                    // floor, the far side of a wall. Not this walk.
                    return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Blocked);
                }

                float metres = Distance(start, _path[0]) + Distance(_path[_path.Count - 1], end);
                for (int i = 1; i < _path.Count; i++)
                {
                    metres += Distance(_path[i - 1], _path[i]);
                }

                _unbuiltSince.Remove(from);
                _unbuiltSince.Remove(to);
                return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Reachable, metres);
            }
        }
        finally
        {
            _path.Clear();
        }

        bool fromBuilt = OnMesh(pathfinding, from);
        bool toBuilt = OnMesh(pathfinding, to);
        if (fromBuilt && toBuilt)
        {
            return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Blocked);
        }

        if ((fromBuilt || Settled(from, now)) && (toBuilt || Settled(to, now)))
        {
            return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Blocked);
        }

        return Unknown(from, to, now);
    }

    private WorkerWalkEvidence Unknown(WorkPoint from, WorkPoint to, float now)
    {
        _lastFrom = from;
        _lastTo = to;
        _lastUnknownAt = now;
        return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Unknown);
    }

    /// <summary>True once this point has been waited on for longer than the
    /// mesh takes to build. The first call starts the clock.</summary>
    private bool Settled(WorkPoint point, float now)
    {
        if (!_unbuiltSince.TryGetValue(point, out float since))
        {
            if (_unbuiltSince.Count >= 256)
            {
                _unbuiltSince.Clear();
            }

            _unbuiltSince[point] = now;
            return false;
        }

        return now - since >= SettleSeconds || now < since;
    }

    private static bool OnMesh(Pathfinding pathfinding, WorkPoint point) =>
        pathfinding.FindValidPoint(out Vector3 snapped, V(point), 1f, Pathfinding.AgentType.Humanoid) &&
        Near(snapped, point);

    private static bool Near(Vector3 corner, WorkPoint point)
    {
        float dx = corner.x - point.X;
        float dz = corner.z - point.Z;
        return (dx * dx) + (dz * dz) <= EndpointHorizontalMetres * EndpointHorizontalMetres &&
            Math.Abs(corner.y - point.Y) <= EndpointVerticalMetres;
    }

    private static float Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

    private static Vector3 V(WorkPoint point) => new Vector3(point.X, point.Y, point.Z);
}
