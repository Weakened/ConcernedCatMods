using System;

namespace TheConcernedCat.Companions.Identity;

/// <summary>Identity of one companion within a product. Scoped by
/// <see cref="ProductId"/>, so two products may both use the slug
/// <c>hulgi</c> without colliding.</summary>
internal readonly struct CompanionId : IEquatable<CompanionId>
{
    private readonly string? _value;

    public CompanionId(string value)
    {
        _value = IdentitySlug.Require(value, nameof(value));
    }

    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static bool TryCreate(string? value, out CompanionId id)
    {
        if (!IdentitySlug.IsValid(value))
        {
            id = default;
            return false;
        }

        id = new CompanionId(value!);
        return true;
    }

    public bool Equals(CompanionId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is CompanionId other && Equals(other);
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
