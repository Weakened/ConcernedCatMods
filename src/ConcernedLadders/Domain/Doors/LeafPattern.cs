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

    /// <summary>The Wood Door's (#555): tight planks with thin seams over two braces.</summary>
    Planked,

    /// <summary>The Roundpole gate's (#555): narrow poles side by side over three rails.</summary>
    Poles,

    /// <summary>The Iron Gate's (#555): a frame of four rails around vertical bars, open between them, with a cross bar.</summary>
    Bars,

    /// <summary>The Darkwood gate's (#555): planks with two dark iron straps across them.</summary>
    Strapped,

    /// <summary>The Timberwood, Hexagonal and Ashwood doors' (#555): a raised frame around a recessed panel that shows the
    /// door's own panel texture.</summary>
    Panelled,

    /// <summary>The Flametal gate's (#555): two plates with three bands across them.</summary>
    Plated,
}

/// <summary>What a visual part of a leaf is made of: a board shows the donor leaf's material, a rail the donor
/// frame's, so the two read apart.</summary>
internal enum LeafPieceRole
{
    Board,
    Rail,

    /// <summary>Iron hardware (#555): the darkened hinge material, the ghost's hinge marks' own.</summary>
    Strap,
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

    /// <summary>The look of a family's leaf (#547 the Wood Gate; #555 every family, owner 2026-11-10: "any door type we
    /// have should have a corresponding bulkhead ... beautifully integrate with vanilla assets"): each reads as its own
    /// door. Every look stays inside the leaf's collider box.</summary>
    internal static LeafLook For(DoorFamily family) => family.Donor switch
    {
        "wood_door" => LeafLook.Planked,
        "wood_gate" => LeafLook.Gate,
        "wood_fence_gate" => LeafLook.Poles,
        "iron_grate" => LeafLook.Bars,
        "darkwood_gate" => LeafLook.Strapped,
        "stave_gate" => LeafLook.Panelled,
        "piece_hexagonal_door" => LeafLook.Panelled,
        "ashwood_door" => LeafLook.Panelled,
        "flametal_gate" => LeafLook.Plated,
        _ => LeafLook.Solid,
    };

