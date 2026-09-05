using BepInEx;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

namespace TheConcernedCat.ConcernedTeamster;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedteamster";
    public const string PluginName = "Concerned Teamster";
    public const string PluginVersion = "0.6.0";

    private bool _cartographerProbePending;

    private void Awake()
    {
        TeamsterSettings settings = TeamsterSettings.Bind(Config);

        // CT-032: write the translator template and load any teamster-strings.tsv
        // overrides before UI strings are resolved; English is the fallback.
        Adapters.LocalizationFiles.Initialize(Logger);

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        LogEnvironmentBanner();
        Logger.LogInfo(
            "Effective config: " +
            $"Enabled={settings.Enabled.Value}, " +
            $"DebugLogging={settings.DebugLogging.Value}.");

        LogCartCapability(settings);
        ArmTelemetry(settings);

        // CT-021: the Cartographer probe waits for the first Update tick —
        // BepInEx fills PluginInfos in load order, so probing from Awake
        // could misread a not-yet-loaded Cartographer as absent. Skipped
        // entirely when the master switch is off (no features may appear).
        _cartographerProbePending = settings.Enabled.Value;

        // Read-only telemetry, panels, manifest, and advisory warnings only;
        // nothing mutates carts.
    }

    private void Update()
    {
        if (_cartographerProbePending)
        {
            _cartographerProbePending = false;
            CartographerCapability.EnsureProbed(Logger);
        }
    }

    /// <summary>Starts the telemetry pump only when the master switch is on
    /// and the capability probe verified the game surface — otherwise no cart
    /// observation code runs at all (fail closed), which the log states.</summary>
    private void ArmTelemetry(TeamsterSettings settings)
    {
        if (!settings.Enabled.Value)
        {
            Logger.LogInfo("Concerned Teamster is disabled by config; no cart observation runs.");
            return;
        }

        if (!CartAdapter.CapabilityEnabled)
        {
            // The capability WARN above already explains why; nothing runs.
            return;
        }

        Domain.Load.LoadModel? loadModel = LoadCalibratedModel();
        Domain.Risk.RiskModel? riskModel = LoadDescentModel();

        CartTelemetryPump pump = gameObject.AddComponent<CartTelemetryPump>();
        Domain.Carts.TelemetrySamplerOptions options = pump.Initialize(settings, Logger, loadModel, riskModel);
        Logger.LogInfo(
            $"Cart telemetry sampler armed: interval {options.SampleIntervalSeconds:0.##} s, " +
            $"radius {options.SearchRadiusMeters:0.#} m, {options.MaxCartsPerTick} carts/tick, " +
            $"{options.MaxTrackedCarts} tracked max, evict after {options.EvictAfterSeconds:0.#} s.");
        Logger.LogInfo(
            "Warnings: panel " + (settings.PanelWarningsEnabled.Value ? "on" : "off") +
            ", HUD hint " + (settings.HudWarningHintsEnabled.Value ? "on" : "off") +
            $", steep-climb caution at {settings.SteepGradeCautionPercent.Value:0.#}% " +
            $"(exit −{Domain.Warnings.WarningOptions.ExitBandPercent:0.#}%, " +
            $"fall hold {Domain.Warnings.WarningOptions.FallHoldSeconds:0.#} s).");

        Ui.CartStatusHudController hud = gameObject.AddComponent<Ui.CartStatusHudController>();
        hud.Initialize(settings, Logger, pump);
        Logger.LogInfo("Cart Status panel armed: visible Cart button at the right screen edge while in a world.");
    }

    /// <summary>Loads the embedded calibration data once, reports its
    /// provenance in one line (CT-008), and builds the load model for the
    /// warning evaluator (CT-009). Failure only disables load advice;
    /// everything else runs.</summary>
    private Domain.Load.LoadModel? LoadCalibratedModel()
    {
        Domain.Load.LoadCalibrationData? calibration = Domain.Load.LoadCalibrationSource.TryLoadEmbedded();
        if (calibration is null || calibration.DataVersion <= 0)
        {
            Logger.LogWarning(
                "Load calibration data missing or unreadable; load advice stays off, all telemetry keeps working.");
            return null;
        }

        Logger.LogInfo(
            $"Load calibration v{calibration.DataVersion} loaded: {calibration.Rows.Count} rows " +
            $"({calibration.MeasuredRowCount} measured) for game {calibration.GameVersion}; " +
            (calibration.Errors.Count == 0
                ? "no parse errors."
                : $"{calibration.Errors.Count} malformed line(s) skipped."));
        return new Domain.Load.LoadModel(calibration);
    }

    /// <summary>Loads the embedded descent calibration once and reports its
    /// provenance in one line (CT-011). Failure only leaves descent risk at
    /// Unknown; everything else runs.</summary>
    private Domain.Risk.RiskModel? LoadDescentModel()
    {
        Domain.Risk.DescentCalibrationData? calibration = Domain.Risk.DescentCalibrationSource.TryLoadEmbedded();
        if (calibration is null || calibration.DataVersion <= 0)
        {
            Logger.LogWarning(
                "Descent calibration data missing or unreadable; descent risk stays Unknown, all telemetry keeps working.");
            return null;
        }

        Logger.LogInfo(
            $"Descent calibration v{calibration.DataVersion} loaded: {calibration.Rows.Count} rows " +
            $"({calibration.MeasuredRowCount} measured) for game {calibration.GameVersion}; " +
            (calibration.Errors.Count == 0
                ? "no parse errors."
                : $"{calibration.Errors.Count} malformed line(s) skipped."));
        return new Domain.Risk.RiskModel(calibration);
    }

    /// <summary>Runs the cart capability probe once and reports the outcome
    /// as one line: INFO when every verified member is present, one
    /// actionable WARN naming each missing member otherwise. The probe reads
    /// type metadata only — no world or cart state is touched.</summary>
    private void LogCartCapability(TeamsterSettings settings)
    {
        GameCapabilityReport report = CartAdapter.ProbeCapability();
        if (report.Enabled)
        {
            Logger.LogInfo(
                $"Cart telemetry capability ENABLED: {report.VerifiedMembers.Count} game members verified.");
            if (settings.DebugLogging.Value)
            {
                Logger.LogDebug("Verified cart members: " + string.Join(", ", report.VerifiedMembers));
            }
        }
        else
        {
            Logger.LogWarning(
                "Cart telemetry capability DISABLED: missing " +
                string.Join(", ", report.MissingMembers) +
                ". A Valheim update likely changed cart internals; cart features stay off until a " +
                "Concerned Teamster update, and everything else keeps working.");
        }
    }

    private void LogEnvironmentBanner()
    {
        string bepInExVersion = EnvironmentBanner.Unknown;
        string jotunnVersion = EnvironmentBanner.Unknown;
        string unityVersion = EnvironmentBanner.Unknown;
        try
        {
            bepInExVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? EnvironmentBanner.Unknown;
            jotunnVersion = typeof(Jotunn.Main).Assembly.GetName().Version?.ToString() ?? EnvironmentBanner.Unknown;
            unityVersion = UnityEngine.Application.unityVersion;
        }
        catch
        {
            // Version labels are metadata only; the banner still prints.
        }

        Logger.LogInfo(EnvironmentBanner.Compose(
            "ConcernedTeamster@" + ResolveInformationalVersion(),
            GameVersionResolver.Resolve(),
            unityVersion,
            bepInExVersion,
            jotunnVersion));
    }

    /// <summary>The release identity including the build commit (the SDK
    /// stamps InformationalVersion as "0.1.0+&lt;sha&gt;"), so a log excerpt
    /// identifies the exact binary and nothing about the player.</summary>
    private static string ResolveInformationalVersion()
    {
        try
        {
            return System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Plugin).Assembly)
                ?.InformationalVersion ?? PluginVersion;
        }
        catch
        {
            // The plain version is an acceptable release identity fallback.
            return PluginVersion;
        }
    }
}
