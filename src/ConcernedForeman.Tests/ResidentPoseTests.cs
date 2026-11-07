using TheConcernedCat.ConcernedForeman.Runtime.Presentation;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.Settlement.Housing;
using UnityEngine;
namespace ConcernedForeman.Tests;
public sealed class ResidentPoseTests
{
    private static Animator Controller() => new()
    {
        parameters = new[] { "onGround", "lying_down", "emote_sit" }.Select(name =>
            new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Bool }).ToArray()
    };
    [Fact]
    public void NativeRoutineSitsStandsAndRestsOnOnlyItsExtractedAnimator()
    {
        var own = Controller(); var other = Controller();
        var pose = new ResidentPose(new NpcPresentationFigure(own));
        pose.Apply(ResidentRoutine.At(.3f, true, true));
        Assert.True(own.Values["emote_sit"]); Assert.False(own.Values["lying_down"]);
        pose.Apply(ResidentRoutine.At(.5f, true, true));
        Assert.False(own.Values["emote_sit"]); Assert.False(own.Values["lying_down"]);
        pose.Apply(ResidentRoutine.At(.7f, true, true));
        Assert.True(own.Values["emote_sit"]);
        pose.Apply(ResidentRoutine.At(.9f, true, true));
        Assert.False(own.Values["emote_sit"]); Assert.True(own.Values["lying_down"]);
        Assert.True(own.Values["onGround"]); Assert.Equal(0, other.Writes);
        Assert.Equal(new[] { "emote_sit", "lying_down", "onGround" }, own.Values.Keys.OrderBy(x => x));
    }
    [Fact]
    public void PausedUnknownClockOrInvalidHousingRetainsLastPoseWithoutWrites()
    {
        var own = Controller(); var pose = new ResidentPose(new NpcPresentationFigure(own));
        pose.Apply(ResidentRoutine.At(.3f, true, true)); int writes = own.Writes;
        foreach (var stage in new[] { ResidentRoutine.At(float.NaN, true, true), ResidentRoutine.At(.9f, false, true),
            ResidentRoutine.At(.9f, true, false), ResidentRoutine.At(-1, true, true), ResidentRoutine.At(2, true, true) })
            pose.Apply(stage);
        Assert.Equal(writes, own.Writes); Assert.Equal(ResidentRoutineStage.Morning, pose.Stage);
        pose.Apply(ResidentRoutine.At(.3f, true, true)); Assert.Equal(writes, own.Writes);
    }
    [Fact]
    public void MissingWrongTypeOrRootMovingControllerIsRefusedBeforeAnyWrites()
    {
        var own = Controller(); own.parameters[0].type = AnimatorControllerParameterType.Trigger;
        Assert.Throws<InvalidOperationException>(() => new ResidentPose(new NpcPresentationFigure(own)));
        Assert.Equal(0, own.Writes);
        own = Controller(); own.applyRootMotion = true;
        Assert.Throws<InvalidOperationException>(() => new ResidentPose(new NpcPresentationFigure(own)));
        Assert.Equal(0, own.Writes);
        Assert.Throws<ArgumentException>(() => new ResidentPose(new NpcPresentationFigure(null)));
        Assert.Throws<ArgumentException>(() => new ResidentPose(new NpcPresentationFigure(Controller(), false)));
    }
}
