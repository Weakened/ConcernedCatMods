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

        bool recorded = _atlas.RecordSample(
            kind,
            position,
            _settings.MinimumPointSpacingMeters.Value,
            _settings.MaximumStrokeGapMeters.Value,
            out segment);

        if (recorded && _settings.DebugLogging.Value)
        {
            _log.LogInfo($"Recorded {kind} road segment from {segment.Start} to {segment.End}.");
        }

        return recorded;
    }

    public void EndStroke()
    {
        _atlas.EndStroke();
    }
}
