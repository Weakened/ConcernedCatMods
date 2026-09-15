using System;
using System.Globalization;

namespace TheConcernedCat.Companions.Identity;

/// <summary>Stable identity of one local character.
///
/// This wraps the numeric identifier the game assigns to a profile, never the
/// character's display name: renaming a character, or running two characters
/// with the same name, must not merge or lose companion progress. Zero is
/// reserved for "not resolved yet" so an adapter that cannot read the game
/// can fail closed instead of silently writing to a shared bucket.</summary>
internal readonly struct CharacterId : IEquatable<CharacterId>
{
    public static readonly CharacterId None = default;

    public CharacterId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value != 0L;

    /// <summary>Lowercase fixed-width hexadecimal, used in storage keys. The
    /// unchecked cast keeps negative identifiers from producing a '-' in a
    /// file name while staying perfectly round-trippable.</summary>
    public string ToStorageToken()
    {
        return unchecked((ulong)Value).ToString("x16", CultureInfo.InvariantCulture);
    }

    public static bool TryParseStorageToken(string? token, out CharacterId id)
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

        id = new CharacterId(unchecked((long)parsed));
        return true;
    }

    public bool Equals(CharacterId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is CharacterId other && Equals(other);
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
