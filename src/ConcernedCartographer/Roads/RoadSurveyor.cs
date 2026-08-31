using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The traversal observation source: samples the terrain beneath
/// the local player and feeds sightings into the shared pipeline.</summary>
internal sealed class RoadSurveyor
{
    private readonly CartographerSettings _settings;
    private readonly GroundPaintProbe _probe;
    private readonly RoadObservationPipeline _pipeline;
    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;
    private float _elapsed;

    /// <summary>The most recent traversal sampling attempt, for the
    /// `cc_roads align live` diagnostic.</summary>
    public readonly struct TraversalSample
    {
        public TraversalSample(Vector3 position, bool classified, RoadKind kind, bool accepted)
        {
            Position = position;
            Classified = classified;
            Kind = kind;
            Accepted = accepted;
        }

        public Vector3 Position { get; }

        /// <summary>Whether the paint probe saw road paint at the sample.</summary>
        public bool Classified { get; }

        public RoadKind Kind { get; }

        /// <summary>Whether the pipeline recorded the sample into the atlas
        /// (false also covers duplicate-suppressed re-walks).</summary>
        public bool Accepted { get; }
    }

    public TraversalSample? LatestSample { get; private set; }

    public RoadSurveyor(
        CartographerSettings settings,
        GroundPaintProbe probe,
        RoadObservationPipeline pipeline,
        ManualLogSource log)
    {
        _settings = settings;
        _probe = probe;
        _pipeline = pipeline;
        _log = log;
        _rateLimited = new RateLimitedLog(log, 5f);
    }

    public bool Tick(float deltaTime, out RoadSegment segment)
    {
        segment = default;
        _elapsed += deltaTime;
        if (_elapsed < _settings.SampleIntervalSeconds.Value)
        {
            return false;
        }

        _elapsed = 0f;
        Player player = Player.m_localPlayer;
        if (player is null || player.IsDead())
        {
            _pipeline.EndStroke(RoadObservationSource.Traversal);
            return false;
        }

        Vector3 position = player.transform.position;
        if (!_probe.TryClassify(position, out RoadKind kind))
        {
            LatestSample = new TraversalSample(position, classified: false, default, accepted: false);
            _pipeline.EndStroke(RoadObservationSource.Traversal);
            return false;
        }

        var rules = new RoadSamplingRules(
            _settings.MinimumPointSpacingMeters.Value,
            _settings.MaximumStrokeGapMeters.Value,
            _settings.DuplicateSuppressionMeters.Value);

        var observation = new RoadObservation(
            RoadObservationSource.Traversal,
            kind,
            new RoadPoint(position.x, position.y, position.z));
        bool recorded = _pipeline.Observe(observation, rules, out segment);
        // "Accepted" answers the diagnostic question "is the ground I am
        // standing on in the atlas?", which is also true for a stroke-start
        // point that produced no drawable segment yet.
        bool pointAccepted = recorded ||
            (_pipeline.LastAccepted is { } last &&
             last.Position.X == observation.Position.X &&
             last.Position.Z == observation.Position.Z);
        LatestSample = new TraversalSample(position, classified: true, kind, pointAccepted);

        if (recorded && _settings.DebugLogging.Value)
        {
            _rateLimited.Info("segment-recorded", $"Recorded {observation} segment from {segment.Start} to {segment.End}.");
        }

        return recorded;
    }

    public void EndStroke()
    {
        _pipeline.EndStroke(RoadObservationSource.Traversal);
    }
}
