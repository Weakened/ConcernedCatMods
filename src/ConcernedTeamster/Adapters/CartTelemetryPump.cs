using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
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
    private bool _resetWhileNoLocalPlayer;
    private double _nextDebugSummaryTime;

    /// <summary>Latest telemetry by cart id for future consumers (CT-005
    /// panels); null until initialized.</summary>
    public IReadOnlyDictionary<string, CartTelemetry>? Telemetry => _sampler?.TelemetryByCartId;

    /// <summary>Wires the sampler and returns the effective (clamped)
    /// options so startup can log them.</summary>
    internal TelemetrySamplerOptions Initialize(TeamsterSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            settings.SampleIntervalSeconds.Value,
            settings.SearchRadiusMeters.Value,
            settings.MaxCartsPerTick.Value,
            settings.MaxTrackedCarts.Value);
        _sampler = new TelemetrySampler(options, CartAdapter.CollectNearbyCarts, CartAdapter.TrySampleCart);
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

        if (_settings is { } settings && settings.DebugLogging.Value && now >= _nextDebugSummaryTime)
        {
            _nextDebugSummaryTime = now + DebugSummaryPeriodSeconds;
            _log?.LogDebug(
                $"Cart telemetry: {sampler.TrackedCartCount} tracked, " +
                $"{sampler.SampledOnLastDueTick} sampled this tick.");
        }
    }
}
