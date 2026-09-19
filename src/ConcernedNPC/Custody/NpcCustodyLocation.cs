using System;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>A place, and which instance of it, within one world load.
///
/// <b>Epoch-scoped for the same reason a container reservation is.</b> The key
/// names a world object, and a world load renumbers every one of them. A
/// holding recorded against a chest yesterday names a different object today -
/// possibly a tree, possibly somebody's boat - so a location carries the load
/// it was named in and comparisons that cross a load fail rather than
/// coincide.</summary>
internal readonly struct NpcCustodyLocation : IEquatable<NpcCustodyLocation>, INpcEpochScoped
{
    internal NpcCustodyLocation(NpcCustodyPlace place, string? key, NpcWorldEpoch epoch)
    {
        Place = place;
        Key = key ?? string.Empty;
        Epoch = epoch;
    }

    internal NpcCustodyPlace Place { get; }

    /// <summary>Whatever names the instance within its place: the NPC's own
    /// key, a container's key, a traced drop's id. Opaque here.</summary>
    internal string Key { get; }

    public NpcWorldEpoch Epoch { get; }

    internal bool IsSpecified => Place != NpcCustodyPlace.Unspecified;

    /// <summary><b>Whether the job may move material out of here again.</b>
    /// Delivered, handed-over and lost units stay where the record put them:
    /// the job is done with them, and a later change to that chest is the
    /// player's. Encoded once, here, rather than as a condition each caller
    /// gets right.</summary>
    internal bool MayLeave =>
        Place == NpcCustodyPlace.Ground || Place == NpcCustodyPlace.Carried || Place == NpcCustodyPlace.Stored;

    /// <summary>Whether a count here can be compared against the record.
    ///
    /// Only the places the NPC itself is responsible for. A delivery chest also
    /// holds the player's own things and anything they do to it afterwards is
    /// theirs, so delivered credit is never checked against it and never
    /// reversed; handed-over and lost are dispositions rather than places; and
    /// a traced drop's id means nothing once the world has been reloaded.
    /// </summary>
    internal bool IsObservable =>
        Place == NpcCustodyPlace.Carried || Place == NpcCustodyPlace.Stored;

    public bool Equals(NpcCustodyLocation other) =>
        Place == other.Place
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Epoch.Equals(other.Epoch);

    public override bool Equals(object? obj) => obj is NpcCustodyLocation other && Equals(other);

    public override int GetHashCode() =>
        unchecked(((int)Place * 397) ^ StringComparer.Ordinal.GetHashCode(Key ?? string.Empty) ^ Epoch.GetHashCode());

    public override string ToString() => Place + (Key.Length == 0 ? string.Empty : ":" + Key);
}
