using System.Collections.Generic;
using Jotunn.Managers;

namespace TheConcernedCat.ConcernedLadders.Pieces;

internal static class LadderLocalization
{
    internal static void Register()
    {
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
        {
            ["cl_ladder_wood"] = "Wood ladder",
            ["cl_ladder_copper"] = "Copper ladder",
            ["cl_ladder_iron"] = "Iron ladder",
            ["cl_ladder_description"] = "A vertical 2 m ladder. Snap sections end to end, then interact to climb.",
            ["cl_ladder_climb"] = "Climb",
            ["cl_ladder_dismount"] = "Dismount",
            ["cl_ladder_hint"] = "Forward/back: climb • Interact: safe dismount • Jump: let go",
            ["cl_ladder_blocked"] = "The landing is blocked. Descend or jump to let go."
        });
    }
}
