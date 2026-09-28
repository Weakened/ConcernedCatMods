using System.IO;
using BepInEx;

namespace TheConcernedCat.ConcernedForeman;

/// <summary>Owns every path beneath Concerned Foreman's product data root.</summary>
internal static class ForemanPaths
{
    private const string Vendor = "ConcernedCatMods";
    private const string Product = "ConcernedForeman";

    internal static string Root => Path.Combine(Paths.ConfigPath, Vendor, Product);

    internal static string Settlements => Path.Combine(Root, "settlements");
}
