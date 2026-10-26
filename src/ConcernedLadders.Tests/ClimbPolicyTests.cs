using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

/// <summary>Owner revision of 25 October 2026: each test here fails against the 0.1.1 policy it replaces
/// (ground-only mounts, a single front lane, a fixed 1.8 m/s speed, no stamina, skill or slide).</summary>
public sealed class ClimbPolicyTests
{
    private const float Back = 0.08f, Front = 0.24f, Plane = 0.16f, FirstRung = 1f / 6f, Radius = 0.4f;

    private static LadderGeometry Run(float bottom = 0f, float height = 2f)
    {
        Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(0f, bottom, 0f), new ClimbPoint(0f, bottom + height, 0f),
            ClimbHeading.FromXz(0f, 1f), 0.8f, 1f / 3f, out LadderGeometry geometry));
        return geometry;
    }

    [Fact]
    public void RearLaneIsMeasuredFromTheRearColliderSurfaceNotNegatedFromTheFront()
    {
        float front = ClimbPolicy.LaneDepth(LadderFace.Front, Radius, Back, Front);
        float rear = ClimbPolicy.LaneDepth(LadderFace.Rear, Radius, Back, Front);
        Assert.Equal(Front + ClimbPolicy.LaneGap, front - Radius, 5);
        Assert.Equal(Back - ClimbPolicy.LaneGap, rear + Radius, 5);
        // The 0.1.1 runtime aimed every approach at the front line, through the ladder's own box.
        Assert.NotEqual(-front, rear, 3);
        Assert.True(rear + Radius < Back && front - Radius > Front);
    }

    [Theory]
    [InlineData(0.7f, 1)]
    [InlineData(0.17f, 1)]
    [InlineData(0.15f, -1)]
    [InlineData(-0.38f, -1)]
    [InlineData(-1.2f, -1)]
    public void ApproachSelectsTheFaceOnItsSideOfTheCollider(float depth, int expected) =>
        Assert.Equal((LadderFace)expected, ClimbPolicy.FaceOf(depth, Back, Front));

    [Theory]
    [InlineData(0.04f, 0.24f)]
    [InlineData(0.4f, 0.3f)]
    public void ImpossibleFacesOrCapsulesRefuse(float radius, float back)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => ClimbPolicy.LaneDepth(LadderFace.Rear, radius, back, 0.2f));
    }

    [Theory]
    [InlineData(1.0f, true)]
    [InlineData(1.5f, true)]
    [InlineData(1.52f, false)]
    [InlineData(-1.0f, true)]
    [InlineData(-1.52f, false)]
    public void ReachIsChestToTheRungSpanOnEitherFace(float fromPlane, bool inReach)
    {
        var chest = new ClimbPoint(0f, 1.3f, Plane + fromPlane);
        float reach = ClimbPolicy.ChestReach(Run(), chest, Plane, FirstRung);
        Assert.Equal(Math.Abs(fromPlane), reach, 3);
        Assert.Equal(inReach, ClimbPolicy.InReach(reach));
    }

    [Fact]
    public void ReachCountsHeightAboveTheTopRungAndSidewaysBeyondTheRung()
    {
        // Chest 1 m above the rails' top ends and 0.5 m out: sqrt(1 + 0.25).
        float above = ClimbPolicy.ChestReach(Run(), new ClimbPoint(0f, 2f + 1f, Plane + 0.5f), Plane, FirstRung);
        Assert.Equal((float)Math.Sqrt(1.25), above, 3);
        float side = ClimbPolicy.ChestReach(Run(), new ClimbPoint(ClimbPolicy.RungHalfWidth + 0.6f, 1f, Plane + 0.8f), Plane, FirstRung);
        Assert.Equal(1f, side, 3);
        Assert.False(ClimbPolicy.InReach(float.NaN));
        Assert.Equal(float.PositiveInfinity, ClimbPolicy.ChestReach(Run(), new ClimbPoint(float.NaN, 1f, 1f), Plane, FirstRung));
    }

    [Fact]
    public void AirborneStateIsNotAnAdmissionGateButOwnershipOverlayAndCooldownAre()
    {
        // 0.1.1 refused any unsupported or falling player. Admission now has no support input at all.
        Assert.True(ClimbPolicy.MayAdmit(true, true, false, 0f));
        Assert.False(ClimbPolicy.MayAdmit(false, true, false, 0f));
        Assert.False(ClimbPolicy.MayAdmit(true, false, false, 0f));
        Assert.False(ClimbPolicy.MayAdmit(true, true, true, 0f));
        Assert.False(ClimbPolicy.MayAdmit(true, true, false, 0.01f));
        Assert.False(ClimbPolicy.MayAdmit(true, true, false, float.NaN));
    }

    [Theory]
    [InlineData(0.6f, 0.6f)]   // Falling past mid-ladder: held where the body is, no hoist.
    [InlineData(-0.9f, -0.9f)] // Below the foot but the hands reach the lowest rung: hanging.
    [InlineData(2.1f, 2f)]     // Rising just past the top: settles onto the top.
    public void ACatchHoldsWhereTheBodyAlreadyIs(float feet, float expected)
    {
        float lowest = ClimbPolicy.LowestHang(0f, FirstRung, 1.57f);
        Assert.Equal(expected, ClimbPolicy.GrabHeight(feet, lowest, 2f), 4);
    }

    [Theory]
    [InlineData(1.85f, 0.45f, true)]
    [InlineData(1.85f, 0.7f, false)]
    [InlineData(1.8f, 0.6f, true)]
    public void AnUpperFloorRemountReachesTheRailTops(float capsule, float behindPlane, bool expected)
    {
        // Feet 2 cm above the run's top on the floor behind it: the 0.1.1-era span ended at the top rung and
        // refused every such remount on vertical distance alone (Codex review of da8c5a2).
        var chest = new ClimbPoint(0f, 2.02f + capsule * ClimbPolicy.ChestFraction, Plane - behindPlane);
        Assert.Equal(expected, ClimbPolicy.InReach(ClimbPolicy.ChestReach(Run(), chest, Plane, FirstRung)));
    }

    [Theory]
    [InlineData(0.10f, -0.41f, true)]   // Measured: floor 0.10 m above the top, body against the rails' back.
    [InlineData(0.10f, -1.30f, true)]
    [InlineData(0.10f, -1.70f, false)]
    [InlineData(0.80f, -0.41f, false)]  // Higher than a step above the top.
    [InlineData(-0.40f, -0.41f, false)] // Below the top band: the chest measure applies instead.
    public void AStandingPlayerAtTheTopIsMeasuredByTheStepOntoTheRails(float aboveTop, float depth, bool expected)
    {
        var feet = new ClimbPoint(0f, 2f + aboveTop, depth);
        Assert.Equal(expected, ClimbPolicy.InReach(ClimbPolicy.TopStepReach(Run(), feet, Plane)));
    }

    [Fact]
    public void OnlySupportedPlayersStepOnFromTheTop()
    {
        var feet = new ClimbPoint(0f, 2.10f, -0.41f);
        var chest = new ClimbPoint(0f, 2.10f + 1.85f * ClimbPolicy.ChestFraction, -0.41f);
        // The 0.2.0 dev build refused this measured upper-floor remount: chest reach is 1.56 m.
        Assert.False(ClimbPolicy.InReach(ClimbPolicy.ChestReach(Run(), chest, Plane, FirstRung)));
        Assert.True(ClimbPolicy.InReach(ClimbPolicy.MountReach(Run(), feet, chest, Plane, FirstRung, true)));
        Assert.False(ClimbPolicy.InReach(ClimbPolicy.MountReach(Run(), feet, chest, Plane, FirstRung, false)));
    }

    [Fact]
    public void LowHangUsesTheRealGripHeightInsteadOfTheModuleFoot()
    {
        Assert.Equal(FirstRung - 1.57f, ClimbPolicy.LowestHang(0f, FirstRung, 1.57f), 4);
        Assert.True(ClimbPolicy.LowestHang(0f, FirstRung, 1.57f) < 0f);
    }

    [Theory]
    [InlineData(0.9f, 0.2f, true)]
    [InlineData(1.0f, -0.25f, true)]
    [InlineData(1.05f, 0f, false)]
    [InlineData(0.5f, 0.3f, false)]
    [InlineData(0.5f, -0.4f, false)]
    [InlineData(float.NaN, 0f, false)]
    public void AirAlignmentIsBoundedSeparately(float horizontal, float vertical, bool allowed) =>
        Assert.Equal(allowed, ClimbPolicy.AirAlignmentAllowed(horizontal, vertical));

    [Theory]
    [InlineData(1f, false, 0f, 1)]
    [InlineData(1f, true, 0f, 2)]
    [InlineData(-1f, true, 19.99f, 2)]
    [InlineData(-1f, true, 20f, 3)]
    [InlineData(-1f, true, 57f, 3)]
    [InlineData(1f, true, 57f, 2)]
    [InlineData(0f, true, 57f, 0)]
    [InlineData(-0.4f, false, 57f, 1)]
    public void RunAndTheCurrentLearnedLevelChooseTheGait(float input, bool run, float level, int expected) =>
        Assert.Equal((ClimbGait)expected, ClimbPolicy.SelectGait(input, run, level, 100f, 0.02f));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ExhaustionFallsBackToANormalClimbNeverAFall(int gait)
    {
        var wanted = (ClimbGait)gait;
        float level = wanted == ClimbGait.Slide ? 20f : 0f;
        float input = wanted == ClimbGait.Slide ? -1f : 1f;
        Assert.Equal(wanted, ClimbPolicy.SelectGait(input, true, level, 1f, 0.02f));
        Assert.Equal(ClimbGait.Normal, ClimbPolicy.SelectGait(input, true, level, 0f, 0.02f));
        Assert.Equal(ClimbGait.Normal, ClimbPolicy.SelectGait(input, true, level, float.NaN, 0.02f));
    }

    [Fact]
    public void BudgetUsesTheEffectiveCostOfTheStepActuallyIssued()
    {
        float fast = ClimbPolicy.StepCost(ClimbGait.Fast, 1f, 0f, 0.02f);
        Assert.Equal(0.12f, fast, 4);
        // A doubled native stamina rate makes 0.15 too little for a 0.24 step (Codex review of da8c5a2).
        Assert.Equal(ClimbGait.Fast, ClimbPolicy.SelectGait(1f, true, 0f, 0.15f, 0.02f, 1f));
        Assert.Equal(ClimbGait.Normal, ClimbPolicy.SelectGait(1f, true, 0f, 0.15f, 0.02f, 2f));
        // A slide runs at full speed whatever the analog input, so it is budgeted at full speed too.
        float slide = ClimbPolicy.StepCost(ClimbGait.Slide, -0.2f, 20f, 0.02f);
        Assert.Equal(ClimbPolicy.StaminaPerMetre(ClimbGait.Slide, 20f) * ClimbPolicy.SlideSpeed * 0.02f, slide, 5);
        Assert.Equal(ClimbGait.Normal, ClimbPolicy.SelectGait(-0.2f, true, 20f, slide * 0.9f, 0.02f));
        Assert.Equal(ClimbGait.Normal, ClimbPolicy.SelectGait(1f, true, 0f, 100f, 0.02f, float.NaN));
    }

    [Fact]
    public void SpeedsAreTheOwnerDefaultsAndTheOld18ClampIsGone()
    {
        Assert.Equal(1.8f, ClimbPolicy.StepSpeed(ClimbGait.Normal, 1f), 4);
        Assert.Equal(2.43f, ClimbPolicy.StepSpeed(ClimbGait.Fast, 1f), 4);
        Assert.Equal(-2.43f, ClimbPolicy.StepSpeed(ClimbGait.Fast, -1f), 4);
        Assert.Equal(-4.5f, ClimbPolicy.StepSpeed(ClimbGait.Slide, -0.3f), 4);
        Assert.Equal(0.9f, ClimbPolicy.StepSpeed(ClimbGait.Normal, 0.5f), 4);
        Assert.Equal(0f, ClimbPolicy.StepSpeed(ClimbGait.Hold, 1f));
        // A fast frame really moves 2.43 m/s worth: Advance no longer caps at 1.8.
        Assert.Equal(1f + 2.43f * 0.02f, LadderRules.Advance(1f, 2.43f, 0.02f, 0f, 10f), 4);
        Assert.Equal(1f - 4.5f * 0.02f, LadderRules.Advance(1f, -4.5f, 0.02f, 0f, 10f), 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(50f, 0.125f)]
    [InlineData(100f, 0.25f)]
    [InlineData(250f, 0.25f)]
    [InlineData(-5f, 0f)]
    [InlineData(float.NaN, 0f)]
    public void StaminaSavingIsLinearAndBounded(float level, float saving) =>
        Assert.Equal(saving, ClimbPolicy.StaminaSaving(level), 4);

    [Fact]
    public void FastAndSlideCostTheirRateForEachSecondOfFullSpeedTravel()
    {
        Assert.Equal(6f, ClimbPolicy.StaminaPerMetre(ClimbGait.Fast, 0f) * ClimbPolicy.FastSpeed, 4);
        Assert.Equal(2f, ClimbPolicy.StaminaPerMetre(ClimbGait.Slide, 0f) * ClimbPolicy.SlideSpeed, 4);
        Assert.Equal(4.5f, ClimbPolicy.StaminaPerMetre(ClimbGait.Fast, 100f) * ClimbPolicy.FastSpeed, 4);
        Assert.Equal(0f, ClimbPolicy.StaminaPerMetre(ClimbGait.Normal, 0f));
        Assert.Equal(0f, ClimbPolicy.StaminaPerMetre(ClimbGait.Hold, 0f));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    [InlineData(0.2f)]
    public void AnalogInputCostsTheSameStaminaPerMetre(float input)
    {
        var ledger = new ClimbLedger();
        float height = 0f, total = 0f;
        while (height < 1f)
        {
            float step = ClimbPolicy.StepSpeed(ClimbGait.Fast, input) * 0.02f;
            ledger.Plan(ClimbGait.Fast, height, step);
            height += step;
            ledger.Settle(height, 0f, out float stamina, out _);
            total += stamina;
        }

        Assert.Equal(ClimbPolicy.StaminaPerMetre(ClimbGait.Fast, 0f) * height, total, 3);
    }

    [Fact]
    public void SettlementChargesOnlyObservedTravelBoundedByThePlan()
    {
        var ledger = new ClimbLedger();
        float perMetre = ClimbPolicy.StaminaPerMetre(ClimbGait.Fast, 0f);
        ledger.Plan(ClimbGait.Fast, 5f, 0.05f);
        ledger.Settle(5.02f, 0f, out float blocked, out _);
        Assert.Equal(perMetre * 0.02f, blocked, 5); // Obstructed halfway: half the step.
        ledger.Plan(ClimbGait.Fast, 5f, 0.05f);
        ledger.Settle(9f, 0f, out float knocked, out _);
        Assert.Equal(perMetre * 0.05f, knocked, 5); // A shove is not climbing distance.
        ledger.Settle(9.05f, 0f, out float twice, out _);
        Assert.Equal(0f, twice); // Settled once.
        ledger.Plan(ClimbGait.Fast, 5f, 0.05f);
        ledger.Discard();
        ledger.Settle(5.05f, 0f, out float discarded, out _);
        Assert.Equal(0f, discarded); // Ownership loss drops the pending step.
        ledger.Plan(ClimbGait.Hold, 5f, 0.05f);
        Assert.False(ledger.Pending);
    }

    [Fact]
    public void TravelInTheOtherDirectionSettlesNothing()
    {
        // Codex review of 4926903: an upward fast step from 5.000 left at 4.900 by something else charged a full step.
        var ledger = new ClimbLedger();
        ledger.Plan(ClimbGait.Fast, 5f, 0.0486f);
        ledger.Settle(4.9f, 0f, out float stamina, out int metres);
        Assert.Equal(0f, stamina);
        Assert.Equal(0, metres);
        Assert.Equal(0f, ledger.CarriedMetres);
        ledger.Plan(ClimbGait.Fast, 5f, -0.0486f);
        ledger.Settle(5.03f, 0f, out float up, out _);
        Assert.Equal(0f, up);
        ledger.Plan(ClimbGait.Fast, 5f, -0.0486f);
        ledger.Settle(4.97f, 0f, out float down, out _);
        Assert.Equal(ClimbPolicy.StaminaPerMetre(ClimbGait.Fast, 0f) * 0.03f, down, 5);
    }

    [Fact]
    public void SkillDistanceCarriesFractionsAndNeverCountsSlides()
    {
        var ledger = new ClimbLedger();
        int awarded = 0;
        float height = 0f;
        for (int step = 0; step < 50; step++)
        {
            ledger.Plan(ClimbGait.Normal, height, 0.05f);
            height += 0.05f;
            ledger.Settle(height, 0f, out _, out int metres);
            awarded += metres;
        }

        Assert.Equal(2, awarded);
        Assert.Equal(0.5f, ledger.CarriedMetres, 3);
        ledger.Plan(ClimbGait.Slide, height, -0.09f);
        ledger.Settle(height - 0.09f, 20f, out float slideStamina, out int slideMetres);
        Assert.Equal(0, slideMetres);
        Assert.True(slideStamina > 0f);
        Assert.Equal(0.5f, ledger.CarriedMetres, 3);
        ledger.Reset();
        Assert.Equal(0f, ledger.CarriedMetres);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1f)]       // Neutral: away from the rails.
    [InlineData(1f, 0f, 1f, 0f)]       // Toward a side platform.
    [InlineData(0.6f, 0.8f, 0.6f, 0.8f)]
    [InlineData(0f, -1f, 0f, 1f)]      // Straight into the rails is not a departure.
    [InlineData(0.05f, 0.05f, 0f, 1f)] // Below the input dead zone.
    public void JumpDepartsInTheCameraRelativeMovementDirection(float moveX, float moveZ, float x, float z)
    {
        ClimbPolicy.DepartureDirection(moveX, moveZ, 0f, 1f, out float dx, out float dz);
        Assert.Equal(x, dx, 3);
        Assert.Equal(z, dz, 3);
    }

    [Fact]
    public void RemountNeedsAtLeastFourTenthsOfASecond() => Assert.True(ClimbPolicy.RemountDelay >= 0.4f);
}
