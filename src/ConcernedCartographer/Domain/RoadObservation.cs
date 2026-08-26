namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>A single source-neutral road sighting: some source saw road
/// paint of a kind at a world position. This is the only contract between
/// detection code and the atlas.</summary>
internal readonly struct RoadObservation
{
    public RoadObservation(RoadObservationSource source, RoadKind kind, RoadPoint position)
    {
        Source = source;
        Kind = kind;
        Position = position;
    }

    public RoadObservationSource Source { get; }
    public RoadKind Kind { get; }
    public RoadPoint Position { get; }

    public override string ToString()
    {
        return $"{Source}/{Kind} @ {Position}";
    }
}
