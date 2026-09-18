namespace TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;

/// <summary>What Gunnar's panel can ask for. A typed command rather than the
/// console's words, so the panel layer carries no command strings at all and
/// the adapter decides how to say them to the haul runtime.</summary>
internal enum HaulPanelCommand
{
    Unspecified = 0,

    /// <summary>Read the runtime's own status text.</summary>
    Status = 1,

    /// <summary>Name the cart the player is looking at and ask to confirm.
    /// </summary>
    Assign = 2,

    /// <summary>Confirm the named cart.</summary>
    Confirm = 3,

    /// <summary>Give the cart back; Gunnar keeps nothing.</summary>
    Release = 4,

    /// <summary>Mark where the player stands as the destination.</summary>
    SetDestination = 5,

    /// <summary>Haul the cart to the marked destination.</summary>
    Go = 6,

    /// <summary>Stop where he is, still hitched.</summary>
    Stop = 7,

    /// <summary>Stop, detach and park on suitable ground.</summary>
    Detach = 8,
}
