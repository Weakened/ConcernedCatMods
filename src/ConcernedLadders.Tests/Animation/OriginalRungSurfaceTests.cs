using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Animation;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

public sealed class OriginalRungSurfaceTests
{
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(.6f, .8f)]
    public void FrontAndRearFitVisibleOppositeFacesWithIndependentActorRight(float x, float z)
    {
        Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(5f,2f,7f),new ClimbPoint(5f,10f,7f),ClimbHeading.FromXz(x,z),.8f,1f/3f,out var run));
        var front=OriginalRungSurface.At(run,LadderFace.Front,RungShape.Plane,3f,RungShape.HalfDepth,.18f);
        var rear=OriginalRungSurface.At(run,LadderFace.Rear,RungShape.Plane,3f,RungShape.HalfDepth,.18f);
        Assert.Equal(.22f,run.ReachFrom(front),5); Assert.Equal(.10f,run.ReachFrom(rear),5);
        Assert.Equal(.18f,run.SidewaysOffsetOf(front),5); Assert.Equal(.18f,run.SidewaysOffsetOf(rear),5);
        Assert.Equal(3f,front.Y); Assert.Equal(3f,rear.Y);
        float frontRight=(front.X-5f)*-z+(front.Z-7f)*x;
        float rearRight=(rear.X-5f)*z-(rear.Z-7f)*x;
        Assert.Equal(.18f,frontRight,5); Assert.Equal(.18f,rearRight,5);
    }
}
