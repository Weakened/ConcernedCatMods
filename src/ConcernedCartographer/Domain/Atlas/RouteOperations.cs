using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Route editing with bounded undo/redo: freehand append, partial
/// erase (splits like road reconciliation), waypoint insert/move/remove,
/// split/merge, lock/archive/delete/restore. Undo restores old values
/// under NEW revisions (the same convergence contract as pins), and every
/// geometry edit respects the lock flag.</summary>
internal sealed class RouteOperations
{
    private const int UndoDepth = 20;
    public const float FreehandMinimumSpacingMeters = 2f;

    private readonly RouteStore _store;
    private readonly List<UndoRecord> _undo = new();
    private readonly List<UndoRecord> _redo = new();

    public RouteOperations(RouteStore store)
    {
        _store = store;
    }

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public AtlasRoute StartRoute(RouteKind kind, string name)
    {
        AtlasRoute route = _store.Create(created =>
        {
            created.Kind = kind;
            created.Name = name;
        });
        Push(new UndoRecord($"create {kind}", new List<AtlasRoute>(), new List<AtlasId> { route.Id }));
        return route;
    }

    /// <summary>Appends a point while drawing. Not undo-recorded per point —
    /// the route creation is the undo unit while drawing; partial erase
    /// handles fine corrections afterwards.</summary>
    public bool AppendPoint(AtlasId id, RoadPoint point)
    {
        if (!TryGetEditable(id, out AtlasRoute route))
        {
            return false;
        }

        if (route.Points.Count > 0 &&
            route.Points[route.Points.Count - 1].HorizontalDistanceTo(point) < FreehandMinimumSpacingMeters)
        {
            return false;
        }

        return _store.Mutate(id, edited => edited.Points.Add(point));
    }

    /// <summary>Removes route coverage within the radius. The route splits
    /// into surviving runs: the first keeps the identity, later runs become
    /// new routes with copied metadata. Undoable as one step.</summary>
    public int EraseNear(AtlasId id, RoadPoint center, float radiusMeters, out List<AtlasRoute> created)
    {
        created = new List<AtlasRoute>();
        if (radiusMeters <= 0f || !TryGetEditable(id, out AtlasRoute route))
        {
            return 0;
        }

        var runs = new List<List<RoadPoint>>();
        List<RoadPoint>? current = null;
        int removed = 0;
        foreach (RoadPoint point in route.Points)
        {
            if (point.HorizontalDistanceTo(center) <= radiusMeters)
            {
                removed++;
                current = null;
                continue;
            }

            if (current is null)
            {
                current = new List<RoadPoint>();
                runs.Add(current);
            }

            current.Add(point);
        }

        if (removed == 0)
        {
            return 0;
        }

        Push(SnapshotRecord("erase", new[] { id }));

        _store.Mutate(id, edited =>
        {
            edited.Points.Clear();
            if (runs.Count > 0)
            {
                edited.Points.AddRange(runs[0]);
            }
        });

        if (runs.Count == 0)
        {
            // RC12 blocker 2: erasing the LAST of a route's ink tombstones
            // the route instead of leaving a ghost zero-point entry in the
            // list. The snapshot above restores both points and liveness on
            // undo.
            _store.Delete(id);
        }

        for (int index = 1; index < runs.Count; index++)
        {
            List<RoadPoint> run = runs[index];
            AtlasRoute tail = _store.Create(createdRoute =>
            {
                CopyMetadata(route, createdRoute);
                createdRoute.Points.AddRange(run);
            });
            created.Add(tail);
            _undo[_undo.Count - 1].Created.Add(tail.Id);
        }

        return removed;
    }

    public bool MoveWaypoint(AtlasId id, int index, RoadPoint position)
    {
        if (!TryGetEditable(id, out AtlasRoute route) || index < 0 || index >= route.Points.Count)
        {
            return false;
        }

        Push(SnapshotRecord("move waypoint", new[] { id }));
        return _store.Mutate(id, edited => edited.Points[index] = position);
    }

    public bool InsertWaypoint(AtlasId id, int afterIndex, RoadPoint position)
    {
        if (!TryGetEditable(id, out AtlasRoute route) || afterIndex < -1 || afterIndex >= route.Points.Count)
        {
            return false;
        }

        Push(SnapshotRecord("insert waypoint", new[] { id }));
        return _store.Mutate(id, edited => edited.Points.Insert(afterIndex + 1, position));
    }

    public bool RemoveWaypoint(AtlasId id, int index)
    {
        if (!TryGetEditable(id, out AtlasRoute route) || index < 0 || index >= route.Points.Count)
        {
            return false;
        }

        Push(SnapshotRecord("remove waypoint", new[] { id }));
        return _store.Mutate(id, edited => edited.Points.RemoveAt(index));
    }

    /// <summary>Splits at a point index; both halves share the split point.
    /// The head keeps the identity.</summary>
    public AtlasRoute? Split(AtlasId id, int index)
    {
        if (!TryGetEditable(id, out AtlasRoute route) || index <= 0 || index >= route.Points.Count - 1)
        {
            return null;
        }

        Push(SnapshotRecord("split", new[] { id }));

        var tailPoints = route.Points.GetRange(index, route.Points.Count - index);
        _store.Mutate(id, edited => edited.Points.RemoveRange(index + 1, edited.Points.Count - index - 1));
        AtlasRoute tail = _store.Create(created =>
        {
            CopyMetadata(route, created);
            created.Points.AddRange(tailPoints);
        });
        _undo[_undo.Count - 1].Created.Add(tail.Id);
        return tail;
    }

