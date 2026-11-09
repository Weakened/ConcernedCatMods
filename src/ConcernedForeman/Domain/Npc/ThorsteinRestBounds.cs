using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>The two limits on where Thorstein may rest that the ground itself
/// cannot tell the probe (#295 S5): the door rule and camp. Game-free.
///
/// <b>The door rule.</b> The owner's rule for companions is that a door they may
/// not use is never opened or walked through, open or shut, and that a spot is
/// somewhere a companion may <i>be</i> only if he could get there through doors
/// he may use. Thorstein's figure never walks and never touches a door: he is
/// set down at a spot. So the rule here is that he is never set down on the
/// far side of a doorway, or of a wall, from home: a spot is his only when
/// nothing solid stands between it and home (the probe's line check, which a
/// shut door also blocks) and the straight line from home does not pass
/// through a doorway (open or shut). Concerned Foreman keeps no list of doors
/// players have opened to companions, so every door counts as one he may not
/// use: the default policy with nothing allowed. The only crossing that could
/// ever pass is an open door a player allowed, and Foreman never says
/// <c>Allowed</c>, so in this build none does. A spot in or right beside a
/// doorway is already refused by the probe.
///
/// <b>Camp.</b> Once camp is established (a home and at least one player-built
/// piece), he rests inside it. Before that, home alone decides.</summary>
internal static class ThorsteinRestBounds
{
    /// <summary>True when setting him down at <paramref name="spot"/> would put
    /// a doorway he may not pass between him and <paramref name="home"/>.
    /// Allocation-free: an indexed loop over the caller's list.</summary>
    internal static bool AcrossADoor(WorldPoint home, WorldPoint spot, IReadOnlyList<DoorPortal> doors)
    {
        if (doors == null)
        {
            return false;
        }

        for (int index = 0; index < doors.Count; index++)
        {
            DoorPortal door = doors[index];
            if (door.IsCrossedBy(home, spot) && !(door.Allowed && door.IsOpen))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a spot that passed every ground rule may be his:
    /// inside camp when there is one, nothing solid between it and home
    /// (<paramref name="wallBetween"/>, which the caller measures and which is
    /// true when it could not be measured), and not across a door from home.
    /// </summary>
    internal static bool Admits(
        WorldPoint home,
        WorldPoint spot,
        bool campEstablished,
        bool insideCamp,
        bool wallBetween,
        IReadOnlyList<DoorPortal> doors) =>
        (!campEstablished || insideCamp) && !wallBetween && !AcrossADoor(home, spot, doors);
}
