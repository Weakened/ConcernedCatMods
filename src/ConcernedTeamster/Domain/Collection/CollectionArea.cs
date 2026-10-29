using System;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>The bounded area a player gave Gunnar to collect in (#381): a circle
/// the player stood in the middle of when they set it.
///
/// <b>Explicit, bounded, and never widened.</b> The centre is where the player
/// was standing when they ran <c>ct_collect area</c> - the explicit act - never
/// where Gunnar or the player happens to be later, so the area does not follow
/// anybody around. The radius is clamped to the bounds `DECISIONS.md` D11 gives
/// a work area (4 to 48 m); there is no "search further" path anywhere, and an
/// area that is used up is reported as used up rather than grown.
///
/// <b>Why Teamster writes its own.</b> The shared runtime's circle is internal,
/// and <see cref="INpcWorkArea"/> is the public seam a role answers. Containment
/// is the same flat-distance rule every area in this repository uses.</summary>
internal sealed class CollectionArea : INpcWorkArea
{
    public const float SmallestRadiusMetres = 4f;
    public const float LargestRadiusMetres = 48f;
    public const float DefaultRadiusMetres = 20f;

    private CollectionArea(NpcPoint centre, float radiusMetres, int revision)
    {
        BoundingCentre = centre;
        BoundingRadiusMetres = radiusMetres;
        Revision = revision;
        Describe = "Gunnar's collection area";
    }

    public NpcPoint BoundingCentre { get; }

    public float BoundingRadiusMetres { get; }

    /// <summary>Different for every area set, so a plan made against one area
    /// is stale against the next.</summary>
    public int Revision { get; }

    public string Describe { get; }

    public CollectionPoint Centre => new CollectionPoint(BoundingCentre.X, BoundingCentre.Y, BoundingCentre.Z);

    /// <summary>Builds an area, or says why not. A radius outside the bounds is
    /// clamped and the caller is told what it became; a centre that is not a
    /// real position is refused.</summary>
    public static bool TryCreate(
        CollectionPoint centre, float requestedRadius, int revision, out CollectionArea? area, out string said)
    {
        area = null;
        var point = new NpcPoint(centre.X, centre.Y, centre.Z);
        if (!point.IsFinite)
        {
            said = "the place you are standing could not be read, so no area was set";
            return false;
        }

        float radius = float.IsNaN(requestedRadius) || float.IsInfinity(requestedRadius)
            ? DefaultRadiusMetres
            : requestedRadius;
        if (radius < SmallestRadiusMetres)
        {
            radius = SmallestRadiusMetres;
        }
        else if (radius > LargestRadiusMetres)
        {
            radius = LargestRadiusMetres;
        }

        area = new CollectionArea(point, radius, revision);
        said = "an area of " + radius.ToString("0.#", CultureInfo.InvariantCulture) + " m around where you stand";
        return true;
    }

    public bool Contains(NpcPoint point) =>
        point.IsFinite && BoundingCentre.HorizontalDistanceTo(point) <= BoundingRadiusMetres;

    public bool Contains(CollectionPoint point) => Contains(new NpcPoint(point.X, point.Y, point.Z));

    public override string ToString() =>
        Describe + " at " + BoundingCentre + ", radius " +
        BoundingRadiusMetres.ToString("0.#", CultureInfo.InvariantCulture) + " m";
}
