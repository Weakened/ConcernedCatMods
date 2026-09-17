using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>A doorway as a cart route sees it: an opening in a wall with a side
/// to it. The same geometry the companions use for doors (a vertical plane
/// through the door's centre across its facing axis), copied into Teamster
/// because products never share game-facing code at compile time.
///
/// Contract revision C1 lets no cart through any doorway, open or shut
/// (<c>docs/mods/concerned-teamster/CART_ROUTES.md</c>, "Doors"): a vanilla door
/// opening is narrower than the loaded cart plus its side clearance, Teamster
/// has no door permission of its own to honour, and opening a door is a network
/// call Teamster does not make. So a crossing is all that matters here; whether
/// the door is open is not asked.</summary>
internal readonly struct CartDoorway
{
    public CartDoorway(WorkPoint floorCentre, float normalX, float normalZ, float halfWidthMetres)
    {
        float length = CartRouteGeometry.FlatLength(normalX, normalZ);
        if (!(length > 1e-4f))
        {
            normalX = 0f;
            normalZ = 1f;
            length = 1f;
        }

        FloorCentre = floorCentre;
        NormalX = normalX / length;
        NormalZ = normalZ / length;
        HalfWidthMetres = Math.Max(0.1f, halfWidthMetres);
    }

    /// <summary>The middle of the opening, at floor height.</summary>
    public WorkPoint FloorCentre { get; }

    public float NormalX { get; }

    public float NormalZ { get; }

    /// <summary>Half the opening's width, generously.</summary>
    public float HalfWidthMetres { get; }

    /// <summary>Whether a body <paramref name="bodyHalfWidth"/> either side of
    /// the straight line <paramref name="from"/>–<paramref name="to"/> passes
    /// through this doorway: the line crosses the doorway's plane within the
    /// opening widened by the body, at the doorway's level.</summary>
    public bool IsCrossedBy(WorkPoint from, WorkPoint to, float bodyHalfWidth)
    {
        float a = SignedDistance(from);
        float b = SignedDistance(to);
        if ((a > 0f && b > 0f) || (a < 0f && b < 0f) || a == b)
        {
            return false;
        }

        float t = a / (a - b);
        float x = from.X + ((to.X - from.X) * t);
        float y = from.Y + ((to.Y - from.Y) * t);
        float z = from.Z + ((to.Z - from.Z) * t);

        float lateral = Math.Abs(((x - FloorCentre.X) * -NormalZ) + ((z - FloorCentre.Z) * NormalX));
        return lateral <= HalfWidthMetres + Math.Max(0f, bodyHalfWidth) &&
            Math.Abs(y - FloorCentre.Y) <= CartRouteGeometry.DoorwayHeightBandMetres;
    }

    private float SignedDistance(WorkPoint point)
    {
        return ((point.X - FloorCentre.X) * NormalX) + ((point.Z - FloorCentre.Z) * NormalZ);
    }
}
