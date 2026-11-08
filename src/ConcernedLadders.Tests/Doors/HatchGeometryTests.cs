using System;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 acceptance 3 (alignment) and 4 (physical operation), as geometry: the lid, frame, hinge,
/// aperture and ladder anchor agree with each other and with a native wood floor at every hinge angle.</summary>
public sealed class HatchGeometryTests
{
    private const float Epsilon = 1e-4f;

    [Fact]
    public void TheModuleAndSnapsMatchANativeWoodFloor()
    {
        Assert.Equal(2f, HatchGeometry.Module);
        // wood_floor's collider top and bottom in the installed game (child y 0.037, scale 0.1, box -0.0503 / 1.2975).
        Assert.Equal(0.0968, HatchGeometry.TopY, 3);
        Assert.Equal(-0.0329, HatchGeometry.FrameBottomY, 3);
        Assert.Equal(4, HatchGeometry.CornerSnaps.Count);
        foreach (ClimbPoint snap in HatchGeometry.CornerSnaps)
        {
            Assert.Equal(1f, Math.Abs(snap.X));
            Assert.Equal(1f, Math.Abs(snap.Z));
            Assert.Equal(0f, snap.Y);
        }

        Assert.Equal(4, HatchGeometry.CornerSnaps.Select(s => (s.X, s.Z)).Distinct().Count());
    }

    [Fact]
    public void TheFrameTilesTheModuleAroundTheApertureFlushWithTheFloor()
    {
        float area = 0f;
        foreach (HatchBox box in HatchGeometry.Frame)
        {
            Assert.Equal(HatchGeometry.TopY, box.Max.Y, 5);
            Assert.Equal(HatchGeometry.FrameBottomY, box.Min.Y, 5);
            Assert.True(box.Min.X >= -HatchGeometry.HalfModule - Epsilon && box.Max.X <= HatchGeometry.HalfModule + Epsilon);
            Assert.True(box.Min.Z >= -HatchGeometry.HalfModule - Epsilon && box.Max.Z <= HatchGeometry.HalfModule + Epsilon);
            // No band reaches into the aperture.
            bool insideX = box.Max.X > -HatchGeometry.HalfAperture + Epsilon && box.Min.X < HatchGeometry.HalfAperture - Epsilon;
            bool insideZ = box.Max.Z > -HatchGeometry.HalfAperture + Epsilon && box.Min.Z < HatchGeometry.HalfAperture - Epsilon;
            Assert.False(insideX && insideZ);
            area += box.Size.X * box.Size.Z;
        }

        // Bands do not overlap and leave exactly the 1.6 m x 1.6 m opening.
        Assert.Equal(4f - 1.6f * 1.6f, area, 4);
    }

