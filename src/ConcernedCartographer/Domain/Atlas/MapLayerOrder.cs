namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>#419: where the high-precision road and route ink sits relative
/// to the map's pins on the LARGE map. BelowPins is the shipped behaviour
/// (ink under the map image's pins, so every marker stays on top);
/// AbovePins lifts the ink over the pin icons — still under the pin names,
/// so a label is never covered by a road.</summary>
public enum RoadLayerPosition
{
    BelowPins = 0,
    AbovePins = 1,
}

/// <summary>Which parent the large-map vector frame is built under.</summary>
internal enum RoadLayerParent
{
    /// <summary>A child of the large map image, after Jötunn's overlay:
    /// beneath every pin, as shipped.</summary>
    MapImage,

    /// <summary>A sibling placed directly after the large map's pin root:
    /// above the pin icons, beneath anything later (the pin names).</summary>
    AfterPinRoot,
}

/// <summary>The pure placement decision, so the fallback is a tested rule
/// rather than a hope. AbovePins needs a pin root whose parent lies inside
/// the large map's root: that parent is where the frame goes, and only
/// inside the large root does the frame close (and the minimap's texture
/// ink return) when the map closes. Anything else falls back to the
/// shipped placement, which is always available because it only needs the
/// map image.</summary>
internal static class RoadLayerPlacement
{
    public static RoadLayerParent Resolve(RoadLayerPosition requested, bool pinRootAvailable, bool pinRootParentInsideLargeMap)
    {
        return requested == RoadLayerPosition.AbovePins && pinRootAvailable && pinRootParentInsideLargeMap
            ? RoadLayerParent.AfterPinRoot
            : RoadLayerParent.MapImage;
    }

    /// <summary>The local scale that makes a frame under
    /// <paramref name="parentLossy"/> match the map image's world scale
    /// <paramref name="imageLossy"/> on one axis. A degenerate parent scale
    /// (zero, or a non-finite value from a collapsing canvas) yields 1, so a
    /// transient layout frame can never produce an infinite transform.</summary>
    public static float FrameScale(float imageLossy, float parentLossy)
    {
        if (float.IsNaN(imageLossy) || float.IsInfinity(imageLossy) ||
            float.IsNaN(parentLossy) || float.IsInfinity(parentLossy) ||
            System.Math.Abs(parentLossy) < 1e-6f)
        {
            return 1f;
        }

        return imageLossy / parentLossy;
    }
}

/// <summary>#419: what to do with Cartographer's road and route ink on the
/// SMALL minimap when another mod reshapes it. Round Minimap turns the
/// minimap into a disc and rotates it by changing the map image's own mesh;
/// the shared texture overlay layer the ink is drawn in is a separate image
/// that keeps its square, north-up shape, so the ink would point the wrong
/// way. Hide (default) leaves the ink off the minimap while that is so;
/// Show draws it anyway.</summary>
public enum ReshapedMinimapRoads
{
    Hide = 0,
    Show = 1,
}

internal static class MinimapReshapeRule
{
    /// <summary>Whether the texture overlays must be held off right now.
    /// Only the minimap is affected: with the large map open the texture is
    /// the large map's fallback and is never reshaped, so it is never held
    /// off. A reshaping mod that is installed but not currently reshaping
    /// (its round shape switched off) holds nothing off.</summary>
    public static bool SuppressTexture(
        bool reshaperInstalled,
        bool reshapeActive,
        ReshapedMinimapRoads policy,
        bool largeMapOpen)
    {
        return policy == ReshapedMinimapRoads.Hide &&
            reshaperInstalled &&
            reshapeActive &&
            !largeMapOpen;
    }
}
