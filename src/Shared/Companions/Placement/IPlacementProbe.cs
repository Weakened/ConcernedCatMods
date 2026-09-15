namespace TheConcernedCat.Companions.Placement;

/// <summary>Inspects one candidate position in the loaded world.
///
/// Implementations look only at what is already loaded around the player. There
/// is no world-file read, no terrain edit, no pathfinding, and no moving of
/// anything the player owns - a probe answers a question and changes
/// nothing.</summary>
internal interface IPlacementProbe
{
    /// <summary>Describes <paramref name="position"/>. When the area is not
    /// loaded, return <see cref="PlacementProbeSample.NotLoaded"/> rather than
    /// guessing: the planner defers, and a deferred companion appears a moment
    /// later instead of appearing in the wrong place.</summary>
    PlacementProbeSample Probe(WorldPoint position);
}
