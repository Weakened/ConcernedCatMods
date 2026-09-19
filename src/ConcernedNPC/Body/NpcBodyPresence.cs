namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Where one identity's body stands, from a census of what the world
/// has saved and what is loaded here now.
///
/// <b>Zero is unspecified, deliberately.</b> A census that has not run yet and
/// a census that found nothing are different answers, and only one of them
/// permits building a body. Defaulting to either would make a forgotten scan
/// look like a decided one.</summary>
internal enum NpcBodyPresence
{
    Unspecified = 0,

    /// <summary>The scan of saved objects has not finished. Nothing may be
    /// built: a second body for one identity is the failure this census exists
    /// to prevent, and half a scan cannot rule one out.</summary>
    Searching = 1,

    /// <summary>The scan finished and no saved body anywhere in this world
    /// carries this identity. The only state in which a body may be
    /// built.</summary>
    Missing = 2,

    /// <summary>Exactly one body, loaded here and working.</summary>
    Present = 3,

    /// <summary>Exactly one body, saved in ground that is not loaded. It is
    /// never replaced - the player goes to where they left it.</summary>
    NotLoaded = 4,

    /// <summary>More than one body carries this identity. Nothing is destroyed
    /// to resolve it; a person decides.</summary>
    Duplicated = 5,

    /// <summary>The one loaded body's mind latched a fault and is inert. Still
    /// a body, so still never replaced.</summary>
    Faulted = 6,
}
