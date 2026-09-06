namespace TheConcernedCat.ConcernedTeamster.Domain.Profiles;

/// <summary>The concrete setting values one <see cref="ConfigProfile"/>
/// resolves to (CT-034).</summary>
public sealed class ProfileValues
{
    public ProfileValues(
        bool panelWarningsEnabled,
        bool hudWarningHintsEnabled,
        bool tripsEnabled,
        bool brakeEnabled,
        int riskLookaheadPoints)
    {
        PanelWarningsEnabled = panelWarningsEnabled;
        HudWarningHintsEnabled = hudWarningHintsEnabled;
        TripsEnabled = tripsEnabled;
        BrakeEnabled = brakeEnabled;
        RiskLookaheadPoints = riskLookaheadPoints;
    }

    public bool PanelWarningsEnabled { get; }

    public bool HudWarningHintsEnabled { get; }

    public bool TripsEnabled { get; }

    /// <summary>Always false in every shipped profile — the parking brake is
    /// a mutating feature and stays opt-in regardless of preset.</summary>
    public bool BrakeEnabled { get; }

    public int RiskLookaheadPoints { get; }

    public bool ValueEquals(ProfileValues other)
    {
        return PanelWarningsEnabled == other.PanelWarningsEnabled &&
            HudWarningHintsEnabled == other.HudWarningHintsEnabled &&
            TripsEnabled == other.TripsEnabled &&
            BrakeEnabled == other.BrakeEnabled &&
            RiskLookaheadPoints == other.RiskLookaheadPoints;
    }
}
