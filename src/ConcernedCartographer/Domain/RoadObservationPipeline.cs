namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The single entry point through which every detection source
/// (traversal, construction capture, chunk recovery) feeds the atlas.
/// Guarantees source-neutral semantics: per-source stroke building, shared
/// duplicate suppression across sources, and exact-replay idempotency.</summary>
internal sealed class RoadObservationPipeline
{
    // An observation replayed with identical coordinates must never grow the
    // atlas, even when the player disables configurable duplicate
    // suppression. Far below the 0.5 m minimum point spacing, so it can never
    // reject a genuine new sample.
    private const float ReplayEpsilonMeters = 0.05f;

    private readonly RoadAtlas _atlas;

    public RoadObservationPipeline(RoadAtlas atlas)
    {
        _atlas = atlas;
    }

    public bool Observe(in RoadObservation observation, RoadSamplingRules rules, out RoadSegment segment)
    {
        if (_atlas.ContainsPointNear(observation.Kind, observation.Position, ReplayEpsilonMeters))
        {
            segment = default;
            return false;
        }

        return _atlas.RecordSample(
            observation.Source,
            observation.Kind,
            observation.Position,
            rules,
            out segment);
    }

    /// <summary>Ends one source's active stroke, e.g. when that source loses
    /// its signal (player dies, probe fails). Other sources are unaffected.</summary>
    public void EndStroke(RoadObservationSource source)
    {
        _atlas.EndStroke(source);
    }

    /// <summary>Ends every source's active stroke, e.g. on logout or world
    /// switch, so nothing draws a connector across the discontinuity.</summary>
    public void EndAllStrokes()
    {
        _atlas.EndAllStrokes();
    }
}
