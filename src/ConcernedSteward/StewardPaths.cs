using System.IO;
using BepInEx;

namespace TheConcernedCat.ConcernedSteward;

/// <summary>Owns every path beneath Concerned Steward's product data root.</summary>
internal static class StewardPaths
{
    private const string Vendor = "ConcernedCatMods";
    private const string Product = "ConcernedSteward";

    internal static string Root => Path.Combine(Paths.ConfigPath, Vendor, Product);

    internal static string Settlements => Path.Combine(Root, "settlements");
}
