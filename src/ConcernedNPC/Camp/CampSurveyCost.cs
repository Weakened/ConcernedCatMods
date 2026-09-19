using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>What one survey actually cost, counted rather than estimated.
///
/// <b>Why this is a returned value and not a log line.</b> "No full-world
/// scan, no per-frame hull recomputation" is a contract, and a contract nothing
/// measures is a hope. Every number here is incremented at the place the work
/// happens, so a test can assert that a piece 500 m away was never so much as
/// looked at - which is the only way to prove the boundary cannot be stretched
/// by something the survey never visits.
///
/// <b>The stated cost.</b> A recomputed survey costs, in
/// <see cref="NeighbourChecks"/>, roughly the number of member pieces times the
/// number of pieces sharing their nine neighbouring grid cells - that is,
/// linear in the size of <i>camp</i> and independent of how many pieces exist
/// elsewhere in the world, because a cell no member reaches is never visited.
/// The hull is <c>O(m log m)</c> over the <see cref="Members"/> found. A survey
/// that was not recomputed costs nothing at all: <see cref="Recomputed"/> is
/// false and every counter is zero.</summary>
internal readonly struct CampSurveyCost
{
    internal CampSurveyCost(
        bool recomputed, int cellsVisited, int piecesConsidered, int neighbourChecks, int members,
        int hullPoints, bool truncated)
    {
        Recomputed = recomputed;
        CellsVisited = cellsVisited;
        PiecesConsidered = piecesConsidered;
        NeighbourChecks = neighbourChecks;
        Members = members;
        HullPoints = hullPoints;
        Truncated = truncated;
    }

    /// <summary>False only on the empty snapshot, which no survey produced.
    ///
    /// A <i>cached</i> survey hands back the same snapshot object it handed
    /// back before, cost and all, so a snapshot cannot say whether the call
    /// that returned it did any work. <c>CampRegistry.Recomputations</c> is
    /// where that is counted.</summary>
    internal bool Recomputed { get; }

    /// <summary>Grid cells opened. Bounded by camp's own footprint plus its
    /// one-cell fringe; never by the world's.</summary>
    internal int CellsVisited { get; }

    /// <summary>Pieces taken out of a visited cell and looked at, including the
    /// ones rejected for being too far to link. <b>Never the registry's total
    /// count</b> unless every piece really is in camp.</summary>
    internal int PiecesConsidered { get; }

    /// <summary>Distance comparisons performed.</summary>
    internal int NeighbourChecks { get; }

    /// <summary>Pieces that ended up in camp.</summary>
    internal int Members { get; }

    /// <summary>Points on the perimeter that came out.</summary>
    internal int HullPoints { get; }

    /// <summary>Growth stopped at <see cref="CampSurveyOptions.MaxMembers"/>.
    /// The snapshot is a real camp, and a small one: incomplete, not wrong.
    /// </summary>
    internal bool Truncated { get; }

    internal static CampSurveyCost Cached => default;

    public override string ToString()
    {
        if (!Recomputed)
        {
            return "cached";
        }

        return Members.ToString(CultureInfo.InvariantCulture) + " members from " +
            PiecesConsidered.ToString(CultureInfo.InvariantCulture) + " pieces in " +
            CellsVisited.ToString(CultureInfo.InvariantCulture) + " cells, " +
            NeighbourChecks.ToString(CultureInfo.InvariantCulture) + " distance checks, " +
            HullPoints.ToString(CultureInfo.InvariantCulture) + " perimeter points" +
            (Truncated ? ", truncated" : string.Empty);
    }
}
