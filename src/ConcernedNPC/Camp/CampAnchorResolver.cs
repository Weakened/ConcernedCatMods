using System;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>The one place the anchor ordering is written down.
///
/// <b>The rule, in the owner's order.</b> The player's current valid respawn or
/// home bed; else the established settlement anchor; else the world's start, as
/// a temporary fallback before a residence exists. Never a distant arbitrary
/// structure, adopted silently or otherwise - there is no branch here that
/// looks at the pieces at all, which is what makes that promise structural
/// rather than a matter of care.
///
/// <b>The part that is easy to get wrong.</b> An offer this library cannot
/// verify does not demote the anchor: if the bed cannot be read and the anchor
/// already is that bed, the bed is kept. Only
/// <see cref="CampAnchorValidity.Gone"/> gives one up. Without that, a player
/// sailing far from home would have every companion's camp collapse to the
/// world's start and then re-expand on arrival, moving every boundary twice per
/// voyage.
///
/// <b>What it deliberately does not do: remember.</b> The previous anchor is a
/// parameter, not a field. A resolver that held state would be a second place
/// camp lives, and the registry already is one.</summary>
internal static class CampAnchorResolver
{
    /// <summary>Picks this tick's anchor.</summary>
    /// <param name="previous">What was held until now;
    /// <see cref="CampAnchor.None"/> at the start of a world load.</param>
    /// <param name="source">The role's three offers, read now.</param>
    internal static CampAnchor Resolve(CampAnchor previous, ICampAnchorSource source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        CampAnchor fromBed = Consider(previous, Read(source, CampAnchorKind.PlayerBed));
        if (fromBed.HasAnchor)
        {
            return fromBed;
        }

        CampAnchor fromSettlement = Consider(previous, Read(source, CampAnchorKind.Settlement));
        if (fromSettlement.HasAnchor)
        {
            return fromSettlement;
        }

        CampAnchor fromSpawn = Consider(previous, Read(source, CampAnchorKind.WorldSpawn));
        if (fromSpawn.HasAnchor)
        {
            return fromSpawn;
        }

        // Nothing could be had. Not "wherever there are buildings": unknown.
        return CampAnchor.None;
    }

    /// <summary>One candidate, judged against what is already held. Returns an
    /// anchor when this candidate decides the answer, and
    /// <see cref="CampAnchor.None"/> when the resolver should look further
    /// down.</summary>
    private static CampAnchor Consider(CampAnchor previous, CampAnchorOffer offer)
    {
        if (offer.IsUsable)
        {
            return new CampAnchor(offer.Kind, offer.Position);
        }

        if (offer.Validity == CampAnchorValidity.Unknown
            && previous.HasAnchor
            && previous.Kind == offer.Kind)
        {
            // Cannot be read, and it is what we already have: keep it. This is
            // the whole reason the validity is three-way.
            return previous;
        }

        return CampAnchor.None;
    }

    /// <summary>Reads one offer, treating a source that throws or answers about
    /// the wrong kind as unreadable rather than absent. A role's reader touches
    /// the game, and a game read that fails is exactly the case
    /// <see cref="CampAnchorValidity.Unknown"/> is for.</summary>
    private static CampAnchorOffer Read(ICampAnchorSource source, CampAnchorKind kind)
    {
        CampAnchorOffer offer;
        try
        {
            switch (kind)
            {
                case CampAnchorKind.PlayerBed:
                    offer = source.PlayerBed;
                    break;
                case CampAnchorKind.Settlement:
                    offer = source.Settlement;
                    break;
                default:
                    offer = source.WorldSpawn;
                    break;
            }
        }
        catch (Exception)
        {
            return CampAnchorOffer.Unreadable(kind);
        }

        if (offer.Kind != kind)
        {
            // A source that answers about another kind has told us nothing
            // about this one. Never silently accepted as this one: that is how
            // a settlement anchor would end up standing in for a bed.
            return CampAnchorOffer.Unreadable(kind);
        }

        return offer;
    }
}
