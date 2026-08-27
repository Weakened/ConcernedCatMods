using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Pin operations with bounded undo/redo, batch tools, and
/// duplicate merge. Pure and store-backed.
///
/// Undo/redo never rolls a revision backward: restoring a snapshot copies
/// the old field values under a NEW revision, and undoing a creation
/// tombstones it. Journal replay and revision-based sync therefore always
/// converge on the visible state — an undone edit can never resurrect.</summary>
internal sealed class PinOperations
{
    private const int UndoDepth = 20;
    private const float DuplicateOffsetMeters = 4f;

    private readonly PinStore _store;
    private readonly List<UndoRecord> _undo = new();
    private readonly List<UndoRecord> _redo = new();

    public PinOperations(PinStore store)
    {
        _store = store;
    }

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public bool Move(AtlasId id, Roads.RoadPoint newPosition)
    {
        return Apply("move", new[] { id }, pin => pin.Position = newPosition);
    }

    public AtlasPin? Duplicate(AtlasId id)
    {
        if (!_store.TryGet(id, out AtlasPin source) || source.Deleted)
        {
            return null;
        }

        AtlasPin copy = _store.Create(pin =>
        {
            DateTime now = pin.CreatedUtc;
            pin.CopyFrom(source);
            // CopyFrom clones state wholesale; re-establish creation facts.
            pin.Revision = 1;
            pin.CreatedUtc = now;
            pin.ModifiedUtc = now;
            pin.Source = AtlasPinSource.Managed;
            pin.Deleted = false;
            pin.DeletedUtc = null;
            pin.Name = source.Name.Length == 0 ? "Copy" : source.Name + " (copy)";
            pin.Position = new Roads.RoadPoint(
                source.Position.X + DuplicateOffsetMeters,
                source.Position.Y,
                source.Position.Z);
        });

        Push(new UndoRecord("duplicate", new List<AtlasPin>(), new List<AtlasId> { copy.Id }));
        return copy;
    }

    public bool SetArchived(AtlasId id, bool archived)
    {
        return Apply(archived ? "archive" : "unarchive", new[] { id }, pin => pin.Archived = archived);
    }

    public bool Delete(AtlasId id)
    {
        if (!_store.TryGet(id, out AtlasPin pin) || pin.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord("delete", new[] { id }));
        return _store.Delete(id);
    }

    public bool RestoreDeleted(AtlasId id)
    {
        if (!_store.TryGet(id, out AtlasPin pin) || !pin.Deleted)
        {
            return false;
        }

        Push(SnapshotRecord("restore", new[] { id }));
        return _store.Restore(id);
    }

    /// <summary>One undoable edit applied to every selected pin.</summary>
    public int BatchEdit(IReadOnlyList<AtlasId> ids, Action<AtlasPin> edit, string description = "batch edit")
    {
        var editable = new List<AtlasId>();
        foreach (AtlasId id in ids)
        {
            if (_store.TryGet(id, out AtlasPin pin) && !pin.Deleted)
            {
                editable.Add(id);
            }
        }

        if (editable.Count == 0)
        {
            return 0;
        }

        Push(SnapshotRecord(description, editable));
        foreach (AtlasId id in editable)
        {
            _store.Mutate(id, edit);
        }

        return editable.Count;
    }

