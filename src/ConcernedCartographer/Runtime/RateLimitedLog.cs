using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Logs a keyed message at most once per interval so repeat-prone
/// diagnostics (per-sample classification, per-segment draw failures, autosave
/// failures) can never spam the BepInEx log.</summary>
internal sealed class RateLimitedLog
{
    private readonly ManualLogSource _log;
    private readonly float _minimumIntervalSeconds;
    private readonly Dictionary<string, float> _lastLoggedAt = new();

    public RateLimitedLog(ManualLogSource log, float minimumIntervalSeconds)
    {
        _log = log;
        _minimumIntervalSeconds = minimumIntervalSeconds;
    }

    public void Info(string key, string message)
    {
        if (ShouldLog(key))
        {
            _log.LogInfo(message);
        }
    }

    public void Warning(string key, string message)
    {
        if (ShouldLog(key))
        {
            _log.LogWarning(message);
        }
    }

    public void Error(string key, string message)
    {
        if (ShouldLog(key))
        {
            _log.LogError(message);
        }
    }

    private bool ShouldLog(string key)
    {
        float now = Time.unscaledTime;
        if (_lastLoggedAt.TryGetValue(key, out float last) && now - last < _minimumIntervalSeconds)
        {
            return false;
        }

        _lastLoggedAt[key] = now;
        return true;
    }
}
