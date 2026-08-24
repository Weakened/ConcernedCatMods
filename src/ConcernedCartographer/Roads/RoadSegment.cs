using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal readonly struct RoadSegment
{
    public RoadSegment(RoadKind kind, Vector3 start, Vector3 end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public RoadKind Kind { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }
}
