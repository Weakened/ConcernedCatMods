using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>English names for the cellar doors (CL-005 #486), from <see cref="CellarDoors.English"/>. Registered only
/// when the cellar-door setting is on, so with it off the game's localization is unchanged too.</summary>
internal static class CellarDoorLocalization
{
    internal static void Register() =>
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", CellarDoors.English());
}
