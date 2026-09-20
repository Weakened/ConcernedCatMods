namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>What kind of thing a piece is, as far as camp membership is
/// concerned. <b>The role classifies; this library only counts.</b>
///
/// A library that decided this for itself would have to know what a workbench
/// is called, what a paved path is called and which of the two a player built -
/// which is precisely the knowledge it is forbidden to hold, and precisely the
/// knowledge that goes stale when the game patches. So the role's adapter reads
/// the world object and says which of these it is, and the rule below is the
/// whole of what this library does with the answer.</summary>
internal enum CampPieceClass
{
    /// <summary>Not classified. <b>Excluded.</b> A piece nobody could place in
    /// one of the categories below is not evidence of a camp, and admitting it
    /// is how a boundary grows for reasons nobody can name.</summary>
    Unknown = 0,

    /// <summary>A player-built structural object: walls, floors, roofs,
    /// crafting stations, containers, beds, furniture, fires, torches, gates,
    /// and the structural pieces that hold them up. <b>The only class that is a
    /// member of camp.</b></summary>
    Built = 1,

    /// <summary>A terrain edit of any kind: flattening, raising, digging a
    /// trench, a path, a road, paint. <b>Excluded, deliberately and
    /// completely.</b>
    ///
    /// These are the pieces that make a camp boundary meaningless. A road is a
    /// line of player-made objects running to the next biome; a levelled field
    /// covers ground nobody lives on. Counting either one stretches camp along
    /// every path the player ever walked, and the first symptom is an NPC
    /// standing in a meadow because the meadow is technically inside.</summary>
    TerrainEdit = 2,

    /// <summary>Something the world grew or spawned: a tree, a rock, a ruin, a
    /// grave. <b>Excluded.</b> An NPC anchored beside a forest does not own the
    /// forest.</summary>
    Natural = 3,
}
