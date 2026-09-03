namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The single entry point through which road detection feeds the
/// atlas.
///
/// v1 ROAD SOURCE AUTHORITY (RC8): only successful explicit LOCAL PLAYER
/// road construction — Pathen ⇒ Dirt, Paved ⇒ Paved, captured as
/// <see cref="RoadObservationSource.Construction"/> — may create road atlas
/// data. The passive sources (Traversal, ChunkRecovery) are refused
/// outright: arbitrary terrain paint (native world dirt, spawn areas,
/// sacrificial-stone surroundings, Level Ground side-effect paint) must
/// never become a road. The refusal is enforced HERE, at the single choke
/// point, so no adapter wiring mistake can reintroduce passive creation.
///
/// For the accepted source it still guarantees exact-replay idempotency,
/// and the negative terrain intent mask (DEF-v1.0-005) remains as defense
/// in depth for any hypothetical future non-construction source.</summary>
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

    /// <summary>The most recent observation the atlas actually accepted,
    /// for the `cc_roads align live` diagnostic. Refused or suppressed
    /// observations never overwrite it.</summary>
    public RoadObservation? LastAccepted { get; private set; }

    public bool Observe(in RoadObservation observation, RoadSamplingRules rules, out RoadSegment segment)
    {
        // RC8 STRICT PRODUCT RULE: passive sources create no road data —
        // ever. Any legacy active stroke of that source ends too, so nothing
        // can connect through a refusal.
        if (observation.Source != RoadObservationSource.Construction)
        {
            _atlas.EndStroke(observation.Source);
            segment = default;
            return false;
        }

        // DEF-v1.0-005 defense in depth for future non-construction sources:
        // dirt paint inside explicitly-terraformed ground is a leveling side
        // effect, not a road. Construction never reaches this check with an
        // excluded cell because its op clears its own footprint first, and
        // the source gate above refuses everything else already.
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

        bool segmentProduced = _atlas.RecordSample(
            observation.Source,
            observation.Kind,
            observation.Position,
            rules,
            out segment);

        // RecordSample's bool means "a drawable segment exists", which is
        // false for an accepted stroke-START point. The point was recorded
        // exactly when it is now on recorded ground — the replay pre-check
        // above already proved it was not there before this call.
        if (segmentProduced ||
            _atlas.ContainsPointNear(observation.Kind, observation.Position, ReplayEpsilonMeters))
        {
            LastAccepted = observation;
        }

        return segmentProduced;
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
