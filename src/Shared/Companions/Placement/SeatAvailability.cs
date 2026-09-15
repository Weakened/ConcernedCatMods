namespace TheConcernedCat.Companions.Placement;

/// <summary>Whether a candidate position offers seating the companion may use.
///
/// Using a seat here means adopting a sitting pose at that spot and nothing
/// else. The companion never claims the seat, never sends an attachment
/// message, and never takes it from anyone: a seat with a real occupant is
/// reported as <see cref="Occupied"/> and the ground is used instead.</summary>
internal enum SeatAvailability
{
    /// <summary>No seat here.</summary>
    None = 0,

    /// <summary>A seat is present and nobody is using it.</summary>
    Free = 1,

    /// <summary>A seat is present but someone is in it. The companion yields.</summary>
    Occupied = 2,

    /// <summary>Seating exists nearby but this build cannot yet confirm that
    /// posing on it is safe. Treated exactly like <see cref="None"/> until the
    /// integration is verified in game - a documented gap beats a guess that
    /// puts a figure floating over a bench.</summary>
    Unverified = 3,
}
