using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using TheConcernedCat.Workers;
using static ConcernedTeamster.Tests.NavigationFixtures;

namespace ConcernedTeamster.Tests;

/// <summary>#314 CART-05: whether a loaded cart fits a route, judged game-free
/// over synthetic terrain and obstacles (docs/mods/concerned-teamster/CART_ROUTES.md).
/// </summary>
public class HaulingNavigationRouteTests
{
    private static readonly HaulLimits Limits = HaulLimits.Default;

    private static float HalfCorridor => (VanillaCart.WidthMetres / 2f) + Limits.SideClearanceMetres;

    [Fact]
    public void AFlatClearRouteIsSuitableWithTheVerifiedCorridorAndItsEndAsTheStop()
    {
        var (planner, _, paths) = Planner();

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());
        Assert.Equal(P(0, 0), plan.Waypoints[0]);
        Assert.Equal(P(20, 0), plan.Waypoints[plan.Waypoints.Count - 1]);
        Assert.Equal(plan.Waypoints[plan.Waypoints.Count - 1], plan.StopPoint);
        Assert.Equal(0f, plan.SteepestGradeRatio);
        Assert.Equal(VanillaCart.WidthMetres + (2f * Limits.SideClearanceMetres), plan.NarrowestClearanceMetres, 3);
        Assert.Equal(20f, plan.LengthMetres, 3);
        Assert.Equal(7, plan.RequestRevision);
        Assert.Equal(1, paths.Calls);
        Assert.InRange(planner.LastAssessment.Costs.ClearanceProbes, 1, Limits.ClearanceProbesPerPlan);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void AGradeSteeperThanTheLimitIsRefusedUphillAndDownhill(float direction)
    {
        var (planner, world, _) = Planner();
        float grade = Limits.MaxGradeRatio * 1.5f;
        world.Height = (x, z) => direction * Math.Clamp(x - 6f, 0f, 8f) * grade;

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.Equal(CartRouteVerdict.TooSteep, plan.Verdict);
        Assert.Equal(CartRouteFinding.RunningGradeTooSteep, planner.LastAssessment!.Finding);
        Assert.Empty(plan.Waypoints);
    }

