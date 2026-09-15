using System;

namespace TheConcernedCat.Companions.Identity;

/// <summary>Identity of one independently packaged Concerned Cat product.
///
/// A product owns its companions, quests, and sidecar data. Two products
/// compiled from this shared source never share storage or unlock state, and
/// a product never learns another product's identity at compile time: each
/// one declares its own constant.</summary>
internal readonly struct ProductId : IEquatable<ProductId>
{
    private readonly string? _value;

    public ProductId(string value)
    {
        _value = IdentitySlug.Require(value, nameof(value));
    }

    /// <summary>The validated slug, or the empty string for a default
    /// instance. Callers should check <see cref="IsEmpty"/> rather than
    /// relying on a null.</summary>
    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static bool TryCreate(string? value, out ProductId id)
    {
        if (!IdentitySlug.IsValid(value))
        {
            id = default;
            return false;
        }

        id = new ProductId(value!);
        return true;
    }

    public bool Equals(ProductId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ProductId other && Equals(other);
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
