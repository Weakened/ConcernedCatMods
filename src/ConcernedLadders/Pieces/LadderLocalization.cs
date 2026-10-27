using System.Collections.Generic;
using Jotunn.Managers;

namespace TheConcernedCat.ConcernedLadders.Pieces;

internal static class LadderLocalization
{
    internal static void Register()
    {
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
        {
            // Owner 2026-10-27: "Wood Ladder" collided with the game's own Wood ladder in the hammer menu; all three
            // say Vertical. Prefab IDs are unchanged, so placed ladders keep working.
            ["cl_ladder_wood"] = "Vertical Wood Ladder",
            ["cl_ladder_copper"] = "Vertical Copper Ladder",
            ["cl_ladder_iron"] = "Vertical Iron Ladder",
            ["cl_ladder_description"] = "A vertical 2 m ladder. Snap sections end to end, then interact to climb.",
            ["cl_ladder_climb"] = "Climb",
            ["cl_ladder_dismount"] = "Dismount",
            ["cl_ladder_hint"] = "Forward/back: climb • [$KEY_Use] safe dismount • [$KEY_Jump] jump off • [$KEY_Crouch] drop",
            ["cl_ladder_jump"] = "Jump off",
            ["cl_ladder_drop"] = "Drop",
            ["cl_ladder_blocked"] = "The landing is blocked. Descend or jump to let go.",
            ["cl_hover_reach"] = "Move closer to climb",
            ["cl_snap_left"] = "Left side",
            ["cl_snap_right"] = "Right side",
            ["cl_skill_climbing"] = "Climbing",
            ["cl_skill_climbing_description"] = "Climbing ladders. Practice lowers the stamina cost of fast climbing and sliding; at level 20 you can slide down with Run.",
            ["cl_msg_reach"] = "Move closer to the ladder to climb.",
            ["cl_msg_unavailable"] = "You cannot climb right now.",
            ["cl_msg_buildtool"] = "Put away the building tool to climb.",
            ["cl_msg_startup"] = "Ladder climbing is unavailable. Check Concerned Ladders' startup log.",
            ["cl_msg_disabled"] = "Ladder climbing is turned off in Concerned Ladders' settings (General / ClimbingEnabled).",
            ["cl_msg_foreman"] = "Foreman's ladder motor is active. Disable Foreman's Ladders/Enabled and restart to use these ladders.",
            ["cl_msg_invalidrun"] = "This ladder has an invalid or ambiguous connection. Check the segment ends.",
            ["cl_msg_building"] = "Another building piece is in the way:",
            ["cl_msg_ownladder"] = "The ladder's own rails are in the way from here. Approach a clear face.",
            ["cl_msg_terrain"] = "The ground is in the way.",
            ["cl_msg_actor"] = "Someone is in the way.",
            ["cl_msg_obstacle"] = "Something is in the way.",
            ["cl_msg_unchecked"] = "The way could not be checked here. Try again from a clearer spot.",
            ["cl_msg_nolanding"] = "There is no safe landing within reach. Keep climbing, or jump to let go.",
            ["cl_msg_notop"] = "There is no clear landing at the top. Descend or jump to let go.",
            ["cl_msg_nofooting"] = "There is no footing below the ladder. Climb back up, or jump to let go."
        });
    }
}
