using System;

namespace TheConcernedCat.Companions.Identity;

/// <summary>The full isolation key for companion data: one product, one
/// world, one character.
///
/// Every read and every write is addressed by a scope. Nothing is shared
/// across products, worlds, or characters, so a scope that is not
/// <see cref="IsComplete"/> is a hard stop rather than a fallback to some
/// default bucket - silently writing unscoped data is exactly the failure
/// this type exists to prevent.</summary>
internal readonly struct CompanionScope : IEquatable<CompanionScope>
{
    public CompanionScope(ProductId product, WorldId world, CharacterId character)
    {
        Product = product;
        World = world;
        Character = character;
    }

    public ProductId Product { get; }
    public WorldId World { get; }
    public CharacterId Character { get; }

    /// <summary>True only when all three components resolved. A caller that
    /// sees false must defer, not substitute a placeholder.</summary>
    public bool IsComplete => !Product.IsEmpty && World.IsValid && Character.IsValid;

    /// <summary>Filesystem-safe file-name stem, unique per scope. The product
    /// slug is already constrained to <c>a-z0-9-</c> and the two identifiers
    /// to fixed-width hex, so the result needs no further escaping.</summary>
    public string ToStorageKey()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException(
                "A companion storage key requires a resolved product, world, and character.");
        }

        return Product.Value + "." + World.ToStorageToken() + "." + Character.ToStorageToken();
    }

    public bool Equals(CompanionScope other)
    {
        return Product.Equals(other.Product)
            && World.Equals(other.World)
            && Character.Equals(other.Character);
    }

    public override bool Equals(object? obj)
    {
        return obj is CompanionScope other && Equals(other);
    }

    public override int GetHashCode()
    {
        int hash = Product.GetHashCode();
        hash = (hash * 397) ^ World.GetHashCode();
        hash = (hash * 397) ^ Character.GetHashCode();
        return hash;
    }

    public override string ToString()
    {
        return IsComplete ? ToStorageKey() : "<incomplete companion scope>";
    }
}