    /// <summary>Likely duplicates: living pins within the radius that share
    /// an icon or a normalized name. Each group's first entry (the oldest)
    /// is the suggested merge primary. Spatially bucketed so the scan stays
    /// near-linear on 10,000-pin atlases.</summary>
    public List<List<AtlasPin>> FindDuplicateGroups(float radiusMeters)
    {
        var pins = new List<AtlasPin>(_store.Living);
        pins.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));

        float cell = Math.Max(1f, radiusMeters);
        var buckets = new Dictionary<long, List<int>>();
        for (int index = 0; index < pins.Count; index++)
        {
            long key = BucketKey(pins[index], cell);
            if (!buckets.TryGetValue(key, out List<int>? bucket))
            {
                bucket = new List<int>();
                buckets.Add(key, bucket);
            }

            bucket.Add(index);
        }

        var grouped = new bool[pins.Count];
        var groups = new List<List<AtlasPin>>();

        for (int i = 0; i < pins.Count; i++)
        {
            if (grouped[i])
            {
                continue;
            }

            List<AtlasPin>? group = null;
            int cellX = (int)Math.Floor(pins[i].Position.X / cell);
            int cellZ = (int)Math.Floor(pins[i].Position.Z / cell);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!buckets.TryGetValue(Combine(cellX + dx, cellZ + dz), out List<int>? bucket))
                    {
                        continue;
                    }

                    foreach (int j in bucket)
                    {
                        if (j <= i || grouped[j] || !AreLikelyDuplicates(pins[i], pins[j], radiusMeters))
                        {
                            continue;
                        }

                        group ??= new List<AtlasPin> { pins[i] };
                        group.Add(pins[j]);
                        grouped[j] = true;
                    }
                }
            }

            if (group is not null)
            {
                grouped[i] = true;
                groups.Add(group);
            }
        }

        return groups;
    }

    private static long BucketKey(AtlasPin pin, float cell)
    {
        return Combine((int)Math.Floor(pin.Position.X / cell), (int)Math.Floor(pin.Position.Z / cell));
    }

    private static long Combine(int cellX, int cellZ)
    {
        return ((long)(uint)cellX << 32) ^ (uint)cellZ;
    }

    /// <summary>Merges duplicates into the primary: tags union, notes
    /// concatenated with provenance lines, earliest creation time kept,
    /// primary identity untouched, duplicates tombstoned. Undoable.</summary>
    public bool Merge(AtlasId primaryId, IReadOnlyList<AtlasId> duplicateIds)
    {
        if (!_store.TryGet(primaryId, out AtlasPin primary) || primary.Deleted)
        {
            return false;
        }

        var duplicates = new List<AtlasPin>();
        foreach (AtlasId id in duplicateIds)
        {
            if (id.Equals(primaryId) || !_store.TryGet(id, out AtlasPin duplicate) || duplicate.Deleted)
            {
                continue;
            }

            duplicates.Add(duplicate);
        }

        if (duplicates.Count == 0)
        {
            return false;
        }

        var affected = new List<AtlasId> { primaryId };
        foreach (AtlasPin duplicate in duplicates)
        {
            affected.Add(duplicate.Id);
        }

        Push(SnapshotRecord("merge", affected));

        _store.Mutate(primaryId, pin =>
        {
            foreach (AtlasPin duplicate in duplicates)
            {
                foreach (string tag in duplicate.Tags)
                {
                    if (!pin.Tags.Contains(tag))
                    {
                        pin.Tags.Add(tag);
                    }
                }

                if (duplicate.Notes.Length > 0)
                {
                    pin.Notes = pin.Notes.Length == 0
                        ? duplicate.Notes
                        : pin.Notes + "\n" + duplicate.Notes;
                }

                pin.Notes = pin.Notes.Length == 0
                    ? $"[merged {duplicate.Id} \"{duplicate.Name}\"]"
                    : pin.Notes + $"\n[merged {duplicate.Id} \"{duplicate.Name}\"]";

                if (duplicate.CreatedUtc < pin.CreatedUtc)
                {
                    pin.CreatedUtc = duplicate.CreatedUtc;
                }
            }
        });

        foreach (AtlasPin duplicate in duplicates)
        {
            _store.Delete(duplicate.Id);
        }

        return true;
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

        // Capture the inverse before touching anything.
        var inverseAffected = new List<AtlasId>();
        foreach (AtlasPin before in record.Before)
        {
            inverseAffected.Add(before.Id);
        }

        foreach (AtlasId created in record.Created)
        {
            inverseAffected.Add(created);
        }

        to.Add(SnapshotRecord(record.Description, inverseAffected));
        Trim(to);

        foreach (AtlasId created in record.Created)
        {
            if (_store.TryGet(created, out AtlasPin pin) && !pin.Deleted)
            {
                _store.Delete(created);
            }
        }

        foreach (AtlasPin before in record.Before)
        {
            RestoreSnapshot(before);
        }

        summary = $"{char.ToUpperInvariant(verb[0])}{verb.Substring(1)}: '{record.Description}' reverted ({record.Before.Count + record.Created.Count} pin(s)).";
        return true;
    }

    /// <summary>Recently deleted pins, newest first.</summary>
    public List<AtlasPin> RecentlyDeleted(int limit = 10)
    {
        var tombstones = new List<AtlasPin>(_store.Tombstones);
        tombstones.Sort((a, b) => (b.DeletedUtc ?? DateTime.MinValue).CompareTo(a.DeletedUtc ?? DateTime.MinValue));
        if (tombstones.Count > limit)
        {
            tombstones.RemoveRange(limit, tombstones.Count - limit);
        }

        return tombstones;
    }

    private bool Apply(string description, IReadOnlyList<AtlasId> ids, Action<AtlasPin> edit)
    {
        var editable = new List<AtlasId>();
        foreach (AtlasId id in ids)
        {
            if (_store.TryGet(id, out AtlasPin pin) && !pin.Deleted)
            {
                editable.Add(id);
            }
        }

        if (editable.Count == 0)
        {
            return false;
        }

        Push(SnapshotRecord(description, editable));
        foreach (AtlasId id in editable)
        {
            _store.Mutate(id, edit);
        }

        return true;
    }

    private void RestoreSnapshot(AtlasPin snapshot)
    {
        // Field-value restore under a NEW revision, so replays converge.
        _store.Mutate(snapshot.Id, pin =>
        {
            long currentRevision = pin.Revision;
            DateTime created = pin.CreatedUtc;
            pin.CopyFrom(snapshot);
            pin.Revision = currentRevision;
            pin.CreatedUtc = created;
        });
    }

    private UndoRecord SnapshotRecord(string description, IReadOnlyList<AtlasId> ids)
    {
        var before = new List<AtlasPin>();
        foreach (AtlasId id in ids)
        {
            if (_store.TryGet(id, out AtlasPin pin))
            {
                before.Add(pin.Clone());
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

    private static bool AreLikelyDuplicates(AtlasPin a, AtlasPin b, float radiusMeters)
    {
        if (a.Position.HorizontalDistanceTo(b.Position) > radiusMeters)
        {
            return false;
        }

        if (string.Equals(a.IconId, b.IconId, StringComparison.Ordinal))
        {
            return true;
        }

        string nameA = a.Name.Trim().ToLowerInvariant();
        string nameB = b.Name.Trim().ToLowerInvariant();
        return nameA.Length > 0 && nameA == nameB;
    }

    private sealed class UndoRecord
    {
        public UndoRecord(string description, List<AtlasPin> before, List<AtlasId> created)
        {
            Description = description;
            Before = before;
            Created = created;
        }

        public string Description { get; }
        public List<AtlasPin> Before { get; }
        public List<AtlasId> Created { get; }
    }
}
