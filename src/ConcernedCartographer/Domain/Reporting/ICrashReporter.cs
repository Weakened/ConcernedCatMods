using System;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Snapshot of the per-event booleans the privacy policy allows
/// (#97). Nothing positional, personal, or world-identifying.</summary>
internal readonly struct CrashReportRuntimeState
{
    public CrashReportRuntimeState(bool multiplayer, bool noMap, bool mapOpen)
    {
        Multiplayer = multiplayer;
        NoMap = noMap;
        MapOpen = mapOpen;
    }

    public bool Multiplayer { get; }
    public bool NoMap { get; }
    public bool MapOpen { get; }
}

/// <summary>The complete allowlisted metadata a crash report may carry
/// (#97). This type is the privacy boundary: an event is built ONLY from
/// these fields plus the sanitized exception text — there is no bag for
/// arbitrary context, so forbidden data (names, worlds, coordinates,
/// identifiers, credentials) has no field to travel in.</summary>
internal sealed class CrashReportContext
{
    /// <summary>Sentry release identity:
    /// <c>ConcernedCartographer@&lt;semver&gt;+&lt;commit&gt;</c>.</summary>
    public string Release = "";

    public string ModVersion = "";
    public string ValheimVersion = "";
    public string UnityVersion = "";
    public string BepInExVersion = "";
    public string JotunnVersion = "";

    /// <summary>Live boolean snapshot provider; failures fall back to
    /// all-false. Never sampled before a capture is accepted.</summary>
    public Func<CrashReportRuntimeState>? RuntimeState;

    public CrashReportRuntimeState SampleRuntimeState()
    {
        try
        {
            return RuntimeState?.Invoke() ?? default;
        }
        catch
        {
            return default;
        }
    }
}

/// <summary>Provider abstraction for crash reporting (#97): the runtime
/// talks only to this interface, so the backend (Sentry today) can be
/// replaced without touching feature code. Implementations must never
/// throw out of any member, never block the caller, and never transmit
/// anything before consent.</summary>
internal interface ICrashReporter : IDisposable
{
    /// <summary>True only when the player explicitly opted in. Captures
    /// while false are discarded before any queueing.</summary>
    bool ConsentGranted { get; set; }

    void Initialize(CrashReportContext context);

    /// <summary>An unhandled exception whose stack involves Concerned
    /// Cartographer. rawExceptionText = "Type: message\n stack".</summary>
    void CaptureException(string subsystem, string rawExceptionText);

    /// <summary>A fail-closed subsystem disable, persistence/migration or
    /// decoder failure, or invariant violation, as logged by the mod's
    /// own error channel.</summary>
    void CaptureFatalSubsystemFailure(string subsystem, string message);

    /// <summary>Best-effort drain of the outgoing queue, bounded by the
    /// timeout. Returns true when the queue emptied in time.</summary>
    bool Flush(TimeSpan timeout);
}
