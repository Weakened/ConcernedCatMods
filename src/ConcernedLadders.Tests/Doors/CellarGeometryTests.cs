using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-005 #486 acceptance 3 (geometry) and 6 (collision), as geometry: the frame, both leaves, the hinge
/// axes, the sweep envelope and the clear passage agree with each other and with a native wood floor at every angle.</summary>
public sealed class CellarGeometryTests
{
    private const float Epsilon = 1e-4f;

    private static readonly CellarLeaf[] Leaves = { CellarLeaf.Left, CellarLeaf.Right };

    private static IEnumerable<float> Angles(float step)
    {
        for (float angle = 0f; angle < CellarGeometry.OpenAngleDegrees; angle += step) { yield return angle; }
        yield return CellarGeometry.OpenAngleDegrees;
    }

    [Fact]
    public void TheBaseTakesAWoodFloorTilesPlaceLikeTheTrapdoor()
    {
        Assert.Equal(2f, CellarGeometry.Module);
        Assert.Equal(HatchGeometry.TopY, CellarGeometry.FloorTopY);
        Assert.Equal(HatchGeometry.FrameBottomY, CellarGeometry.FrameBottomY);
        Assert.Same(HatchGeometry.CornerSnaps, CellarGeometry.CornerSnaps);
        Assert.Equal("$hud_snappoint_corner", CellarGeometry.CornerSnapToken);
    }

    [Fact]
    public void TheLeavesShareTheTrapdoorsMotionBecauseTheyOpenThroughTheSameAngle()
    {
        // HatchMotion.Angle is HatchGeometry.OpenAngleDegrees times the ease; reusing it for the leaves needs this.
        Assert.Equal(HatchGeometry.OpenAngleDegrees, CellarGeometry.OpenAngleDegrees);
        // And its 5-degree check spacing is a tighter bound for a leaf than for the lid: the leaf reaches less far.
        Assert.True(CellarGeometry.LeafReach < HatchGeometry.LidLength);
    }

    [Fact]
    public void TheDocumentedDimensionsAreTheModels()
    {
        // DOOR_DONOR_MATRIX.md is CL-004's; CELLAR_DOORS.md section 2 lists these values.
        // #549: the high end raised to a bulkhead's height and the low end lowered so the front panel's top stays where
        // the ladder exit over it was measured (0.375 m above the floor top).
        Assert.Equal(0.2625f, CellarGeometry.FrontTopY - CellarGeometry.FloorTopY, 4);
        Assert.Equal(1.7625f, CellarGeometry.RearTopY - CellarGeometry.FloorTopY, 4);
        Assert.Equal(36.870, CellarGeometry.SlopeDegrees, 3);
        Assert.Equal(0.375f, CellarGeometry.TopAt(CellarGeometry.ApertureFrontZ) - CellarGeometry.FloorTopY, 4);
        Assert.Equal(1.3125f, CellarGeometry.RearBeamBottomY - CellarGeometry.FloorTopY, 4);
        Assert.Equal(1.7f, 2f * CellarGeometry.HalfAperture, 4);
        Assert.Equal(1.65f, CellarGeometry.ApertureRearZ - CellarGeometry.ApertureFrontZ, 4);
        Assert.Equal(1.56f, 2f * CellarGeometry.ClearHalfWidth, 4);
        Assert.Equal(0.84f, CellarGeometry.HingeU, 4);
        Assert.Equal(0.835f, CellarGeometry.LeafWidth, 4);
        Assert.Equal(1.9925, CellarGeometry.LeafLength, 3);
        Assert.Equal(0.8372, CellarGeometry.LeafReach, 3);
        HatchBox sweep = CellarGeometry.SweepInPiece(CellarLeaf.Right);
        Assert.Equal(-1.341, sweep.Min.Z, 3);
        Assert.Equal(0.79, sweep.Max.Z, 3);
        Assert.Equal(2.3428, sweep.Max.Y, 3);
    }

