namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>Where a role reads the three anchor candidates from.
///
/// <b>Why all three, every time, rather than "give me the best one".</b> The
/// resolver has to see the difference between "there is no bed" and "I could
/// not read the bed", and it has to see it for each candidate separately,
/// because the second one keeps an anchor the first one gives up. A source that
/// answered with one winner would have made that decision itself, differently
/// in each role, which is the state this library exists to end.
///
/// Each property is read fresh at the moment the resolver asks, exactly as a
/// container's permission is: a bed can be destroyed, a zone can unload and a
/// settlement anchor can be cleared between two surveys.</summary>
internal interface ICampAnchorSource
{
    /// <summary>The player's current respawn or home bed.</summary>
    CampAnchorOffer PlayerBed { get; }

    /// <summary>The established settlement anchor, for a role that keeps one.
    /// A role with no such concept answers
    /// <see cref="CampAnchorOffer.Absent"/>, which is honest: there is no
    /// settlement, as opposed to one it cannot see.</summary>
    CampAnchorOffer Settlement { get; }

    /// <summary>The world's start point. The temporary fallback, and the one
    /// offer that is nearly always readable.</summary>
    CampAnchorOffer WorldSpawn { get; }
}
