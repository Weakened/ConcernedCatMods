using System.IO;
using BepInEx;

namespace TheConcernedCat.ConcernedLadders;

/// <summary>Single product data-root owner required by the repository contract.
/// The current ladder runtime writes no sidecar data and never creates this
/// directory. Native custom pieces persist through the game's normal save path.</summary>
internal static class LaddersPaths
{
    internal static string Root => Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedLadders");
}
