using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Carts;

/// <summary>The bounded cart telemetry pipeline (CT-003): pure scheduling,
/// budget, and state logic with an injected clock and injected game
/// callbacks, so every behavior is provable off-game. Steady-state discipline:
/// the not-due path is one comparison with zero allocation, due ticks reuse
/// buffers, and this class never logs — the runtime pump owns the (gated,
/// rate-limited) debug summary instead.</summary>
public sealed class TelemetrySampler
{
    private readonly TelemetrySamplerOptions _options;
    private readonly Action<List<object>, TelemetrySamplerOptions> _collectNearbyCarts;
    private readonly Func<object, double, IReadOnlyDictionary<string, CartTelemetry>, CartTelemetry?> _sampleCart;
    private readonly List<object> _cartBuffer = new();
    private readonly List<string> _staleKeyBuffer = new();
    private readonly Dictionary<string, CartTelemetry> _telemetryByCartId = new();
    private double _nextDueTimeSeconds;
    private int _rotation;

    /// <param name="collectNearbyCarts">Fills the (cleared) buffer with cart
    /// components near the local player, nearest first, bounded by the
    /// options. Must not throw; the adapter fails closed.</param>
    /// <param name="sampleCart">Maps one cart component to telemetry, or null
    /// when the cart is gone or unreadable. Receives the live store so
    /// stateful derivations (CT-004 grade smoothing) can read the cart's
    /// previous sample — and inherit reset/eviction lifecycle for free.
    /// Must not throw and must not mutate the store.</param>
    public TelemetrySampler(
        TelemetrySamplerOptions options,
        Action<List<object>, TelemetrySamplerOptions> collectNearbyCarts,
        Func<object, double, IReadOnlyDictionary<string, CartTelemetry>, CartTelemetry?> sampleCart)
    {
        _options = options;
        _collectNearbyCarts = collectNearbyCarts;
        _sampleCart = sampleCart;
    }

    public TelemetrySamplerOptions Options => _options;

    /// <summary>Latest telemetry keyed by cart id. Live view: entries update
    /// on due ticks and disappear on eviction or reset.</summary>
    public IReadOnlyDictionary<string, CartTelemetry> TelemetryByCartId => _telemetryByCartId;

    public int TrackedCartCount => _telemetryByCartId.Count;

    /// <summary>Successful samples on the most recent due tick (debug
    /// summary input; not part of the budget, which counts attempts).</summary>
    public int SampledOnLastDueTick { get; private set; }

    /// <summary>Advances the pipeline. Returns true only when a due tick ran.
    /// Not-due calls are the steady-state fast path: one comparison, no
    /// allocation, no callback invocation.</summary>
    public bool Tick(double nowSeconds)
    {
        if (nowSeconds < _nextDueTimeSeconds)
        {
            return false;
        }

        // Schedule from "now", not from the previous due time: after a long
        // pause this runs once instead of burning a catch-up burst.
        _nextDueTimeSeconds = nowSeconds + _options.SampleIntervalSeconds;

        _cartBuffer.Clear();
        _collectNearbyCarts(_cartBuffer, _options);

        int cartCount = _cartBuffer.Count;
        int attempts = 0;
        int sampled = 0;
        if (cartCount > 0)
        {
            // Round-robin start index so a budget smaller than the nearby
            // cart count still reaches every cart across successive ticks
            // instead of starving the farther ones.
            int start = _rotation % cartCount;
            _rotation = (_rotation + 1) & int.MaxValue;
            for (int offset = 0; offset < cartCount && attempts < _options.MaxCartsPerTick; offset++)
            {
                object cart = _cartBuffer[(start + offset) % cartCount];
                attempts++;
                CartTelemetry? telemetry = _sampleCart(cart, nowSeconds, _telemetryByCartId);
                if (telemetry is null)
                {
                    continue;
                }

                if (!_telemetryByCartId.ContainsKey(telemetry.CartId) &&
                    _telemetryByCartId.Count >= _options.MaxTrackedCarts)
                {
                    continue;
                }

                _telemetryByCartId[telemetry.CartId] = telemetry;
                sampled++;
            }
        }

        SampledOnLastDueTick = sampled;
        EvictStale(nowSeconds);

        // Never keep game object references between ticks: a destroyed cart
        // must not be pinned by the buffer.
        _cartBuffer.Clear();
        return true;
    }

    /// <summary>Forgets everything and makes the next tick due immediately.
    /// Called on logout/world switch so no other world's carts can ever be
    /// shown; idempotent and allocation-free when already clear.</summary>
    public void Reset()
    {
        _telemetryByCartId.Clear();
        _cartBuffer.Clear();
        _staleKeyBuffer.Clear();
        _nextDueTimeSeconds = 0d;
        _rotation = 0;
        SampledOnLastDueTick = 0;
    }

    /// <summary>Removes entries not refreshed within the eviction window —
    /// the path that forgets destroyed carts (they stop being collected, go
    /// stale, and drop out) without ever touching game objects.</summary>
    private void EvictStale(double nowSeconds)
    {
        _staleKeyBuffer.Clear();
        foreach (KeyValuePair<string, CartTelemetry> entry in _telemetryByCartId)
        {
            if (nowSeconds - entry.Value.SampleTimeSeconds > _options.EvictAfterSeconds)
            {
                _staleKeyBuffer.Add(entry.Key);
            }
        }

        for (int index = 0; index < _staleKeyBuffer.Count; index++)
        {
            _telemetryByCartId.Remove(_staleKeyBuffer[index]);
        }

        _staleKeyBuffer.Clear();
    }
}
