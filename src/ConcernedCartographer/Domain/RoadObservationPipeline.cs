namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The single entry point through which every detection source
/// (traversal, construction capture, chunk recovery) feeds the atlas.
/// Guarantees source-neutral semantics: per-source stroke building, shared
/// duplicate suppression across sources, and exact-replay idempotency.
/// Enforces negative terrain intent (DEF-v1.0-005): Dirt sightings by the
/// passive sources are refused inside the world's exclusion mask.</summary>
internal sealed class RoadObservationPipeline
{
    // An observation replayed with identical coordinates must never grow the
    // atlas, even when the player disables configurable duplicate
    // suppression. Far below the 0.5 m minimum point spacing, so it can never
    // reject a genuine new sample.
    private const float ReplayEpsilonMeters = 0.05f;

    private readonly RoadAtlas _atlas;
    private readonly TerrainIntentMask? _terrainIntent;

    public RoadObservationPipeline(RoadAtlas atlas, TerrainIntentMask? terrainIntent = null)
    {
        _atlas = atlas;
        _terrainIntent = terrainIntent;
    }

    public bool Observe(in RoadObservation observation, RoadSamplingRules rules, out RoadSegment segment)
    {
        // DEF-v1.0-005: dirt paint inside explicitly-terraformed ground is a
        // leveling side effect, not a road. Only the passive sources are
        // gated — an explicit Pathen/Paved op (Construction) is deliberate
        // road building and has already cleared its own footprint. The
        // active stroke ends so no connector is ever drawn across the
        // excluded ground.
        if (_terrainIntent is not null &&
            observation.Kind == RoadKind.Dirt &&
            observation.Source != RoadObservationSource.Construction &&
            _terrainIntent.IsExcluded(observation.Position.X, observation.Position.Z))
        {
            _atlas.EndStroke(observation.Source);
            segment = default;
            return false;
        }

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
