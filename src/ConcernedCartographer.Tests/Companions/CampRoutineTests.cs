using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Companions;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>The companion's whole "common sense": when he gets up, when he
/// stays, and what outranks what.</summary>
public sealed class CampRoutineTests
{
    private static RoutineInputs At(
        RoutineState state,
        float seconds,
        bool arrived = false,
        bool playerNearby = false,
        bool sheltered = false,
        bool nightOrStorm = false,
        CompanionPose pose = CompanionPose.SitOnGround,
        float secondsSinceShelterSearchFailed = float.PositiveInfinity)
    {
        return new RoutineInputs(
            state, seconds, arrived, playerNearby, sheltered, nightOrStorm, pose,
            secondsSinceShelterSearchFailed);
    }

    [Fact]
    public void AFailedLookForShelterIsNotRepeatedEveryFewSeconds()
    {
        // Seen in game at 7287919: at night, in a camp with no roof in reach,
        // he got up "looking for shelter", sat down in the open, and got up
        // again - roughly every fifteen seconds, eighteen times in four
        // minutes. Having just looked and found nowhere dry, the same empty
        // field is not worth another look yet.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, 0f, nightOrStorm: true,
                secondsSinceShelterSearchFailed: 1f)));

        // Nor is it a reason to leave a seat somebody built him: the search
        // that would justify leaving it has just failed.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds * 5f, nightOrStorm: true,
                pose: CompanionPose.SitOnSeat, secondsSinceShelterSearchFailed: 1f)));

        // In the meantime he potters at the ordinary pace, no faster...
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds - 1f, nightOrStorm: true,
                secondsSinceShelterSearchFailed: 1f)));
        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds, nightOrStorm: true,
                secondsSinceShelterSearchFailed: 1f)));

        // ...and from the ground he looks again once it has been long enough for
        // something to have changed.
        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(
                RoutineState.Settled, 0f, nightOrStorm: true,
                secondsSinceShelterSearchFailed: CampRoutine.ShelterRetrySeconds)));
    }

    [Fact]
    public void AFailedLookHoldsHimOnASeatForTheRestOfTheWeather()
    {
        // Review of the first fix found the slower version of the same loop: on
        // a seat, the retry fired every five minutes, walked him off the chair
        // to sit on the grass, and the seat sweep put him back. A seat somebody
        // built him keeps him until the weather changes...
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, 0f, nightOrStorm: true, pose: CompanionPose.SitOnSeat,
                secondsSinceShelterSearchFailed: CampRoutine.ShelterRetrySeconds * 10f)));

        // ...and a new spell of weather, with no failure on record, still gets
        // him up for one honest look.
        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(
                RoutineState.Settled, 0f, nightOrStorm: true, pose: CompanionPose.SitOnSeat)));
    }

    [Fact]
    public void HeStaysPutWhileSomebodyIsTalkingToHim()
    {
        // The most broken-looking thing he could do is walk off mid-sentence,
        // so this outranks every other reason to move - including the storm
        // rule, which is otherwise the strongest one there is.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds * 10f, playerNearby: true)));

        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, 999f, playerNearby: true, nightOrStorm: true)));
    }

    [Fact]
    public void HeGetsUpWhenHeIsOutInTheDarkOrTheRain()
    {
        // The one case where a perfectly good spot is not good enough, and the
        // one case that does not wait for the settle interval: a person caught
        // out in a storm moves now, not in a minute.
        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(RoutineState.Settled, 0f, nightOrStorm: true, sheltered: false)));

        // Under a roof in the same weather he is already where he should be.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(RoutineState.Settled, 0f, nightOrStorm: true, sheltered: true)));
    }

    [Fact]
    public void HeDoesNotWanderOffASeatSomebodyBuiltHim()
    {
        // Abandoning the chair you made him for a patch of grass reads as a
        // bug, not as character. The seat holds him however long he sits on it.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds * 5f,
                pose: CompanionPose.SitOnSeat)));

        // But a storm still gets him off it, because the roof matters more.
        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(
                RoutineState.Settled, 0f, nightOrStorm: true, pose: CompanionPose.SitOnSeat)));
    }

    [Fact]
    public void OnTheGroundHeStrollsOnlyAfterALongWhile()
    {
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(RoutineState.Settled, CampRoutine.SettledSeconds - 0.1f)));

        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(RoutineState.Settled, CampRoutine.SettledSeconds)));
    }

    [Fact]
    public void ArrivingAndGivingUpEndAStrollTheSameWay()
    {
        // Deliberately the same answer. "I am there" and "I am not going to get
        // there" both mean stop walking - and without the second one, a
        // destination behind a wall is a companion walking on the spot until
        // the world unloads.
        Assert.Equal(
            RoutineAction.Stand,
            CampRoutine.Decide(At(RoutineState.Strolling, 1f, arrived: true)));

        Assert.Equal(
            RoutineAction.Stand,
            CampRoutine.Decide(At(
                RoutineState.Strolling, CampRoutine.StrollPatienceSeconds, arrived: false)));

        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(RoutineState.Strolling, 1f, arrived: false)));
    }

    [Fact]
    public void AStrollAlwaysEndsInAPauseBeforeHeSitsAgain()
    {
        // The pause is what separates somebody pottering around a camp from
        // something patrolling it, so arrival may never go straight to sitting.
        Assert.Equal(
            RoutineAction.Stand,
            CampRoutine.Decide(At(RoutineState.Strolling, 1f, arrived: true)));

        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(RoutineState.Standing, CampRoutine.StandingSeconds - 0.1f)));

        Assert.Equal(
            RoutineAction.Settle,
            CampRoutine.Decide(At(RoutineState.Standing, CampRoutine.StandingSeconds)));
    }

    [Fact]
    public void BeingTalkedToDoesNotFreezeHimMidStroll()
    {
        // The player-nearby rule belongs to sitting down, not to walking. A
        // companion who stopped dead the moment you came within earshot, and
        // stayed stopped standing in the open, would be worse than one who
        // finishes crossing the camp.
        Assert.Equal(
            RoutineAction.Stand,
            CampRoutine.Decide(At(RoutineState.Strolling, 1f, arrived: true, playerNearby: true)));

        Assert.Equal(
            RoutineAction.Settle,
            CampRoutine.Decide(At(
                RoutineState.Standing, CampRoutine.StandingSeconds, playerNearby: true)));
    }
}
