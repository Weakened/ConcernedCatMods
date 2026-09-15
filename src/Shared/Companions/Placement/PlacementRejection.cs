using System;

namespace TheConcernedCat.Companions.Placement;

/// <summary>Why a candidate position is unusable. A probe may report several at
/// once, which keeps the diagnostic honest: "water and occupied" is more useful
/// than whichever check happened to run first.</summary>
[Flags]
internal enum PlacementRejection
{
    None = 0,

    /// <summary>The area is not loaded, so nothing is known about it. Distinct
    /// from a rejection: the planner defers instead of ruling the spot out.</summary>
    NotLoaded = 1 << 0,

    /// <summary>In or over water.</summary>
    Water = 1 << 1,

    /// <summary>No solid ground to stand on.</summary>
    Unsupported = 1 << 2,

    /// <summary>Too steep to sit on.</summary>
    TooSteep = 1 << 3,

    /// <summary>Something already occupies the space, including another actor.</summary>
    Occupied = 1 << 4,

    /// <summary>Inside a fire's hazard radius. Sitting <i>near</i> a fire is the
    /// goal; sitting <i>in</i> one is not.</summary>
    Fire = 1 << 5,

    /// <summary>In a doorway or other passage that must stay clear.</summary>
    Doorway = 1 << 6,

    /// <summary>On or against a bed.</summary>
    Bed = 1 << 7,

    /// <summary>Within protected shrine or start-location geometry.</summary>
    Shrine = 1 << 8,

    /// <summary>Outside the allowed distance band from the anchor.</summary>
    OutOfRange = 1 << 9,
}
