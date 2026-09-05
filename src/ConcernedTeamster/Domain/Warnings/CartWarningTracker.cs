using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Warnings;

/// <summary>Evaluates load/grade warnings (CT-009) — called exactly once
/// per new telemetry snapshot and never anywhere else; reads are free.
/// Level dynamics: a higher level applies immediately (safety first), a
/// lower level applies only after <see cref="WarningOptions.FallHoldSeconds"/>
/// of continuously lower evaluations, and the steep-grade cause releases
/// only below (enter − <see cref="WarningOptions.ExitBandPercent"/>) — so
/// riding a boundary produces one transition pair, never a stream.
///
/// Causes, in precedence order:
/// 1. Calibrated verdict No while climbing → DANGER (proven failure).
/// 2. Calibrated verdict Marginal while climbing → CAUTION.
/// 3. Smoothed climb at/above the steep threshold → CAUTION (a terrain
///    fact — the text says what is steep, not that the cart will fail).
/// An Unknown verdict never warns: uncalibrated is not danger.</summary>
public sealed class CartWarningTracker
{
    private sealed class CartState
    {
        public WarningLevel Level;
        public CartWarning? Warning;
        public double LowerSinceSeconds = double.NaN;
        public bool SteepCauseActive;
        public double LastUpdateSeconds;
    }

    private readonly LoadModel? _loadModel;
    private readonly Dictionary<string, CartState> _stateByCartId = new();
    private readonly List<string> _staleKeyBuffer = new();

    public CartWarningTracker(LoadModel? loadModel)
    {
        _loadModel = loadModel;
    }

    /// <summary>Total evaluations performed — lets tests assert that only
    /// snapshot updates evaluate.</summary>
    public int EvaluationCount { get; private set; }

    /// <summary>The current warning for a cart, or null. Never evaluates.</summary>
    public CartWarning? TryGet(string cartId)
    {
        return _stateByCartId.TryGetValue(cartId, out CartState? state) ? state.Warning : null;
    }

    /// <summary>Evaluates one new snapshot. The snapshot's own sample time
    /// is the clock, so behavior is exactly reproducible in tests.</summary>
    public CartWarning? Update(CartTelemetry telemetry, WarningOptions options)
    {
        EvaluationCount++;

        if (!_stateByCartId.TryGetValue(telemetry.CartId, out CartState? state))
        {
            state = new CartState();
            _stateByCartId[telemetry.CartId] = state;
        }

        double now = telemetry.SampleTimeSeconds;
        state.LastUpdateSeconds = now;

        (WarningLevel targetLevel, CartWarning? targetWarning) = EvaluateTarget(telemetry, options, state);

        if (targetLevel > state.Level)
        {
            // Rise immediately.
            state.Level = targetLevel;
            state.Warning = targetWarning;
            state.LowerSinceSeconds = double.NaN;
        }
        else if (targetLevel < state.Level)
        {
            // Fall only after a continuous hold.
            if (double.IsNaN(state.LowerSinceSeconds))
            {
                state.LowerSinceSeconds = now;
            }

            if (now - state.LowerSinceSeconds >= WarningOptions.FallHoldSeconds)
            {
                state.Level = targetLevel;
                state.Warning = targetWarning;
                state.LowerSinceSeconds = double.NaN;
            }
        }
        else
        {
            // Same level: adopt the freshest message and clear any pending
            // fall.
            if (targetWarning is not null)
            {
                state.Warning = targetWarning;
            }

            state.LowerSinceSeconds = double.NaN;
        }

        return state.Warning;
    }

    /// <summary>Removes state for carts not updated within the window
    /// (mirrors the sampler's eviction) — bounded memory.</summary>
    public void Sweep(double nowSeconds, double evictAfterSeconds)
    {
        _staleKeyBuffer.Clear();
        foreach (KeyValuePair<string, CartState> entry in _stateByCartId)
        {
            if (nowSeconds - entry.Value.LastUpdateSeconds > evictAfterSeconds)
            {
                _staleKeyBuffer.Add(entry.Key);
            }
        }

        for (int index = 0; index < _staleKeyBuffer.Count; index++)
        {
            _stateByCartId.Remove(_staleKeyBuffer[index]);
        }

        _staleKeyBuffer.Clear();
    }

    public void Reset()
    {
        _stateByCartId.Clear();
        _staleKeyBuffer.Clear();
    }

    private (WarningLevel, CartWarning?) EvaluateTarget(
        CartTelemetry telemetry, WarningOptions options, CartState state)
    {
        if (!telemetry.GradeAvailable || telemetry.SmoothedGradePercent <= 0f)
        {
            state.SteepCauseActive = false;
            return (WarningLevel.None, null);
        }

        float grade = telemetry.SmoothedGradePercent;
        string gradeText = grade.ToString("F0", CultureInfo.InvariantCulture) + "%";
        string massText = telemetry.TotalMass.ToString("F0", CultureInfo.InvariantCulture);

        if (_loadModel is not null)
        {
            LoadVerdict verdict = _loadModel.Query(grade, telemetry.TotalMass);
            if (verdict.Climbability == Climbability.No)
            {
                state.SteepCauseActive = false;
                return (WarningLevel.Danger, new CartWarning(
                    telemetry.CartId, WarningLevel.Danger,
                    TeamsterStrings.Format(
                        "warn.impossibleSituation", gradeText, massText, verdict.Explanation),
                    TeamsterStrings.Get("warn.impossibleAction")));
            }

            if (verdict.Climbability == Climbability.Marginal)
            {
                state.SteepCauseActive = false;
                return (WarningLevel.Caution, new CartWarning(
                    telemetry.CartId, WarningLevel.Caution,
                    TeamsterStrings.Format(
                        "warn.marginalSituation", gradeText, massText, verdict.Explanation),
                    TeamsterStrings.Get("warn.marginalAction")));
            }

            if (verdict.Climbability == Climbability.Yes)
            {
                // Calibration proves this climb: it outranks the generic
                // steep-terrain threshold, so no warning fires.
                state.SteepCauseActive = false;
                return (WarningLevel.None, null);
            }
        }

        // Steep-grade terrain fact, with its own enter/exit band.
        float enter = options.SteepGradeCautionPercent;
        float exit = enter - WarningOptions.ExitBandPercent;
        bool steep = state.SteepCauseActive ? grade > exit : grade >= enter;
        state.SteepCauseActive = steep;
        if (steep)
        {
            return (WarningLevel.Caution, new CartWarning(
                telemetry.CartId, WarningLevel.Caution,
                TeamsterStrings.Format("warn.steepSituation", gradeText, massText),
                TeamsterStrings.Get("warn.steepAction")));
        }

        return (WarningLevel.None, null);
    }
}
