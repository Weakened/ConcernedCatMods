using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace Shared.Settlement.Tests;

/// <summary>The ladder domain (CF-LAD-001, `docs/mods/concerned-foreman/LADDERS.md`):
/// the geometry a climb is decided from, who may start one, how it moves, how
/// it ends, and the rule that a player is never left stuck on a ladder.
///
/// Nothing here touches the game. The numbers are the tuned defaults, so a
/// change to them has to come past these tests.</summary>
public class LadderClimbTests
{
    // A plain ladder: 3 m tall, its rungs facing east, so a climber stands to
    // the east of it (x+) and looks west.
    private static LadderGeometry Ladder(float height = 3f, float width = 0.7f)
    {
        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(10f, 0f, 10f),
            new ClimbPoint(10f, height, 10f),
            ClimbHeading.FromXz(1f, 0f),
            width,
            0.35f,
            out LadderGeometry geometry));
        return geometry;
    }

    private static ClimberState StandingAt(float x, float y, float z, float lookX = -1f, float lookZ = 0f) =>
        new ClimberState(
            new ClimbPoint(x, y, z),
            ClimbHeading.FromXz(lookX, lookZ),
            canClimbNow: true,
            alreadyClimbing: false);

    // ---------------------------------------------------------------- geometry

    [Fact]
    public void AStepIsNotALadderAndAnUnmeasurableOneIsNotEither()
    {
        Assert.False(LadderGeometry.TryMeasure(
            new ClimbPoint(0f, 0f, 0f),
            new ClimbPoint(0f, 0.5f, 0f),
            ClimbHeading.FromXz(1f, 0f),
            0.7f,
            0.35f,
            out _));

        Assert.False(LadderGeometry.TryMeasure(
            new ClimbPoint(0f, 0f, 0f),
            new ClimbPoint(0f, 3f, 0f),
            ClimbHeading.None,
            0.7f,
            0.35f,
            out _));

        Assert.False(LadderGeometry.TryMeasure(
            new ClimbPoint(0f, 0f, 0f),
            new ClimbPoint(0f, 3f, 0f),
            ClimbHeading.FromXz(1f, 0f),
            0f,
            0.35f,
            out _));
    }

    [Fact]
    public void AnUnmeasurableRungSpacingBecomesTheConventionalOneRatherThanZero()
    {
        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(0f, 0f, 0f),
            new ClimbPoint(0f, 3f, 0f),
            ClimbHeading.FromXz(1f, 0f),
            0.7f,
            rungPitch: 0f,
            out LadderGeometry geometry));
        Assert.Equal(LadderGeometry.ConventionalRungPitch, geometry.RungPitch);
        Assert.Equal(0, geometry.RungAt(0f));
        Assert.True(geometry.RungAt(3f) > geometry.RungAt(1f));
    }

    [Fact]
    public void ProgressAndPositionsStayOnTheLadderHoweverAbsurdTheInput()
    {
        LadderGeometry ladder = Ladder();

        Assert.Equal(0f, ladder.ProgressOf(new ClimbPoint(10f, -50f, 10f)));
        Assert.Equal(3f, ladder.ProgressOf(new ClimbPoint(10f, 50f, 10f)));
        Assert.Equal(0f, ladder.PointAt(-10f).Y, 3);
        Assert.Equal(3f, ladder.PointAt(10f).Y, 3);

        // The body hangs out in the open air, never inside the rungs.
        ClimbPoint body = ladder.ClimbPositionAt(1.5f, 0.35f);
        Assert.Equal(10.35f, body.X, 3);
        Assert.Equal(1.5f, body.Y, 3);
    }

    [Fact]
    public void TheWallSideIsBehindTheRungsAndTheOpenAirIsInFront()
    {
        LadderGeometry ladder = Ladder();

        Assert.True(ladder.ReachFrom(new ClimbPoint(10.8f, 1f, 10f)) > 0f);
        Assert.True(ladder.ReachFrom(new ClimbPoint(9.2f, 1f, 10f)) < 0f);
        Assert.Equal(0.6f, ladder.SidewaysOffsetOf(new ClimbPoint(10.5f, 1f, 10.6f)), 3);
        Assert.Equal(ladder.FacingWhileClimbing.X, -ladder.StandingSide.X, 3);
    }

    // ------------------------------------------------------------------- mount

    [Fact]
    public void WalkingIntoALadderIsEnoughToStartClimbingIt()
    {
        LadderGeometry ladder = Ladder();
        MountDecision decision = LadderMount.Decide(ladder, StandingAt(10.7f, 0f, 10.2f), ClimbLimits.Default);

        Assert.True(decision.Allowed);
        Assert.Equal(0f, decision.Progress, 3);
        Assert.Equal(ladder.FacingWhileClimbing, decision.Facing);
        Assert.Equal(10.35f, decision.Position.X, 3);
    }

    [Fact]
    public void WalkingPastALadderDoesNotGrabIt()
    {
        LadderGeometry ladder = Ladder();
        ClimberState passingBy = StandingAt(10.7f, 0f, 10f, lookX: 0f, lookZ: 1f);

        Assert.Equal(MountRefusal.LookingAway, LadderMount.Decide(ladder, passingBy, ClimbLimits.Default).Refusal);

        // Pressing Use is intent enough on its own.
        Assert.True(LadderMount.Decide(ladder, passingBy, ClimbLimits.Default, byDeliberateUse: true).Allowed);
    }

    [Fact]
    public void EveryRefusalHasItsOwnReasonAndItsOwnSentence()
    {
        LadderGeometry ladder = Ladder();
        ClimbLimits limits = ClimbLimits.Default.Validate();

        Assert.Equal(
            MountRefusal.TooFar,
            LadderMount.Decide(ladder, StandingAt(13f, 0f, 10f), limits).Refusal);
        Assert.Equal(
            MountRefusal.WrongSide,
            LadderMount.Decide(ladder, StandingAt(9.5f, 0f, 10f, lookX: 1f), limits).Refusal);
        Assert.Equal(
            MountRefusal.OffToTheSide,
            LadderMount.Decide(ladder, StandingAt(10.3f, 0f, 11.5f), limits).Refusal);
        Assert.Equal(
            MountRefusal.OutOfSpan,
            LadderMount.Decide(ladder, StandingAt(10.3f, 6f, 10f), limits).Refusal);
        Assert.Equal(
            MountRefusal.CharacterBusy,
            LadderMount.Decide(
                ladder,
                new ClimberState(new ClimbPoint(10.3f, 0f, 10f), ClimbHeading.FromXz(-1f, 0f), canClimbNow: false, alreadyClimbing: false),
                limits).Refusal);
        Assert.Equal(
            MountRefusal.AlreadyClimbing,
            LadderMount.Decide(
                ladder,
                new ClimberState(new ClimbPoint(10.3f, 0f, 10f), ClimbHeading.FromXz(-1f, 0f), canClimbNow: true, alreadyClimbing: true),
                limits).Refusal);
        Assert.Equal(
            MountRefusal.Disabled,
            LadderMount.Decide(ladder, StandingAt(10.3f, 0f, 10f), limits, laddersEnabled: false).Refusal);

        foreach (MountRefusal refusal in Enum.GetValues(typeof(MountRefusal)))
        {
            if (refusal == MountRefusal.Unspecified)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => MountDecision.Refuse(refusal));
                continue;
            }

            string sentence = LadderMount.Explain(refusal);
            Assert.False(string.IsNullOrWhiteSpace(sentence), refusal.ToString());
            Assert.NotEqual("You cannot climb that.", sentence);
        }
    }

    [Fact]
    public void SteppingOnFromTheTopStartsAtTheTopNotTheBottom()
    {
        LadderGeometry ladder = Ladder();
        MountDecision decision = LadderMount.Decide(ladder, StandingAt(10.6f, 3.1f, 10f), ClimbLimits.Default);

        Assert.True(decision.Allowed);
        Assert.Equal(3f, decision.Progress, 2);
    }

    // ------------------------------------------------------------------- climb

    [Fact]
    public void AClimbStopsAtBothEndsAndNeverWalksOnTheSpot()
    {
        ClimbLimits limits = ClimbLimits.Default.Validate();
        var track = new ClimbTrack(3f, limits, startProgress: 0f);

        Assert.Equal(ClimbMotion.Climbing, track.Step(1f, 0.5f));
        Assert.Equal(0.9f, track.Progress, 2);

        // A frame long enough to overshoot the top still stops at the top, and
        // the speed reported is the distance actually covered.
        Assert.Equal(ClimbMotion.PressingAgainstTheTop, track.Step(1f, 10f));
        Assert.Equal(3f, track.Progress, 3);
        Assert.Equal(0.21f, track.Velocity, 2);

        // Pressing on at the top moves nothing and the animation stops.
        Assert.Equal(ClimbMotion.PressingAgainstTheTop, track.Step(1f, 0.5f));
        Assert.Equal(0f, track.Velocity, 3);
        Assert.Equal(0f, track.AnimationSpeed(), 3);

        Assert.Equal(ClimbMotion.Descending, track.Step(-1f, 0.5f));
        Assert.True(track.AnimationSpeed() < 0f);
        Assert.Equal(ClimbMotion.StandingOnTheGround, track.Step(-1f, 10f));
        Assert.Equal(0f, track.Progress, 3);
    }

    [Fact]
    public void LettingGoOfTheKeysRestsInsteadOfCreeping()
    {
        var track = new ClimbTrack(3f, ClimbLimits.Default, startProgress: 1f);

        Assert.Equal(ClimbMotion.Resting, track.Step(0.05f, 0.5f));
        Assert.Equal(1f, track.Progress, 3);
        Assert.Equal(0f, track.AnimationSpeed(), 3);

        Assert.Equal(ClimbMotion.Resting, track.Step(1f, 0f));
        Assert.Equal(ClimbMotion.Resting, track.Step(float.NaN, 0.5f));
        Assert.Equal(1f, track.Progress, 3);
    }

    [Fact]
    public void ClimbingIsFreeUnlessAPlayerAsksForItToCost()
    {
        var free = new ClimbTrack(3f, ClimbLimits.Default, 0f);
        free.Step(1f, 0.5f);
        Assert.Equal(0f, free.StaminaFor(0.5f), 3);

        var costly = new ClimbTrack(3f, new ClimbLimits { StaminaPerSecond = 2f }.Validate(), 0f);
        costly.Step(1f, 0.5f);
        Assert.Equal(1f, costly.StaminaFor(0.5f), 3);

        costly.Step(0f, 0.5f);
        Assert.Equal(0f, costly.StaminaFor(0.5f), 3);
    }

    [Fact]
    public void ARunWithNoHeightIsRefusedRatherThanDividedBy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClimbTrack(0f, ClimbLimits.Default, 0f));
        Assert.Throws<ArgumentNullException>(() => new ClimbTrack(3f, null!, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClimbLimits { ClimbSpeedMetresPerSecond = 0f }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClimbLimits { LostContactMetres = 0.1f }.Validate());
    }

    // -------------------------------------------------------------------- exit

    [Fact]
    public void TheTopExitCrossesTheEdgeOntoTheFloorAndNotIntoIt()
    {
        LadderGeometry ladder = Ladder();
        ClimbLimits limits = ClimbLimits.Default.Validate();
        var landing = new TopLanding(true, new ClimbPoint(10f, 3f, 10f));

        ClimbExitPlan plan = ClimbExit.OverTheTop(ladder, landing, limits)!.Value;

        Assert.Equal(ClimbExitKind.Top, plan.Kind);
        Assert.True(plan.PlaceCharacter);
        // In over the edge, away from the drop, and standing on the floor.
        Assert.Equal(9.4f, plan.Landing.X, 2);
        Assert.Equal(3.4f, plan.Landing.Y, 2);
        Assert.Equal(ladder.FacingWhileClimbing, plan.Facing);
    }

    [Fact]
    public void ALadderThatEndsUnderARoofLeavesTheClimberOnItRatherThanInTheRoof()
    {
        LadderGeometry ladder = Ladder();
        Assert.Null(ClimbExit.OverTheTop(ladder, TopLanding.None, ClimbLimits.Default));
        Assert.Null(ClimbExit.OverTheTop(ladder, new TopLanding(true, new ClimbPoint(float.NaN, 3f, 10f)), ClimbLimits.Default));
    }

    [Fact]
    public void TheBottomExitPutsTheClimberBackWhereTheyWalkedInFrom()
    {
        LadderGeometry ladder = Ladder();
        ClimbExitPlan plan = ClimbExit.OffTheBottom(ladder, ClimbLimits.Default);

        Assert.Equal(ClimbExitKind.Bottom, plan.Kind);
        Assert.True(plan.PlaceCharacter);
        Assert.Equal(10.35f, plan.Landing.X, 2);
        Assert.Equal(0f, plan.Landing.Y, 3);
    }

    [Fact]
    public void LettingGoNeverMovesTheBody()
    {
        ClimbExitPlan jumped = ClimbExit.LetGo(deliberate: true);
        ClimbExitPlan dropped = ClimbExit.LetGo(deliberate: false);

        Assert.Equal(ClimbExitKind.JumpedOff, jumped.Kind);
        Assert.Equal(ClimbExitKind.LetGo, dropped.Kind);
        Assert.False(jumped.PlaceCharacter);
        Assert.False(dropped.PlaceCharacter);
        Assert.Throws<ArgumentOutOfRangeException>(() => ClimbExitPlan.Step(ClimbExitKind.LetGo, default, ClimbHeading.None));
    }

    // ------------------------------------------------------------------ safety

    [Fact]
    public void EverythingThatCanEndAClimbDoesEndIt()
    {
        ClimbLimits limits = ClimbLimits.Default.Validate();

        Assert.Equal(ClimbEndReason.Unspecified, ClimbSafety.MustEnd(ClimbConditions.Fine(), limits));

        Assert.Equal(
            ClimbEndReason.WorldUnloading,
            ClimbSafety.MustEnd(new ClimbConditions(true, true, true, true, true, worldUp: false, true, 0f), limits));
        Assert.Equal(
            ClimbEndReason.RuntimeStopped,
            ClimbSafety.MustEnd(new ClimbConditions(true, true, true, true, true, true, runtimeRunning: false, 0f), limits));
        Assert.Equal(
            ClimbEndReason.CharacterDied,
            ClimbSafety.MustEnd(new ClimbConditions(true, true, true, characterAlive: false, true, true, true, 0f), limits));
        Assert.Equal(
            ClimbEndReason.CharacterTaken,
            ClimbSafety.MustEnd(new ClimbConditions(true, true, true, true, characterFree: false, true, true, 0f), limits));
        Assert.Equal(
            ClimbEndReason.LadderDestroyed,
            ClimbSafety.MustEnd(new ClimbConditions(ladderAlive: false, true, true, true, true, true, true, 0f), limits));
        Assert.Equal(
            ClimbEndReason.LadderUnloaded,
            ClimbSafety.MustEnd(new ClimbConditions(true, ladderLoaded: false, true, true, true, true, true, 0f), limits));
        Assert.Equal(
            ClimbEndReason.LadderMoved,
            ClimbSafety.MustEnd(new ClimbConditions(true, true, ladderStill: false, true, true, true, true, 0f), limits));
        Assert.Equal(
            ClimbEndReason.LostContact,
            ClimbSafety.MustEnd(ClimbConditions.Fine(distanceFromLadder: 2f), limits));
    }

    [Fact]
    public void ADeadOrUnloadedClimberIsLetGoWhereTheyAreAndHandedBackWhole()
    {
        foreach (ClimbEndReason reason in Enum.GetValues(typeof(ClimbEndReason)))
        {
            if (reason == ClimbEndReason.Unspecified)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => ClimbSafety.Release(reason));
                continue;
            }

            ClimbExitPlan plan = ClimbSafety.Release(reason);
            Assert.False(plan.PlaceCharacter, reason.ToString());
            Assert.Equal(ClimbExitKind.LetGo, plan.Kind);

            string sentence = ClimbSafety.Explain(reason);
            Assert.False(string.IsNullOrWhiteSpace(sentence), reason.ToString());
            Assert.NotEqual("The climb ended.", sentence);
        }

        Assert.True(ClimbSafety.Restoration.IsComplete);
    }

    // ------------------------------------------------------------------- stack

    [Fact]
    public void FiveLaddersUpATowerAreOneClimb()
    {
        var pieces = new List<LadderGeometry>();
        for (int index = 4; index >= 0; index--)
        {
            // Deliberately out of order, to prove the join sorts them.
            Assert.True(LadderGeometry.TryMeasure(
                new ClimbPoint(10f, index * 3f, 10f),
                new ClimbPoint(10f, (index * 3f) + 3f, 10f),
                ClimbHeading.FromXz(1f, 0f),
                0.7f,
                0.35f,
                out LadderGeometry piece));
            pieces.Add(piece);
        }

        Assert.True(LadderRun.TryJoin(pieces, out LadderRun run));
        Assert.Equal(15f, run.Height, 3);
        Assert.Equal(5, run.Pieces.Count);
        Assert.Equal(0f, run.Bottom.Y, 3);
        Assert.Equal(15f, run.AsOne.Height, 3);

        // The piece a climber is on decides what is watched for destruction.
        Assert.Equal(3f, run.PieceAt(1f).Top.Y, 3);
        Assert.Equal(15f, run.PieceAt(14.5f).Top.Y, 3);
        Assert.Equal(15f, run.PieceAt(100f).Top.Y, 3);
    }

    [Fact]
    public void LaddersOnOppositeWallsOrWithAGapBetweenThemAreNotOneClimb()
    {
        LadderGeometry lower = Ladder();

        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(10f, 3f, 10f),
            new ClimbPoint(10f, 6f, 10f),
            ClimbHeading.FromXz(-1f, 0f),
            0.7f,
            0.35f,
            out LadderGeometry facingTheOtherWay));
        Assert.False(LadderRun.TryJoin(new[] { lower, facingTheOtherWay }, out _));

        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(10f, 5f, 10f),
            new ClimbPoint(10f, 8f, 10f),
            ClimbHeading.FromXz(1f, 0f),
            0.7f,
            0.35f,
            out LadderGeometry farAbove));
        Assert.False(LadderRun.TryJoin(new[] { lower, farAbove }, out _));

        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(12f, 3f, 10f),
            new ClimbPoint(12f, 6f, 10f),
            ClimbHeading.FromXz(1f, 0f),
            0.7f,
            0.35f,
            out LadderGeometry besideIt));
        Assert.False(LadderRun.TryJoin(new[] { lower, besideIt }, out _));

        Assert.False(LadderRun.TryJoin(new LadderGeometry[0], out _));
        Assert.False(LadderRun.TryJoin(null!, out _));
    }

    [Fact]
    public void OneLadderIsARunWithoutCeremony()
    {
        LadderRun run = LadderRun.Single(Ladder());
        Assert.Single(run.Pieces);
        Assert.Equal(3f, run.Height, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => LadderRun.Single(default));
    }
}
