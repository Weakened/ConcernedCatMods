using System.Globalization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Immutable descent risk for one cart at one sample instant
/// (CT-011): the risk where the cart is now, and the risk of the worst
/// upcoming downgrade within the bounded lookahead. Lookahead may be
/// unavailable (disabled, unloaded terrain, flipped cart) — flagged, never
/// defaulted.</summary>
public sealed class DescentRiskInfo
{
    public DescentRiskInfo(
        string cartId,
        RiskVerdict current,
        bool lookaheadAvailable,
        float worstAheadDownGradePercent,
        RiskVerdict? lookahead,
        double sampleTimeSeconds)
    {
        CartId = cartId;
        Current = current;
        LookaheadAvailable = lookaheadAvailable;
        WorstAheadDownGradePercent = worstAheadDownGradePercent;
        Lookahead = lookahead;
        SampleTimeSeconds = sampleTimeSeconds;
    }

    public string CartId { get; }

    /// <summary>Risk at the cart's current position and speed.</summary>
    public RiskVerdict Current { get; }

    public bool LookaheadAvailable { get; }

    /// <summary>Steepest upcoming downgrade magnitude within the lookahead
    /// window (0 when the path ahead never descends). Meaningful only when
    /// <see cref="LookaheadAvailable"/>.</summary>
    public float WorstAheadDownGradePercent { get; }

    /// <summary>Risk of the worst upcoming downgrade at current mass and
    /// speed; null when lookahead is unavailable.</summary>
    public RiskVerdict? Lookahead { get; }

    public double SampleTimeSeconds { get; }

    /// <summary>One diagnostic line for logs/panels, freshness-agnostic.</summary>
    public string Describe()
    {
        string ahead = LookaheadAvailable && Lookahead is not null
            ? Lookahead.Level + " (worst ahead " +
              WorstAheadDownGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "% down)"
            : "unavailable";
        return "descent risk: here " + Current.Level + ", ahead " + ahead;
    }
}
