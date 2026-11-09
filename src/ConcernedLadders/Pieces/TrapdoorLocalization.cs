using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>English names for the trapdoors (CL-004 #485). Registered only when the trapdoor setting is on, so with
/// it off the game's localization is unchanged too.</summary>
internal static class TrapdoorLocalization
{
    internal static void Register() =>
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", TrapdoorBlueprint.English());
}
