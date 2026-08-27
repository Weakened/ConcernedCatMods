using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadStroke
{
    public RoadStroke(Guid id, RoadKind kind)
    {
        Id = id;
        Kind = kind;
    }

    public Guid Id { get; }
    public RoadKind Kind { get; }
    public List<RoadPoint> Points { get; } = new();
}
