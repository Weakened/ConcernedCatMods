using System;

namespace TheConcernedCat.Companions.Placement;

/// <summary>The resolved home position a companion lives near.</summary>
internal readonly struct CompanionAnchor : IEquatable<CompanionAnchor>
{
    public static readonly CompanionAnchor None = default;

    public CompanionAnchor(AnchorKind kind, WorldPoint position)
    {
        Kind = kind;
        Position = position;
    }

    public AnchorKind Kind { get; }
    public WorldPoint Position { get; }

    public bool IsValid => Kind != AnchorKind.None;

    /// <summary>True when the anchor moved far enough to be worth relocating
    /// the companion. A small tolerance keeps a bed that reports a fractionally
    /// different position between sessions from causing a pointless move.</summary>
    public bool DiffersFrom(CompanionAnchor other, float tolerance)
    {
        if (Kind != other.Kind)
        {
            return true;
        }

        if (!IsValid)
        {
            return false;
        }

        return Position.HorizontalDistanceTo(other.Position) > tolerance
            || Position.VerticalDistanceTo(other.Position) > tolerance;
    }

    public bool Equals(CompanionAnchor other)
    {
        return Kind == other.Kind && Position.Equals(other.Position);
    }

    public override bool Equals(object? obj)
    {
        return obj is CompanionAnchor other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ((int)Kind * 397) ^ Position.GetHashCode();
    }

    public override string ToString()
    {
        return IsValid ? Kind + " " + Position : "no anchor";
    }
}