    [Fact]
    public void AGradeWithinTheLimitIsMeasuredAndAllowed()
    {
        var (planner, world, _) = Planner();
        float grade = Limits.MaxGradeRatio * 0.8f;
        world.Height = (x, z) => Math.Clamp(x - 6f, 0f, 8f) * grade;

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(24, 0)), 0f);

        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());
        Assert.InRange(plan.SteepestGradeRatio, grade * 0.9f, Limits.MaxGradeRatio);
    }

    [Fact]
    public void GroundTiltedAcrossTheCartBeyondTheLimitIsTooSteep()
    {
        var (planner, world, _) = Planner();
        world.Height = (x, z) => z * Limits.MaxGradeRatio * 2f;

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(15, 0)), 0f);

        Assert.Equal(CartRouteVerdict.TooSteep, plan.Verdict);
        Assert.Equal(CartRouteFinding.CrossSlopeTooSteep, planner.LastAssessment!.Finding);
    }

    [Fact]
    public void APassageNarrowerThanTheCartAndItsClearanceIsTooNarrowButAWideOneIsNot()
    {
        float narrow = (HalfCorridor * 2f) - 0.7f;
        var (planner, world, _) = Planner();
        world.Obstacles.Add(FakeObstacle.Box("north wall", 9.5f, 10.5f, narrow / 2f, 6f));
        world.Obstacles.Add(FakeObstacle.Box("south wall", 9.5f, 10.5f, -6f, -narrow / 2f));

        CartRoutePlan refused = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.Equal(CartRouteVerdict.TooNarrow, refused.Verdict);
        Assert.Equal(CartRouteFinding.MeasuredTooNarrow, planner.LastAssessment!.Finding);
        Assert.Contains("wall", planner.LastAssessment.Obstacle);

        float wide = (HalfCorridor * 2f) + 0.4f;
        var (widePlanner, wideWorld, _) = Planner();
        wideWorld.Obstacles.Add(FakeObstacle.Box("north wall", 9.5f, 10.5f, wide / 2f, 6f));
        wideWorld.Obstacles.Add(FakeObstacle.Box("south wall", 9.5f, 10.5f, -6f, -wide / 2f));

        Assert.True(widePlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).IsSuitable, widePlanner.LastAssessment!.Describe());
    }

    [Fact]
    public void AWallAcrossTheRouteIsTooNarrowNotAWayAround()
    {
        var (planner, world, _) = Planner();
        world.Obstacles.Add(FakeObstacle.Box("fence", 9.8f, 10.2f, -20f, 20f));

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.Equal(CartRouteVerdict.TooNarrow, plan.Verdict);
    }

    [Fact]
    public void TheTrailingCartIsPredictedToCutInsideATurnByAboutHalfItsHitch()
    {
        var corner = new[] { P(0, 0), P(12, 0), P(12, 12) };

        Assert.InRange(DeepestInsideCut(corner, VanillaCart.HitchLengthMetres), 0.9f, 1.1f);
        Assert.Equal(0f, DeepestInsideCut(corner, 0f), 3);
    }

    [Fact]
    public void ACornerTheWalkersCorridorClearsButTheTrailingCartWouldClipIsSwungWide()
    {
        var (planner, world, paths) = Planner();
        var corner = new List<WorkPoint> { P(0, 0), P(12, 0), P(12, 12) };
        paths.Corners = corner;
        var pillar = FakeObstacle.Pillar("post", 10f, 2f, 0.3f);
        world.Obstacles.Add(pillar);

        // A body the corridor's width walking the navmesh line itself would pass
        // the post: only the cart cutting the corner reaches it.
        Assert.True(DistanceToLine(P(pillar.X, pillar.Z), corner) - pillar.Radius > HalfCorridor);

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(12, 12)), 0f);

        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());
        Assert.True(planner.LastAssessment.Costs.Repairs >= 1);
        Assert.Contains(plan.Waypoints, point => point.X > 12.05f || point.Z < -0.05f);
        Assert.Equal(P(0, 0), plan.Waypoints[0]);
        Assert.Equal(P(12, 12), plan.Waypoints[plan.Waypoints.Count - 1]);
    }

    [Fact]
    public void ACornerWithNoRoomToSwingWideIsRefusedAsTooNarrow()
    {
        var (planner, world, paths) = Planner();
        paths.Corners = new List<WorkPoint> { P(0, 0), P(12, 0), P(12, 12) };
        world.Obstacles.Add(FakeObstacle.Pillar("post", 10f, 2f, 0.3f));
        world.Obstacles.Add(FakeObstacle.Box("outer wall south", -4f, 16f, -3f, -HalfCorridor - 0.02f));
        world.Obstacles.Add(FakeObstacle.Box("outer wall east", 12f + HalfCorridor + 0.02f, 16f, -3f, 16f));

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(12, 12)), 0f);

        Assert.Equal(CartRouteVerdict.TooNarrow, plan.Verdict);
    }

    [Fact]
    public void WaterAndLavaOnTheRouteAreRefused()
    {
        var (planner, world, _) = Planner();
        world.Water.Add((new FakeArea(8f, 12f, -5f, 5f), 0.3f));
        Assert.Equal(CartRouteVerdict.Water, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.Water, planner.LastAssessment!.Finding);

        var (lavaPlanner, lavaWorld, _) = Planner();
        lavaWorld.Lava.Add(new FakeArea(8f, 12f, -5f, 5f));
        Assert.Equal(CartRouteVerdict.Water, lavaPlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.Lava, lavaPlanner.LastAssessment!.Finding);
    }

    [Fact]
    public void NothingSolidUnderTheRouteIsAnUnsupportedGap()
    {
        var (planner, world, _) = Planner();
        world.Holes.Add(new FakeArea(9f, 11f, -5f, 5f));

        Assert.Equal(CartRouteVerdict.UnsupportedGap, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.NoSurface, planner.LastAssessment!.Finding);
    }

    [Fact]
    public void ABridgeWideEnoughForAWalkerButNotForTheWheelsIsRefused()
    {
        var (planner, world, _) = Planner();
        world.Holes.Add(new FakeArea(8f, 12f, 0.5f, 10f));
        world.Holes.Add(new FakeArea(8f, 12f, -10f, -0.5f));

        Assert.Equal(CartRouteVerdict.UnsupportedGap, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.WheelUnsupported, planner.LastAssessment!.Finding);
    }

    [Fact]
    public void AGroundThatDropsAwayIsAGapAndOneThatRisesAbruptlyIsTooSteep()
    {
        var (planner, world, _) = Planner();
        world.Height = (x, z) => x < 10f ? 0f : -1.5f;
        Assert.Equal(CartRouteVerdict.UnsupportedGap, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.Drop, planner.LastAssessment!.Finding);

        var (ledgePlanner, ledgeWorld, _) = Planner();
        ledgeWorld.Height = (x, z) => x < 10f ? 0f : 1.5f;
        Assert.Equal(CartRouteVerdict.TooSteep, ledgePlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.LedgeUp, ledgePlanner.LastAssessment!.Finding);
    }

    [Fact]
    public void AnyDoorwayOnTheRouteIsForbiddenButOneBesideItIsNot()
    {
        var (planner, world, _) = Planner();
        world.Doorways.Add(new CartDoorway(P(10, 0), 1f, 0f, 1f));

        Assert.Equal(CartRouteVerdict.ForbiddenDoor, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.Doorway, planner.LastAssessment!.Finding);

        var (asidePlanner, asideWorld, _) = Planner();
        asideWorld.Doorways.Add(new CartDoorway(P(10, 9), 1f, 0f, 1f));
        Assert.True(asidePlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).IsSuitable);
    }

    [Fact]
    public void AnUnsafeEndStopsTheCartAtTheLastLevelPlaceBeforeIt()
    {
        var (planner, world, _) = Planner();
        world.Height = (x, z) => x < 12f ? 0f : -(x - 12f) * 0.06f;

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());
        Assert.NotNull(plan.StopPoint);
        Assert.InRange(plan.StopPoint!.Value.X, 12f, 16f);
        Assert.True(planner.LastAssessment.StopShortfallMetres > 3f);

        SteeringGoal? last = null;
        foreach (WorkPoint waypoint in plan.Waypoints)
        {
            CartSteering steering = planner.Follow(plan, waypoint, P(waypoint.X - 2.21f, waypoint.Z));
            if (steering.Goal.HasValue)
            {
                last = steering.Goal;
            }
        }

        Assert.True(last.HasValue);
        Assert.True(last!.Value.IsFinalStop);
        Assert.Equal(plan.StopPoint.Value, last.Value.Target);
    }

    [Fact]
    public void ARouteWithNoLevelPlaceToStopIsAnUnsafeStop()
    {
        var (planner, world, _) = Planner();
        world.Height = (x, z) => x * 0.06f;

        Assert.Equal(CartRouteVerdict.UnsafeStop, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.NoLevelStop, planner.LastAssessment!.Finding);
    }

    [Fact]
    public void UnloadedGroundAnywhereOnTheRouteIsOutsideTheLoadedArea()
    {
        var (planner, world, _) = Planner();
        world.Unloaded.Add(new FakeArea(9f, 11f, -30f, 30f));
        Assert.Equal(CartRouteVerdict.OutsideLoadedArea, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);

        var (endPlanner, endWorld, endPaths) = Planner();
        endWorld.Unloaded.Add(new FakeArea(18f, 22f, -2f, 2f));
        Assert.Equal(CartRouteVerdict.OutsideLoadedArea, endPlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(0, endPaths.Calls);
    }

    [Fact]
    public void ALegLongerThanOnePlannedLegIsRefusedWithoutANavmeshQuery()
    {
        var (planner, _, paths) = Planner();

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(Limits.MaxLegMetres + 1f, 0)), 0f);

        Assert.Equal(CartRouteVerdict.NoPath, plan.Verdict);
        Assert.Equal(CartRouteFinding.LegTooLong, planner.LastAssessment!.Finding);
        Assert.Equal(0, paths.Calls);
    }

    [Fact]
    public void TooFewClearanceProbesForTheRouteIsBudgetExhausted()
    {
        var limits = new HaulLimits { ClearanceProbesPerPlan = 3 };
        var (planner, world, _) = Planner(limits);

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.Equal(CartRouteVerdict.BudgetExhausted, plan.Verdict);
        Assert.Equal(CartRouteFinding.ProbeBudget, planner.LastAssessment!.Finding);
        Assert.Equal(3, world.Sweeps);
    }

    [Fact]
    public void NavmeshQueriesAreLimitedPerMinuteAcrossEveryPlan()
    {
        var limits = new HaulLimits { PathQueriesPerMinute = 2 };
        var (planner, _, paths) = Planner(limits);

        Assert.True(planner.Plan(Request(P(0, 0), P(10, 0)), 0f).IsSuitable);
        Assert.True(planner.Plan(Request(P(0, 0), P(12, 0)), 1f).IsSuitable);
        Assert.Equal(CartRouteVerdict.BudgetExhausted, planner.Plan(Request(P(0, 0), P(14, 0)), 2f).Verdict);
        Assert.Equal(CartRouteFinding.QueryBudget, planner.LastAssessment!.Finding);
        Assert.Equal(2, paths.Calls);
        Assert.Equal(60f, planner.Budget.NextAvailableAt(2f), 3);

        Assert.True(planner.Plan(Request(P(0, 0), P(14, 0)), 60f).IsSuitable);
        Assert.Equal(3, paths.Calls);
    }

    [Fact]
    public void TheQueryBudgetSlidesAndSurvivesAClockThatRunsBackwards()
    {
        var budget = new CartQueryBudget(3);
        Assert.True(budget.TryTake(0f));
        Assert.True(budget.TryTake(10f));
        Assert.True(budget.TryTake(20f));
        Assert.False(budget.TryTake(59.9f));
        Assert.Equal(60f, budget.NextAvailableAt(59.9f), 3);
        Assert.True(budget.TryTake(60f));
        Assert.Equal(0, budget.RemainingAt(60f));
        Assert.Equal(1, budget.RemainingAt(70f));

        Assert.True(budget.TryTake(5f));
        Assert.Equal(1, budget.UsedAt(5f));
        Assert.False(budget.TryTake(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CartQueryBudget(0));
    }

    [Fact]
    public void TheNavmeshMovingTheEndsIsBridgedByVerifiedStraightStretches()
    {
        var (planner, _, paths) = Planner();
        paths.Corners = new List<WorkPoint> { P(2, 1), P(18, 1) };

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(20, 0)), 0f);

        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());
        Assert.Equal(P(0, 0), plan.Waypoints[0]);
        Assert.Equal(P(20, 0), plan.Waypoints[plan.Waypoints.Count - 1]);
    }

    [Fact]
    public void NoPathAndABrokenNavmeshAreRefusedAsNoPath()
    {
        var (planner, _, paths) = Planner();
        paths.Status = CartPathStatus.NotFound;
        Assert.Equal(CartRouteVerdict.NoPath, planner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.PathNotFound, planner.LastAssessment!.Finding);

        paths.Status = CartPathStatus.Found;
        paths.Throw = true;
        Assert.Equal(CartRouteVerdict.NoPath, planner.Plan(Request(P(0, 0), P(20, 0)), 1f).Verdict);
        Assert.Equal(CartRouteFinding.PathSourceUnavailable, planner.LastAssessment!.Finding);
    }

    [Fact]
    public void AProbeThatThrowsBecomesARefusalNeverAnEscape()
    {
        var (groundPlanner, groundWorld, _) = Planner();
        groundWorld.ThrowOnGround = true;
        Assert.Equal(CartRouteVerdict.NoPath, groundPlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.ProbeFaulted, groundPlanner.LastAssessment!.Finding);

        var (sweepPlanner, sweepWorld, _) = Planner();
        sweepWorld.ThrowOnSweep = true;
        Assert.Equal(CartRouteVerdict.NoPath, sweepPlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.ProbeFaulted, sweepPlanner.LastAssessment!.Finding);

        var (doorPlanner, doorWorld, _) = Planner();
        doorWorld.DoorsUnreadable = true;
        Assert.Equal(CartRouteVerdict.NoPath, doorPlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.DoorScanUnreadable, doorPlanner.LastAssessment!.Finding);

        var (readPlanner, readWorld, _) = Planner();
        readWorld.Unreadable.Add(new FakeArea(9f, 11f, -5f, 5f));
        Assert.Equal(CartRouteVerdict.NoPath, readPlanner.Plan(Request(P(0, 0), P(20, 0)), 0f).Verdict);
        Assert.Equal(CartRouteFinding.GroundUnreadable, readPlanner.LastAssessment!.Finding);
    }

    [Fact]
    public void AnInvalidRequestIsRefusedWithoutAQuery()
    {
        var (planner, _, paths) = Planner();
        var request = new CartRouteRequest(new WorkPoint(float.NaN, 0f, 0f), P(10, 0), VanillaCart, 70f, 1);

        Assert.Equal(CartRouteVerdict.NoPath, planner.Plan(request, 0f).Verdict);
        Assert.Equal(CartRouteVerdict.NoPath, planner.Plan(new CartRouteRequest(P(0, 0), P(10, 0), default, 70f, 1), 0f).Verdict);
        Assert.Equal(0, paths.Calls);
    }

    [Fact]
    public void CalibratedDataRefusesALoadItSaysStallsOrRunsAwayButNotALighterOne()
    {
        const string climbs = "data-version: 1\nrow: 5 | 50 | Stalls | Measured | test stall\n";
        const string descents = "data-version: 1\nrow: 5 | 50 | 0 | Runaway | Measured | test runaway\n";
        var world = new FakeCartWorld();
        world.Height = (x, z) => Math.Clamp(x - 6f, 0f, 8f) * 0.08f;
        var planner = new CartRoutePlanner(
            Limits, new FakePathSource(), world, null,
            new LoadModel(LoadCalibrationData.Parse(climbs)), new RiskModel(DescentCalibrationData.Parse(descents)));

        Assert.Equal(CartRouteVerdict.TooSteep, planner.Plan(Request(P(0, 0), P(24, 0), mass: 60f), 0f).Verdict);
        Assert.Equal(CartRouteFinding.CalibratedClimbRefused, planner.LastAssessment!.Finding);
        Assert.True(planner.Plan(Request(P(0, 0), P(24, 0), mass: 40f), 1f).IsSuitable);

        world.Height = (x, z) => -Math.Clamp(x - 6f, 0f, 8f) * 0.08f;
        Assert.Equal(CartRouteVerdict.TooSteep, planner.Plan(Request(P(0, 0), P(24, 0), mass: 60f), 2f).Verdict);
        Assert.Equal(CartRouteFinding.CalibratedDescentRefused, planner.LastAssessment!.Finding);
    }

    [Fact]
    public void EveryFindingDecidesExactlyOneRealVerdictAndEveryVerdictHasAFinding()
    {
        var covered = new HashSet<CartRouteVerdict>();
        foreach (CartRouteFinding finding in CartRouteFindings.All())
        {
            CartRouteVerdict verdict = CartRouteFindings.VerdictOf(finding);
            Assert.NotEqual(CartRouteVerdict.Unspecified, verdict);
            Assert.NotEqual("unknown finding", CartRouteFindings.Describe(finding));
            covered.Add(verdict);
        }

        foreach (CartRouteVerdict verdict in Enum.GetValues<CartRouteVerdict>())
        {
            if (verdict != CartRouteVerdict.Unspecified)
            {
                Assert.Contains(verdict, covered);
            }
        }
    }

    [Fact]
    public void EveryRefusalButASpentBudgetStopsAHaulWithAnAttentionReason()
    {
        foreach (CartRouteVerdict verdict in Enum.GetValues<CartRouteVerdict>())
        {
            bool mapped = CartRouteFindings.TryGetAttention(verdict, out HaulAttentionReason reason);
            if (verdict == CartRouteVerdict.Unspecified || verdict == CartRouteVerdict.Suitable ||
                verdict == CartRouteVerdict.BudgetExhausted)
            {
                Assert.False(mapped);
            }
            else
            {
                Assert.True(mapped, verdict.ToString());
                Assert.NotEqual(HaulAttentionReason.Unspecified, reason);
            }
        }
    }

    private static float DeepestInsideCut(IReadOnlyList<WorkPoint> corner, float hitch)
    {
        var dense = new List<WorkPoint>();
        for (int leg = 0; leg + 1 < corner.Count; leg++)
        {
            for (int step = 0; step < 120; step++)
            {
                dense.Add(CartRouteGeometry_Lerp(corner[leg], corner[leg + 1], step / 120f));
            }
        }

        dense.Add(corner[corner.Count - 1]);
        CartPose pose = CartTrackPredictor.Start(dense, hitch, null);
        float deepest = 0f;
        for (int index = 1; index < dense.Count; index++)
        {
            pose = CartTrackPredictor.Advance(pose, dense[index], hitch);
            float inside = Math.Min(pose.Axle.Z, corner[1].X - pose.Axle.X);
            deepest = Math.Max(deepest, inside);
        }

        return deepest;
    }

    private static WorkPoint CartRouteGeometry_Lerp(WorkPoint a, WorkPoint b, float t) =>
        new WorkPoint(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));

    private static float DistanceToLine(WorkPoint point, IReadOnlyList<WorkPoint> line)
    {
        float best = float.MaxValue;
        for (int leg = 0; leg + 1 < line.Count; leg++)
        {
            float abx = line[leg + 1].X - line[leg].X;
            float abz = line[leg + 1].Z - line[leg].Z;
            float t = Math.Clamp(
                (((point.X - line[leg].X) * abx) + ((point.Z - line[leg].Z) * abz)) / ((abx * abx) + (abz * abz)), 0f, 1f);
            float dx = point.X - (line[leg].X + (abx * t));
            float dz = point.Z - (line[leg].Z + (abz * t));
            best = Math.Min(best, MathF.Sqrt((dx * dx) + (dz * dz)));
        }

        return best;
    }
}
