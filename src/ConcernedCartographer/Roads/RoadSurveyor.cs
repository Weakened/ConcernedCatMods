using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Traversal sampler, diagnostics-only since RC8: it still probes
/// the terrain beneath the local player on the configured cadence, but its
/// samples feed ONLY the `cc_roads align live` diagnostic. Road atlas data
/// is created exclusively by explicit local-player Pathen/Paved
/// construction (see <see cref="RoadObservationPipeline"/>); walking any
/// painted ground never records anything.</summary>
internal sealed class RoadSurveyor
{
    private readonly CartographerSettings _settings;
    private readonly GroundPaintProbe _probe;
    private readonly RoadAtlas _atlas;
    private float _elapsed;

    /// <summary>The most recent traversal sampling attempt, for the
    /// `cc_roads align live` diagnostic.</summary>
    public readonly struct TraversalSample
    {
        public TraversalSample(Vector3 position, bool classified, RoadKind kind, bool onRecordedRoad)
        {
            Position = position;
            Classified = classified;
            Kind = kind;
            OnRecordedRoad = onRecordedRoad;
        }

        public Vector3 Position { get; }

        /// <summary>Whether the paint probe saw road paint at the sample.</summary>
        public bool Classified { get; }

        public RoadKind Kind { get; }

        /// <summary>Whether recorded road geometry of the classified kind
        /// passes near the sample — i.e. the ground under the player is in
        /// the atlas (within the A-verdict tolerance).</summary>
        public bool OnRecordedRoad { get; }
    }

    public TraversalSample? LatestSample { get; private set; }

    public RoadSurveyor(
        CartographerSettings settings,
        GroundPaintProbe probe,
        RoadAtlas atlas,
        ManualLogSource log)
    {
        _settings = settings;
        _probe = probe;
        _atlas = atlas;
        _ = log;
    }

    public void Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        if (_elapsed < _settings.SampleIntervalSeconds.Value)
        {
            return;
        }

        _elapsed = 0f;
        Player player = Player.m_localPlayer;
        if (player is null || player.IsDead())
        {
            return;
        }

        Vector3 position = player.transform.position;
        if (!_probe.TryClassify(position, out RoadKind kind))
        {
            LatestSample = new TraversalSample(position, classified: false, default, onRecordedRoad: false);
            return;
        }

        bool recorded = _atlas.ContainsPointNear(
            kind,
            new RoadPoint(position.x, position.y, position.z),
            AlignmentVerdicts.ObservationPassMeters);
        LatestSample = new TraversalSample(position, classified: true, kind, recorded);
    }
}
