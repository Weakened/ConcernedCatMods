using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class RoadSurveyor
{
    private readonly CartographerSettings _settings;
    private readonly GroundPaintProbe _probe;
    private readonly RoadAtlas _atlas;
    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;
    private float _elapsed;

    public RoadSurveyor(
        CartographerSettings settings,
        GroundPaintProbe probe,
        RoadAtlas atlas,
        ManualLogSource log)
    {
        _settings = settings;
        _probe = probe;
        _atlas = atlas;
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
            _atlas.EndStroke();
            return false;
        }

        Vector3 position = player.transform.position;
        if (!_probe.TryClassify(position, out RoadKind kind))
        {
            _atlas.EndStroke();
            return false;
        }

        var rules = new RoadSamplingRules(
            _settings.MinimumPointSpacingMeters.Value,
            _settings.MaximumStrokeGapMeters.Value,
            _settings.DuplicateSuppressionMeters.Value);

        bool recorded = _atlas.RecordSample(
            kind,
            new RoadPoint(position.x, position.y, position.z),
            rules,
            out segment);

        if (recorded && _settings.DebugLogging.Value)
        {
            _rateLimited.Info("segment-recorded", $"Recorded {kind} road segment from {segment.Start} to {segment.End}.");
        }

        return recorded;
    }

    public void EndStroke()
    {
        _atlas.EndStroke();
    }
}
