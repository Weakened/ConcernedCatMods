using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Families;

namespace TheConcernedCat.ConcernedLadders.Domain.Railings;

/// <summary>The railing setting (#556). Off by default, as every 1.1.0 addition is: with it off no railing is cloned, no
/// token is added and the hammer menu is unchanged.</summary>
internal static class RailingSettings
{
    internal const string Section = "Railings";

    internal const string Key = "Enabled";

    internal const bool EnabledByDefault = false;

    internal const string Description =
        "UNFINISHED (#556): adds railings to the hammer's Building tab in eight materials (wood, copper, iron, darkwood, " +
        "ashwood, flametal, dvergr and log): level (2 m and 1 m), for the game's stairs (26.57 degrees), for the step ladder " +
        "(45 degrees), and a newel post - none of it yet tested in game. " +
        "Leave this off. Turning it off again, or uninstalling, makes the game delete every placed railing the next time a " +
        "world loads, with no refund. In multiplayer every player and the host or server must have the same value. " +
        "Restart after changing it.";
}

/// <summary>One railing piece (#556): a family's railing of one shape, with its stable prefab ID (a save contract once
/// shipped) and its name. Everything else - donor, material, region, tint, style, health, station, cost - is its family's
/// (<see cref="BuildFamilies"/>).</summary>
internal sealed class RailingPiece
{
    internal RailingPiece(BuildFamily family, RailingAngle angle)
    {
        Family = BuildFamilies.Of(family);
        Angle = angle;
    }

    internal BuildFamilyInfo Family { get; }

    internal RailingAngle Angle { get; }

    internal RailingStyle Style => Family.Style;

    internal string Prefab => "tcc_railing_" + Family.Word + "_" + Angle.ToString().ToLower(CultureInfo.InvariantCulture);

    internal string Token => "cl_railing_" + Family.Word + "_" + Angle.ToString().ToLower(CultureInfo.InvariantCulture);

    internal string English => Family.Name + Angle switch
    {
        RailingAngle.Level => " Railing",
        RailingAngle.Short => " Short Railing",
        RailingAngle.Stair => " Stair Railing",
        RailingAngle.Steep => " Steep Stair Railing",
        _ => " Newel Post",
    };

    /// <summary>The fixed recipe of the ladders' three families; null for a vanilla family, which costs what its donor
    /// pole costs. Lead default: a 2 m railing about half a 2 m ladder, a little more on a slope or a curve.</summary>
    internal IReadOnlyList<RailingCostLine>? Costs
    {
        get
        {
            if (Family.FollowsDonor) return null;
            bool level = Angle == RailingAngle.Level || Angle == RailingAngle.Newel;
            bool small = Angle == RailingAngle.Short;
            return Family.Family switch
            {
                BuildFamily.Wood => new[] { new RailingCostLine("Wood", small ? 1 : level ? 2 : 3) },
                BuildFamily.Copper => new[] { new RailingCostLine("Copper", 1), new RailingCostLine("Wood", 1) },
                _ => new[] { new RailingCostLine("Iron", 1), new RailingCostLine("Wood", 1) },
            };
        }
    }
}

/// <summary>Every railing (#556): every family in every shape.</summary>
internal static class RailingFamilies
{
    internal const string DescriptionToken = "cl_railing_description";

    internal static readonly RailingAngle[] Shapes =
    {
        RailingAngle.Level, RailingAngle.Short, RailingAngle.Stair, RailingAngle.Steep, RailingAngle.Newel,
    };

    internal static readonly IReadOnlyList<RailingPiece> All = Build();

    /// <summary>The English the railings add (tokens without <c>$</c>).</summary>
    internal static Dictionary<string, string> English()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DescriptionToken] = "A railing. Snaps to a stair's side edge, a floor's edge or another railing.",
        };
        foreach (RailingPiece piece in All) english[piece.Token] = piece.English;
        return english;
    }

    private static IReadOnlyList<RailingPiece> Build()
    {
        var all = new List<RailingPiece>();
        foreach (BuildFamilyInfo family in BuildFamilies.All)
        {
            foreach (RailingAngle angle in Shapes) all.Add(new RailingPiece(family.Family, angle));
        }

        return all;
    }
}
