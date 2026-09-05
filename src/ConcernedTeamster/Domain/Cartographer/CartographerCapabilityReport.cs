namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>Immutable outcome of the Cartographer capability probe (CT-021).
/// Carries the availability decision, what was detected, why a hidden state
/// was chosen, and the single INFO line the plugin logs — exactly one line
/// per session in every state, per the CT-021 spec.</summary>
public sealed class CartographerCapabilityReport
{
    private CartographerCapabilityReport(
        CartographerAvailability availability,
        string detectedVersion,
        string detail,
        int verifiedMemberCount)
    {
        Availability = availability;
        DetectedVersion = detectedVersion;
        Detail = detail;
        VerifiedMemberCount = verifiedMemberCount;
    }

    public CartographerAvailability Availability { get; }

    /// <summary>The version string the registry reported, or "unknown".</summary>
    public string DetectedVersion { get; }

    /// <summary>Why a hidden state was chosen (missing members, exception
    /// type, floor text); empty when <see cref="Availability"/> is Available.</summary>
    public string Detail { get; }

    /// <summary>How many contract members verified; 0 unless Available.</summary>
    public int VerifiedMemberCount { get; }

    public bool IsAvailable => Availability == CartographerAvailability.Available;

    /// <summary>The one INFO line for this probe outcome. Always a single
    /// line: hidden states explain themselves here and nowhere else.</summary>
    public string LogLine
    {
        get
        {
            switch (Availability)
            {
                case CartographerAvailability.Available:
                    return $"Cartographer integration AVAILABLE: {CartographerContract.ProductName} " +
                        $"{DetectedVersion} detected, {VerifiedMemberCount} contract members verified. " +
                        "Route features can appear once a world is loaded.";
                case CartographerAvailability.Absent:
                    return $"Cartographer integration hidden: {CartographerContract.ProductName} is not " +
                        "installed. Concerned Teamster runs fully standalone.";
                case CartographerAvailability.VersionTooLow:
                    return $"Cartographer integration hidden: {CartographerContract.ProductName} " +
                        $"{DetectedVersion} is below the supported floor {CartographerContract.FloorVersion}; " +
                        $"update {CartographerContract.ProductName} to enable route features.";
                default:
                    return $"Cartographer integration hidden: {CartographerContract.ProductName} " +
                        $"{DetectedVersion} was detected but its route surface did not verify ({Detail}). " +
                        "A Concerned Teamster update is likely needed; everything else keeps working.";
            }
        }
    }

    public static CartographerCapabilityReport Available(string detectedVersion, int verifiedMemberCount)
    {
        return new CartographerCapabilityReport(
            CartographerAvailability.Available, detectedVersion, "", verifiedMemberCount);
    }

    public static CartographerCapabilityReport Absent()
    {
        return new CartographerCapabilityReport(
            CartographerAvailability.Absent, "unknown", "not installed", 0);
    }

    public static CartographerCapabilityReport VersionTooLow(string detectedVersion)
    {
        return new CartographerCapabilityReport(
            CartographerAvailability.VersionTooLow, detectedVersion,
            $"below floor {CartographerContract.FloorVersion}", 0);
    }

    public static CartographerCapabilityReport ProbeFailed(string detectedVersion, string detail)
    {
        return new CartographerCapabilityReport(
            CartographerAvailability.ProbeFailed, detectedVersion, detail, 0);
    }
}
