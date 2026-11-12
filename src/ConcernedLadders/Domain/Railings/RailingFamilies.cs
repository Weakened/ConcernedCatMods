using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedLadders.Domain.Railings;

/// <summary>The railing setting (#556). Off by default, as every 1.1.0 addition is: with it off no railing is cloned, no
/// token is added and the hammer menu is unchanged.</summary>
internal static class RailingSettings
{
    internal const string Section = "Railings";

    internal const string Key = "Enabled";

    internal const bool EnabledByDefault = false;

    internal const string Description =
        "UNFINISHED (#556): adds nine railings to the hammer's Building tab - wood, copper and iron, each level, for the " +
        "game's stairs (26.57 degrees) and for the step ladder (45 degrees) - none of it yet tested in game. " +
        "Leave this off. Turning it off again, or uninstalling, makes the game delete every placed railing the next time a " +
        "world loads, with no refund. In multiplayer every player and the host or server must have the same value. " +
        "Restart after changing it.";
}

/// <summary>One recipe line.</summary>
internal readonly struct RailingCost
{
    internal RailingCost(string item, int amount)
    {
        Item = item;
        Amount = amount;
    }

    internal string Item { get; }

    internal int Amount { get; }
}

/// <summary>One railing piece (#556): a tier at an angle, with its stable prefab ID (a save contract once shipped), its
/// name, the vanilla donor it is cloned from (its health class, support and damage come from it, as the ladders' do), the
/// material it borrows, its tint, health, station and cost. The tiers' donors, materials, tints, health and stations are
/// the ladders' own (<c>LadderPieceRegistration</c>), so a railing reads as the ladder of its tier.</summary>
internal sealed class RailingPiece
{
    internal RailingPiece(RailingTier tier, RailingAngle angle)
    {
        Tier = tier;
        Angle = angle;
    }

    internal RailingTier Tier { get; }

    internal RailingAngle Angle { get; }

    internal string Prefab => "tcc_railing_" + TierWord + "_" + AngleWord;

    internal string Token => "cl_railing_" + TierWord + "_" + AngleWord;

    internal string English => TierName + (Angle switch
    {
        RailingAngle.Level => " Railing",
        RailingAngle.Stair => " Stair Railing",
        _ => " Steep Stair Railing",
    });

    internal string Donor => Tier == RailingTier.Wood ? "woodwall" : "woodiron_pole";

    /// <summary>The prefab whose renderers hold <see cref="Material"/>.</summary>
    internal string MaterialPrefab => Tier switch
    {
        RailingTier.Wood => "woodwall",
        RailingTier.Copper => "Copper",
        _ => "woodiron_pole",
    };

    internal string Material => Tier switch
    {
        RailingTier.Wood => "woodwall",
        RailingTier.Copper => "copper",
        _ => "Ironbeam_mat",
    };

    /// <summary>The tint multiplied into a copy of the borrowed material (RGB): the ladders' own.</summary>
    internal (float R, float G, float B) Tint => Tier switch
    {
        RailingTier.Wood => (1f, 1f, 1f),
        RailingTier.Copper => (1f, 0.84f, 0.7f),
        _ => (0.8f, 0.84f, 0.9f),
    };

    internal float Health => Tier switch
    {
        RailingTier.Wood => 200f,
        RailingTier.Copper => 400f,
        _ => 800f,
    };

    internal string Station => Tier == RailingTier.Wood ? "piece_workbench" : "forge";

    /// <summary>Lead default: a 2 m railing costs about half a 2 m ladder, a little more on a slope.</summary>
    internal IReadOnlyList<RailingCost> Costs => Tier switch
    {
        RailingTier.Wood => new[] { new RailingCost("Wood", Angle == RailingAngle.Level ? 2 : 3) },
        RailingTier.Copper => new[] { new RailingCost("Copper", 1), new RailingCost("Wood", 1) },
        _ => new[] { new RailingCost("Iron", 1), new RailingCost("Wood", 1) },
    };

    private string TierWord => Tier.ToString().ToLower(CultureInfo.InvariantCulture);

    private string TierName => Tier.ToString();

    private string AngleWord => Angle.ToString().ToLower(CultureInfo.InvariantCulture);
}

/// <summary>The nine railings (#556): every tier at every angle.</summary>
internal static class RailingFamilies
{
    internal const string DescriptionToken = "cl_railing_description";

    internal static readonly IReadOnlyList<RailingPiece> All = Build();

    /// <summary>The English the railings add (tokens without <c>$</c>).</summary>
    internal static Dictionary<string, string> English()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DescriptionToken] = "A 2 m railing. Snaps to a stair's side edge, a floor's edge or another railing.",
        };
        foreach (RailingPiece piece in All) english[piece.Token] = piece.English;
        return english;
    }

    private static IReadOnlyList<RailingPiece> Build()
    {
        var all = new List<RailingPiece>();
        foreach (RailingTier tier in new[] { RailingTier.Wood, RailingTier.Copper, RailingTier.Iron })
        {
            foreach (RailingAngle angle in new[] { RailingAngle.Level, RailingAngle.Stair, RailingAngle.Steep }) all.Add(new RailingPiece(tier, angle));
        }

        return all;
    }
}
