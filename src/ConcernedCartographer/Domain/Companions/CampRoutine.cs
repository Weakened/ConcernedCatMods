using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>What the companion is doing with himself right now.</summary>
internal enum RoutineState
{
    /// <summary>Sitting at his spot. Where he spends most of his time, and
    /// where he returns to.</summary>
    Settled = 0,

    /// <summary>On his feet, walking to somewhere in camp.</summary>
    Strolling = 1,

    /// <summary>Standing still, looking at something. The pause that stops a
    /// stroll reading as a patrol.</summary>
    Standing = 2,
}

/// <summary>What he should do next.</summary>
internal enum RoutineAction
{
    /// <summary>Carry on.</summary>
    Continue = 0,

    /// <summary>Get up and walk somewhere else in camp.</summary>
    Stroll = 1,

    /// <summary>Stop walking and stand for a moment.</summary>
    Stand = 2,

    /// <summary>Sit down where he is.</summary>
    Settle = 3,
}

/// <summary>What the world looks like to him this pass.</summary>
internal readonly struct RoutineInputs
{
    public RoutineInputs(
        RoutineState state,
        float secondsInState,
        bool arrived,
        bool playerNearby,
        bool sheltered,
        bool nightOrStorm,
        CompanionPose pose,
        float secondsSinceShelterSearchFailed = float.PositiveInfinity)
    {
        State = state;
        SecondsInState = secondsInState;
        Arrived = arrived;
        PlayerNearby = playerNearby;
        Sheltered = sheltered;
        NightOrStorm = nightOrStorm;
        Pose = pose;
        SecondsSinceShelterSearchFailed = secondsSinceShelterSearchFailed;
    }

    public RoutineState State { get; }

    public float SecondsInState { get; }

    /// <summary>Whether a stroll has reached its destination, or given up on
    /// it. Both end the stroll: a companion who cannot reach a spot must stop
    /// walking at something rather than push against it forever.</summary>
    public bool Arrived { get; }

    /// <summary>Whether the player is close enough to be talked to.</summary>
    public bool PlayerNearby { get; }

    /// <summary>Whether he is under a roof.</summary>
    public bool Sheltered { get; }

    /// <summary>Whether it is night, or raining hard enough that a person would
    /// go inside.</summary>
    public bool NightOrStorm { get; }

    /// <summary>The pose he settles into. A seat is somewhere he stays.
    /// </summary>
    public CompanionPose Pose { get; }

    /// <summary>How long ago he last looked for somewhere dry and found
    /// nowhere in reach, or infinity when there is no such failure on record -
    /// which is the default, and what a fresh spell of weather starts
    /// from.</summary>
    public float SecondsSinceShelterSearchFailed { get; }
}

/// <summary>Decides when the companion gets up, walks, stops and sits down
/// again.
///
/// The whole of his "common sense" is three rules, and they are here rather
/// than in the adapter so they can be argued with in a test instead of in a
/// playthrough:
///
/// <list type="number">
/// <item>He stays put when there is a reason to. Somebody is talking to him;
/// he is on a seat somebody built; it is dark or pouring and he is under a
/// roof. A companion who wanders off mid-conversation, or who abandons the
/// chair you made him for a patch of grass, does not read as sensible - it
/// reads as broken.</item>
/// <item>Weather and dark push him towards shelter rather than away from it.
/// Being caught out in a storm is the one case where he will get up from a
/// perfectly good spot.</item>
/// <item>Otherwise he moves rarely, and pauses when he arrives. The pause is
/// what separates somebody pottering around a camp from something patrolling
/// it.</item>
/// </list>
///
/// Every interval is a floor, never a timer that fires: this is asked on a slow
/// tick and answers "not yet" the overwhelming majority of the time.</summary>
internal static class CampRoutine
{
    /// <summary>How long he sits before he might get up. Long: he is somebody
    /// resting at a camp, not a guard on rotation.</summary>
    public const float SettledSeconds = 75f;

    /// <summary>How long he stands looking around before sitting again.
    /// </summary>
    public const float StandingSeconds = 9f;

    /// <summary>The longest a stroll may last before he gives up and sits down
    /// wherever he got to. Without it, a destination behind a wall is a
    /// companion walking on the spot until the world unloads.</summary>
    public const float StrollPatienceSeconds = 14f;

    /// <summary>How long a failed look for shelter holds before he tries
    /// again. Long, because nothing about an empty field changes in a minute:
    /// what changes it is somebody building a roof, and that is worth noticing
    /// within a few minutes, not within a few seconds.</summary>
    public const float ShelterRetrySeconds = 300f;

    public static RoutineAction Decide(RoutineInputs inputs)
    {
        switch (inputs.State)
        {
            case RoutineState.Strolling:
                // Arrival ends a stroll; so does running out of patience, and
                // deliberately at the same door, because "I am there" and "I am
                // not going to get there" have the same right answer: stop.
                return inputs.Arrived || inputs.SecondsInState >= StrollPatienceSeconds
                    ? RoutineAction.Stand
                    : RoutineAction.Continue;

            case RoutineState.Standing:
                return inputs.SecondsInState >= StandingSeconds
                    ? RoutineAction.Settle
                    : RoutineAction.Continue;

            default:
                return DecideFromSettled(inputs);
        }
    }

    private static RoutineAction DecideFromSettled(RoutineInputs inputs)
    {
        // Being spoken to outranks everything. Walking away from somebody
        // mid-sentence is the single most broken-looking thing he could do.
        if (inputs.PlayerNearby)
        {
            return RoutineAction.Continue;
        }

        if (inputs.NightOrStorm)
        {
            // Under a roof: exactly where he should be, so he stays.
            if (inputs.Sheltered)
            {
                return RoutineAction.Continue;
            }

            // Out in it: worth getting up for, whatever he is sitting on - but
            // only if the last look did not just come back empty. This was the
            // one rule here with no floor, and in a camp with no roof in reach
            // it turned into a loop in game: up, walk, sit "in the open", up
            // again, every fifteen seconds all night. After a failed look he
            // falls through to the ordinary rules below, which keep him on a
            // seat somebody built and pace his pottering like any other
            // evening, until it is worth looking again.
            if (inputs.SecondsSinceShelterSearchFailed >= ShelterRetrySeconds)
            {
                return RoutineAction.Stroll;
            }
        }

        // A seat somebody built for him is not a thing to wander off from.
        if (inputs.Pose == CompanionPose.SitOnSeat)
        {
            return RoutineAction.Continue;
        }

        return inputs.SecondsInState >= SettledSeconds
            ? RoutineAction.Stroll
            : RoutineAction.Continue;
    }
}
