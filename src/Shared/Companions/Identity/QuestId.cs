using System;

namespace TheConcernedCat.Companions.Identity;

/// <summary>Identity of one quest within a product. A quest is the unit of
/// persisted progress, so this slug appears verbatim in sidecar rows and must
/// stay stable across releases once shipped.</summary>
internal readonly struct QuestId : IEquatable<QuestId>
{
    private readonly string? _value;

    public QuestId(string value)
    {
        _value = IdentitySlug.Require(value, nameof(value));
    }

    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static bool TryCreate(string? value, out QuestId id)
    {
        if (!IdentitySlug.IsValid(value))
        {
            id = default;
            return false;
        }

        id = new QuestId(value!);
        return true;
    }

    public bool Equals(QuestId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is QuestId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value;
    }
}
