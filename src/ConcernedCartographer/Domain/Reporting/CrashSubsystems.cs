using System;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Derives a short subsystem name from the mod's own error-log
/// messages (#97), which follow stable shapes like "Pin adapter failed
/// and was disabled…", "Could not save road atlas to …", or "Workbench
/// invariant violated: …".</summary>
internal static class CrashSubsystems
{
    private static readonly string[] Markers =
    {
        " failed", " could not", " was disabled", " invariant", " error",
    };

    public static string Infer(string? logMessage)
    {
        string text = (logMessage ?? "").Trim();
        if (text.Length == 0)
        {
            return "unknown";
        }

        if (text.StartsWith("Could not ", StringComparison.OrdinalIgnoreCase))
        {
            return FirstWords(text.Substring("Could not ".Length), 3);
        }

        string lower = text.ToLowerInvariant();
        int cut = -1;
        foreach (string marker in Markers)
        {
            int index = lower.IndexOf(marker, StringComparison.Ordinal);
            if (index > 0 && (cut < 0 || index < cut))
            {
                cut = index;
            }
        }

        string prefix = cut > 0 ? text.Substring(0, cut) : FirstWords(text, 3);
        prefix = prefix.TrimEnd(':', ' ', '-');
        if (prefix.Length == 0)
        {
            prefix = "unknown";
        }

        return prefix.Length > 48 ? prefix.Substring(0, 48) : prefix;
    }

    private static string FirstWords(string text, int count)
    {
        string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int take = Math.Min(count, words.Length);
        return string.Join(" ", words, 0, take);
    }
}
