using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

internal sealed class GroundPaintProbe
{
    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly List<Heightmap> _heightmaps = new();
    private bool _disabledForSession;
    private bool _failureLogged;

    public GroundPaintProbe(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    public bool TryClassify(Vector3 worldPosition, out RoadKind kind)
    {
        kind = default;
        if (_disabledForSession)
        {
            return false;
        }

        try
        {
            _heightmaps.Clear();
            Heightmap.FindHeightmap(worldPosition, 2f, _heightmaps);
            if (_heightmaps.Count == 0)
            {
                return false;
            }

            Heightmap heightmap = FindNearestHeightmap(worldPosition);
            heightmap.WorldToVertex(worldPosition, out int centerX, out int centerY);

            int radius = _settings.PaintSampleRadius.Value;
            int maxCoordinate = heightmap.m_width;
            Color total = Color.clear;
            int samples = 0;

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (y < 0 || y > maxCoordinate)
                {
                    continue;
                }

                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || x > maxCoordinate)
                    {
                        continue;
                    }

                    total += heightmap.GetPaintMask(x, y);
                    samples++;
                }
            }

            if (samples == 0)
            {
                return false;
            }

            Color average = total / samples;
            float threshold = _settings.PaintThreshold.Value;

            if (average.b >= threshold && average.b > average.r)
            {
                kind = RoadKind.Paved;
                LogClassification(kind, average);
                return true;
            }

            if (average.r >= threshold && average.r > average.b)
            {
                kind = RoadKind.Dirt;
                LogClassification(kind, average);
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            _disabledForSession = true;
            if (!_failureLogged)
            {
                _failureLogged = true;
                _log.LogError($"Terrain paint probing failed and was disabled for this session: {exception}");
            }

            return false;
        }
    }

    private Heightmap FindNearestHeightmap(Vector3 worldPosition)
    {
        Heightmap nearest = _heightmaps[0];
        float nearestDistance = HorizontalSqrDistance(nearest.transform.position, worldPosition);

        for (int index = 1; index < _heightmaps.Count; index++)
        {
            Heightmap candidate = _heightmaps[index];
            float distance = HorizontalSqrDistance(candidate.transform.position, worldPosition);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void LogClassification(RoadKind kind, Color color)
    {
        if (_settings.DebugLogging.Value)
        {
            _log.LogDebug($"Terrain classified as {kind}; paint RGBA={color}.");
        }
    }

    private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
    {
        float dx = left.x - right.x;
        float dz = left.z - right.z;
        return (dx * dx) + (dz * dz);
    }
}
