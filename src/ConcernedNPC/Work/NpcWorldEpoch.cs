using System;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Which world load an in-session id belongs to.
///
/// <b>What it guarantees: that a saved reference to a world object is never
/// mistaken for a live one.</b> When a world loads, every saved object is given
/// a fresh network id from a counter that starts over. So the id that named the
/// player's supply chest yesterday is, today, overwhelmingly likely to name a
/// different object - a tree, a door, somebody's boat. An NPC that remembered
/// the number would take wood out of whatever now answers to it. Every product
/// in this repository states this in a comment beside its own id type; this is
/// the one place it is a type.
///
/// An id is therefore only usable with the epoch it was minted in. Comparing
/// epochs is how "this key names nothing any more" becomes a check instead of a
/// hope, and <see cref="IsUnknown"/> resolves nothing at all - an epoch nobody
/// set refuses every id rather than matching them all.
///
/// <b>Which form this library picked, and what the roles must do about it.</b>
/// The shipped products use two incompatible epoch representations - a
/// <c>Guid</c> per world load in the collection, haul and custody layers, and a
/// short text token in the designation and fuel layers. This library uses one,
/// the <c>Guid</c>, because it cannot be confused with a name, cannot be
/// half-parsed, and has an unambiguous "nobody set this" value. A role whose
/// existing epoch is text converts at the boundary, and that conversion is the
/// role's, not this library's: nothing here reads or writes an epoch to
/// disk.</summary>
internal readonly struct NpcWorldEpoch : IEquatable<NpcWorldEpoch>
{
    internal NpcWorldEpoch(Guid value)
    {
        Value = value;
    }

    /// <summary>The epoch nobody set. Matches nothing, including itself as a
    /// key: an id carrying it is refused rather than accepted everywhere.
    /// </summary>
    internal static NpcWorldEpoch Unknown => default;

    internal Guid Value { get; }

    /// <summary>True when no epoch was set. Fail closed: treat every id as
    /// stale.</summary>
    internal bool IsUnknown => Value == Guid.Empty;

    /// <summary>Whether an id minted in <paramref name="other"/> may be used
    /// now. False whenever either side is unknown, so a missing epoch never
    /// resolves anything.</summary>
    internal bool Matches(NpcWorldEpoch other) => !IsUnknown && Value == other.Value;

    public bool Equals(NpcWorldEpoch other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is NpcWorldEpoch other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => IsUnknown ? "<unknown epoch>" : Value.ToString("N");
}
