using TheConcernedCat.ConcernedTeamster.Domain.Risk;

namespace TheConcernedCat.ConcernedTeamster.Domain.Profiles;

/// <summary>The three documented presets (CT-034). Every profile keeps the
/// parking brake opt-in (never auto-enabled) — mutating behavior stays an
/// explicit, separate choice regardless of preset, per the product's "no
/// cheats by default" principle. Resolution is pure and total: an
/// unrecognized profile value (a stale enum member from a hypothetical
/// future removed profile, or a corrupted raw value) fails closed to
/// Standard's values rather than throwing — the same fail-closed shape
/// <c>LoadModel</c>/<c>RiskModel</c> already use for their Unknown
/// verdicts.</summary>
public static class ConfigProfileCatalog
{
    public static ProfileValues Resolve(ConfigProfile profile)
    {
        return profile switch
        {
            ConfigProfile.Minimal => new ProfileValues(
                panelWarningsEnabled: false,
                hudWarningHintsEnabled: false,
                tripsEnabled: false,
                brakeEnabled: false,
                riskLookaheadPoints: LookaheadOptions.MinPoints),

            ConfigProfile.EverythingObservational => new ProfileValues(
                panelWarningsEnabled: true,
                hudWarningHintsEnabled: true,
                tripsEnabled: true,
                brakeEnabled: false,
                riskLookaheadPoints: LookaheadOptions.MaxPoints),

            // Standard, and the fail-closed default for any other value —
            // these are the settings Teamster has always shipped with.
            _ => new ProfileValues(
                panelWarningsEnabled: true,
                hudWarningHintsEnabled: false,
                tripsEnabled: true,
                brakeEnabled: false,
                riskLookaheadPoints: LookaheadOptions.DefaultPoints),
        };
    }
}
