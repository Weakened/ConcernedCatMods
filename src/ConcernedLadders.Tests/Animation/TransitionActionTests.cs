using System;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

/// <summary>Owner spec 2026-10-27 section 5: every supported transition is authored, well formed, and joins the climbing
/// pose without a pop at the end it shares with the climb.</summary>
public sealed class TransitionActionTests
{
    private const float Tolerance = 0.01f;

    private static TransitionAction Get(TransitionKind kind) => DefaultTransitions.All().Single(a => a.Kind == kind);

    [Fact]
    public void EveryKindIsAuthoredOnceAndValid()
    {
        TransitionAction[] all = DefaultTransitions.All();
        foreach (TransitionKind kind in Enum.GetValues(typeof(TransitionKind)))
        {
            TransitionAction action = Assert.Single(all, a => a.Kind == kind);
            Assert.True(action.Valid, kind + " is malformed");
            Assert.True(action.Seconds > 0.05f && action.Seconds < 3f, kind + " duration " + action.Seconds);
        }
    }

    [Fact]
    public void ChannelsStayInRange()
    {
        foreach (TransitionAction action in DefaultTransitions.All())
        {
            for (int i = 0; i <= 100; i++)
            {
                float p = i / 100f;
                for (int limb = 0; limb < 4; limb++)
                {
                    Assert.InRange(action.WeightAt(limb, p), -Tolerance, 1f + Tolerance);
                    Assert.InRange(action.ReachAt(limb, p), -Tolerance, 1f + Tolerance);
                    for (int axis = 0; axis < 3; axis++) { Assert.InRange(action.ArcAt(limb, p, axis), -0.5f, 0.5f); }
                }

                Assert.InRange(action.BodyWeightAt(p), -Tolerance, 1f + Tolerance);
            }
        }
    }

    [Theory]
    [InlineData((int)TransitionKind.GroundMount)]
    [InlineData((int)TransitionKind.AirCatch)]
    [InlineData((int)TransitionKind.TopEntry)]
    public void AnEntryEndsExactlyInTheClimbingPose(int kindValue)
    {
        var kind = (TransitionKind)kindValue;
        TransitionAction action = Get(kind);
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.Equal(1f, action.WeightAt(limb, 1f), 2);
            Assert.Equal(0f, action.ReachAt(limb, 1f), 2);
            for (int axis = 0; axis < 3; axis++) { Assert.Equal(0f, action.ArcAt(limb, 1f, axis), 2); }
        }

        Assert.Equal(1f, action.BodyWeightAt(1f), 2);
        Assert.Equal(0f, action.BodyUpAt(1f), 2);
        Assert.Equal(0f, action.BodyTowardAt(1f), 2);
        Assert.Equal(0f, action.LeanAt(1f), 2);
    }

    [Theory]
    [InlineData((int)TransitionKind.TopExit)]
    [InlineData((int)TransitionKind.SideExit)]
    [InlineData((int)TransitionKind.BottomStepOff)]
    public void AnExitStartsExactlyFromTheClimbingPose(int kindValue)
    {
        var kind = (TransitionKind)kindValue;
        TransitionAction action = Get(kind);
        for (int limb = 0; limb < 4; limb++)
        {
            Assert.Equal(1f, action.WeightAt(limb, 0f), 2);
            Assert.Equal(0f, action.ReachAt(limb, 0f), 2);
            for (int axis = 0; axis < 3; axis++) { Assert.Equal(0f, action.ArcAt(limb, 0f, axis), 2); }
        }

        Assert.Equal(1f, action.BodyWeightAt(0f), 2);
        Assert.Equal(0f, action.BodyUpAt(0f), 2);
        Assert.Equal(0f, action.BodyTowardAt(0f), 2);
        Assert.Equal(0f, action.LeanAt(0f), 2);
    }

    [Theory]
    [InlineData((int)TransitionKind.TopExit)]
    [InlineData((int)TransitionKind.SideExit)]
    [InlineData((int)TransitionKind.BottomStepOff)]
    public void AnExitHandsEveryLimbBackToTheGameOnTheLanding(int kindValue)
    {
        var kind = (TransitionKind)kindValue;
        TransitionAction action = Get(kind);
        for (int limb = 0; limb < 4; limb++) { Assert.True(action.WeightAt(limb, 1f) < Tolerance, kind + " limb " + limb + " still held"); }
        // Feet reach the landing before they are released.
        for (int limb = 2; limb < 4; limb++)
        {
            float released = Enumerable.Range(0, 101).Select(i => i / 100f).First(p => action.WeightAt(limb, p) < 0.5f);
            Assert.True(action.ReachAt(limb, released) > 0.9f, kind + " foot " + limb + " released before landing");
        }
    }

    [Fact]
    public void ATopEntryStartsWithTheFeetOnTheFloor()
    {
        TransitionAction action = Get(TransitionKind.TopEntry);
        for (int limb = 2; limb < 4; limb++) { Assert.True(action.ReachAt(limb, 0f) > 0.9f, "foot " + limb); }
    }

    [Fact]
    public void LimbsMoveOneAtATimeNotAllTogether()
    {
        // Support transfer (references IMG_1944-1947, 17-00-06): no two limbs leave their contacts at the same instant.
        foreach (TransitionAction action in DefaultTransitions.All())
        {
            float[] starts = new float[4];
            for (int limb = 0; limb < 4; limb++)
            {
                float rest = action.ReachAt(limb, 0f);
                starts[limb] = Enumerable.Range(0, 101).Select(i => i / 100f)
                    .FirstOrDefault(p => Math.Abs(action.ReachAt(limb, p) - rest) > 0.05f || Math.Abs(action.WeightAt(limb, p) - action.WeightAt(limb, 0f)) > 0.05f, 2f);
            }

            float[] moving = starts.Where(s => s <= 1f).OrderBy(s => s).ToArray();
            for (int i = 1; i < moving.Length; i++)
            {
                Assert.True(moving[i] - moving[i - 1] > 0.015f || moving.Length < 2, action.Kind + " moves limbs together at " + moving[i]);
            }
        }
    }
}
