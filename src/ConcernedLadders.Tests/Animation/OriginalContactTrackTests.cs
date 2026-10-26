using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

public sealed class OriginalContactTrackTests
{
    private const float Pitch = 1f / 3f;
    private static OriginalContactTrack Track()
    {
        var track = new OriginalContactTrack();
        Assert.True(track.Bind(2f, Pitch / 2f, Pitch, 47, 4));
        return track;
    }

    [Fact]
    public void FirstUpStrokeMovesRightHandAndLeftFootWhileOtherWorldContactsStayFixed()
    {
        var track = Track();
        float hand = track.Contact(0).Height, foot = track.Contact(3).Height;
        Assert.True(track.Advance(Pitch / 2f, 2f + Pitch / 2f, .02f, false));
        Assert.Equal(hand, track.Contact(0).Height);
        Assert.Equal(foot, track.Contact(3).Height);
        Assert.True(track.Contact(1).Clearance > .99f);
        Assert.True(track.Contact(2).Clearance > .99f);
    }

    [Fact]
    public void FirstDownStrokeMovesHigherDiagonalAndNotTheLowFoot()
    {
        var track = Track();
        float lowHand = track.Contact(1).Height, lowFoot = track.Contact(2).Height;
        Assert.True(track.Advance(-Pitch / 2f, 2f - Pitch / 2f, .02f, false));
        Assert.Equal(lowHand, track.Contact(1).Height);
        Assert.Equal(lowFoot, track.Contact(2).Height);
        Assert.True(track.Contact(0).Clearance > .99f);
        Assert.True(track.Contact(3).Clearance > .99f);
    }

    [Fact]
    public void MidStrokeReversalRetracesToItsExactOriginalContacts()
    {
        var track = Track();
        float[] start = Enumerable.Range(0,4).Select(i => track.Contact(i).Height).ToArray();
        Assert.True(track.Advance(Pitch * .4f, 2f + Pitch * .4f, .02f, false));
        Assert.True(track.Advance(-Pitch * .4f, 2f, .02f, false));
        for (int i = 0; i < 4; i++) { Assert.Equal(start[i], track.Contact(i).Height, 5); Assert.True(track.Contact(i).Planted); }
    }

    [Fact]
    public void HoldSettlesFreeLimbsAndNeverMovesTheTwoPlantedContacts()
    {
        var track = Track();
        Assert.True(track.Advance(Pitch * .7f, 2f + Pitch * .7f, .02f, false));
        float hand = track.Contact(0).Height, foot = track.Contact(3).Height;
        for (int i = 0; i < 10; i++) { Assert.True(track.Advance(0f, 2f + Pitch * .7f, .02f, true)); }
        Assert.Equal(-1, track.MovingPair);
        Assert.Equal(hand, track.Contact(0).Height); Assert.Equal(foot, track.Contact(3).Height);
        for (int i = 0; i < 4; i++) { Assert.True(track.Contact(i).Planted); Assert.Equal(0f, track.Contact(i).Clearance); }
    }

    [Fact]
    public void StrokeUsesTravelRatherThanElapsedTimeOrRequestedSpeed()
    {
        var normal = Track(); var fast = Track();
        for (int i = 0; i < 8; i++)
        {
            Assert.True(normal.Advance(.03f, 2f + .03f * (i+1), .02f, false));
            Assert.True(fast.Advance(.03f, 2f + .03f * (i+1), .01f, false));
        }
        for (int i = 0; i < 4; i++) { Assert.Equal(normal.Contact(i).Height, fast.Contact(i).Height); Assert.Equal(normal.Contact(i).Clearance, fast.Contact(i).Clearance); }
    }

    [Fact]
    public void AlignedExtensionsAboveAndBelowPreserveEveryInFlightWorldContact()
    {
        var track = Track(); Assert.True(track.Advance(.12f, 2.12f, .02f, false));
        var contacts = Enumerable.Range(0,4).Select(i => track.Contact(i)).ToArray();
        Assert.True(track.RefreshBounds(Pitch/2f - 2f, 59));
        for (int i = 0; i < 4; i++) { Assert.Equal(contacts[i].Height, track.Contact(i).Height); Assert.Equal(contacts[i].Clearance, track.Contact(i).Clearance); }
        Assert.False(track.RefreshBounds(Pitch/2f - 1.9f, 59));
        Assert.False(track.RefreshBounds(10f + Pitch/2f, 5));
    }

    [Fact]
    public void BoundsNeverProducePhantomRungsOrSwingArcWhenBothEndpointsAreTheSame()
    {
        var track = new OriginalContactTrack(); Assert.True(track.Bind(0f, Pitch/2f, Pitch, 5, 4));
        float feet = 0f;
        for (int step = 0; step < 300; step++)
        {
            float d = step < 150 ? .036f : -.036f;
            feet += d;
            Assert.True(track.Advance(d, feet, .02f, false));
            for (int i = 0; i < 4; i++)
            {
                var c = track.Contact(i); Assert.InRange(c.Height, Pitch/2f - .0001f, Pitch/2f + 5*Pitch + .0001f);
                if (c.Planted) { Assert.Equal(0f, c.Clearance, 5); Assert.Equal(Math.Round((c.Height - Pitch/2f)/Pitch), (c.Height - Pitch/2f)/Pitch, 4); }
            }
        }
    }

