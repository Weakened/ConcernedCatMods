using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless presenter for the Cart Status panel (CT-005). Selects
/// which cart to show, formats every displayed string (invariant culture),
/// and makes empty/stale situations explicit. Selection is sticky so the
/// panel never flickers between carts under the sampler's round-robin:
/// 1) a cart the local player is pulling always wins (lowest id if the
/// game ever reports several), 2) otherwise the previously shown cart while
/// it is still tracked, 3) otherwise the lowest cart id (stable).</summary>
public static class CartStatusPresenter
{
    /// <summary>Telemetry older than this renders as STALE. Deliberately
    /// below the sampler's 2-second eviction floor, so a dying entry is
    /// visibly stale before it disappears rather than silently frozen.</summary>
    public const double StaleAfterSeconds = 1.5;

    public static CartStatusViewModel Present(
        IReadOnlyDictionary<string, CartTelemetry>? telemetryByCartId,
        string? previouslySelectedCartId,
        double nowSeconds,
        bool telemetryActive)
    {
        if (!telemetryActive)
        {
            return Message(CartStatusState.TelemetryOff,
                TeamsterStrings.Get("status.telemetryOff"));
        }

        if (telemetryByCartId is null || telemetryByCartId.Count == 0)
        {
            return Message(CartStatusState.NoCart, TeamsterStrings.Get("status.noCart"));
        }

        CartTelemetry selected = Select(telemetryByCartId, previouslySelectedCartId);

        double ageSeconds = nowSeconds - selected.SampleTimeSeconds;
        if (ageSeconds < 0d)
        {
            ageSeconds = 0d;
        }

        bool stale = ageSeconds > StaleAfterSeconds;

        return new CartStatusViewModel(
            stale ? CartStatusState.Stale : CartStatusState.Live,
            TeamsterStrings.Get(selected.IsPulledByLocalPlayer ? "status.pullingThisCart" : "status.nearbyCart"),
            TeamsterStrings.Format(
                "status.totalMass", selected.TotalMass.ToString("F1", CultureInfo.InvariantCulture)),
            ComposeBreakdown(selected),
            ComposeGrade(selected),
            TeamsterStrings.Format("status.surface", DescribeSurface(selected.Surface)),
            ComposePull(selected),
            TeamsterStrings.Format(
                stale ? "status.stale" : "status.updated",
                ageSeconds.ToString("F1", CultureInfo.InvariantCulture)),
            selected.CartId);
    }

    private static CartTelemetry Select(
        IReadOnlyDictionary<string, CartTelemetry> telemetryByCartId,
        string? previouslySelectedCartId)
    {
        CartTelemetry? pulled = null;
        CartTelemetry? lowest = null;
        foreach (KeyValuePair<string, CartTelemetry> entry in telemetryByCartId)
        {
            CartTelemetry candidate = entry.Value;
            if (candidate.IsPulledByLocalPlayer &&
                (pulled is null || string.CompareOrdinal(candidate.CartId, pulled.CartId) < 0))
            {
                pulled = candidate;
            }

            if (lowest is null || string.CompareOrdinal(candidate.CartId, lowest.CartId) < 0)
            {
                lowest = candidate;
            }
        }

        if (pulled is not null)
        {
            return pulled;
        }

        if (previouslySelectedCartId is not null &&
            telemetryByCartId.TryGetValue(previouslySelectedCartId, out CartTelemetry previous))
        {
            return previous;
        }

        return lowest!;
    }

    private static CartStatusViewModel Message(CartStatusState state, string message)
    {
        return new CartStatusViewModel(
            state, message,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty);
    }

    private static string ComposeBreakdown(CartTelemetry telemetry)
    {
        string baseMass = telemetry.BaseMass.ToString("F1", CultureInfo.InvariantCulture);
        if (!telemetry.CargoDataAvailable)
        {
            return TeamsterStrings.Format("status.breakdownCargoUnknown", baseMass);
        }

        string cargo = telemetry.CargoWeight.ToString("F1", CultureInfo.InvariantCulture);
        if (telemetry.ItemWeightMassFactor != 1f)
        {
            return TeamsterStrings.Format(
                "status.breakdownScaled", baseMass, cargo,
                telemetry.ItemWeightMassFactor.ToString("F2", CultureInfo.InvariantCulture));
        }

        return TeamsterStrings.Format("status.breakdown", baseMass, cargo);
    }

    private static string ComposeGrade(CartTelemetry telemetry)
    {
        if (!telemetry.GradeAvailable)
        {
            return TeamsterStrings.Get("status.gradeUnavailable");
        }

        string word = TeamsterStrings.Get(telemetry.GradeDirection switch
        {
            GradeDirection.Climbing => "status.gradeWordClimbing",
            GradeDirection.Descending => "status.gradeWordDescending",
            _ => "status.gradeWordLevel",
        });
        return TeamsterStrings.Format(
            "status.grade",
            telemetry.SmoothedGradePercent.ToString("F1", CultureInfo.InvariantCulture),
            word);
    }

    private static string ComposePull(CartTelemetry telemetry)
    {
        if (telemetry.IsPulledByLocalPlayer)
        {
            return TeamsterStrings.Get("status.pulledByYou");
        }

        return TeamsterStrings.Get(
            telemetry.IsAttached ? "status.attachedOtherPuller" : "status.notAttached");
    }

    private static string DescribeSurface(TerrainSurfaceKind surface)
    {
        return TeamsterStrings.Get(surface switch
        {
            TerrainSurfaceKind.Untouched => "status.surfaceUntouched",
            TerrainSurfaceKind.Dirt => "status.surfaceDirt",
            TerrainSurfaceKind.Cultivated => "status.surfaceCultivated",
            TerrainSurfaceKind.Paved => "status.surfacePaved",
            _ => "status.surfaceUnknown",
        });
    }
}
