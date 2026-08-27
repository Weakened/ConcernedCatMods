namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Structural bounds applied at every parse boundary
/// (SEC-1.0-001): hostile or corrupt rows cannot smuggle absurd revisions,
/// non-finite coordinates, or memory-hostile strings into the stores.
/// String caps truncate gracefully so oversized legitimate data degrades
/// instead of vanishing.</summary>
internal static class AtlasLimits
{
    /// <summary>Far beyond any legitimate edit count, far below overflow.</summary>
    public const long MaxRevision = 1_000_000_000_000L;

    public const int MaxNameLength = 200;
    public const int MaxCategoryLength = 100;
    public const int MaxIconIdLength = 100;
    public const int MaxNotesLength = 10_000;
    public const int MaxTags = 64;
    public const int MaxTagLength = 64;

    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static string Cap(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
