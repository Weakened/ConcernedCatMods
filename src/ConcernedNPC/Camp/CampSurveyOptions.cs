using System;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>The four numbers that decide what camp is, and one that decides how
/// much work finding it may cost.
///
/// <b>Every one of them is a role's setting, not a constant here.</b> A
/// longhouse settlement and a one-hut outpost want different link distances,
/// and a role that wants the defaults simply takes
/// <see cref="Default"/>. Out-of-range values are clamped rather than thrown
/// on: these arrive from a player's configuration file, where a zero or a
/// negative is a typo and a crash on world load is not a proportionate
/// answer.</summary>
internal sealed class CampSurveyOptions
{
    internal CampSurveyOptions(
        float seedRadiusMetres,
        float linkDistanceMetres,
        float perimeterMarginMetres,
        int maxMembers)
    {
        SeedRadiusMetres = Clamp(seedRadiusMetres, 4f, 128f, 24f);
        LinkDistanceMetres = Clamp(linkDistanceMetres, 2f, 32f, 8f);
        PerimeterMarginMetres = Clamp(perimeterMarginMetres, 0f, 32f, 4f);
        MaxMembers = maxMembers < 16 ? 16 : maxMembers > 8192 ? 8192 : maxMembers;
    }

    /// <summary>The settings a role gets by asking for nothing: seed 24 m, link
    /// 8 m, margin 4 m, at most 2,048 members.
    ///
    /// The link distance is the load-bearing one and 8 m is chosen, not
    /// inherited: it is wide enough to join a house to the workbench outside it
    /// and to bridge the gap a fence leaves, and narrow enough that a lone
    /// torch on a hill does not reach back to the village. The 25 m radius the
    /// shipped companion sensing uses is a different measurement for a
    /// different purpose - how far away a fire still counts as his fire - and
    /// is not this number.</summary>
    internal static CampSurveyOptions Default { get; } =
        new CampSurveyOptions(24f, 8f, 4f, 2048);

    /// <summary>How far from the anchor a piece may be and still be a seed.
    /// Seeds are where growth starts, so this is also the promise that camp
    /// begins at home: a cluster that touches no seed is never camp, however
    /// large it is.</summary>
    internal float SeedRadiusMetres { get; }

    /// <summary><b>The bounded structural proximity.</b> Two member pieces
    /// within this distance of one another are in the same camp; a piece within
    /// it of nothing is not in camp at all. This single number is what stops
    /// one stray distant piece from stretching camp across the map, because
    /// membership is connectivity rather than a radius.</summary>
    internal float LinkDistanceMetres { get; }

    /// <summary>How far outside the outermost pieces the perimeter is drawn, so
    /// that standing against the wall of the outermost house is still inside.
    /// </summary>
    internal float PerimeterMarginMetres { get; }

    /// <summary>The survey's budget, in members. A camp larger than this is
    /// reported truncated rather than either refused or allowed to cost
    /// whatever it costs - the same choice the shipped source survey makes, and
    /// for the same reason: a player with a very large base should get a
    /// slightly small camp, not a stutter.</summary>
    internal int MaxMembers { get; }

    private static float Clamp(float value, float low, float high, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return value < low ? low : value > high ? high : value;
    }

    public override string ToString() =>
        "seed " + Round(SeedRadiusMetres) + " m, link " + Round(LinkDistanceMetres) + " m, margin " +
        Round(PerimeterMarginMetres) + " m, at most " + MaxMembers + " pieces";

    private static string Round(float value) => Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