    /// <summary>Appends the second route's points onto the first (reversing
    /// as needed to join the nearest ends) and tombstones the second.</summary>
    public bool Merge(AtlasId firstId, AtlasId secondId)
    {
        if (firstId.Equals(secondId) ||
            !TryGetEditable(firstId, out AtlasRoute first) ||
            !TryGetEditable(secondId, out AtlasRoute second) ||
            first.Points.Count == 0 || second.Points.Count == 0)
        {
            return false;
        }

        Push(SnapshotRecord("merge", new[] { firstId, secondId }));

        RoadPoint firstEnd = first.Points[first.Points.Count - 1];
        bool reverse = second.Points[second.Points.Count - 1].HorizontalDistanceTo(firstEnd) <
            second.Points[0].HorizontalDistanceTo(firstEnd);
        var appended = new List<RoadPoint>(second.Points);
        if (reverse)
        {
            appended.Reverse();
        }

        _store.Mutate(firstId, edited => edited.Points.AddRange(appended));
        _store.Delete(secondId);
        return true;
    }

    public bool SetLocked(AtlasId id, bool locked)
    {
        if (!_store.TryGet(id, out AtlasRoute route) || route.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord(locked ? "lock" : "unlock", new[] { id }));
        return _store.Mutate(id, edited => edited.Locked = locked);
    }

    public bool SetArchived(AtlasId id, bool archived)
    {
        if (!_store.TryGet(id, out AtlasRoute route) || route.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord(archived ? "archive" : "unarchive", new[] { id }));
        return _store.Mutate(id, edited => edited.Archived = archived);
    }

    public bool EditMetadata(AtlasId id, Action<AtlasRoute> edit, string description = "edit")
    {
        if (!_store.TryGet(id, out AtlasRoute route) || route.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord(description, new[] { id }));
        return _store.Mutate(id, edit);
    }

    public bool Delete(AtlasId id)
    {
        if (!_store.TryGet(id, out AtlasRoute route) || route.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord("delete", new[] { id }));
        return _store.Delete(id);
    }

    public bool RestoreDeleted(AtlasId id)
    {
        if (!_store.TryGet(id, out AtlasRoute route) || !route.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord("restore", new[] { id }));
        return _store.Restore(id);
    }

    public bool Undo(out string summary)
    {
        return Rollback(_undo, _redo, "undo", out summary);
    }

    public bool Redo(out string summary)
    {
        return Rollback(_redo, _undo, "redo", out summary);
    }

    private bool Rollback(List<UndoRecord> from, List<UndoRecord> to, string verb, out string summary)
    {
        if (from.Count == 0)
        {
            summary = $"Nothing to {verb}.";
            return false;
        }

        UndoRecord record = from[from.Count - 1];
        from.RemoveAt(from.Count - 1);

        var inverseAffected = new List<AtlasId>();
        foreach (AtlasRoute before in record.Before)
        {
            inverseAffected.Add(before.Id);
        }

        inverseAffected.AddRange(record.Created);
        to.Add(SnapshotRecord(record.Description, inverseAffected));
        Trim(to);

        foreach (AtlasId created in record.Created)
        {
            if (_store.TryGet(created, out AtlasRoute route) && !route.Deleted)
            {
                _store.Delete(created);
            }
        }

        foreach (AtlasRoute before in record.Before)
        {
            _store.Mutate(before.Id, route =>
            {
                long revision = route.Revision;
                DateTime createdUtc = route.CreatedUtc;
                route.CopyFrom(before);
                route.Revision = revision;
                route.CreatedUtc = createdUtc;
            });
        }

        summary = $"{char.ToUpperInvariant(verb[0])}{verb.Substring(1)}: '{record.Description}' reverted.";
        return true;
    }

    private bool TryGetEditable(AtlasId id, out AtlasRoute route)
    {
        return _store.TryGet(id, out route) && !route.Deleted && !route.Locked;
    }

    private static void CopyMetadata(AtlasRoute source, AtlasRoute target)
    {
        target.Name = source.Name.Length == 0 ? "" : source.Name + " (part)";
        target.Kind = source.Kind;
        target.Style = source.Style;
        target.Status = source.Status;
        target.ColorArgb = source.ColorArgb;
        target.Scope = source.Scope;
    }

    private UndoRecord SnapshotRecord(string description, IReadOnlyList<AtlasId> ids)
    {
        var before = new List<AtlasRoute>();
        foreach (AtlasId id in ids)
        {
            if (_store.TryGet(id, out AtlasRoute route))
            {
                before.Add(route.Clone());
            }
        }

        return new UndoRecord(description, before, new List<AtlasId>());
    }

    private void Push(UndoRecord record)
    {
        _undo.Add(record);
        _redo.Clear();
        Trim(_undo);
    }

    private static void Trim(List<UndoRecord> stack)
    {
        while (stack.Count > UndoDepth)
        {
            stack.RemoveAt(0);
        }
    }

    private sealed class UndoRecord
    {
        public UndoRecord(string description, List<AtlasRoute> before, List<AtlasId> created)
        {
            Description = description;
            Before = before;
            Created = created;
        }

        public string Description { get; }
        public List<AtlasRoute> Before { get; }
        public List<AtlasId> Created { get; }
    }
}
