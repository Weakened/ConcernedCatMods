using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Companions;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>When he gets up for a potter around camp, when he stays, and what
/// outranks what. Where he spends his time is <c>CommonSenseTests</c>.</summary>
public sealed class CampRoutineTests
{
    private static RoutineInputs At(
        RoutineState state,
        float seconds,
        bool arrived = false,
        bool playerNearby = false,
        bool nightOrStorm = false,
        CompanionPose pose = CompanionPose.SitOnGround)
    {
        return new RoutineInputs(state, seconds, arrived, playerNearby, nightOrStorm, pose);
    }

    [Fact]
    public void AtNightOrInTheRainHeDoesNotPotterAbout()
    {
        // This used to send him walking around to look for a roof, and in a camp
        // with no roof in reach that became a loop in game at 7287919: up, walk,
        // sit in the open, up again, eighteen times in four minutes. He knows his
        // camp now; if there is a roof or a spare bed he can reach, his common
        // sense takes him straight there, and the routine keeps out of it.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(RoutineState.Settled, CampRoutine.SettledSeconds * 20f, nightOrStorm: true)));
    }

    [Fact]
    public void HeStaysPutWhileSomebodyIsTalkingToHim()
    {
        // The most broken-looking thing he could do is walk off mid-sentence.
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
    public void HeDoesNotWanderOffASeatSomebodyBuiltHimOrOutOfABed()
    {
        // Abandoning the chair you made him for a patch of grass reads as a
        // bug, not as character. The seat holds him however long he sits on it,
        // and so does a bed.
        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds * 5f, pose: CompanionPose.SitOnSeat)));

        Assert.Equal(
            RoutineAction.Continue,
            CampRoutine.Decide(At(
                RoutineState.Settled, CampRoutine.SettledSeconds * 5f, pose: CompanionPose.SleepInBed)));
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

        // A warm patch of ground by the fire is still the ground.
        Assert.Equal(
            RoutineAction.Stroll,
            CampRoutine.Decide(At(RoutineState.Settled, CampRoutine.SettledSeconds, pose: CompanionPose.SitByFire)));
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
