using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>A number that changes when an area's geometry changes, and does not
/// change for anything else.
///
/// <b>This formula is carried verbatim from the shipped one and must not
/// drift.</b> Foreman's collection journal writes the accepted scope's revision
/// into a named field on a schema-3 row, and an order resumed after a reload
/// compares the number it finds there against the number the world computes
/// now. A different hash - a different separator, a different float format, a
/// different seed - makes every in-flight order read as <i>changed</i> on the
/// first load after the upgrade, which pauses every one of them for a person to
/// look at. So: FNV-1a over the invariant round-trip text of the same fields, in
/// the same order, with the same punctuation.
///
/// <b>Derived, never counted.</b> A counter would give the same area a
/// different number in a later session and make every resumed job look changed
/// - the same failure, arrived at from the other side.</summary>
internal static class NpcAreaRevision
{
    /// <summary>The revision of a circle: centre and radius. The same circle
    /// gives the same number in every session, on every machine.</summary>
    internal static int ForCircle(NpcPoint centre, float radiusMetres) =>
        Hash("area|" + Format(centre) + "|" + radiusMetres.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>The revision of any other shape: the provider's own kind name,
    /// then its numbers, in order. Offered so a provider registered from outside
    /// this package gets the derived-not-counted property for free rather than
    /// inventing a scheme that a reload can disagree with.</summary>
    internal static int ForShape(string kind, IReadOnlyList<float> numbers)
    {
        string text = (kind ?? string.Empty) + "|";
        if (numbers != null)
        {
            for (int index = 0; index < numbers.Count; index++)
            {
                text += numbers[index].ToString("R", CultureInfo.InvariantCulture) + ";";
            }
        }

        return Hash(text);
    }

    private static string Format(NpcPoint point) =>
        point.X.ToString("R", CultureInfo.InvariantCulture) + ";" +
        point.Y.ToString("R", CultureInfo.InvariantCulture) + ";" +
        point.Z.ToString("R", CultureInfo.InvariantCulture);

    private static int Hash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)hash;
        }
    }
}

/// <summary>The one work area shape that ships in this package: a bounded
/// horizontal circle.
///
/// <b>Bounded, and that is a requirement rather than a description.</b> A circle
/// with an infinite, NaN or non-positive radius is not an area, and this refuses
/// to be one rather than answering <c>Contains</c> everywhere or nowhere.
/// <see cref="TryCreate"/> is the only way in, so there is no path from bad
/// numbers to a plausible-looking area.
///
/// <b>Horizontal, matching every shipped area.</b> A tree two metres up a bank
/// at the edge of the harvest area is in the harvest area, because that is what
/// a person marking it meant.
///
/// <b>What it deliberately does not do: clamp.</b> The shipped collection scope
/// refuses a radius outside 4-48 m, and that limit stays in the role that has
/// it. It is a policy about how far a worker should be sent, not a fact about
/// circles, and a polygon provider has no radius to clamp - so a clamp here
/// would be a rule one shape can obey and the others cannot, applied to
/// everybody. A role keeps its own limit and refuses before it gets here.
///
/// <b>What it deliberately does not know: where anybody is standing.</b> The
/// centre is a parameter. There is no constructor that reads a position from
/// the world, and nothing in this package can supply one, so an area can be
/// established for ground no NPC and no player has ever walked to - which is the
/// point. Marking an area by standing in the middle of it is a role's input
/// method, not a property of areas, and making it the only one is how you end up
/// walking a player across a map to draw a box.</summary>
internal sealed class NpcCircleWorkArea : INpcWorkArea
{
    private NpcCircleWorkArea(NpcPoint centre, float radiusMetres, string describe)
    {
        BoundingCentre = centre;
        BoundingRadiusMetres = radiusMetres;
        Describe = describe;
        Revision = NpcAreaRevision.ForCircle(centre, radiusMetres);
    }

    public NpcPoint BoundingCentre { get; }

    public float BoundingRadiusMetres { get; }

    public int Revision { get; }

    public string Describe { get; }

    /// <summary>Builds one, or says why not. Returns false for a centre or a
    /// radius that is not a finite number, and for a radius that is not strictly
    /// positive - a zero-radius circle contains nothing but would still report a
    /// bounding circle, which is the shape of an area that silently does
    /// nothing.</summary>
    internal static bool TryCreate(
        NpcPoint centre, float radiusMetres, string? describe, out NpcCircleWorkArea? area, out string reason)
    {
        area = null;

        if (!centre.IsFinite)
        {
            reason = "a circle needs a centre that is a real position";
            return false;
        }

        if (float.IsNaN(radiusMetres) || float.IsInfinity(radiusMetres))
        {
            reason = "a circle needs a radius that is a number";
            return false;
        }

        if (!(radiusMetres > 0f))
        {
            reason = "a circle needs a radius greater than zero";
            return false;
        }

        area = new NpcCircleWorkArea(centre, radiusMetres, describe ?? string.Empty);
        reason = string.Empty;
        return true;
    }

    /// <summary>Horizontal containment, height ignored, inclusive at the edge -
    /// the same test every shipped area makes.</summary>
    public bool Contains(NpcPoint point) =>
        point.IsFinite && BoundingCentre.HorizontalDistanceTo(point) <= BoundingRadiusMetres;

    public override string ToString() =>
        (Describe.Length > 0 ? Describe : "a circle") + " at " + BoundingCentre + ", radius " +
        BoundingRadiusMetres.ToString("0.#", CultureInfo.InvariantCulture) + " m";
}
