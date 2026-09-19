using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>One candidate anchor, as a role offers it: what it is, where it is,
/// and how sure the role is that it is still there.
///
/// <b>An offer is not an anchor.</b> A role reads three of these and the
/// resolver picks one, so the ordering rule lives in one place and every role
/// gets the same one. A role that picked for itself would be re-implementing
/// the ordering, and the two shipped implementations of it already differ.
/// </summary>
internal readonly struct CampAnchorOffer
{
    private CampAnchorOffer(CampAnchorKind kind, NpcPoint position, CampAnchorValidity validity)
    {
        Kind = kind;
        Position = position;
        Validity = validity;
    }

    internal CampAnchorKind Kind { get; }

    internal NpcPoint Position { get; }

    internal CampAnchorValidity Validity { get; }

    /// <summary>Nothing offered. Reads as <see cref="CampAnchorValidity.Gone"/>
    /// rather than <see cref="CampAnchorValidity.Unknown"/> only when the role
    /// says so - see <see cref="Absent"/> and <see cref="Unreadable"/>, which
    /// exist so a caller cannot express the difference by accident.</summary>
    internal static CampAnchorOffer None =>
        new CampAnchorOffer(CampAnchorKind.None, default, CampAnchorValidity.Gone);

    /// <summary>There, at this point, now.</summary>
    internal static CampAnchorOffer At(CampAnchorKind kind, NpcPoint position)
    {
        if (kind == CampAnchorKind.None || !position.IsFinite)
        {
            return None;
        }

        return new CampAnchorOffer(kind, position, CampAnchorValidity.Valid);
    }

    /// <summary>Positively not there: the bed was destroyed, the claim went to
    /// somebody else, the settlement anchor was cleared. This is the answer
    /// that gives an anchor up, so a role says it deliberately.</summary>
    internal static CampAnchorOffer Absent(CampAnchorKind kind) =>
        new CampAnchorOffer(kind, default, CampAnchorValidity.Gone);

    /// <summary>The role could not tell - the zone is not loaded, the profile
    /// could not be read, the world is still coming up. Keeps whatever anchor
    /// is already held rather than demoting it.</summary>
    internal static CampAnchorOffer Unreadable(CampAnchorKind kind) =>
        new CampAnchorOffer(kind, default, CampAnchorValidity.Unknown);

    /// <summary>Usable as an anchor right now: read, believed, and somewhere a
    /// distance can be measured from.</summary>
    internal bool IsUsable =>
        Kind != CampAnchorKind.None && Validity == CampAnchorValidity.Valid && Position.IsFinite;

    public override string ToString() => Kind + " " + Validity + " " + Position;
}
