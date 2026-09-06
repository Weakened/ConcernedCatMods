namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>The outcome of evaluating one <see cref="KnownModProbe"/>
/// against the running game (CT-036).</summary>
public sealed class ModDetectionResult
{
    public ModDetectionResult(KnownModProbe probe, bool found, string? detectedVersion)
    {
        Probe = probe;
        Found = found;
        DetectedVersion = found ? detectedVersion : null;
    }

    public KnownModProbe Probe { get; }

    public bool Found { get; }

    /// <summary>Null when <see cref="Found"/> is false, or when found but no
    /// version string was readable.</summary>
    public string? DetectedVersion { get; }
}
