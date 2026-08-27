using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadStroke
{
    public RoadStroke(Guid id, RoadKind kind, RoadObservationSource source = RoadObservationSource.Traversal)
    {
        Id = id;
        Kind = kind;
        Source = source;
    }

    public Guid Id { get; }
    public RoadKind Kind { get; }

    /// <summary>Origin metadata: which observation source created this
    /// stroke. Pre-v2 sidecar data has no source column and loads as
    /// Traversal.</summary>
    public RoadObservationSource Source { get; }

    /// <summary>Player-controlled visibility (repair tools). A hidden stroke
    /// is not rendered but stays in the atlas and keeps suppressing
    /// re-recording of its ground; delete it to allow re-recording.</summary>
    public bool Hidden { get; set; }

    public List<RoadPoint> Points { get; } = new();
}
