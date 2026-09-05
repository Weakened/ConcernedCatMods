using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using TheConcernedCat.ConcernedTeamster.Domain.Warnings;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Unity-side driver of the domain <see cref="TelemetrySampler"/>
/// (CT-003). Each frame it forwards the unscaled clock; the sampler's not-due
/// fast path makes that a single comparison. While no local player exists
/// (menu, logout, world switch) it resets the sampler exactly once, so no
/// other world's or session's carts can ever be shown. The only logging is a
/// debug summary, gated behind the DebugLogging setting and rate-limited —
/// the sample path itself never logs.</summary>
internal sealed class CartTelemetryPump : MonoBehaviour
{
    private const double DebugSummaryPeriodSeconds = 5.0;

    private TelemetrySampler? _sampler;
    private TeamsterSettings? _settings;
    private ManualLogSource? _log;
    private CartWarningTracker? _warnings;
    private WarningOptions? _warningOptions;
    private RiskModel? _riskModel;
    private LookaheadOptions? _lookaheadOptions;
    private bool _resetWhileNoLocalPlayer;
    private double _nextDebugSummaryTime;

    /// <summary>Descent risk for the cart the local player is pulling, from
    /// the most recent snapshot of it; null when nothing is pulled (CT-011).
    /// Read-only for consumers — evaluation happens on new snapshots only.</summary>
    public DescentRiskInfo? LatestDescentRisk { get; private set; }

    /// <summary>The parking brake owner (CT-012); null when the brake is
    /// disabled by config.</summary>
    public BrakeService? Brake { get; private set; }

    private StuckDetector? _stuckDetector;

    /// <summary>Stuck diagnosis for the pulled cart from its most recent
    /// snapshot (CT-013); None-equivalent null when nothing is pulled.
    /// Read-only for consumers.</summary>
    public CartDiagnostic? LatestDiagnostic { get; private set; }

    /// <summary>Latest telemetry by cart id for future consumers (CT-005
    /// panels); null until initialized.</summary>
    public IReadOnlyDictionary<string, CartTelemetry>? Telemetry => _sampler?.TelemetryByCartId;

    /// <summary>Effective warning configuration; null until initialized.</summary>
    public WarningOptions? WarningOptions => _warningOptions;

    /// <summary>Current warning for a cart, or null. Read-only: evaluation
    /// happens exclusively on new snapshots inside Update (CT-009).</summary>
    public CartWarning? TryGetWarning(string? cartId)
    {
        return cartId is null ? null : _warnings?.TryGet(cartId);
    }

    /// <summary>Wires the sampler, warning evaluation, and descent risk
    /// (CT-011), returning the effective (clamped) sampler options so
    /// startup can log them.</summary>
    internal TelemetrySamplerOptions Initialize(
        TeamsterSettings settings, ManualLogSource log, LoadModel? loadModel, RiskModel? riskModel)
    {
        _settings = settings;
        _log = log;
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            settings.SampleIntervalSeconds.Value,
            settings.SearchRadiusMeters.Value,
            settings.MaxCartsPerTick.Value,
            settings.MaxTrackedCarts.Value);
        _sampler = new TelemetrySampler(options, CartAdapter.CollectNearbyCarts, CartAdapter.TrySampleCart);
        _warnings = new CartWarningTracker(loadModel);
        _warningOptions = Domain.Warnings.WarningOptions.CreateClamped(
            settings.SteepGradeCautionPercent.Value,
            settings.PanelWarningsEnabled.Value,
            settings.HudWarningHintsEnabled.Value);
        _riskModel = riskModel;
        _lookaheadOptions = LookaheadOptions.CreateClamped(settings.RiskLookaheadPoints.Value);
        Brake = settings.BrakeEnabled.Value ? new BrakeService(log) : null;
        _stuckDetector = new StuckDetector(loadModel);
        return options;
    }

    private void Update()
    {
        TelemetrySampler? sampler = _sampler;
        if (sampler is null)
        {
            return;
        }

        if (!CartAdapter.HasLocalPlayer())
        {
            if (!_resetWhileNoLocalPlayer)
            {
                sampler.Reset();
                _warnings?.Reset();
                LatestDescentRisk = null;
                _stuckDetector?.Reset();
                LatestDiagnostic = null;
                Brake?.ReleaseNow("left the world");
                _resetWhileNoLocalPlayer = true;
            }

            return;
        }

        _resetWhileNoLocalPlayer = false;

        double now = Time.unscaledTimeAsDouble;
        if (!sampler.Tick(now))
        {
            return;
        }

        // CT-009: warnings evaluate exactly here — once per snapshot the
        // due tick just produced (their SampleTimeSeconds equals this
        // tick's clock), never from UI reads or extra polling.
        if (_warnings is not null && _warningOptions is not null)
        {
            foreach (KeyValuePair<string, CartTelemetry> entry in sampler.TelemetryByCartId)
            {
                if (entry.Value.SampleTimeSeconds == now)
                {
                    _warnings.Update(entry.Value, _warningOptions);
                }
            }

            _warnings.Sweep(now, sampler.Options.EvictAfterSeconds);
        }

        // CT-011: descent risk for the pulled cart, evaluated on its fresh
        // snapshot only; the lookahead pass is bounded by the options'
        // fixed height-query budget.
        bool anyPulledTracked = false;
        foreach (KeyValuePair<string, CartTelemetry> entry in sampler.TelemetryByCartId)
        {
            if (!entry.Value.IsPulledByLocalPlayer)
            {
                continue;
            }

            anyPulledTracked = true;
            if (entry.Value.SampleTimeSeconds != now || _lookaheadOptions is null)
            {
                continue;
            }

            object? cart = CartAdapter.TryFindCartById(entry.Key);
            TerrainAdapter.LookaheadReading look = cart is null
                ? TerrainAdapter.LookaheadReading.Unavailable
                : TerrainAdapter.TryReadDescentLookahead(cart, _lookaheadOptions);
            RiskVerdict current = DescentRiskEvaluator.EvaluateCurrent(_riskModel, entry.Value);
            RiskVerdict? ahead = DescentRiskEvaluator.EvaluateLookahead(
                _riskModel, entry.Value, look.Available, look.WorstDownGradePercent);
            LatestDescentRisk = new DescentRiskInfo(
                entry.Key, current, look.Available, look.WorstDownGradePercent, ahead, now);

            // CT-013: stuck diagnostics share the pulled-cart, fresh-
            // snapshot gate — parked and unattended carts never reach the
            // detector.
            LatestDiagnostic = _stuckDetector?.Update(entry.Value);
            break;
        }

        if (!anyPulledTracked)
        {
            LatestDescentRisk = null;
            LatestDiagnostic = null;
            _stuckDetector?.Reset();
        }

        // CT-012: the brake re-validates its engaged cart on every due tick.
        Brake?.Tick();

        if (_settings is { } settings && settings.DebugLogging.Value && now >= _nextDebugSummaryTime)
        {
            _nextDebugSummaryTime = now + DebugSummaryPeriodSeconds;
            _log?.LogDebug(
                $"Cart telemetry: {sampler.TrackedCartCount} tracked, " +
                $"{sampler.SampledOnLastDueTick} sampled this tick" +
                (LatestDescentRisk is null ? "." : "; " + LatestDescentRisk.Describe() + "."));
        }
    }

    private void OnDestroy()
    {
        // Plugin shutdown must never leave a frozen cart behind.
        Brake?.ReleaseNow("plugin shutdown");
    }
}
