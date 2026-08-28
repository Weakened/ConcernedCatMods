using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>One sanitized, allowlist-only crash report (#97). The event is
/// constructed exclusively from: the subsystem name, the parsed exception
/// type/message, the sanitized stack, the version strings in
/// <see cref="CrashReportContext"/>, and three session booleans. Arbitrary
/// exception Data entries are dropped unless their key is in
/// <see cref="AllowedDataKeys"/> (currently empty), so forbidden data has
/// no path into the payload.</summary>
internal sealed class CrashReportEvent
{
    /// <summary>Exception.Data keys allowed into a report. Deliberately
    /// empty — additions require a privacy-policy review (PRIVACY.md).</summary>
    public static readonly HashSet<string> AllowedDataKeys = new(StringComparer.Ordinal);

    private static readonly Regex ExceptionTypePattern = new(
        @"([A-Za-z_][A-Za-z0-9_.+`]*(?:Exception|Error))", RegexOptions.Compiled);

    public string Subsystem = "";
    public string ExceptionType = "";
    public string ExceptionMessage = "";
    public string StackTrace = "";
    public bool Fatal;
    public string Release = "";
    public string ModVersion = "";
    public string ValheimVersion = "";
    public string UnityVersion = "";
    public string BepInExVersion = "";
    public string JotunnVersion = "";
    public bool Multiplayer;
    public bool NoMap;
    public bool MapOpen;
    public readonly Dictionary<string, string> AllowedData = new();

    /// <summary>Duplicate-suppression key for the session throttle.</summary>
    public string Fingerprint = "";

    public static CrashReportEvent Create(
        string subsystem,
        string rawText,
        bool fatal,
        CrashReportContext context,
        IReadOnlyDictionary<string, string>? exceptionData = null)
    {
        string text = rawText ?? "";

        // Split headline from the stack (the first "  at " frame line).
        int stackStart = IndexOfStack(text);
        string headline = stackStart < 0 ? text : text.Substring(0, stackStart);
        string stack = stackStart < 0 ? "" : text.Substring(stackStart);

        Match typeMatch = ExceptionTypePattern.Match(headline);
        string exceptionType = typeMatch.Success ? typeMatch.Groups[1].Value : "Error";
        string message = headline;
        if (typeMatch.Success)
        {
            int colon = headline.IndexOf(':', typeMatch.Index + typeMatch.Length);
            if (colon >= 0 && colon + 1 < headline.Length)
            {
                message = headline.Substring(colon + 1);
            }
        }

        CrashReportRuntimeState state = context.SampleRuntimeState();
        var report = new CrashReportEvent
        {
            Subsystem = CrashReportSanitizer.Sanitize(subsystem, CrashReportSanitizer.MaxSubsystemLength),
            ExceptionType = CrashReportSanitizer.Sanitize(exceptionType, CrashReportSanitizer.MaxSubsystemLength),
            ExceptionMessage = CrashReportSanitizer.Sanitize(message.Trim(), CrashReportSanitizer.MaxMessageLength),
            StackTrace = CrashReportSanitizer.Sanitize(stack.Trim(), CrashReportSanitizer.MaxStackLength),
            Fatal = fatal,
            Release = context.Release,
            ModVersion = context.ModVersion,
            ValheimVersion = context.ValheimVersion,
            UnityVersion = context.UnityVersion,
            BepInExVersion = context.BepInExVersion,
            JotunnVersion = context.JotunnVersion,
            Multiplayer = state.Multiplayer,
            NoMap = state.NoMap,
            MapOpen = state.MapOpen,
        };

        if (exceptionData is not null)
        {
            foreach (KeyValuePair<string, string> entry in exceptionData)
            {
                // Drop-by-default: only explicitly allowlisted keys survive.
                if (AllowedDataKeys.Contains(entry.Key))
                {
                    report.AllowedData[entry.Key] =
                        CrashReportSanitizer.Sanitize(entry.Value, CrashReportSanitizer.MaxMessageLength);
                }
            }
        }

        string firstFrame = report.StackTrace;
        int newline = firstFrame.IndexOf('\n');
        if (newline > 0)
        {
            firstFrame = firstFrame.Substring(0, newline);
        }

        if (firstFrame.Length == 0)
        {
            firstFrame = report.ExceptionMessage.Length > 80
                ? report.ExceptionMessage.Substring(0, 80)
                : report.ExceptionMessage;
        }

        report.Fingerprint = report.Subsystem + "|" + report.ExceptionType + "|" + firstFrame.Trim();
        return report;
    }

    /// <summary>The Sentry event payload. Every emitted field is named
    /// here — there is no passthrough of any other data.</summary>
    public string ToJson(string eventId, DateTime utcNow)
    {
        var json = new StringBuilder(1024);
        json.Append('{');
        json.Append("\"event_id\":").Append(Quote(eventId)).Append(',');
        json.Append("\"timestamp\":").Append(Quote(utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))).Append(',');
        json.Append("\"platform\":\"csharp\",");
        json.Append("\"level\":\"error\",");
        json.Append("\"logger\":").Append(Quote(Subsystem)).Append(',');
        json.Append("\"release\":").Append(Quote(Release)).Append(',');
        json.Append("\"environment\":\"production\",");
        json.Append("\"exception\":{\"values\":[{");
        json.Append("\"type\":").Append(Quote(ExceptionType)).Append(',');
        json.Append("\"value\":").Append(Quote(ExceptionMessage));
        json.Append("}]},");
        json.Append("\"tags\":{");
        json.Append("\"cc.subsystem\":").Append(Quote(Subsystem)).Append(',');
        json.Append("\"cc.fatal\":").Append(Quote(Fatal ? "true" : "false")).Append(',');
        json.Append("\"cc.version\":").Append(Quote(ModVersion)).Append(',');
        json.Append("\"game.valheim\":").Append(Quote(ValheimVersion)).Append(',');
        json.Append("\"game.unity\":").Append(Quote(UnityVersion)).Append(',');
        json.Append("\"env.bepinex\":").Append(Quote(BepInExVersion)).Append(',');
        json.Append("\"env.jotunn\":").Append(Quote(JotunnVersion)).Append(',');
        json.Append("\"session.multiplayer\":").Append(Quote(Multiplayer ? "true" : "false")).Append(',');
        json.Append("\"session.nomap\":").Append(Quote(NoMap ? "true" : "false")).Append(',');
        json.Append("\"session.map_open\":").Append(Quote(MapOpen ? "true" : "false"));
        json.Append("},");
        json.Append("\"extra\":{");
        json.Append("\"cc_stack\":").Append(Quote(StackTrace));
        foreach (KeyValuePair<string, string> entry in AllowedData)
        {
            json.Append(',').Append(Quote(entry.Key)).Append(':').Append(Quote(entry.Value));
        }

        json.Append("}}");
        return json.ToString();
    }

    private static int IndexOfStack(string text)
    {
        int windows = text.IndexOf("\n  at ", StringComparison.Ordinal);
        int compact = text.IndexOf("\n at ", StringComparison.Ordinal);
        if (windows < 0)
        {
            return compact;
        }

        return compact < 0 ? windows : Math.Min(windows, compact);
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
