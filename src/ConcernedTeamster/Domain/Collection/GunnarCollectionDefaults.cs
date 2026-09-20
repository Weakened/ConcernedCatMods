namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Every default of Gunnar's collection runtime in one place, so the
/// settings binding only reads them and a test can lock them (#381).
///
/// Collection is <b>off</b> by default, for the same reason hauling is
/// (docs/settlement/cart-and-collection/DECISIONS.md D3): installing Concerned
/// Teamster for its telemetry must never enrol a player in a feature that
/// changes real resources. It is a separate switch from hauling's because it is
/// a separate capability under a separate owner decision (2026-09-19), and a
/// player who wants a cart pulled has not thereby asked for anything to be
/// picked up.</summary>
internal static class GunnarCollectionDefaults
{
    /// <summary><c>Workers/GunnarCollectionEnabled</c>. Off until a person turns
    /// it on.</summary>
    public const bool CollectionEnabled = false;
}
