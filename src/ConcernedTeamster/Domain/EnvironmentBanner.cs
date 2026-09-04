namespace TheConcernedCat.ConcernedTeamster.Domain;

/// <summary>Composes the single-line startup environment banner (CT-001).
/// Pure string logic so the exact wording, the unknown-value fallbacks, and
/// the one-line guarantee are provable off-game. The one-line guarantee is a
/// log-integrity property: version labels come from foreign assemblies and
/// reflective game lookups, and none of them may inject extra log lines.</summary>
public static class EnvironmentBanner
{
    /// <summary>Fallback label for a version that could not be resolved.</summary>
    public const string Unknown = "unknown";

    /// <summary>The banner logged once at plugin startup, for example:
    /// <c>Release ConcernedTeamster@0.1.0+abc1234 | Valheim 0.220.5 |
    /// Unity 6000.0.32f1 | BepInEx 5.4.23.3 | Jotunn 2.29.2</c>.</summary>
    public static string Compose(
        string? releaseIdentity,
        string? valheimVersion,
        string? unityVersion,
        string? bepInExVersion,
        string? jotunnVersion)
    {
        return
            $"Release {Normalize(releaseIdentity)} | " +
            $"Valheim {Normalize(valheimVersion)} | " +
            $"Unity {Normalize(unityVersion)} | " +
            $"BepInEx {Normalize(bepInExVersion)} | " +
            $"Jotunn {Normalize(jotunnVersion)}";
    }

    /// <summary>Collapses a label to one trimmed line: null, empty, and
    /// whitespace-only become <see cref="Unknown"/>; embedded CR/LF (which
    /// would forge extra log lines) become single spaces.</summary>
    public static string Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Unknown;
        }

        string flattened = label!
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return flattened.Length == 0 ? Unknown : flattened;
    }
}
