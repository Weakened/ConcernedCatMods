using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>One world object, as camp needs to see it: a name for this world
/// load, a place, and which of the three classes it is.
///
/// <b>Why it is epoch-scoped.</b> <see cref="Key"/> is whatever the role uses
/// to name a world object, and every one of those is renumbered when a world
/// loads. A registry that kept pieces across a load would be holding names that
/// now point at other things - a door, somebody's boat - and would build a camp
/// out of them. So a piece carries the load its name was minted in, and the
/// registry refuses one from any other.</summary>
internal readonly struct CampPiece : INpcEpochScoped
{
    internal CampPiece(string? key, NpcPoint position, CampPieceClass pieceClass, NpcWorldEpoch epoch)
    {
        Key = key ?? string.Empty;
        Position = position;
        Class = pieceClass;
        Epoch = epoch;
    }

    /// <summary>How the role names this object within this world load. Compared
    /// only for equality, never parsed.</summary>
    internal string Key { get; }

    internal NpcPoint Position { get; }

    internal CampPieceClass Class { get; }

    public NpcWorldEpoch Epoch { get; }

    /// <summary><b>The membership rule, in one line.</b> Player-built
    /// structural objects are camp; terrain edits of every kind and unrelated
    /// natural objects are not.</summary>
    internal bool IsMember => Class == CampPieceClass.Built;

    /// <summary>Enough of a piece to be usable at all: it has a name, a real
    /// position, and a world load its name means something in.</summary>
    internal bool IsWellFormed => Key.Length != 0 && Position.IsFinite && !Epoch.IsUnknown;

    public override string ToString() => Class + " " + Key + " at " + Position;
}
