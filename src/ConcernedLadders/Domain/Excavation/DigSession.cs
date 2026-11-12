using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What one press did to the dig session (#551, #574).</summary>
internal enum DigAction
{
    /// <summary>Nothing: no ground aimed at.</summary>
    None = 0,

    /// <summary>The player asked to dig the hole shown.</summary>
    Dig = 3,

    /// <summary>Secondary: the next Secondary within <see cref="DigSession.UndoConfirmSeconds"/> undoes the last dig.</summary>
    UndoArmed = 4,

    /// <summary>The second Secondary: undo the last dig.</summary>
    Undo = 5,
}

/// <summary>One ready-made hole size (#574): a square <see cref="Side"/> metres across and <see cref="Depth"/> metres
/// deep.</summary>
internal readonly struct DigSize : IEquatable<DigSize>
{
    internal DigSize(int side, int depth)
    {
        Side = side;
        Depth = depth;
    }

    internal int Side { get; }

    internal int Depth { get; }

    internal string Name => N(Side) + "x" + N(Side) + "x" + N(Depth);

    public bool Equals(DigSize other) => Side == other.Side && Depth == other.Depth;

    public override bool Equals(object? obj) => obj is DigSize other && Equals(other);

    public override int GetHashCode() => Side * 31 + Depth;

    public override string ToString() => Name;

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The shovel's sizes (#574, owner playtest 2026-11-11: "instead of having the step-by-step digging, we should
/// have prefixed sizes"; the owner chose the three cubes and three shallower ones for floors and low rooms), smallest
/// first. The wheel steps through them.</summary>
internal static class DigSizes
{
    internal static readonly DigSize[] All =
    {
        new DigSize(1, 1), new DigSize(2, 1), new DigSize(2, 2), new DigSize(4, 1), new DigSize(4, 2), new DigSize(4, 4),
    };

    /// <summary>The size the shovel starts on: 2x2x2, a room you can stand in (lead default).</summary>
    internal const int DefaultIndex = 2;
}

/// <summary>Where a hole of a size goes for an aimed lattice vertex (#574, owner: "we should be able to autosnap holes to
/// make digging straight holes easier"). A hole snaps to the lattice of the nearest hole already dug: the newest
/// remembered hole whose edges lie within one hole-width of the aim. Its corner becomes the origin, and the new hole is
/// placed on a grid of its own side from there, so it meets that hole edge to edge and a row of holes stays straight even
/// when the sizes differ. With no hole near, the origin is the world's, and holes of one size tile anyway. Only the holes
/// <see cref="DigHistory"/> remembers (this shovel's, since the world loaded) are snapped to; an older hole is not.</summary>
internal static class DigSnap
{
    internal static DigFootprint Place(int x, int z, DigSize size, IReadOnlyList<DigFootprint> dug)
    {
        int originX = 0, originZ = 0;
        for (int i = dug.Count - 1; i >= 0; --i)
        {
            DigFootprint near = dug[i];
            if (x < near.MinX - size.Side || x > near.MaxX + size.Side || z < near.MinZ - size.Side || z > near.MaxZ + size.Side) continue;
            originX = near.MinX;
            originZ = near.MinZ;
            break;
        }

        int minX = originX + FloorDiv(x - originX, size.Side) * size.Side;
        int minZ = originZ + FloorDiv(z - originZ, size.Side) * size.Side;
        return DigFootprint.FromCorners(minX, minZ, minX + size.Side, minZ + size.Side, size.Depth);
    }

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);
}

/// <summary>The shovel's dig flow (#574, replacing #551's corner, corner, depth): the wheel chooses a size, the outline
/// follows the aim and snaps (<see cref="DigSnap"/>), and Attack digs it. Secondary arms undo, and a second Secondary
/// within <see cref="UndoConfirmSeconds"/> undoes the last dig, so a careless double press can undo only after the line
/// has said what it will do. The session only decides; it never reads the world or digs.</summary>
internal sealed class DigSession
{
    /// <summary>How long an armed undo waits for its confirming press (lead default).</summary>
    internal const float UndoConfirmSeconds = 3f;

    private readonly DigHistory _history;
    private readonly List<DigFootprint> _dug = new List<DigFootprint>(DigHistory.Capacity);
    private int _sizeIndex = DigSizes.DefaultIndex;
    private float _undoArmedAt = float.NaN;

    /// <param name="history">The holes this shovel has dug, newest last: what a new hole snaps to.</param>
    internal DigSession(DigHistory history) => _history = history ?? throw new ArgumentNullException(nameof(history));

    internal bool HasAim { get; private set; }

    internal int AimX { get; private set; }

    internal int AimZ { get; private set; }

    internal DigSize Size => DigSizes.All[_sizeIndex];

    /// <summary>The wheel: the next (positive) or previous size, wrapping round. Returns whether it changed.</summary>
    internal bool ChangeSize(int steps)
    {
        if (steps == 0) return false;
        int count = DigSizes.All.Length;
        _sizeIndex = ((_sizeIndex + steps) % count + count) % count;
        return true;
    }

    /// <summary>True while an undo waits for its confirming press.</summary>
    internal bool UndoArmed(float now) => LadderRules.Finite(_undoArmedAt) && now - _undoArmedAt <= UndoConfirmSeconds && now >= _undoArmedAt;

    /// <summary>The hole shown now: the size chosen, at the aim, snapped.</summary>
    internal DigFootprint Footprint
    {
        get
        {
            _dug.Clear();
            foreach (DigRecord record in _history.Records) _dug.Add(record.Footprint);
            return DigSnap.Place(AimX, AimZ, Size, _dug);
        }
    }

    internal void Aim(int x, int z)
    {
        HasAim = true;
        AimX = x;
        AimZ = z;
    }

    internal void LoseAim() => HasAim = false;

    /// <summary>Attack: dig what is shown.</summary>
    internal DigAction Primary()
    {
        _undoArmedAt = float.NaN;
        return HasAim ? DigAction.Dig : DigAction.None;
    }

    /// <summary>Secondary: arm or confirm undo.</summary>
    internal DigAction Secondary(float now)
    {
        if (UndoArmed(now))
        {
            _undoArmedAt = float.NaN;
            return DigAction.Undo;
        }

        _undoArmedAt = LadderRules.Finite(now) ? now : float.NaN;
        return LadderRules.Finite(now) ? DigAction.UndoArmed : DigAction.None;
    }

    /// <summary>A dig was committed.</summary>
    internal void Dug() => _undoArmedAt = float.NaN;

    /// <summary>The shovel was put away or a screen opened: forget the aim and any armed undo, keeping the size.</summary>
    internal void Reset()
    {
        HasAim = false;
        _undoArmedAt = float.NaN;
    }
}
