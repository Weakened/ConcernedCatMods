using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>Where camp is measured from. Everything else about camp - which
/// pieces belong to it, where its edge is, where inside it is safe to stand -
/// is derived from this one point, which is why choosing it is a rule of its
/// own rather than a step in the survey.</summary>
internal readonly struct CampAnchor
{
    internal CampAnchor(CampAnchorKind kind, NpcPoint position)
    {
        Kind = kind == CampAnchorKind.None || !position.IsFinite ? CampAnchorKind.None : kind;
        Position = Kind == CampAnchorKind.None ? default : position;
    }

    /// <summary>No anchor: camp is unknown. Surveys over it find nothing, which
    /// is the point - an NPC with no home does not adopt one.</summary>
    internal static CampAnchor None => default;

    internal CampAnchorKind Kind { get; }

    internal NpcPoint Position { get; }

    internal bool HasAnchor => Kind != CampAnchorKind.None;

    /// <summary>True while camp is anchored on the world's start because no
    /// residence exists yet. A role may say so to a player, and should expect
    /// it to stop being true without being told.</summary>
    internal bool IsProvisional => Kind == CampAnchorKind.WorldSpawn;

    /// <summary>Whether this is the same anchor, to within a metre. Used to
    /// decide whether a cached survey is still about the right place; a bed
    /// read with a hair of float noise is not a reason to recompute a hull.
    /// </summary>
    internal bool SamePlaceAs(CampAnchor other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        if (!HasAnchor)
        {
            return true;
        }

        return Position.HorizontalDistanceTo(other.Position) <= 1f
            && Position.VerticalDistanceTo(other.Position) <= 1f;
    }

    public override string ToString() => HasAnchor ? Kind + " at " + Position : "<no anchor>";
}
