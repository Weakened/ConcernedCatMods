using System.Globalization;
using System.Text;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure verdict logic for the live player/road alignment
/// diagnostic (DEF-v1.0-006, `cc_roads align live`). The four error
/// classes are answered SEPARATELY so a failure localizes itself:
/// A observation (is the stored geometry where the player stands?),
/// B projection (does the native pixel projection agree with the CC
/// overlay projection for the same coordinate?), C render resolution
/// (is correct data visibly quantized by the 2048-texel texture?), and
/// D marker anchor (does the live player marker sit where the canonical
/// projection says it should?). No magic offsets anywhere.</summary>
internal static class AlignmentVerdicts
{
    public const float ObservationPassMeters = 3.0f;
    public const float ProjectionPassTexels = 1.0f;
    public const float MarkerPassPixels = 2.0f;

    /// <summary>Screen pixels per texel above which texture quantization
    /// is judged visibly coarse at the current zoom.</summary>
    public const float QuantizationVisiblePixelsPerTexel = 4.0f;

    public static string Evaluate(
        bool standingOnRoad,
        bool hasNearestRoadPoint,
        float nearestRoadDistanceMeters,
        bool hasProjectionDelta,
        float projectionDeltaTexels,
        float screenPixelsPerTexel,
        bool vectorLayerActive,
        bool hasMarkerDelta,
        float markerDeltaPixels)
    {
        var report = new StringBuilder();

        if (!standingOnRoad)
        {
            report.Append("A observation: N/A — no road paint classified under the player (stand on the road to test).");
        }
        else if (!hasNearestRoadPoint)
        {
            report.Append("A observation: FAIL — standing on road paint but NO stored road point nearby (observation/source pipeline).");
        }
        else if (nearestRoadDistanceMeters <= ObservationPassMeters)
        {
            report.Append(Invariant($"A observation: PASS — nearest stored road point {nearestRoadDistanceMeters:0.00} m from the player (≤ {ObservationPassMeters:0.0} m)."));
        }
        else
        {
            report.Append(Invariant($"A observation: FAIL — nearest stored road point is {nearestRoadDistanceMeters:0.00} m away; stored geometry does not match the player position."));
        }

        report.Append('\n');
        if (!hasProjectionDelta)
        {
            report.Append("B projection: N/A — native pixel projection unavailable.");
        }
        else if (projectionDeltaTexels <= ProjectionPassTexels)
        {
            report.Append(Invariant($"B projection: PASS — native vs CC projection differ by {projectionDeltaTexels:0.00} texels (≤ {ProjectionPassTexels:0.0})."));
        }
        else
        {
            report.Append(Invariant($"B projection: FAIL — native vs CC projection differ by {projectionDeltaTexels:0.00} texels; projections diverge."));
        }

        report.Append('\n');
        if (screenPixelsPerTexel <= QuantizationVisiblePixelsPerTexel)
        {
            report.Append(Invariant($"C render resolution: PASS — one texel spans {screenPixelsPerTexel:0.0} px at this zoom; texture quantization is not visually significant."));
        }
        else if (vectorLayerActive)
        {
            report.Append(Invariant($"C render resolution: PASS — one texel spans {screenPixelsPerTexel:0.0} px, but the high-precision vector layer is ACTIVE (sub-texel road ink)."));
        }
        else
        {
            report.Append(Invariant($"C render resolution: FAIL — one texel spans {screenPixelsPerTexel:0.0} px at this zoom and the vector layer is INACTIVE: texture road ink visibly snaps up to ±half a texel (enable Map/HighPrecisionLargeMapRoads)."));
        }

        report.Append('\n');
        if (!hasMarkerDelta)
        {
            report.Append("D marker anchor: N/A — live player marker not available (large map closed?).");
        }
        else if (markerDeltaPixels <= MarkerPassPixels)
        {
            report.Append(Invariant($"D marker anchor: PASS — live player marker sits {markerDeltaPixels:0.00} px from the canonical projection (≤ {MarkerPassPixels:0.0} px)."));
        }
        else
        {
            report.Append(Invariant($"D marker anchor: FAIL — live player marker is {markerDeltaPixels:0.00} px from the canonical projection; the marker anchor itself is offset."));
        }

        return report.ToString();
    }

    private static string Invariant(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }
}
