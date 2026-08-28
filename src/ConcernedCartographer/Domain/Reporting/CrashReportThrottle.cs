using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Session-local rate limiting (#97): each distinct failure
/// fingerprint is submitted at most once, the whole session is capped,
/// and the player is notified at most once per subsystem. Never persists
/// anything.</summary>
internal sealed class CrashReportThrottle
{
    public const int DefaultMaxEventsPerSession = 10;

    private readonly HashSet<string> _sentFingerprints = new();
    private readonly HashSet<string> _notifiedSubsystems = new();
    private readonly int _maxEventsPerSession;
    private int _sent;

    public CrashReportThrottle(int maxEventsPerSession = DefaultMaxEventsPerSession)
    {
        _maxEventsPerSession = maxEventsPerSession;
    }

    /// <summary>True when this fingerprint may be submitted now (first
    /// sighting, session cap not reached). Counts the submission.</summary>
    public bool ShouldSend(string fingerprint)
    {
        if (_sent >= _maxEventsPerSession || !_sentFingerprints.Add(fingerprint))
        {
            return false;
        }

        _sent++;
        return true;
    }

    /// <summary>True when the player has not yet been notified about this
    /// subsystem's failure this session.</summary>
    public bool ShouldNotify(string subsystem)
    {
        return _notifiedSubsystems.Add(subsystem);
    }
}
