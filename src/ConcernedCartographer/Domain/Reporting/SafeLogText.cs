using System;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Privacy-safe renderings of exceptions for the BepInEx log
/// (privacy audit, CC-098). LogOutput.log is exactly what players paste
/// into bug reports, so exception text entering it follows the same
/// scrubbing contract as crash reports: absolute paths (which embed
/// machine usernames), world-uid-bearing sidecar file names, save-file
/// names, URLs, IPs, coordinate pairs, and long numeric ids never reach
/// the log. The exception type and the mod's own wording survive, so a
/// log line still says what failed and why.</summary>
internal static class SafeLogText
{
    /// <summary>Full diagnostic rendering (type, message, stack trace),
    /// scrubbed. Use where a call site previously interpolated the whole
    /// exception.</summary>
    public static string Describe(Exception exception)
    {
        return exception is null
            ? ""
            : CrashReportSanitizer.Sanitize(exception.ToString(), CrashReportSanitizer.MaxStackLength);
    }

    /// <summary>One-line rendering (type plus scrubbed message). Use where
    /// a call site previously interpolated only the exception message —
    /// the type survives scrubbing, so a fully masked message still
    /// identifies the failure class.</summary>
    public static string Brief(Exception exception)
    {
        return exception is null
            ? ""
            : exception.GetType().Name + ": " +
                CrashReportSanitizer.Sanitize(exception.Message, CrashReportSanitizer.MaxMessageLength);
    }
}
