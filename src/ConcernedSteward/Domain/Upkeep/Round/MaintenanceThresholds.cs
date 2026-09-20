using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>When a light is worth walking to, and how much of it to fill.
///
/// <b>Measured in time, not in fuel.</b> A hearth at 4 of 10 and a torch at 4 of
/// 10 are the same number and not the same situation: vanilla burns one unit
/// every <c>m_secPerFuel</c> seconds, and that field differs from piece to
/// piece. So the question this product asks is "how long has this one got",
/// which is the question #382 actually poses — <i>prioritise what is closest to
/// going out rather than topping everything to full</i> — and which a fuel level
/// on its own cannot answer.
///
/// <b>And the top-up target is a time as well.</b> Filling every light to
/// <c>m_maxFuel</c> is the behaviour worth avoiding: it spends a player's whole
/// chest on the lights that need it least, and it is what makes a round take
/// four trips that could have taken one. Topping a light up to a stated number
/// of minutes of light spends the fewest units that answer the actual problem.
///
/// <b>Every bound is configurable and every one of them is validated.</b> A
/// threshold above the top-up target would mean a light is serviced and
/// immediately wants servicing again, which is an NPC in a loop rather than a
/// setting somebody chose.</summary>
internal readonly struct MaintenanceThresholds
{
    internal MaintenanceThresholds(
        float refuelBelowSeconds,
        float refuelToSeconds,
        int mostUnitsPerLight)
    {
        if (!(refuelBelowSeconds > 0f) || float.IsInfinity(refuelBelowSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refuelBelowSeconds), refuelBelowSeconds,
                "A light is serviced when it has less than some positive, finite amount of " +
                "time left. Zero would mean waiting for it to go out first.");
        }

        if (!(refuelToSeconds >= refuelBelowSeconds) || float.IsInfinity(refuelToSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refuelToSeconds), refuelToSeconds,
                "Topping a light up to less than the level that made it worth servicing would " +
                "leave it wanting servicing the moment it was serviced.");
        }

        if (mostUnitsPerLight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mostUnitsPerLight), mostUnitsPerLight,
                "A light that may be given nothing is not a light this round can service.");
        }

        RefuelBelowSeconds = refuelBelowSeconds;
        RefuelToSeconds = refuelToSeconds;
        MostUnitsPerLight = mostUnitsPerLight;
    }

    /// <summary>A light with less than this much burning time left is worth a
    /// stop. One that has more is left alone, whatever its fuel level reads.
    /// </summary>
    public float RefuelBelowSeconds { get; }

    /// <summary>How much burning time a serviced light is brought up to. Never
    /// past the piece's own capacity, which vanilla clamps anyway.</summary>
    public float RefuelToSeconds { get; }

    /// <summary>The most units one light may be given in one round, whatever
    /// the arithmetic says. A bonfire with an enormous capacity beside a full
    /// chest would otherwise be a single stop that carries the whole chest.
    /// </summary>
    public int MostUnitsPerLight { get; }

    /// <summary>How much time one unit of fuel buys in a piece that burns one
    /// every <paramref name="secondsPerUnit"/> seconds — and the answer for a
    /// piece that burns none, which is that it never runs out.</summary>
    internal static float SecondsLeft(float fuel, float secondsPerUnit)
    {
        if (!(secondsPerUnit > 0f) || float.IsNaN(secondsPerUnit) || float.IsInfinity(secondsPerUnit))
        {
            // Vanilla's UpdateFireplace only decays when m_secPerFuel is above
            // zero. A piece that never consumes has all the time there is, and
            // reporting it as urgent would send somebody to feed a light that
            // is in no danger.
            return float.PositiveInfinity;
        }

        if (float.IsNaN(fuel) || fuel <= 0f)
        {
            return 0f;
        }

        return fuel * secondsPerUnit;
    }

    /// <summary>The fuel level this much burning time corresponds to, bounded by
    /// the piece's own capacity.</summary>
    internal float TargetLevel(float maxFuel, float secondsPerUnit)
    {
        if (!(secondsPerUnit > 0f) || float.IsNaN(secondsPerUnit) || float.IsInfinity(secondsPerUnit))
        {
            return 0f;
        }

        float wanted = RefuelToSeconds / secondsPerUnit;
        if (float.IsNaN(maxFuel) || maxFuel <= 0f)
        {
            return 0f;
        }

        return wanted > maxFuel ? maxFuel : wanted;
    }

    /// <summary>The shipped values.
    ///
    /// Five minutes of light left is the point at which a player walking their
    /// own base would notice a fire getting low; twenty minutes is what it is
    /// topped up to, which is over a third of a vanilla day and comfortably more
    /// than a round takes. Ten units a light is one vanilla hearth's whole
    /// capacity, so a single stop can always finish a hearth and can never
    /// strip a chest.</summary>
    public static MaintenanceThresholds Default => new MaintenanceThresholds(
        refuelBelowSeconds: 300f,
        refuelToSeconds: 1200f,
        mostUnitsPerLight: 10);

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "below {0:0}s, up to {1:0}s, at most {2} a light",
        RefuelBelowSeconds, RefuelToSeconds, MostUnitsPerLight);
}
