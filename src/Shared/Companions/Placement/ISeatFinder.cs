using System.Collections.Generic;

namespace TheConcernedCat.Companions.Placement;

/// <summary>Finds seats around an anchor directly, rather than only as a side
/// effect of probing ground beside them.
///
/// Optional, and separate from <see cref="IPlacementProbe"/> on purpose: a probe
/// that does not offer it keeps the planner's original behaviour exactly.
///
/// Why it exists. Seats used to be noticed only through a ring candidate within
/// a couple of metres of them, and a seat was dropped whenever that ground
/// candidate was rejected - a post beside a spot he would never stand on could
/// hide the chair next to it. And every ring candidate is at least the minimum
/// radius from the anchor, so a stool built inside a small shelter, a metre from
/// the bed, could never be found at all. Seen in the owner's own test shelter:
/// a claimed bed, a fire, a chair, and a companion who never appeared.
/// </summary>
internal interface ISeatFinder
{
    /// <summary>Free seats whose attachment points lie within
    /// <paramref name="radius"/> of <paramref name="center"/>, as samples
    /// positioned at the attachment point and carrying their
    /// <see cref="SeatOffer"/>. In a deterministic order - nearest first, then by
    /// position - so the same world picks the same seat every session. Only what
    /// is loaded; nothing is moved, claimed or written.</summary>
    IReadOnlyList<PlacementProbeSample> FindSeats(WorldPoint center, float radius);
}
