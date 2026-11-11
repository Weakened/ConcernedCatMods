using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>How a door family's moving leaf looks (#547). Presentation only: the leaf's collider, sweep, occupancy and
/// ladder rules are the same box whatever the look.</summary>
internal enum LeafLook
{
    /// <summary>One slab, the size of the leaf's collider.</summary>
    Solid,

    /// <summary>Boards running from the hinge to the free edge with gaps between them, over rails across the leaf.</summary>
    Gate,
}

/// <summary>What a visual part of a leaf is made of: a board shows the donor leaf's material, a rail the donor
/// frame's, so the two read apart.</summary>
internal enum LeafPieceRole
{
    Board,
    Rail,
}

/// <summary>One visual box of a leaf, in the leaf's own parent space.</summary>
internal readonly struct LeafPiece
{
    internal LeafPiece(string name, LeafPieceRole role, ClimbPoint centre, ClimbPoint size)
    {
        Name = name;
        Role = role;
        Centre = centre;
        Size = size;
    }

    internal string Name { get; }

    internal LeafPieceRole Role { get; }

    internal ClimbPoint Centre { get; }

    internal ClimbPoint Size { get; }
}

/// <summary>The visual boxes of a trapdoor lid or a cellar leaf (#547). The Wood Door and the Wood Gate borrow the same
/// donor materials (<see cref="TrapdoorBlueprint.MaterialsFor"/>), so before this their trapdoors and cellar doors were
/// identical but for the name. The Wood Gate's leaf is now a gate: boards along the leaf's length (z) with gaps,
/// laid over rails across it (x). Every part stays inside the leaf's collider box, so nothing the tested geometry
/// measures changes: the sweep, the clearances and the occupancy queries all use the box. Game-free so the shape is
/// tested.</summary>
internal static class LeafPattern
{
    /// <summary>The widest a gate board may be; the count is the fewest boards that keep each at or under this.</summary>
    internal const float BoardWidthMax = 0.18f;

    /// <summary>The gap between neighbouring boards.</summary>
    internal const float BoardGap = 0.03f;

    /// <summary>The share of the leaf's thickness the boards take, at its top; the rails take the rest, below.</summary>
    internal const float BoardShare = 0.55f;

    /// <summary>A rail's width along the leaf.</summary>
    internal const float RailWidth = 0.12f;

    /// <summary>Where the two rails' centres sit, as a share of the leaf's length from each end.</summary>
    internal const float RailInset = 0.2f;

    /// <summary>The look of a family's leaf. Only the Wood Gate is a gate: it is the one family whose donor materials
    /// match another's (the Wood Door's), and the only one the owner asked to change.</summary>
    internal static LeafLook For(DoorFamily family) =>
        string.Equals(family.Donor, "wood_gate", StringComparison.Ordinal) ? LeafLook.Gate : LeafLook.Solid;

    /// <summary>The visual parts of a leaf whose collider box is <paramref name="centre"/> and <paramref name="size"/>
    /// (x across, y thickness with the top at +y, z along from hinge to free edge or up the slope). A solid leaf is
    /// exactly one board named <paramref name="name"/>, the box itself, as before #547.</summary>
    internal static IReadOnlyList<LeafPiece> Parts(string name, ClimbPoint centre, ClimbPoint size, LeafLook look)
    {
        if (!(size.X > 0f && size.Y > 0f && size.Z > 0f)) throw new ArgumentException("A leaf has a positive size.", nameof(size));
        if (look == LeafLook.Solid) return new[] { new LeafPiece(name, LeafPieceRole.Board, centre, size) };

        int boards = BoardCount(size.X);
        float boardWidth = (size.X - (boards - 1) * BoardGap) / boards;
        float boardThickness = size.Y * BoardShare;
        float railThickness = size.Y - boardThickness;
        float top = centre.Y + size.Y / 2f;
        float left = centre.X - size.X / 2f;
        var parts = new List<LeafPiece>(boards + 2);
        for (int index = 0; index < boards; ++index)
        {
            float x = left + boardWidth / 2f + index * (boardWidth + BoardGap);
            parts.Add(new LeafPiece(name + "_board_" + index, LeafPieceRole.Board,
                new ClimbPoint(x, top - boardThickness / 2f, centre.Z),
                new ClimbPoint(boardWidth, boardThickness, size.Z)));
        }

        float railY = top - boardThickness - railThickness / 2f;
        float railWidth = Math.Min(RailWidth, size.Z * RailInset);
        float reach = size.Z / 2f - size.Z * RailInset;
        parts.Add(new LeafPiece(name + "_rail_0", LeafPieceRole.Rail, new ClimbPoint(centre.X, railY, centre.Z - reach), new ClimbPoint(size.X, railThickness, railWidth)));
        parts.Add(new LeafPiece(name + "_rail_1", LeafPieceRole.Rail, new ClimbPoint(centre.X, railY, centre.Z + reach), new ClimbPoint(size.X, railThickness, railWidth)));
        return parts;
    }

    /// <summary>The fewest boards, at least two, that keep each no wider than <see cref="BoardWidthMax"/>.</summary>
    internal static int BoardCount(float width)
    {
        int count = (int)Math.Ceiling((width + BoardGap) / (BoardWidthMax + BoardGap) - 1e-4);
        return Math.Max(2, count);
    }
}