    [Fact]
    public void TheHighEndIsOpenBelowItsBeam()
    {
        // #549 (owner playtest 2026-11-10): a bulkhead's high end stands against the house and the stair goes on under
        // it. Below the beam, between the cheeks, from the floor's bottom down and back to the module's rear edge, no
        // frame part stands; the beam is RearBeamHeight deep under the slope.
        CellarPrism beam = CellarGeometry.Frame.Single(p => p.Name == "rear_header");
        Assert.Equal(CellarGeometry.RearBeamHeight, CellarGeometry.TopAt(CellarGeometry.ApertureRearZ) - beam.Profile.Min(p => p.Y), 5);
        var passage = new HatchBox(
            new ClimbPoint(-CellarGeometry.HalfAperture, CellarGeometry.FrameBottomY - 3f, CellarGeometry.ApertureFrontZ),
            new ClimbPoint(CellarGeometry.HalfAperture, CellarGeometry.RearBeamBottomY, CellarGeometry.HalfModule));
        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            for (int index = 0; index < 4; ++index)
            {
                ProfilePoint p = prism.Profile[index];
                bool overlapsX = prism.MaxX > passage.Min.X + Epsilon && prism.MinX < passage.Max.X - Epsilon;
                bool inside = p.Y < passage.Max.Y - Epsilon && p.Z > passage.Min.Z + Epsilon && p.Z <= passage.Max.Z;
                Assert.False(overlapsX && inside, prism.Name + " stands in the passage under the high end");
            }
        }
    }

    [Fact]
    public void AStairDownFromTheLowEndHasHeadroomUnderTheBeam()
    {
        // A 45-degree stair from the front panel's inner face at floor level, going down toward the high end: where it
        // passes under the beam (the opening's rear face to the module's rear edge) a player's head (2 m with margin)
        // clears the beam's underside by far. Before #549 the solid header met the head at ground level.
        const float Player = 2f;
        foreach (float z in new[] { CellarGeometry.ApertureRearZ, CellarGeometry.HalfModule })
        {
            float stair = CellarGeometry.FloorTopY - (z - CellarGeometry.ApertureFrontZ);
            Assert.True(CellarGeometry.RearBeamBottomY - stair > Player + 0.9f, "headroom at z " + z);
        }

        // The game's own stair pieces are shallower: wood_stair, stone_stair, ashwood_stair, the black marble and grausten
        // stairs all rise 1 m over 2 m (26.57 degrees, measured from their snap points); only wood_stepladder is 45. On
        // the shallow one the head still clears the beam at its inner face by 2.14 m.
        float shallow = CellarGeometry.FloorTopY - 0.5f * (CellarGeometry.ApertureRearZ - CellarGeometry.ApertureFrontZ);
        Assert.InRange(CellarGeometry.RearBeamBottomY - shallow, 2.137f, 2.138f);
        Assert.True(CellarGeometry.RearBeamBottomY - shallow > Player);

        // And the beam is above a standing player's knees at the top of the stair, so it is never a step to trip on.
        Assert.True(CellarGeometry.RearBeamBottomY - CellarGeometry.FloorTopY > 1.3f);
    }

    [Fact]
    public void TheSlopeFrameIsTheTransformTheRuntimeWillUse()
    {
        // Unity's Quaternion.Euler(-slope, 0, 0) about x, applied to slope-local (u, w, v) as (x, y, z), then the origin.
        double phi = -CellarGeometry.SlopeDegrees * Math.PI / 180.0;
        foreach ((float u, float w, float v) in new[] { (0f, 0f, 0f), (0.3f, -0.06f, 1f), (-0.84f, 0.835f, 1.97f) })
        {
            float y = (float)(w * Math.Cos(phi) - v * Math.Sin(phi));
            float z = (float)(w * Math.Sin(phi) + v * Math.Cos(phi));
            ClimbPoint expected = new ClimbPoint(u, CellarGeometry.SlopeOrigin.Y + y, CellarGeometry.SlopeOrigin.Z + z);
            ClimbPoint actual = CellarGeometry.ToPiece(u, w, v);
            Assert.Equal(expected.X, actual.X, 4);
            Assert.Equal(expected.Y, actual.Y, 4);
            Assert.Equal(expected.Z, actual.Z, 4);
        }

        // The slope plane w = 0 is the cover's top line.
        for (float v = 0f; v <= 2.2f; v += 0.1f)
        {
            ClimbPoint top = CellarGeometry.ToPiece(0f, 0f, v);
            Assert.Equal(CellarGeometry.TopAt(top.Z), top.Y, 4);
        }
    }

    [Fact]
    public void TheFrameIsFourConvexPrismsInsideTheModuleWithTopsInTheSlope()
    {
        Assert.Equal(4, CellarGeometry.Frame.Count);
        Assert.Equal(4, CellarGeometry.Frame.Select(p => p.Name).Distinct().Count());
        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            Assert.True(prism.MinX < prism.MaxX);
            Assert.True(prism.MinX >= -CellarGeometry.HalfModule - Epsilon && prism.MaxX <= CellarGeometry.HalfModule + Epsilon);
            Assert.Equal(4, prism.Profile.Count);
            for (int index = 0; index < 4; ++index)
            {
                ProfilePoint a = prism.Profile[index], b = prism.Profile[(index + 1) % 4], c = prism.Profile[(index + 2) % 4];
                float cross = (b.Z - a.Z) * (c.Y - b.Y) - (b.Y - a.Y) * (c.Z - b.Z);
                Assert.True(cross > 0f, prism.Name + " is not convex counter-clockwise at " + index);
                Assert.True(a.Z >= -CellarGeometry.HalfModule - Epsilon && a.Z <= CellarGeometry.HalfModule + Epsilon);
                Assert.True(a.Y >= CellarGeometry.FrameBottomY - Epsilon);
                // Every point is at or below the cover's top, and the top points are on it.
                Assert.True(a.Y <= CellarGeometry.TopAt(a.Z) + Epsilon, prism.Name + " rises above the slope");
            }

            // Every part reaches the floor's bottom but the rear header, a beam under the slope since #549.
            float bottom = prism.Name == "rear_header" ? CellarGeometry.RearBeamBottomY : CellarGeometry.FrameBottomY;
            Assert.Equal(bottom, prism.Profile.Min(p => p.Y), 5);
            Assert.Equal(2, prism.Profile.Count(p => Math.Abs(p.Y - CellarGeometry.TopAt(p.Z)) < Epsilon));
        }

        // The cheeks run the full depth; the panel and header sit between them, at the low and high ends.
        CellarPrism left = CellarGeometry.Frame.Single(p => p.Name == "cheek_left");
        CellarPrism right = CellarGeometry.Frame.Single(p => p.Name == "cheek_right");
        Assert.Equal(-CellarGeometry.HalfModule, left.MinX);
        Assert.Equal(-CellarGeometry.HalfAperture, left.MaxX);
        Assert.Equal(CellarGeometry.HalfAperture, right.MinX);
        Assert.Equal(CellarGeometry.HalfModule, right.MaxX);
        Assert.Equal(CellarGeometry.ApertureFrontZ, CellarGeometry.Frame.Single(p => p.Name == "front_panel").Profile.Max(p => p.Z), 5);
        Assert.Equal(CellarGeometry.ApertureRearZ, CellarGeometry.Frame.Single(p => p.Name == "rear_header").Profile.Min(p => p.Z), 5);
    }

    [Fact]
    public void TheFrameLeavesTheOpeningClearThroughTheFloor()
    {
        // No prism reaches into the opening between the cheeks, the panel and the header, at any height.
        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            bool insideX = prism.MaxX > -CellarGeometry.HalfAperture + Epsilon && prism.MinX < CellarGeometry.HalfAperture - Epsilon;
            bool insideZ = prism.Profile.Max(p => p.Z) > CellarGeometry.ApertureFrontZ + Epsilon &&
                           prism.Profile.Min(p => p.Z) < CellarGeometry.ApertureRearZ - Epsilon;
            Assert.False(insideX && insideZ, prism.Name + " intrudes into the opening");
        }
    }

    [Fact]
    public void TheClosedLeavesAreFlushWithTheFrameAndCoverTheOpeningWithTheirGaps()
    {
        foreach (CellarLeaf leaf in Leaves)
        {
            ClimbPoint[] corners = CellarGeometry.LeafCorners(leaf, 0f);
            for (int index = 0; index < 8; ++index)
            {
                bool bottom = (index & 2) != 0;
                ClimbPoint corner = corners[index];
                float drop = CellarGeometry.TopAt(corner.Z) - corner.Y;
                // Top corners lie in the slope plane; bottom corners a thickness below it, measured square to it.
                float expected = bottom ? CellarGeometry.LeafThickness / (float)Math.Cos(CellarGeometry.SlopeDegrees * Math.PI / 180.0) : 0f;
                Assert.Equal(expected, drop, 4);
            }

            float outer = corners.Max(c => Math.Abs(c.X)), inner = corners.Min(c => Math.Abs(c.X));
            Assert.Equal(CellarGeometry.HalfAperture - CellarGeometry.LeafGap, outer, 5);
            Assert.Equal(CellarGeometry.SeamGap / 2f, inner, 5);
            // Along the slope, inside the panel and the header with their gaps.
            Assert.True(corners.Min(c => c.Z) >= CellarGeometry.ApertureFrontZ + CellarGeometry.LeafGap - Epsilon);
            Assert.Equal(CellarGeometry.ApertureRearZ - CellarGeometry.LeafGap, corners.Max(c => c.Z), 4);
        }
    }

    [Fact]
    public void TheLeftLeafIsTheRightLeafMirroredAtEveryAngle()
    {
        foreach (float angle in Angles(1f))
        {
            for (int index = 0; index < 8; ++index)
            {
                ClimbPoint right = CellarGeometry.LeafCorner(CellarLeaf.Right, angle, index);
                ClimbPoint left = CellarGeometry.LeafCorner(CellarLeaf.Left, angle, index);
                Assert.Equal(-right.X, left.X, 5);
                Assert.Equal(right.Y, left.Y, 5);
                Assert.Equal(right.Z, left.Z, 5);
            }
        }
    }

    [Fact]
    public void OpeningLiftsTheSeamEdgesAndTurnsEachLeafOutward()
    {
        // Free (seam) top corner, front end: index 4.
        ClimbPoint closed = CellarGeometry.LeafCorner(CellarLeaf.Right, 0f, 4);
        ClimbPoint half = CellarGeometry.LeafCorner(CellarLeaf.Right, 45f, 4);
        ClimbPoint open = CellarGeometry.LeafCorner(CellarLeaf.Right, 90f, 4);
        Assert.True(half.Y > closed.Y && open.Y > half.Y);
        Assert.True(half.X > closed.X && open.X > half.X);
        // Upright: the seam edge stands over the hinge, a leaf's width out along the slope's normal.
        Assert.Equal(CellarGeometry.HingeU, open.X, 4);
        CellarGeometry.SlopeCorner(CellarLeaf.Right, 90f, 4, out float u, out float w, out _);
        Assert.Equal(CellarGeometry.HingeU, u, 4);
        Assert.Equal(CellarGeometry.LeafWidth, w, 4);
    }

    [Fact]
    public void TheHingePivotAndItsRotationReproduceTheLeafCorners()
    {
        // The runtime convention: the pivot at HingeInSlope, turned by Euler(0, 0, sign * angle) about its local z, with
        // the leaf box at LeafCentreInHinge of size LeafSize. Unity's z rotation: (x, y) -> (x cos - y sin, x sin + y cos).
        foreach (CellarLeaf leaf in Leaves)
        {
            foreach (float angle in new[] { 0f, 10f, 45f, 80f, 90f })
            {
                ClimbPoint hinge = CellarGeometry.HingeInSlope(leaf);
                ClimbPoint centre = CellarGeometry.LeafCentreInHinge(leaf);
                ClimbPoint size = CellarGeometry.LeafSize;
                double phi = CellarGeometry.HingeRotationSign(leaf) * angle * Math.PI / 180.0;
                var built = new List<ClimbPoint>();
                for (int index = 0; index < 8; ++index)
                {
                    float x = centre.X + ((index & 1) == 0 ? -0.5f : 0.5f) * size.X;
                    float y = centre.Y + ((index & 2) == 0 ? -0.5f : 0.5f) * size.Y;
                    float z = centre.Z + ((index & 4) == 0 ? -0.5f : 0.5f) * size.Z;
                    float rx = (float)(x * Math.Cos(phi) - y * Math.Sin(phi));
                    float ry = (float)(x * Math.Sin(phi) + y * Math.Cos(phi));
                    built.Add(CellarGeometry.ToPiece(hinge.X + rx, hinge.Y + ry, hinge.Z + z));
                }

                ClimbPoint[] model = CellarGeometry.LeafCorners(leaf, angle);
                foreach (ClimbPoint corner in model)
                {
                    Assert.Contains(built, b => Math.Abs(b.X - corner.X) < 1e-4f && Math.Abs(b.Y - corner.Y) < 1e-4f && Math.Abs(b.Z - corner.Z) < 1e-4f);
                }
            }
        }
    }

    [Fact]
    public void NoLeafEverSwingsIntoTheFrameTheOtherLeafOrBehindTheHeader()
    {
        foreach (float angle in Angles(0.5f))
        {
            foreach (CellarLeaf leaf in Leaves)
            {
                for (int index = 0; index < 8; ++index)
                {
                    CellarGeometry.SlopeCorner(leaf, angle, index, out float u, out float w, out _);
                    float magnitude = Math.Abs(u);
                    // Never past its hinge toward the cheek, never across the centre line toward the other leaf.
                    Assert.True(magnitude <= CellarGeometry.HingeU + Epsilon, leaf + " passes its hinge at " + angle);
                    Assert.True(magnitude > 0f, leaf + " crosses the seam at " + angle);
                    Assert.True(Math.Sign(u) == (leaf == CellarLeaf.Right ? 1 : -1));
                    Assert.True(w >= -CellarGeometry.LeafThickness - Epsilon);
                    ClimbPoint corner = CellarGeometry.LeafCorner(leaf, angle, index);
                    Assert.True(corner.Z <= CellarGeometry.ApertureRearZ - CellarGeometry.LeafGap + Epsilon, leaf + " reaches the header at " + angle);
                }

                // A box is not only its corners: sample its volume against every frame prism.
                foreach (ClimbPoint point in LeafSamples(leaf, angle))
                {
                    foreach (CellarPrism prism in CellarGeometry.Frame)
                    {
                        Assert.False(prism.ContainsStrictly(point, 1e-4f), leaf + " enters " + prism.Name + " at " + angle);
                    }
                }
            }
        }
    }

    [Fact]
    public void TheSweepEnvelopeHoldsEveryPoseAndIsTight()
    {
        foreach (CellarLeaf leaf in Leaves)
        {
            HatchBox sweep = CellarGeometry.SweepInSlope(leaf);
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity, minW = float.PositiveInfinity, maxW = float.NegativeInfinity;
            foreach (float angle in Angles(0.05f))
            {
                for (int index = 0; index < 8; ++index)
                {
                    CellarGeometry.SlopeCorner(leaf, angle, index, out float u, out float w, out float v);
                    Assert.InRange(u, sweep.Min.X - Epsilon, sweep.Max.X + Epsilon);
                    Assert.InRange(w, sweep.Min.Y - Epsilon, sweep.Max.Y + Epsilon);
                    Assert.InRange(v, sweep.Min.Z - Epsilon, sweep.Max.Z + Epsilon);
                    minU = Math.Min(minU, u); maxU = Math.Max(maxU, u); minW = Math.Min(minW, w); maxW = Math.Max(maxW, w);
                }
            }

            Assert.True(Math.Abs(sweep.Min.X - minU) < 1e-3f);
            Assert.True(Math.Abs(sweep.Max.X - maxU) < 1e-3f);
            Assert.True(Math.Abs(sweep.Min.Y - minW) < 1e-3f);
            Assert.True(Math.Abs(sweep.Max.Y - maxW) < 1e-3f);
        }

        // The two leaves' sweeps never meet, so the pair can never collide with itself.
        Assert.True(CellarGeometry.SweepInSlope(CellarLeaf.Right).Min.X > CellarGeometry.SweepInSlope(CellarLeaf.Left).Max.X);
    }

    [Fact]
    public void TheSweepStaysBesideTheCheeksAndInFrontOfAWallOnTheRearEdge()
    {
        foreach (CellarLeaf leaf in Leaves)
        {
            HatchBox piece = CellarGeometry.SweepInPiece(leaf);
            Assert.True(Math.Max(Math.Abs(piece.Min.X), Math.Abs(piece.Max.X)) <= CellarGeometry.HingeU + Epsilon);
            // A wall centred on the rear grid edge up to twice the header's depth thick never meets a leaf.
            Assert.True(piece.Max.Z <= CellarGeometry.HalfModule - CellarGeometry.RearHeaderDepth + Epsilon);
            Assert.True(piece.Min.Y > CellarGeometry.FloorTopY);
            // An open leaf leans over the low end: the sweep leaves the module forward, above the front panel only.
            Assert.True(piece.Min.Z < -CellarGeometry.HalfModule);
        }

        foreach (float angle in Angles(1f))
        {
            foreach (ClimbPoint corner in CellarGeometry.LeafCorners(CellarLeaf.Right, angle))
            {
                if (corner.Z < -CellarGeometry.HalfModule) { Assert.True(corner.Y > CellarGeometry.FrontTopY); }
            }
        }
    }

    [Fact]
    public void OpenTheColumnBetweenTheLeavesIsClearAtEveryHeight()
    {
        HatchBox column = CellarGeometry.ClearColumn(CellarGeometry.FrameBottomY - 3f, CellarGeometry.RearTopY + 3f);
        foreach (CellarLeaf leaf in Leaves)
        {
            foreach (ClimbPoint point in LeafSamples(leaf, CellarGeometry.OpenAngleDegrees))
            {
                bool inside = point.X > column.Min.X + Epsilon && point.X < column.Max.X - Epsilon &&
                              point.Z > column.Min.Z + Epsilon && point.Z < column.Max.Z - Epsilon;
                Assert.False(inside, leaf + " open is inside the passage");
            }
        }

        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            bool insideX = prism.MaxX > column.Min.X + Epsilon && prism.MinX < column.Max.X - Epsilon;
            bool insideZ = prism.Profile.Max(p => p.Z) > column.Min.Z + Epsilon && prism.Profile.Min(p => p.Z) < column.Max.Z - Epsilon;
            Assert.False(insideX && insideZ, prism.Name + " is inside the passage");
        }

        // Closed, the leaves do cross the column: nothing passes a closed cellar door.
        Assert.Contains(LeafSamples(CellarLeaf.Right, 0f), p => p.X > column.Min.X && p.X < column.Max.X && p.Z > column.Min.Z && p.Z < column.Max.Z);
    }

    [Fact]
    public void APlayerFitsTheOpenPassageAndOnThePlannedLadder()
    {
        Assert.True(CellarGeometry.PassageMargins(0.4f, out float side, out float travel));
        Assert.Equal(0.38f, side, 4);
        Assert.Equal(0.85f, travel, 4);

        Assert.True(CellarGeometry.ClimberMargins(0.4f, out float front, out float rear, out float climberSide));
        Assert.True(HatchGeometry.ClimberMargins(0.4f, out float hatchFront, out float hatchRear, out float hatchSide));
        // The planned ladder hangs from the front panel exactly as the trapdoor's hangs from its front band.
        Assert.Equal(hatchFront, front, 4);
        Assert.Equal(hatchRear + ((CellarGeometry.ApertureRearZ - CellarGeometry.ApertureFrontZ) - (HatchGeometry.ClearRearZ - HatchGeometry.ClearFrontZ)), rear, 4);
        Assert.Equal(CellarGeometry.ClearHalfWidth - 0.4f, climberSide, 4);
        Assert.True(Math.Min(front, Math.Min(rear, climberSide)) > 0.2f);

        foreach (float bad in new[] { float.NaN, 0f, 2f, float.PositiveInfinity })
        {
            Assert.False(CellarGeometry.PassageMargins(bad, out float s, out float t));
            Assert.True(float.IsNegativeInfinity(s) && float.IsNegativeInfinity(t));
            Assert.False(CellarGeometry.ClimberMargins(bad, out _, out _, out _));
        }
    }

    [Fact]
    public void AnglesOutsideTheSwingClampAndNaNIsClosed()
    {
        for (int index = 0; index < 8; ++index)
        {
            Assert.Equal(CellarGeometry.LeafCorner(CellarLeaf.Right, 0f, index), CellarGeometry.LeafCorner(CellarLeaf.Right, float.NaN, index));
            Assert.Equal(CellarGeometry.LeafCorner(CellarLeaf.Right, 0f, index), CellarGeometry.LeafCorner(CellarLeaf.Right, -30f, index));
            Assert.Equal(CellarGeometry.LeafCorner(CellarLeaf.Right, 90f, index), CellarGeometry.LeafCorner(CellarLeaf.Right, float.PositiveInfinity, index));
        }
    }

    [Fact]
    public void ThePrismContainmentTestHonoursItsSkin()
    {
        CellarPrism cheek = CellarGeometry.Frame.Single(p => p.Name == "cheek_right");
        float x = (cheek.MinX + cheek.MaxX) / 2f;
        Assert.True(cheek.ContainsStrictly(new ClimbPoint(x, 0.2f, 0f), 0.01f));
        // On a face is not inside; just outside the slope is not inside.
        Assert.False(cheek.ContainsStrictly(new ClimbPoint(cheek.MinX, 0.2f, 0f), 0f));
        Assert.False(cheek.ContainsStrictly(new ClimbPoint(x, CellarGeometry.TopAt(0f) + 0.001f, 0f), 0f));
        Assert.False(cheek.ContainsStrictly(new ClimbPoint(x, CellarGeometry.TopAt(0f) - 0.005f, 0f), 0.01f));
        Assert.True(cheek.ContainsStrictly(new ClimbPoint(x, CellarGeometry.TopAt(0f) - 0.05f, 0f), 0.01f));
    }

    /// <summary>A 5 x 5 x 5 lattice through a leaf's box (faces included) at an angle, in piece space.</summary>
    private static IEnumerable<ClimbPoint> LeafSamples(CellarLeaf leaf, float angle)
    {
        ClimbPoint hinge = CellarGeometry.HingeInSlope(leaf);
        ClimbPoint centre = CellarGeometry.LeafCentreInHinge(leaf);
        ClimbPoint size = CellarGeometry.LeafSize;
        double phi = CellarGeometry.HingeRotationSign(leaf) * angle * Math.PI / 180.0;
        for (int i = 0; i <= 4; ++i)
        for (int j = 0; j <= 4; ++j)
        for (int k = 0; k <= 4; ++k)
        {
            float x = centre.X + (i / 4f - 0.5f) * size.X;
            float y = centre.Y + (j / 4f - 0.5f) * size.Y;
            float z = centre.Z + (k / 4f - 0.5f) * size.Z;
            float rx = (float)(x * Math.Cos(phi) - y * Math.Sin(phi));
            float ry = (float)(x * Math.Sin(phi) + y * Math.Cos(phi));
            yield return CellarGeometry.ToPiece(hinge.X + rx, hinge.Y + ry, hinge.Z + z);
        }
    }
}
