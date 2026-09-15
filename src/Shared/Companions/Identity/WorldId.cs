using System;
using System.Globalization;

namespace TheConcernedCat.Companions.Identity;

/// <summary>Stable identity of one world, wrapping the game's world UID.
///
/// The UID, not the world name, is the isolation key: two worlds may share a
/// name, and a renamed world must keep its companion progress. Zero means the
/// world is not resolved, which callers treat as "do not read or write".</summary>
internal readonly struct WorldId : IEquatable<WorldId>
{
    public static readonly WorldId None = default;

    public WorldId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value != 0L;

    public string ToStorageToken()
    {
        return unchecked((ulong)Value).ToString("x16", CultureInfo.InvariantCulture);
    }

    public static bool TryParseStorageToken(string? token, out WorldId id)
    {
        id = None;
        if (string.IsNullOrEmpty(token) || token!.Length != 16)
        {
            return false;
        }

        if (!ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
        {
            return false;
        }

        id = new WorldId(unchecked((long)parsed));
        return true;
    }

    public bool Equals(WorldId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is WorldId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return ToStorageToken();
    }
}