    /// <summary>The visual parts of a leaf whose collider box is <paramref name="centre"/> and <paramref name="size"/>
    /// (x across, y thickness with the top at +y, z along from hinge to free edge or up the slope). A solid leaf is
    /// exactly one board named <paramref name="name"/>, the box itself, as before #547.</summary>
    internal static IReadOnlyList<LeafPiece> Parts(string name, ClimbPoint centre, ClimbPoint size, LeafLook look)
    {
        if (!(size.X > 0f && size.Y > 0f && size.Z > 0f)) throw new ArgumentException("A leaf has a positive size.", nameof(size));
        switch (look)
        {
            case LeafLook.Solid: return new[] { new LeafPiece(name, LeafPieceRole.Board, centre, size) };
            case LeafLook.Planked: return Boarded(name, centre, size, 0.2f, 0.006f, 0.7f, new[] { RailInset, 1f - RailInset }, LeafPieceRole.Rail);
            case LeafLook.Poles: return Boarded(name, centre, size, 0.1f, 0.012f, 0.6f, new[] { 0.15f, 0.5f, 0.85f }, LeafPieceRole.Rail);
            case LeafLook.Bars: return Barred(name, centre, size);
            case LeafLook.Strapped: return Strapped(name, centre, size);
            case LeafLook.Panelled: return Panelled(name, centre, size);
            case LeafLook.Plated: return Plated(name, centre, size);
        }

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

    /// <summary>Boards along the leaf, no wider than <paramref name="maxWidth"/> with <paramref name="gap"/> between them, in
    /// the top <paramref name="share"/> of the thickness, over rails across the leaf at the given shares of its length.</summary>
    private static IReadOnlyList<LeafPiece> Boarded(string name, ClimbPoint centre, ClimbPoint size, float maxWidth, float gap,
        float share, float[] rails, LeafPieceRole railRole)
    {
        var parts = new List<LeafPiece>();
        float top = centre.Y + size.Y / 2f, bottom = centre.Y - size.Y / 2f;
        float boardThickness = size.Y * share;
        Boards(parts, name, centre.X - size.X / 2f, centre.X + size.X / 2f, top - boardThickness, top, centre.Z, size.Z, maxWidth, gap, LeafPieceRole.Board);
        float railWidth = Math.Min(RailWidth, size.Z * 0.12f);
        for (int index = 0; index < rails.Length; index++)
        {
            float z = centre.Z - size.Z / 2f + size.Z * rails[index];
            z = Math.Max(centre.Z - size.Z / 2f + railWidth / 2f, Math.Min(centre.Z + size.Z / 2f - railWidth / 2f, z));
            parts.Add(new LeafPiece(name + "_rail_" + index, railRole, new ClimbPoint(centre.X, (bottom + top - boardThickness) / 2f, z),
                new ClimbPoint(size.X, size.Y - boardThickness, railWidth)));
        }

        return parts;
    }

    private static void Boards(List<LeafPiece> parts, string name, float minX, float maxX, float minY, float maxY, float z, float length,
        float maxWidth, float gap, LeafPieceRole role)
    {
        float width = maxX - minX;
        int count = Math.Max(2, (int)Math.Ceiling((width + gap) / (maxWidth + gap) - 1e-4));
        float board = (width - (count - 1) * gap) / count;
        for (int index = 0; index < count; index++)
        {
            float x = minX + board / 2f + index * (board + gap);
            parts.Add(new LeafPiece(name + "_board_" + index, role, new ClimbPoint(x, (minY + maxY) / 2f, z), new ClimbPoint(board, maxY - minY, length)));
        }
    }

    /// <summary>The Iron Gate's: a frame of four flat bars, full thickness, around vertical bars in the middle of the
    /// thickness with open gaps between them, and one cross bar half way.</summary>
    private static IReadOnlyList<LeafPiece> Barred(string name, ClimbPoint centre, ClimbPoint size)
    {
        const float frame = 0.06f, bar = 0.035f, pitch = 0.12f;
        var parts = new List<LeafPiece>();
        float minX = centre.X - size.X / 2f, maxX = centre.X + size.X / 2f, minZ = centre.Z - size.Z / 2f, maxZ = centre.Z + size.Z / 2f;
        parts.Add(new LeafPiece(name + "_frame_0", LeafPieceRole.Rail, new ClimbPoint(minX + frame / 2f, centre.Y, centre.Z), new ClimbPoint(frame, size.Y, size.Z)));
        parts.Add(new LeafPiece(name + "_frame_1", LeafPieceRole.Rail, new ClimbPoint(maxX - frame / 2f, centre.Y, centre.Z), new ClimbPoint(frame, size.Y, size.Z)));
        parts.Add(new LeafPiece(name + "_frame_2", LeafPieceRole.Rail, new ClimbPoint(centre.X, centre.Y, minZ + frame / 2f), new ClimbPoint(size.X - 2f * frame, size.Y, frame)));
        parts.Add(new LeafPiece(name + "_frame_3", LeafPieceRole.Rail, new ClimbPoint(centre.X, centre.Y, maxZ - frame / 2f), new ClimbPoint(size.X - 2f * frame, size.Y, frame)));
        parts.Add(new LeafPiece(name + "_cross", LeafPieceRole.Rail, new ClimbPoint(centre.X, centre.Y, centre.Z), new ClimbPoint(size.X - 2f * frame, size.Y * 0.6f, frame * 0.8f)));
        float inner = size.X - 2f * frame;
        int count = Math.Max(2, (int)Math.Round(inner / pitch));
        float step = inner / count;
        for (int index = 0; index < count - 1; index++)
        {
            float x = minX + frame + step * (index + 1);
            parts.Add(new LeafPiece(name + "_bar_" + index, LeafPieceRole.Board, new ClimbPoint(x, centre.Y, centre.Z), new ClimbPoint(bar, size.Y * 0.5f, size.Z - 2f * frame)));
        }

        return parts;
    }

    /// <summary>The Darkwood gate's: planks in the bottom four fifths of the thickness, two dark straps across the top.</summary>
    private static IReadOnlyList<LeafPiece> Strapped(string name, ClimbPoint centre, ClimbPoint size)
    {
        var parts = new List<LeafPiece>();
        float top = centre.Y + size.Y / 2f, bottom = centre.Y - size.Y / 2f, strap = size.Y * 0.2f;
        Boards(parts, name, centre.X - size.X / 2f, centre.X + size.X / 2f, bottom, top - strap, centre.Z, size.Z, 0.2f, 0.008f, LeafPieceRole.Board);
        foreach (float share in new[] { 0.22f, 0.78f })
        {
            float z = centre.Z - size.Z / 2f + size.Z * share;
            parts.Add(new LeafPiece(name + "_strap_" + (share < 0.5f ? 0 : 1), LeafPieceRole.Strap, new ClimbPoint(centre.X, top - strap / 2f, z),
                new ClimbPoint(size.X * 0.94f, strap, Math.Min(0.09f, size.Z * 0.1f))));
        }

        return parts;
    }

    /// <summary>The panelled doors': a raised frame, full thickness, around a panel recessed to three fifths of it.</summary>
    private static IReadOnlyList<LeafPiece> Panelled(string name, ClimbPoint centre, ClimbPoint size)
    {
        float frame = Math.Min(0.1f, Math.Min(size.X, size.Z) * 0.15f);
        float bottom = centre.Y - size.Y / 2f, panel = size.Y * 0.6f;
        float minX = centre.X - size.X / 2f, maxX = centre.X + size.X / 2f, minZ = centre.Z - size.Z / 2f, maxZ = centre.Z + size.Z / 2f;
        return new[]
        {
            new LeafPiece(name + "_frame_0", LeafPieceRole.Rail, new ClimbPoint(minX + frame / 2f, centre.Y, centre.Z), new ClimbPoint(frame, size.Y, size.Z)),
            new LeafPiece(name + "_frame_1", LeafPieceRole.Rail, new ClimbPoint(maxX - frame / 2f, centre.Y, centre.Z), new ClimbPoint(frame, size.Y, size.Z)),
            new LeafPiece(name + "_frame_2", LeafPieceRole.Rail, new ClimbPoint(centre.X, centre.Y, minZ + frame / 2f), new ClimbPoint(size.X - 2f * frame, size.Y, frame)),
            new LeafPiece(name + "_frame_3", LeafPieceRole.Rail, new ClimbPoint(centre.X, centre.Y, maxZ - frame / 2f), new ClimbPoint(size.X - 2f * frame, size.Y, frame)),
            new LeafPiece(name + "_panel", LeafPieceRole.Board, new ClimbPoint(centre.X, bottom + panel / 2f, centre.Z), new ClimbPoint(size.X - 2f * frame, panel, size.Z - 2f * frame)),
        };
    }

    /// <summary>The Flametal gate's: two plates with a seam, in the bottom four fifths of the thickness, three bands across.</summary>
    private static IReadOnlyList<LeafPiece> Plated(string name, ClimbPoint centre, ClimbPoint size)
    {
        var parts = new List<LeafPiece>();
        float top = centre.Y + size.Y / 2f, bottom = centre.Y - size.Y / 2f, band = size.Y * 0.2f;
        Boards(parts, name, centre.X - size.X / 2f, centre.X + size.X / 2f, bottom, top - band, centre.Z, size.Z, size.X / 2f, 0.01f, LeafPieceRole.Board);
        float[] shares = { 0.15f, 0.5f, 0.85f };
        for (int index = 0; index < shares.Length; index++)
        {
            float z = centre.Z - size.Z / 2f + size.Z * shares[index];
            parts.Add(new LeafPiece(name + "_band_" + index, LeafPieceRole.Strap, new ClimbPoint(centre.X, top - band / 2f, z),
                new ClimbPoint(size.X, band, Math.Min(0.07f, size.Z * 0.08f))));
        }

        return parts;
    }

    /// <summary>The fewest boards, at least two, that keep each no wider than <see cref="BoardWidthMax"/>.</summary>
    internal static int BoardCount(float width)
    {
        int count = (int)Math.Ceiling((width + BoardGap) / (BoardWidthMax + BoardGap) - 1e-4);
        return Math.Max(2, count);
    }
}
