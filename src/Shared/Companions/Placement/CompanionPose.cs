namespace TheConcernedCat.Companions.Placement;

/// <summary>How the companion is presented once placed.</summary>
internal enum CompanionPose
{
    /// <summary>Sitting on the ground. Always available, and the fallback
    /// whenever anything better is unconfirmed.</summary>
    SitOnGround = 0,

    /// <summary>Sitting on the ground within the warmth of a fire.</summary>
    SitByFire = 1,

    /// <summary>Sitting on a free seat, as a local visual pose only.</summary>
    SitOnSeat = 2,
}
