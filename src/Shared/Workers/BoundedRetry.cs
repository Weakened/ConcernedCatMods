using System;

namespace TheConcernedCat.Workers;

/// <summary>What a bounded retry says after a failure.</summary>
internal readonly struct RetryDecision
{
    public RetryDecision(bool giveUp, float retryAt, int failures)
    {
        GiveUp = giveUp;
        RetryAt = retryAt;
        Failures = failures;
    }

    /// <summary>The ceiling was reached: stop and report, never escalate.</summary>
    public bool GiveUp { get; }

    /// <summary>The earliest time, on the caller's clock, to try again.</summary>
    public float RetryAt { get; }

    public int Failures { get; }
}

/// <summary>Retries with a ceiling and a doubling backoff. Every asynchronous
/// phase of a worker (approach, hitch, pickup, transfer, rendezvous) uses one,
/// so nothing retries forever and nothing hammers the game while it waits.
/// Clock-free: the caller passes its own time in seconds.</summary>
internal sealed class BoundedRetry
{
    public BoundedRetry(int maxFailures, float firstDelaySeconds, float maxDelaySeconds)
    {
        if (maxFailures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFailures), "At least one failure must be allowed.");
        }

        if (firstDelaySeconds < 0f || float.IsNaN(firstDelaySeconds) ||
            maxDelaySeconds < firstDelaySeconds || float.IsNaN(maxDelaySeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(firstDelaySeconds), "Delays must be 0 <= first <= max.");
        }

        MaxFailures = maxFailures;
        FirstDelaySeconds = firstDelaySeconds;
        MaxDelaySeconds = maxDelaySeconds;
    }

    public int MaxFailures { get; }

    public float FirstDelaySeconds { get; }

    public float MaxDelaySeconds { get; }

    public int Failures { get; private set; }

    public float RetryAt { get; private set; }

    public bool IsExhausted => Failures >= MaxFailures;

    public bool IsWaiting(float now) => !IsExhausted && Failures > 0 && now < RetryAt;

    public RetryDecision RecordFailure(float now)
    {
        if (!IsExhausted)
        {
            Failures++;
        }

        if (IsExhausted)
        {
            return new RetryDecision(true, float.PositiveInfinity, Failures);
        }

        float delay = FirstDelaySeconds;
        for (int failure = 1; failure < Failures && delay < MaxDelaySeconds; failure++)
        {
            delay *= 2f;
        }

        RetryAt = now + Math.Min(delay, MaxDelaySeconds);
        return new RetryDecision(false, RetryAt, Failures);
    }

    public void Reset()
    {
        Failures = 0;
        RetryAt = 0f;
    }
}

/// <summary>A phase's time limit. Every asynchronous phase has one (CART-06).
/// </summary>
internal readonly struct PhaseDeadline
{
    public PhaseDeadline(float startedAt, float limitSeconds)
    {
        if (limitSeconds <= 0f || float.IsNaN(limitSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(limitSeconds), "A phase limit must be positive.");
        }

        StartedAt = startedAt;
        LimitSeconds = limitSeconds;
    }

    public float StartedAt { get; }

    public float LimitSeconds { get; }

    public bool IsExpired(float now) => now - StartedAt >= LimitSeconds;

    public float Remaining(float now) => Math.Max(0f, LimitSeconds - (now - StartedAt));
}

/// <summary>At most one notification per key per cooldown, so a state that
/// persists for minutes says so once, not every frame (COOP-04).</summary>
internal sealed class AttentionThrottle
{
    private readonly System.Collections.Generic.Dictionary<string, float> _lastShown =
        new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);

    public AttentionThrottle(float cooldownSeconds)
    {
        if (cooldownSeconds <= 0f || float.IsNaN(cooldownSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(cooldownSeconds), "A cooldown must be positive.");
        }

        CooldownSeconds = cooldownSeconds;
    }

    public float CooldownSeconds { get; }

    public bool ShouldNotify(string key, float now)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (_lastShown.TryGetValue(key, out float last) && now - last < CooldownSeconds)
        {
            return false;
        }

        _lastShown[key] = now;
        return true;
    }

    public void Forget(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _lastShown.Remove(key);
        }
    }
}
