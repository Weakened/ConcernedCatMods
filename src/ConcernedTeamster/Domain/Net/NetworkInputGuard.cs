using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Net;

/// <summary>Bounds and validates every network-derived numeric Teamster
/// consumes (CT-029). Values on a remote-owned cart arrive through the
/// game's replication — potentially stale, partial, or garbage — so each is
/// treated as hostile: non-finite becomes the safe default, negatives and
/// over-cap values clamp to the documented range, and the result is ALWAYS
/// finite and in range. Pure and allocation-free; the caller decides whether
/// to log (see <see cref="OncePerKeyGate"/>). Caps sit far above any real
/// cart value, so a legitimate reading is never altered.</summary>
public static class NetworkInputGuard
{
    /// <summary>Upper bound for any single mass/weight field. A loaded
    /// vanilla cart is a few thousand at most; 1e7 only ever trips on
    /// corrupt/hostile data.</summary>
    public const float MaxMass = 1e7f;

    /// <summary>Upper bound for the cargo-weight-to-mass multiplier.</summary>
    public const float MaxMassFactor = 1e4f;

    /// <summary>Speed magnitude cap (m/s). Vanilla carts move single digits;
    /// this only clamps teleport-glitch or corrupt velocity spikes.</summary>
    public const float MaxSpeed = 1e4f;

    /// <summary>Grade percent magnitude cap. Real terrain is well under
    /// this; beyond it the reading is meaningless.</summary>
    public const float MaxGradePercent = 1e4f;

    public readonly struct Sanitized
    {
        public Sanitized(float value, bool adjusted)
        {
            Value = value;
            Adjusted = adjusted;
        }

        /// <summary>Always finite and within the requested range.</summary>
        public float Value { get; }

        /// <summary>True when the raw input was non-finite or out of range
        /// and had to be corrected — the caller may log this once.</summary>
        public bool Adjusted { get; }
    }

    /// <summary>Clamps a value to [0, cap], mapping NaN/Inf to 0.</summary>
    public static Sanitized NonNegative(float raw, float cap)
    {
        if (float.IsNaN(raw) || float.IsInfinity(raw))
        {
            return new Sanitized(0f, adjusted: true);
        }

        if (raw < 0f)
        {
            return new Sanitized(0f, adjusted: true);
        }

        if (raw > cap)
        {
            return new Sanitized(cap, adjusted: true);
        }

        return new Sanitized(raw, adjusted: false);
    }

    /// <summary>Clamps a signed value to [-cap, cap], mapping NaN/Inf to 0.</summary>
    public static Sanitized Signed(float raw, float cap)
    {
        if (float.IsNaN(raw) || float.IsInfinity(raw))
        {
            return new Sanitized(0f, adjusted: true);
        }

        if (raw > cap)
        {
            return new Sanitized(cap, adjusted: true);
        }

        if (raw < -cap)
        {
            return new Sanitized(-cap, adjusted: true);
        }

        return new Sanitized(raw, adjusted: false);
    }

    public static Sanitized Mass(float raw) => NonNegative(raw, MaxMass);

    public static Sanitized MassFactor(float raw) => NonNegative(raw, MaxMassFactor);

    public static Sanitized Speed(float raw) => Signed(raw, MaxSpeed);

    public static Sanitized Grade(float raw) => Signed(raw, MaxGradePercent);

    /// <summary>Bounds a display string from network-derived data (e.g. a
    /// remote player or cart label): trims, caps length, and strips control
    /// characters so a hostile name cannot break a log line or a panel.</summary>
    public static string Label(string? raw, int maxLength = 32)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string trimmed = raw!.Trim();
        int length = Math.Min(trimmed.Length, maxLength);
        char[] buffer = new char[length];
        for (int index = 0; index < length; index++)
        {
            char c = trimmed[index];
            buffer[index] = char.IsControl(c) ? ' ' : c;
        }

        return new string(buffer);
    }
}