    [Fact]
    public void InvalidTravelDoesNotAdvanceContactsAndHoldCannotSmuggleMovement()
    {
        var track = Track(); float before = track.Contact(1).Height;
        Assert.False(track.Advance(float.NaN, 2f, .02f, false));
        Assert.False(track.Advance(.1f, 2f, .02f, true));
        Assert.Equal(before, track.Contact(1).Height);
    }
    [Theory]
    [InlineData(.51f, 1f)]
    [InlineData(.49f, 1f)]
    [InlineData(.51f, -1f)]
    [InlineData(.49f, -1f)]
    public void RepeatedPartialStrokeHoldsCannotRatchetContactsAwayFromCompletedTravel(float fraction, float direction)
    {
        var track = new OriginalContactTrack(); Assert.True(track.Bind(20f, Pitch/2f, Pitch, 200, 4));
        float feet = 20f;
        for (int cycle = 0; cycle < 60; cycle++)
        {
            float d = fraction * Pitch * direction; feet += d;
            Assert.True(track.Advance(d, feet, .02f, false));
            for (int tick = 0; tick < 10; tick++) { Assert.True(track.Advance(0f, feet, .02f, true)); }
            float relative = (track.Contact(2).Height + track.Contact(3).Height) * .5f - feet;
            Assert.InRange(relative, -Pitch * .1f, Pitch * 2.1f);
        }
    }

    [Theory]
    [InlineData(.4f, .00001f)]
    [InlineData(.4f, -.00001f)]
    [InlineData(.7f, .00001f)]
    [InlineData(.7f, -.00001f)]
    public void PartialSettlementResumeOrReverseDoesNotSnapAContact(float fraction, float resume)
    {
        var track = Track(); float feet = 2f + fraction * Pitch;
        Assert.True(track.Advance(fraction * Pitch, feet, .02f, false));
        Assert.True(track.Advance(0f, feet, .02f, true));
        var before = Enumerable.Range(0,4).Select(i => track.Contact(i)).ToArray();
        Assert.True(track.Advance(resume, feet + resume, .02f, false));
        for (int i = 0; i < 4; i++)
        {
            Assert.InRange(Math.Abs(before[i].Height - track.Contact(i).Height), 0f, .0002f);
            if (before[i].Planted) { Assert.Equal(before[i].Height, track.Contact(i).Height); }
        }
    }

    [Fact]
    public void RemovedOccupiedBoundInvalidatesTheTrackRatherThanContinuingOnOldGeometry()
    {
        var track = Track();
        Assert.False(track.RefreshBounds(10f + Pitch/2f, 5));
        Assert.False(track.Ready);
        Assert.False(track.Advance(.02f, 2.02f, .02f, false));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void OnlyTravelBeyondThePostSettlementActivationBoundaryStartsANewStroke(float direction)
    {
        var track = Track(); float feet=2f + direction*.51f*Pitch;
        Assert.True(track.Advance(direction*.51f*Pitch,feet,.02f,false));
        for(int i=0;i<10;i++) { Assert.True(track.Advance(0f,feet,.02f,true)); }
        var before=Enumerable.Range(0,4).Select(i=>track.Contact(i).Height).ToArray();
        float gap=direction>0 ? Math.Min(before[2],before[3])-feet-Pitch*.5f : feet+Pitch*1.5f-Math.Max(before[2],before[3]);
        Assert.True(gap>.001f);
        float almost=gap-.0001f; feet+=direction*almost;
        Assert.True(track.Advance(direction*almost,feet,.02f,false));
        feet+=direction*.0001f; Assert.True(track.Advance(direction*.0001f,feet,.02f,false));
        for(int i=0;i<4;i++) { Assert.Equal(before[i],track.Contact(i).Height,5); }
        Assert.Equal(-1,track.MovingPair);
        feet+=direction*.04f; Assert.True(track.Advance(direction*.04f,feet,.02f,false));
        Assert.Equal(.04f/Pitch,track.StrokeProgress,4);
    }

    [Fact]
    public void CombinedAndPartitionedTravelAgreeAcrossStrokeEndsAndReversal()
    {
        var combined=Track();var partitioned=Track();float feet=2f;
        foreach(float travel in new[]{Pitch*2.37f,-Pitch*1.73f,Pitch*.91f})
        {
            Assert.True(combined.Advance(travel,feet+travel,.02f,false));
            const int count=31;
            for(int i=0;i<count;i++) { Assert.True(partitioned.Advance(travel/count,feet+travel*(i+1)/count,.02f,false)); }
            feet+=travel;
            Assert.Equal(combined.MovingPair,partitioned.MovingPair);
            for(int i=0;i<4;i++)
            {
                Assert.Equal(combined.Contact(i).Height,partitioned.Contact(i).Height,4);
                Assert.Equal(combined.Contact(i).Clearance,partitioned.Contact(i).Clearance,4);
            }
        }
    }

}
