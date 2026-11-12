using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>Where on a donor material's texture a door surface samples (#548). Most of the donor doors' textures are
/// atlases: several parts packed into one sheet (the Roundpole gate's holds bark, log-end rings and a pale cut face; the
/// Iron Gate's is the catacombs props sheet). A surface mapped across the whole 0 to 1 square, as a built-in cube is,
/// shows all of them at once. A region surface instead maps each face into one rectangle of the sheet, its longer side
/// along the region's longer side, so the grain runs the way the donor's own leaf runs it. A tiling surface repeats a
/// texture that is one seamless material (the plank and metal-wall sheets) once per <see cref="UvMetres"/>.</summary>
internal readonly struct UvRegion
{
    private UvRegion(bool tiles, float u0, float v0, float u1, float v1)
    {
        Tiles = tiles;
        U0 = u0;
        V0 = v0;
        U1 = u1;
        V1 = v1;
    }

    /// <summary>A texture that tiles: no region, metric UVs.</summary>
    internal static UvRegion Tiling => new UvRegion(true, 0f, 0f, 1f, 1f);

    /// <summary>One rectangle of an atlas, in texture UV (v up, as Unity samples it).</summary>
    internal static UvRegion Rect(float u0, float v0, float u1, float v1)
    {
        if (!(u0 >= 0f && v0 >= 0f && u1 <= 1f && v1 <= 1f && u1 > u0 && v1 > v0))
            throw new ArgumentException("A UV region lies inside the unit square and has an area.");
        return new UvRegion(false, u0, v0, u1, v1);
    }

    /// <summary>A texture repeats once per this many metres of a tiling surface: the module's width, as the cellar
    /// frame prisms have always used.</summary>
    internal const float UvMetres = CellarGeometry.Module;

    internal bool Tiles { get; }

    internal float U0 { get; }

    internal float V0 { get; }

    internal float U1 { get; }

    internal float V1 { get; }

    /// <summary>A stable key for caching the meshes built with this region.</summary>
    internal string Key => Tiles ? "tile" : FormattableString.Invariant($"{U0:0.####}_{V0:0.####}_{U1:0.####}_{V1:0.####}");

    /// <summary>The UV of a point on a face. <paramref name="a"/> and <paramref name="b"/> are the point's coordinates
    /// along the face's two in-plane axes, and <paramref name="minA"/> to <paramref name="maxA"/>,
    /// <paramref name="minB"/> to <paramref name="maxB"/> the face's extents along them. A tiling surface takes the
    /// coordinates in <see cref="UvMetres"/>; a region surface stretches the face over the region, its longer extent
    /// along the region's longer side.</summary>
    internal CellarUv Map(float a, float b, float minA, float maxA, float minB, float maxB)
    {
        if (Tiles) return new CellarUv(a / UvMetres, b / UvMetres);
        float sa = maxA > minA ? (a - minA) / (maxA - minA) : 0.5f;
        float sb = maxB > minB ? (b - minB) / (maxB - minB) : 0.5f;
        bool faceLongAlongA = maxA - minA >= maxB - minB;
        bool regionLongAlongV = V1 - V0 >= U1 - U0;
        // The face's long axis goes on the region's long axis.
        float su = faceLongAlongA == regionLongAlongV ? sb : sa;
        float sv = faceLongAlongA == regionLongAlongV ? sa : sb;
        return new CellarUv(U0 + su * (U1 - U0), V0 + sv * (V1 - V0));
    }
}

/// <summary>Where a family's leaf (lid, cellar leaves, their boards and hinge hardware) and its frame sample their
/// donor materials (#548).</summary>
internal readonly struct DoorSurface
{
    internal DoorSurface(UvRegion leaf, UvRegion frame)
    {
        Leaf = leaf;
        Frame = frame;
    }

    internal UvRegion Leaf { get; }

    internal UvRegion Frame { get; }
}

/// <summary>The pinned texture regions of the nine families (#548), read from the installed game (Valheim 1.0.21) by
/// the read-only bundle probe <c>cl-ladders-devtools/doors/leafuv_islands.py</c>: each region is a UV island the donor's
/// own leaf or frame mesh uses on its largest faces, so the door shows the same part of the sheet the native door does.
/// The worn textures share their fresh texture's layout (checked side by side), so one region serves both. Two islands
/// were trimmed by eye, and the record says so: the Darkwood leaf's (the native island reaches a grey stone in the
/// sheet's corner) and the Ashwood frame's (a blue patch at the bottom of its island). DOOR_ASSET_SOURCES.md section 6a
/// lists them with their sources. Game-free so the table is tested.</summary>
internal static class DoorSurfaces
{
    internal static DoorSurface For(DoorFamily family)
    {
        switch (family.Donor)
        {
            // Planks5c_low and its worn copy are seamless plank sheets.
            case "wood_door":
            case "wood_gate":
                return new DoorSurface(UvRegion.Tiling, UvRegion.Tiling);
            // stakewall_d: the bark island of the gate's poles (door/high, the side faces), for leaf and frame alike.
            case "wood_fence_gate":
                return new DoorSurface(UvRegion.Rect(0.0672f, 0.1824f, 0.4651f, 0.8068f), UvRegion.Rect(0.0672f, 0.1824f, 0.4651f, 0.8068f));
            // fi_village_catacombs_hd_2: the bar island of the grate's door; the frame's metalwall is a seamless tile.
            case "iron_grate":
                return new DoorSurface(UvRegion.Rect(0.4522f, 0.3633f, 0.4860f, 0.7784f), UvRegion.Tiling);
            // DarkWoodGate_d: the leaf's plank island (door/low, its face) trimmed clear of the stone at u 0.16-0.2,
            // v 0.62-0.70; the frame's post island (New/high).
            case "darkwood_gate":
                return new DoorSurface(UvRegion.Rect(0.20f, 0.0895f, 0.3960f, 0.62f), UvRegion.Rect(0.7880f, 0.0780f, 0.8867f, 0.7490f));
            // TimberGate_d: the leaf's face island (door/high, the diamond panel); the frame's plank island (New/high).
            case "stave_gate":
                return new DoorSurface(UvRegion.Rect(0.2845f, 0.6207f, 0.5415f, 0.9746f), UvRegion.Rect(0.0178f, 0.0101f, 0.2359f, 0.8017f));
            // Hexagonaldoor_d: the door's face island (LOD, the carved panel); its edge strip for the frame.
            case "piece_hexagonal_door":
                return new DoorSurface(UvRegion.Rect(0.3079f, 0.0892f, 0.8403f, 0.6274f), UvRegion.Rect(0.9033f, 0.0250f, 0.9824f, 0.9526f));
            // ashwooddoor_d: the leaf's panel island (door/low); the frame island (Frame/high) trimmed above the blue
            // patch at v below 0.25.
            case "ashwood_door":
                return new DoorSurface(UvRegion.Rect(0.0103f, 0.4158f, 0.3950f, 0.9897f), UvRegion.Rect(0.4448f, 0.25f, 0.9248f, 0.9414f));
            // flametalgate_d: the leaf's face island (door/high); its side strip for the frame.
            case "flametal_gate":
                return new DoorSurface(UvRegion.Rect(0.0300f, 0.0382f, 0.1815f, 0.8328f), UvRegion.Rect(0.7525f, 0.0445f, 0.7938f, 0.8121f));
            default:
                throw new ArgumentException("No pinned texture regions for donor " + family.Donor, nameof(family));
        }
    }
}
