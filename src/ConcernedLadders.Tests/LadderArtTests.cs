using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

/// <summary>Owner note 2026-10-26: copper and iron ladders get thinner metal rails and round rungs; wood is unchanged.
/// Height, rung count and rung centres, the collider, the snap footprint and placed sections stay what they were.</summary>
public sealed class LadderArtTests
{
    public static IEnumerable<object[]> Styles() => new[]
    {
        new object[] { "wood" }, new object[] { "copper" }, new object[] { "iron" },
    };

    private static LadderStyle Style(string name) => name switch
    {
        "wood" => LadderStyle.Wood,
        "copper" => LadderStyle.Copper,
        "iron" => LadderStyle.Iron,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Fact]
    public void WoodIsUnchanged()
    {
        LadderStyle wood = LadderStyle.Wood;
        Assert.Equal(LadderProfile.Box, wood.RailProfile);
        Assert.Equal(LadderProfile.Box, wood.RungProfile);
        Assert.Equal(0.34f, wood.RailCentre, 5);
        Assert.Equal(0.12f, wood.RailWidth, 5);
        Assert.Equal(0.14f, wood.RailDepth, 5);
        Assert.Equal(RungShape.HalfWidth, wood.RungHalfLength);
        Assert.Equal(RungShape.HalfHeight, wood.RungHalfHeight);
        Assert.Equal(RungShape.HalfDepth, wood.RungHalfDepth);
        Assert.Equal(0f, wood.RungRadius);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void EveryStyleFitsTheSharedColliderAndFootprint(string name)
    {
        LadderStyle style = Style(name);
        LadderFootprint footprint = LadderFootprint.Section;
        Assert.Equal(footprint.HalfWidth, style.RailCentre + style.RailWidth * 0.5f, 5); // Outer rail edges at ±0.40 m.
        Assert.True(style.RailBack >= footprint.Back - 1e-5f);
        Assert.True(style.RailFront <= footprint.Front + 1e-5f);
        // Rungs reach at least the rails' inner faces and never past their centres.
        Assert.True(style.RungHalfLength >= style.RailCentre - style.RailWidth * 0.5f - 1e-5f);
        Assert.True(style.RungHalfLength <= style.RailCentre + 1e-5f);
        // Rungs stay inside the rail depth around the shared rung plane.
        Assert.True(LadderStyle.RailPlane - style.RungHalfDepth >= style.RailBack - 1e-5f);
        Assert.True(LadderStyle.RailPlane + style.RungHalfDepth <= style.RailFront + 1e-5f);
    }

    [Theory]
    [InlineData("copper")]
    [InlineData("iron")]
    public void MetalHasSlimRailsThatTouchTheSurfaceAndRoundRungs(string name)
    {
        LadderStyle metal = Style(name);
        Assert.True(metal.RailWidth <= 0.05f + 1e-5f); // Wood is 0.12 m.
        Assert.Equal(LadderEdgeSnap.RailBack, metal.RailBack, 5); // Flush with the collider's back: rests on the face.
        Assert.Equal(LadderProfile.Round, metal.RungProfile);
        Assert.True(metal.RungRadius > 0f);
        Assert.Equal(metal.RungRadius, metal.RungHalfHeight);
        Assert.Equal(metal.RungRadius, metal.RungHalfDepth);
        Assert.True(metal.RungRadius < RungShape.HalfHeight); // Thinner than the wood rung.
    }

    [Fact]
    public void CopperAndIronDifferInShape()
    {
        Assert.NotEqual(LadderStyle.Copper.RailProfile, LadderStyle.Iron.RailProfile);
        Assert.NotEqual(LadderStyle.Copper.RungRadius, LadderStyle.Iron.RungRadius);
    }

    [Fact]
    public void RungCentresAreUnchanged()
    {
        Assert.Equal(6, RungShape.PerSection);
        for (int i = 0; i < RungShape.PerSection; i++)
        {
            Assert.Equal((i + 0.5f) / 3f, LadderStyle.RungHeight(i), 5);
        }
    }

    [Theory]
    [InlineData((int)LadderProfile.Box, 0.04f, 0.16f)]
    [InlineData((int)LadderProfile.Stadium, 0.05f, 0.16f)]
    [InlineData((int)LadderProfile.Round, 0.05f, 0.05f)]
    public void ProfileRingsAreClosedAndBounded(int profileValue, float width, float depth)
    {
        var profile = (LadderProfile)profileValue;
        IReadOnlyList<ProfileVertex> ring = LadderProfiles.Ring(profile, width, depth);
        Assert.True(ring.Count >= 8);
        ProfileVertex first = ring[0], last = ring[ring.Count - 1];
        if (profile != LadderProfile.Box)
        {
            Assert.Equal(first.A, last.A, 4); Assert.Equal(first.B, last.B, 4); // Closed for the texture seam.
        }

        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
        float previous = -1f;
        double signedArea = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            ProfileVertex v = ring[i];
            Assert.Equal(1f, (float)Math.Sqrt(v.NormalA * v.NormalA + v.NormalB * v.NormalB), 4);
            Assert.True(v.Distance >= previous - 1e-6f); // Distance around the ring never goes back.
            previous = v.Distance;
            minA = Math.Min(minA, v.A); maxA = Math.Max(maxA, v.A);
            minB = Math.Min(minB, v.B); maxB = Math.Max(maxB, v.B);
            ProfileVertex next = ring[(i + 1) % ring.Count];
            signedArea += v.A * next.B - next.A * v.B;
            // Each normal points away from the centre (outward).
            Assert.True(v.A * v.NormalA + v.B * v.NormalB > 0f);
        }

        Assert.Equal(-width / 2f, minA, 3); Assert.Equal(width / 2f, maxA, 3);
        Assert.Equal(-depth / 2f, minB, 3); Assert.Equal(depth / 2f, maxB, 3);
        Assert.True(signedArea > 0); // Counter-clockwise, which the mesh builder's winding relies on.
    }

    [Fact]
    public void AProfileNeedsAPositiveSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LadderProfiles.Ring(LadderProfile.Round, 0f, 0.05f));
        Assert.Throws<ArgumentOutOfRangeException>(() => LadderProfiles.Ring(LadderProfile.Box, float.NaN, 0.05f));
    }

    [Fact]
    public void ATurnedSectionNeverJoinsAClimbingRun()
    {
        // A section the player rotated on a wall (22.5 degrees) right on top of a straight one is not one column.
        static LadderGeometry Section(float bottom, float yawDegrees)
        {
            double yaw = yawDegrees * Math.PI / 180.0;
            Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(0f, bottom, 0f), new ClimbPoint(0f, bottom + 2f, 0f),
                ClimbHeading.FromXz((float)Math.Sin(yaw), (float)Math.Cos(yaw)), 0.8f, 1f / 3f, out LadderGeometry geometry));
            return geometry;
        }

        foreach (float yaw in new[] { 22.5f, -22.5f, 45f, 90f, 180f })
        {
            Assert.False(LadderRules.Joins(Section(0f, 0f), Section(2f, yaw)));
            var samples = new[] { new LadderSample(1, Section(0f, 0f)), new LadderSample(2, Section(2f, yaw)) };
            Assert.True(LadderRules.TryBuildRun(samples, 1, out List<int> ids, out _));
            Assert.Equal(new[] { 1 }, ids);
        }

        Assert.True(LadderRules.Joins(Section(0f, 0f), Section(2f, 0f)));
    }
}
