using System;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>The reporter used whenever crash reporting cannot or must not
/// operate (no DSN embedded, provider failure). Accepts every call and
/// does nothing — the rest of the mod never needs to care.</summary>
internal sealed class NullCrashReporter : ICrashReporter
{
    public bool ConsentGranted { get; set; }

    public void Initialize(CrashReportContext context)
    {
    }

    public void CaptureException(string subsystem, string rawExceptionText)
    {
    }

    public void CaptureFatalSubsystemFailure(string subsystem, string message)
    {
    }

    public bool Flush(TimeSpan timeout)
    {
        return true;
    }

    public void Dispose()
    {
    }
}
