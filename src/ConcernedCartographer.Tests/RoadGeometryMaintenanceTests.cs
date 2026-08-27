using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RoadGeometryMaintenanceTests
{
    private static readonly RoadSamplingRules DefaultRules = new(
        minimumSpacingMeters: 1.5f,
        maximumGapMeters: 8.0f,
        duplicateSuppressionMeters: 2.0f);

    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    private static RoadStroke Stroke(RoadKind kind, RoadObservationSource source, params (float X, float Z)[] points)
    {
        var stroke = new RoadStroke(Guid.NewGuid(), kind, source);
        foreach ((float x, float z) in points)
        {
            stroke.Points.Add(P(x, z));
        }

        return stroke;
    }

    [Fact]
    public void Simplify_CollapsesStraightLine_ToEndpoints()
    {
        var points = new List<RoadPoint>();
        for (float x = 0f; x <= 100f; x += 2f)
        {
            points.Add(P(x, 0f));
        }

        List<RoadPoint> simplified = RoadGeometry.Simplify(points, 1.0f);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(points[0], simplified[0]);
        Assert.Equal(points[^1], simplified[^1]);
    }

    [Fact]
    public void Simplify_KeepsEveryPointWithinTolerance()
    {
        // A sine-like curve: no original point may end up farther than the
        // tolerance from the simplified polyline.
        var points = new List<RoadPoint>();
        for (float x = 0f; x <= 60f; x += 1.5f)
        {
            points.Add(P(x, 5f * MathF.Sin(x / 6f)));
        }

        List<RoadPoint> simplified = RoadGeometry.Simplify(points, 1.0f);

        Assert.True(simplified.Count < points.Count);
        foreach (RoadPoint original in points)
        {
            float best = float.MaxValue;
            for (int index = 1; index < simplified.Count; index++)
            {
                best = Math.Min(best, RoadGeometry.HorizontalDistanceToSegment(
                    original, simplified[index - 1], simplified[index]));
            }

            Assert.True(best <= 1.0f + 0.001f, $"Point {original} deviates {best:0.###} m.");
        }
    }

    [Fact]
    public void Simplify_PreservesSharpCorners()
    {
        var points = new List<RoadPoint>();
        for (float x = 0f; x <= 20f; x += 2f)
        {
            points.Add(P(x, 0f));
        }

        for (float z = 2f; z <= 20f; z += 2f)
        {
            points.Add(P(20f, z));
        }

        List<RoadPoint> simplified = RoadGeometry.Simplify(points, 1.0f);

        Assert.Equal(3, simplified.Count);
        Assert.Equal(P(20f, 0f), simplified[1]);
    }

    [Fact]
    public void Maintenance_ReducesPointCount_OnTypicalTraversalData()
    {
        var atlas = new RoadAtlas();
        for (float x = 0f; x <= 200f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(x, 0f), DefaultRules, out _);
        }

        int before = atlas.PointCount;
        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.True(result.RemovedPoints >= before / 2, $"Only {result.RemovedPoints} of {before} removed.");
        Assert.True(atlas.IsDirty);
    }

    [Fact]
    public void Maintenance_JoinsFragmentsOfTheSameRoad()
    {
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 0f), (10f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (12f, 0f), (22f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (24f, 0f), (34f, 0f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.Equal(2, result.MergedStrokes);
        Assert.Single(atlas.Strokes);
        Assert.Equal(P(0f, 0f), atlas.Strokes[0].Points[0]);
        Assert.Equal(P(34f, 0f), atlas.Strokes[0].Points[atlas.Strokes[0].Points.Count - 1]);
    }

    [Fact]
    public void Maintenance_JoinsReversedFragments()
    {
        // The second fragment was recorded walking the other way; its END
        // sits near the first fragment's end and must be stitched reversed.
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 0f), (10f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (22f, 0f), (12f, 0f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.Equal(1, result.MergedStrokes);
        Assert.Single(atlas.Strokes);
        var points = atlas.Strokes[0].Points;
        Assert.Equal(P(0f, 0f), points[0]);
        Assert.Equal(P(22f, 0f), points[points.Count - 1]);
    }

    [Fact]
    public void Maintenance_NeverBridgesTeleportSizedGaps()
    {
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 0f), (10f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (500f, 500f), (510f, 500f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.Equal(0, result.MergedStrokes);
        Assert.Equal(2, atlas.Strokes.Count);
    }

    [Fact]
    public void Maintenance_NeverMergesParallelRoads()
    {
        // Two parallel roads 5 m apart: endpoints are farther than the join
        // tolerance, so they stay separate strokes.
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 0f), (20f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 5f), (20f, 5f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.Equal(0, result.MergedStrokes);
        Assert.Equal(2, atlas.Strokes.Count);
    }

    [Fact]
    public void Maintenance_NeverMergesAcrossKindOrSource()
    {
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 0f), (10f, 0f)),
            Stroke(RoadKind.Paved, RoadObservationSource.Traversal, (12f, 0f), (22f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.ChunkRecovery, (-12f, 0f), (-2f, 0f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.Equal(0, result.MergedStrokes);
        Assert.Equal(3, atlas.Strokes.Count);
    }

    [Fact]
    public void Maintenance_LeavesLoopsAlone()
    {
        // A closed loop road: its own endpoints are adjacent, but a stroke
        // never joins itself.
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal,
                (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f), (0f, 2f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        Assert.Equal(0, result.MergedStrokes);
        Assert.Single(atlas.Strokes);
        Assert.Equal(5, atlas.Strokes[0].Points.Count);
    }

    [Fact]
    public void Maintenance_KeepsFirstStrokeIdentity()
    {
        var first = Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, 0f), (10f, 0f));
        var second = Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (12f, 0f), (22f, 0f));
        var atlas = new RoadAtlas(new[] { first, second });

        atlas.PerformMaintenance();

        Assert.Equal(first.Id, atlas.Strokes[0].Id);
    }

    [Fact]
    public void SimplifiedStraightStretch_StillSuppressesReWalks()
    {
        // The heart of segment-based suppression: after a long straight
        // stroke collapses to its endpoints, walking its middle must still
        // be recognized as covered ground.
        var atlas = new RoadAtlas();
        for (float x = 0f; x <= 100f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(x, 0f), DefaultRules, out _);
        }

        atlas.PerformMaintenance();
        Assert.Equal(2, atlas.PointCount);
        int before = atlas.PointCount;

        for (float x = 30f; x <= 70f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(x, 0.5f), DefaultRules, out _);
        }

        Assert.Equal(before, atlas.PointCount);
    }

    [Fact]
    public void CrossingRoads_SurviveMaintenanceIndependently()
    {
        var atlas = new RoadAtlas(new[]
        {
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (-20f, 0f), (0f, 0f), (20f, 0f)),
            Stroke(RoadKind.Dirt, RoadObservationSource.Traversal, (0f, -20f), (0f, 0.5f), (0f, 20f)),
        });

        RoadAtlas.MaintenanceResult result = atlas.PerformMaintenance();

        // The crossing point is mid-polyline, not an endpoint, so the roads
        // stay separate strokes with their shapes intact.
        Assert.Equal(0, result.MergedStrokes);
        Assert.Equal(2, atlas.Strokes.Count);
    }
}
