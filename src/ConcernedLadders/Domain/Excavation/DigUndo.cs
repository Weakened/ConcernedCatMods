using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>One completed dig, kept so it can be undone (#551): the rectangle and every vertex it lowered, with the
/// height each had before and the floor it was set to.</summary>
internal sealed class DigRecord
{
    internal DigRecord(DigFootprint footprint, IReadOnlyList<DigVertex> vertices)
    {
        if (vertices == null || vertices.Count == 0) throw new ArgumentException("A dig record lists what it lowered.", nameof(vertices));
        Footprint = footprint;
        Vertices = vertices;
    }

    internal DigFootprint Footprint { get; }

    internal IReadOnlyList<DigVertex> Vertices { get; }
}

/// <summary>The digs this player made in this world, newest last, for undo (#551). Owner decision 2026-11-10: undo
/// restores the last dig's ground exactly, newest first, with no refund. Local and in memory only: it is never saved,
/// so a world change or a restart forgets it (cleared by the runtime), and it holds at most <see cref="Capacity"/>
/// digs (lead default), dropping the oldest.</summary>
internal sealed class DigHistory
{
    internal const int Capacity = 10;

    private readonly List<DigRecord> _records = new List<DigRecord>(Capacity);

    internal int Count => _records.Count;

    internal DigRecord? Last => _records.Count == 0 ? null : _records[_records.Count - 1];

    internal void Push(DigRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (_records.Count == Capacity) _records.RemoveAt(0);
        _records.Add(record);
    }

    /// <summary>Removes <paramref name="record"/> once its undo has been read back complete. Only the newest may go:
    /// undo is newest first.</summary>
    internal bool Remove(DigRecord record)
    {
        if (_records.Count == 0 || !ReferenceEquals(_records[_records.Count - 1], record)) return false;
        _records.RemoveAt(_records.Count - 1);
        return true;
    }

    internal void Clear() => _records.Clear();
}

/// <summary>Why an undo cannot run (#551). Every one fails closed: nothing is changed.</summary>
internal enum UndoRefusal
{
    None = 0,

    /// <summary>No dig to undo.</summary>
    Nothing = 1,

    /// <summary>A recorded vertex is not loaded.</summary>
    Unloaded = 2,

    /// <summary>A recorded vertex is no longer at the floor the dig left: anyone's edit since, which an undo would
    /// overwrite.</summary>
    Changed = 3,

    /// <summary>A building piece stands in the hole (the adapter's world check).</summary>
    PieceInside = 4,

    /// <summary>A dig or an undo is still being applied.</summary>
    Busy = 5,
}

/// <summary>Game-free undo checks (#551). An undo raises each recorded vertex back to the height it had before, with
/// single-vertex raise operations whose delta is <see cref="RaiseStep"/>: the engine raises at most that far per
/// operation, so a vertex lowered further takes more than one.</summary>
internal static class DigUndo
{
    /// <summary>The single-vertex raise operation's fixed delta (the engine's own per-edit clamp).</summary>
    internal const float RaiseStep = 8f;

    /// <summary>Whether <paramref name="record"/> can be undone over the terrain <paramref name="read"/> reports now:
    /// every vertex loaded, finite and still at the floor the dig left it at.</summary>
    internal static UndoRefusal Check(DigRecord? record, Func<int, int, float?> read)
    {
        if (record == null) return UndoRefusal.Nothing;
        if (read == null) throw new ArgumentNullException(nameof(read));
        foreach (DigVertex vertex in record.Vertices)
        {
            float? height = read(vertex.X, vertex.Z);
            if (!height.HasValue || !LadderRules.Finite(height.Value)) return UndoRefusal.Unloaded;
            if (Math.Abs(height.Value - vertex.After) > ExcavationPlanner.LevelTolerance) return UndoRefusal.Changed;
        }

        return UndoRefusal.None;
    }

    /// <summary>Raise operations a vertex needs to get back from its floor to its height before.</summary>
    internal static int RaiseOperations(DigVertex vertex)
    {
        float rise = vertex.Before - vertex.After;
        return rise <= 0f ? 0 : (int)Math.Ceiling(rise / RaiseStep - 1e-4);
    }

    /// <summary>Whether an undo has finished, read back: every vertex at its height before (within tolerance).</summary>
    internal static ExcavationProgress Progress(DigRecord record, Func<int, int, float?> read)
    {
        bool pending = false;
        foreach (DigVertex vertex in record.Vertices)
        {
            float? height = read(vertex.X, vertex.Z);
            if (!height.HasValue || !LadderRules.Finite(height.Value)) return ExcavationProgress.Unreadable;
            if (height.Value > vertex.Before + ExcavationPlanner.LevelTolerance) return ExcavationProgress.Overshot;
            if (height.Value < vertex.Before - ExcavationPlanner.LevelTolerance) pending = true;
        }

        return pending ? ExcavationProgress.Pending : ExcavationProgress.Complete;
    }
}
