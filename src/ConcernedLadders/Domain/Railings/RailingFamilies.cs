using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Families;

namespace TheConcernedCat.ConcernedLadders.Domain.Railings;

/// <summary>The railing setting (#556). On by default from Concerned Builder 1.1.0 (owner 2026-11-12): with it off no
/// railing is cloned, no token is added and the hammer menu is unchanged.</summary>
internal static class RailingSettings
{
    internal const string Section = "Railings";

    internal const string Key = "Enabled";

    internal const bool EnabledByDefault = true;

    internal const string Description =
        "Adds railings to the hammer's Building tab in eight materials (wood, copper, iron, darkwood, " +
        "ashwood, flametal, dvergr and log): half (1 m) and full (2 m) level railings, 27 for the game's stairs (26.57 degrees), " +
        "45 for the step ladder (45 degrees), and a newel post. " +
        "Turning it off again, or uninstalling, makes the game delete every placed railing the next time a " +
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

    /// <summary>The build-menu name (#573, owner 2026-11-11: "Half Wood Railing, Wood Railing, 23 Wood Stair Railing, 45 Wood
    /// Stair Railing and so forth"; the owner then chose 27, the game's stairs' 26.57 degrees rounded, over 23).</summary>
    internal string English => Angle switch
    {
        RailingAngle.Level => Family.Name + " Railing",
        RailingAngle.Short => "Half " + Family.Name + " Railing",
        RailingAngle.Stair or RailingAngle.Steep => DegreesLabel(Angle) + " " + Family.Name + " Stair Railing",
        _ => Family.Name + " Newel Post",
    };

    /// <summary>The slope a stair railing fits, in whole degrees: 27 for the game's stairs, 45 for the step ladder.</summary>
    internal static string DegreesLabel(RailingAngle angle) =>
        ((int)Math.Round(RailingGeometry.Degrees(angle))).ToString(CultureInfo.InvariantCulture);

    /// <summary>The hammer icon's view (#573, owner: "angled images for stair railing and straight ones for regular"), as
    /// the rotation given to the piece in front of the icon camera, in degrees. A railing runs along its own z, so a quarter
    /// turn lays it across the icon. A level railing and a newel post are seen straight from the side, tipped just enough
    /// to show the handrail's top; a stair railing is seen from a three-quarter angle and from a little above, so its
    /// slope reads.</summary>
    internal (float Pitch, float Yaw) IconView => Angle is RailingAngle.Stair or RailingAngle.Steep
        ? (RailingIcons.AngledPitch, RailingIcons.AngledYaw)
        : (RailingIcons.StraightPitch, RailingIcons.StraightYaw);

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

/// <summary>The two icon views (#573), in degrees.</summary>
internal static class RailingIcons
{
    internal const float StraightPitch = 8f;

    internal const float StraightYaw = 90f;

    internal const float AngledPitch = 18f;

    internal const float AngledYaw = 55f;
}

/// <summary>Every railing (#556): every family in every shape.</summary>
internal static class RailingFamilies
{
    internal const string DescriptionToken = "cl_railing_description";

    /// <summary>In the build menu's order (#573): half, full, 27, 45, then the newel post.</summary>
    internal static readonly RailingAngle[] Shapes =
    {
        RailingAngle.Short, RailingAngle.Level, RailingAngle.Stair, RailingAngle.Steep, RailingAngle.Newel,
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
