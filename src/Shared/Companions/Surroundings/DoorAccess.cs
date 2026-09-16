using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.Companions.Surroundings;

/// <summary>How companions treat a door nobody has marked.</summary>
internal enum DoorAccessPolicy
{
    /// <summary>A door is closed to companions until its owner lets them through.
    /// The default: a companion walking into somebody's house uninvited is not
    /// common sense.</summary>
    OnlyAllowedDoors = 0,

    /// <summary>Every door a player could open is open to companions too.</summary>
    AllDoors = 1,
}

/// <summary>A doorway, remembered by where it is.
///
/// Not by the game's object id: Valheim renumbers every object each time a
/// world loads (<c>ZDO.Load</c> assigns <c>++ZDOID.m_loadID</c>), so an id
/// written tonight names a different object tomorrow. A door does not move, so
/// its place and its piece type are the identity that survives a reload. A door
/// broken and rebuilt in the same frame is the same doorway, and keeps its
/// permission - which is what a player who replaced a door would
/// expect.</summary>
internal readonly struct DoorPlace : IEquatable<DoorPlace>
{
    /// <summary>How far apart, horizontally, two records may be and still name
    /// the same doorway. Tight: two doors side by side are a metre or more
    /// apart.</summary>
    public const float MatchMetres = 0.35f;

    /// <summary>How far apart in height two records may be.</summary>
    public const float MatchHeightMetres = 0.5f;

    public DoorPlace(WorldPoint position, int prefab)
    {
        Position = position;
        Prefab = prefab;
    }

    public WorldPoint Position { get; }

    /// <summary>The piece's prefab hash, or 0 when it is not known. Two known
    /// prefabs that differ are different doors wherever they stand.</summary>
    public int Prefab { get; }

    public bool SamePlaceAs(DoorPlace other)
    {
        if (Prefab != 0 && other.Prefab != 0 && Prefab != other.Prefab)
        {
            return false;
        }

        return Position.HorizontalDistanceTo(other.Position) <= MatchMetres &&
            Math.Abs(Position.Y - other.Position.Y) <= MatchHeightMetres;
    }

    public bool Equals(DoorPlace other)
    {
        return Prefab == other.Prefab && Position.Equals(other.Position);
    }

    public override bool Equals(object? obj)
    {
        return obj is DoorPlace other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Prefab * 397) ^ Position.GetHashCode();
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0}", Position.X, Position.Y, Position.Z);
    }
}

/// <summary>Which doors companions may use, in one world, on this computer.
///
/// A presentation setting, stored beside the mod's own data and never in the
/// world: companions are local to each player, so what one player lets them
/// through is nobody else's business, and nothing about a door changes in the
/// save when it is marked.</summary>
internal sealed class DoorAccessBook
{
    private readonly List<DoorPlace> _allowed = new List<DoorPlace>();

    public IReadOnlyList<DoorPlace> Allowed => _allowed;

    /// <summary>True once something changed since the last save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Whether companions may use this door under
    /// <paramref name="policy"/>. A door the player could not open themselves
    /// is never asked about here; that is the caller's first check.</summary>
    public bool IsAllowed(DoorPlace door, DoorAccessPolicy policy)
    {
        return policy == DoorAccessPolicy.AllDoors || IndexOf(door) >= 0;
    }

    /// <summary>Lets companions through this door, or stops them. Returns
    /// whether anything changed.</summary>
    public bool SetAllowed(DoorPlace door, bool allowed)
    {
        int index = IndexOf(door);
        if (allowed == (index >= 0))
        {
            return false;
        }

        if (allowed)
        {
            _allowed.Add(door);
        }
        else
        {
            _allowed.RemoveAt(index);
        }

        IsDirty = true;
        return true;
    }

    /// <summary>Flips the door and returns whether companions may now use it.
    /// </summary>
    public bool Toggle(DoorPlace door)
    {
        bool allowed = IndexOf(door) < 0;
        SetAllowed(door, allowed);
        return allowed;
    }

    /// <summary>Forgets every door. Returns how many there were.</summary>
    public int Clear()
    {
        int count = _allowed.Count;
        if (count > 0)
        {
            _allowed.Clear();
            IsDirty = true;
        }

        return count;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    /// <summary>Adds a record read from disk without marking the book dirty.
    /// A duplicate of a record already present is dropped.</summary>
    internal void Restore(DoorPlace door)
    {
        if (IndexOf(door) < 0)
        {
            _allowed.Add(door);
        }
    }

    private int IndexOf(DoorPlace door)
    {
        for (int index = 0; index < _allowed.Count; index++)
        {
            if (_allowed[index].SamePlaceAs(door))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>The door access file: a header, then one line per door companions
/// may use. Tab-separated and invariant-culture, like the companion
/// sidecar.</summary>
internal static class DoorAccessCodec
{
    public const string Header = "# Concerned Companions: doors companions may use in this world";

    private const string DoorRow = "door";

    public static IEnumerable<string> Serialize(DoorAccessBook book)
    {
        yield return Header;
        foreach (DoorPlace door in book.Allowed)
        {
            yield return string.Join(
                "\t",
                DoorRow,
                door.Prefab.ToString(CultureInfo.InvariantCulture),
                door.Position.X.ToString("0.###", CultureInfo.InvariantCulture),
                door.Position.Y.ToString("0.###", CultureInfo.InvariantCulture),
                door.Position.Z.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Reads what can be read. A damaged line is skipped and counted,
    /// never guessed at: the worst a damaged file can do is forget a door, which
    /// only ever keeps a companion out.</summary>
    public static DoorAccessBook Parse(IEnumerable<string>? lines, out int skipped)
    {
        var book = new DoorAccessBook();
        skipped = 0;
        if (lines == null)
        {
            return book;
        }

        foreach (string raw in lines)
        {
            string line = raw?.Trim() ?? string.Empty;
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields[0] != DoorRow)
            {
                // A row kind a later version added. Not damage.
                continue;
            }

            if (fields.Length < 5 ||
                !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prefab) ||
                !TryReadFloat(fields[2], out float x) ||
                !TryReadFloat(fields[3], out float y) ||
                !TryReadFloat(fields[4], out float z))
            {
                skipped++;
                continue;
            }

            book.Restore(new DoorPlace(new WorldPoint(x, y, z), prefab));
        }

        book.MarkClean();
        return book;
    }

    private static bool TryReadFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