    [Fact]
    public void TheClosedLidIsFlushAndCoversTheApertureInsideItsGap()
    {
        ClimbPoint[] corners = HatchGeometry.LidCorners(0f);
        Assert.Equal(HatchGeometry.TopY, corners.Max(c => c.Y), 5);
        Assert.Equal(HatchGeometry.TopY - HatchGeometry.LidThickness, corners.Min(c => c.Y), 5);
        Assert.Equal(HatchGeometry.HalfAperture - HatchGeometry.LidGap, corners.Max(c => c.Z), 5);
        Assert.Equal(-(HatchGeometry.HalfAperture - HatchGeometry.LidGap), corners.Min(c => c.Z), 5);
        Assert.Equal(HatchGeometry.HalfAperture - HatchGeometry.LidGap, corners.Max(c => c.X), 5);
        // The lid sits inside the frame, above the frame's bottom.
        Assert.True(HatchGeometry.TopY - HatchGeometry.LidThickness > HatchGeometry.FrameBottomY);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    [InlineData(22.5f)]
    [InlineData(45f)]
    [InlineData(67.5f)]
    [InlineData(89f)]
    [InlineData(90f)]
    public void TheLidNeverSwingsIntoTheFrameOrOutOfTheModule(float angle)
    {
        foreach (ClimbPoint corner in HatchGeometry.LidCorners(angle))
        {
            // Every lid point stays in front of the hinge and above the lid's own closed bottom, so it can never
            // enter the rear band (behind the hinge) or the front band (in front of the aperture) or the sides.
            Assert.True(corner.Z <= HatchGeometry.HingeZ + Epsilon, $"z {corner.Z} at {angle}");
            Assert.True(corner.Z >= -HatchGeometry.HalfAperture - Epsilon, $"z {corner.Z} at {angle}");
            Assert.True(Math.Abs(corner.X) <= HatchGeometry.HalfAperture + Epsilon);
            Assert.True(corner.Y >= HatchGeometry.TopY - HatchGeometry.LidThickness - Epsilon, $"y {corner.Y} at {angle}");
        }
    }

    [Fact]
    public void TheOpenLidStandsUprightBehindTheClearOpening()
    {
        ClimbPoint[] corners = HatchGeometry.LidCorners(HatchGeometry.OpenAngleDegrees);
        Assert.Equal(HatchGeometry.ClearRearZ, corners.Min(c => c.Z), 4);
        Assert.Equal(HatchGeometry.HingeZ, corners.Max(c => c.Z), 4);
        Assert.Equal(HatchGeometry.TopY, corners.Min(c => c.Y), 4);
        Assert.Equal(HatchGeometry.TopY + HatchGeometry.LidLength, corners.Max(c => c.Y), 4);
        // Clear opening: 1.6 m wide, at least 1.5 m deep.
        Assert.True(HatchGeometry.ClearRearZ - HatchGeometry.ClearFrontZ >= 1.5f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(90f)]
    public void TheLidPoseCentreIsTheCentreOfItsCorners(float angle)
    {
        LidPose pose = HatchGeometry.Lid(angle);
        ClimbPoint[] corners = HatchGeometry.LidCorners(angle);
        Assert.Equal(corners.Average(c => c.Y), pose.Centre.Y, 4);
        Assert.Equal(corners.Average(c => c.Z), pose.Centre.Z, 4);
        Assert.Equal(0f, pose.Centre.X, 4);
        Assert.Equal(angle, pose.AngleDegrees);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(-10f, 0f)]
    [InlineData(120f, 90f)]
    public void OutOfRangeAnglesClampToTheSwing(float angle, float expected)
    {
        Assert.Equal(expected, HatchGeometry.Lid(angle).AngleDegrees);
    }

    [Fact]
    public void TheLadderHangsFromTheFrontEdgeFacingTheHingeTopAtTheSnapPlane()
    {
        ClimbPoint origin = HatchGeometry.LadderOrigin;
        Assert.Equal(0f, origin.X);
        Assert.Equal(HatchGeometry.SnapPlaneY - LadderEdgeSnap.SectionHeight, origin.Y, 5);
        // The rails' back face sits the native ladder gap inside the frame's inner face.
        Assert.Equal(-HatchGeometry.HalfAperture + LadderEdgeSnap.Gap, origin.Z + LadderEdgeSnap.RailBack, 5);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.4f)]
    [InlineData(0.5f)]
    public void AClimberOnTheLadderFitsTheClearOpening(float radius)
    {
        Assert.True(HatchGeometry.ClimberMargin(radius) > 0.05f, $"margin {HatchGeometry.ClimberMargin(radius)}");
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    [InlineData(2f)]
    public void AnUnusableRadiusNeverReportsAFit(float radius)
    {
        Assert.Equal(float.NegativeInfinity, HatchGeometry.ClimberMargin(radius));
    }

    [Fact]
    public void ACapsuleWiderThanTheOpeningDoesNotFit()
    {
        // A sanity anchor for the margin: a capsule wider than the aperture cannot fit.
        Assert.True(HatchGeometry.ClimberMargin(0.85f) < 0f);
    }
}
