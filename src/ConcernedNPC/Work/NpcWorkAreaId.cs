using System;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>What a work area is called, for as long as it exists - across
/// ticks, across a reload, across a session.
///
/// <b>Two halves, and both come from outside.</b> The provider half says who
/// knows how to rebuild this shape; the key half says which of that provider's
/// areas this is. Neither is minted here, and there is no default for either:
/// this library owns no durable name, for exactly the reason
/// <c>NpcBodyContract</c> owns no prefab name and <c>INpcDataPaths</c> owns no
/// file name. An id lands in whatever a role persists, so a value this package
/// invented would be a durable name this package owned, and the first rename
/// would orphan every area a player had marked.
///
/// <b>Why not an enum.</b> The shipped collection order has one
/// (<c>WorkScopeSource</c>: harvest designation, default camp circle, and a
/// third value reserved for an area supplied by a map product that has never
/// had a provider). An enum cannot gain a value from another assembly, which is
/// the whole requirement here: a mod that is not installed today registers a
/// richer shape tomorrow, and nothing in this package recompiles. A role that
/// persists that enum keeps persisting it and maps it to a provider id at its
/// own boundary - which is why adopting this costs no migration.
///
/// <b>What it is not.</b> Not a world object's id. Nothing here is renumbered
/// by a load, because nothing here is allocated by the game: both halves are
/// text a person or a role chose, and the geometry travels beside them in
/// <see cref="NpcWorkAreaDescriptor"/>.</summary>
public readonly struct NpcWorkAreaId : IEquatable<NpcWorkAreaId>
{
    internal NpcWorkAreaId(string providerId, string key)
    {
        ProviderId = providerId ?? string.Empty;
        Key = key ?? string.Empty;
    }

    /// <summary>Who can rebuild this area's shape. Matched against
    /// <see cref="INpcWorkAreaProvider.ProviderId"/>, ordinally and exactly: a
    /// near miss is a provider that is not installed, and that is a refusal
    /// rather than a search for something similar.</summary>
    internal string ProviderId { get; }

    /// <summary>Which of that provider's areas. Opaque here - this library
    /// never parses it, derives anything from it, or gives it a shape.</summary>
    internal string Key { get; }

    /// <summary>False when either half is missing. A half-named area is never
    /// resolved, because the resolution would have to guess the other half and
    /// the only thing it could guess is "whatever is registered", which is how
    /// an NPC ends up working in somebody else's area.</summary>
    internal bool IsNamed => ProviderId.Length > 0 && Key.Length > 0;

    public bool Equals(NpcWorkAreaId other) =>
        string.Equals(ProviderId, other.ProviderId, StringComparison.Ordinal)
        && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is NpcWorkAreaId other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(ProviderId) * 397)
                ^ StringComparer.Ordinal.GetHashCode(Key);
        }
    }

    public override string ToString() => IsNamed ? ProviderId + "/" + Key : "<unnamed area>";
}
