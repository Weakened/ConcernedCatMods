using System;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>A container, remembered by where it stands and what kind of thing
/// it is.
///
/// <b>Why not by its id.</b> Because an id does not survive a reload. Valheim
/// gives every saved object a fresh network id in load order when a world comes
/// back, so a number written last night names a different object this morning -
/// and because the new numbers are dense from one, it very likely names some
/// other chest. An NPC acting on the remembered number would help itself to
/// whatever now answers to it. Every product in this repository states this in a
/// comment beside its own key type, and the shipped supply-container designation
/// deals with it by refusing: a key from a previous world load resolves to
/// nothing and the player is told to mark the chest again.
///
/// <b>Why a place works where an id does not.</b> A chest does not move. Its
/// position and its piece type are an identity the game itself preserves across
/// a save, because they are facts about the world rather than references to it.
/// This is the door-permission model, which the owner already approved for the
/// same question about a different world object, and it is deliberately the same
/// shape: a chest broken and rebuilt on the same spot is the same chest and
/// keeps its permission, which is what a player who replaced a rotted chest
/// expects.
///
/// <b>What it is not for.</b> Deciding which container a <i>job</i> is holding
/// mid-transfer - that is an in-session key and an epoch, and it stays that way,
/// because within one world load an id is exact and a place is a tolerance. The
/// place is how permission is found again after a load; the key is how a
/// transfer keeps hold of one chest for three ticks.
///
/// <b>Where it must not be used.</b> A container that moves - one riding a cart
/// - has no place. Minting one for a cart's container would attach a player's
/// permission to a patch of ground the cart happened to be standing on, and give
/// it to whatever is parked there tomorrow. Whether a container is fixed is a
/// fact only the role can see, so the role decides; this type simply cannot tell
/// and says so.</summary>
internal readonly struct NpcContainerPlace : IEquatable<NpcContainerPlace>
{
    /// <summary>How far apart, horizontally, two records may be and still name
    /// the same container. The door tolerance, unchanged: tight enough that two
    /// chests side by side are never confused, loose enough that a rebuilt piece
    /// snapping to the same socket still matches.</summary>
    internal const float MatchMetres = 0.35f;

    /// <summary>How far apart in height. Looser than the horizontal tolerance
    /// for the same reason it is on a door: floors are thin and a piece's
    /// recorded height is not always where you would point.</summary>
    internal const float MatchHeightMetres = 0.5f;

    internal NpcContainerPlace(NpcPoint position, int prefab)
    {
        Position = position;
        Prefab = prefab;
    }

    internal NpcPoint Position { get; }

    /// <summary>The piece's prefab hash, or 0 when the role could not tell.
    /// Two <i>known</i> prefabs that differ are different containers wherever
    /// they stand - a reinforced chest replacing a wooden one on the same spot
    /// is a new container and a new decision. An unknown prefab matches any,
    /// because refusing on a fact nobody could establish would silently forget a
    /// permission the player set.</summary>
    internal int Prefab { get; }

    /// <summary>False for a place that could never match anything: a position
    /// that is not a real position. Kept apart from equality because a
    /// defaulted struct is a perfectly good dictionary key and a hopeless
    /// identity.</summary>
    internal bool IsLocatable => Position.IsFinite;

    /// <summary>Whether two records name the same container. Tolerant on
    /// position, exact on a known prefab.</summary>
    internal bool SamePlaceAs(NpcContainerPlace other)
    {
        if (!IsLocatable || !other.IsLocatable)
        {
            return false;
        }

        if (Prefab != 0 && other.Prefab != 0 && Prefab != other.Prefab)
        {
            return false;
        }

        return Position.HorizontalDistanceTo(other.Position) <= MatchMetres
            && Position.VerticalDistanceTo(other.Position) <= MatchHeightMetres;
    }

    /// <summary>Exact value equality, which is <b>not</b>
    /// <see cref="SamePlaceAs"/>. Two records a centimetre apart are the same
    /// container and different values; the book matches, and a caller storing
    /// these in a set gets exactness. Conflating the two would give a hash set
    /// whose membership test disagreed with its own equality.</summary>
    public bool Equals(NpcContainerPlace other) => Prefab == other.Prefab && Position.Equals(other.Position);

    public override bool Equals(object? obj) => obj is NpcContainerPlace other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Prefab * 397) ^ Position.GetHashCode();
        }
    }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.0},{1:0.0},{2:0.0}",
            Position.X,
            Position.Y,
            Position.Z);
}
