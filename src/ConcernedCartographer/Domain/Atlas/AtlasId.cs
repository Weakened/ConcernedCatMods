using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Stable namespaced identity of a managed atlas entity, rendered
/// as <c>cc:&lt;kind&gt;:&lt;guid&gt;</c>. Identity never changes across
/// edits, sessions, migrations, or icon-registry reordering.</summary>
internal readonly struct AtlasId : IEquatable<AtlasId>
{
    public const string PinKind = "pin";
    public const string RouteKind = "route";

    public AtlasId(string kind, Guid value)
    {
        Kind = kind;
        Value = value;
    }

    public string Kind { get; }
    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static AtlasId NewPin()
    {
        return new AtlasId(PinKind, Guid.NewGuid());
    }

    public static bool TryParse(string? text, out AtlasId id)
    {
        id = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[] parts = text!.Split(':');
        if (parts.Length != 3 || parts[0] != "cc" || parts[1].Length == 0 ||
            !Guid.TryParseExact(parts[2], "N", out Guid value))
        {
            return false;
        }

        id = new AtlasId(parts[1], value);
        return true;
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "cc:{0}:{1:N}", Kind, Value);
    }

    public bool Equals(AtlasId other)
    {
        return string.Equals(Kind, other.Kind, StringComparison.Ordinal) && Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is AtlasId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode() ^ (Kind?.GetHashCode() ?? 0);
    }
}
