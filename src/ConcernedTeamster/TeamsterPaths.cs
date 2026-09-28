using System.IO;
using BepInEx;

namespace TheConcernedCat.ConcernedTeamster;

/// <summary>Owns every path beneath Concerned Teamster's product data root.</summary>
internal static class TeamsterPaths
{
    private const string Vendor = "ConcernedCatMods";
    private const string Product = "ConcernedTeamster";

    internal static string Root => Path.Combine(Paths.ConfigPath, Vendor, Product);

    internal static string InRoot(string name) => Path.Combine(Root, name);

    internal static string SupportBundles => Path.Combine(Root, "SupportBundles");
}
