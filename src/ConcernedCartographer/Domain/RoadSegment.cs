namespace TheConcernedCat.ConcernedCartographer.Roads;

internal readonly struct RoadSegment
{
    public RoadSegment(RoadKind kind, RoadPoint start, RoadPoint end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public RoadKind Kind { get; }
    public RoadPoint Start { get; }
    public RoadPoint End { get; }
}
