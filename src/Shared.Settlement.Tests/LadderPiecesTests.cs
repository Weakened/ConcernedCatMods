using TheConcernedCat.ConcernedForeman.Domain.Ladders;
using TheConcernedCat.Ladders;

namespace Shared.Settlement.Tests;

/// <summary>CF-LAD-004: which pieces are climbable, what the `Ladders` settings
/// mean, and what pressing Use does.
///
/// Three questions, none of which needs Valheim to answer: a piece is admitted
/// from measurements the runtime reads off it, a setting is a number that has
/// to survive a person editing a text file, and the Use decision is a table.
/// The Valheim side of each is four lines that carry the answer out.</summary>
public sealed class LadderPiecesTests
{
    // A wooden step ladder as the brief describes one: about two metres tall,
    // a little over half a metre wide, thin.
    private const float LadderHeight = 2f;
    private const float LadderWidth = 0.6f;
    private const float LadderDepth = 0.2f;

    // --- which pieces -----------------------------------------------------

    [Theory]
    [InlineData("wood_stepladder")]
    [InlineData("wood_stepladder(Clone)")]
    [InlineData("WOOD_STEPLADDER")]
    [InlineData("Piece_grausten_stone_ladder")]
    [InlineData("Piece_grausten_stone_ladder(Clone)")]
    [InlineData("  wood_stepladder(Clone) ")]
    public void TheBuildablePiecesAreKnownByName(string name)
    {
        Assert.True(LadderAdmission.IsKnown(name));
        Assert.Equal(
            LadderVerdict.KnownPiece,
            LadderAdmission.Decide(name, buildable: true, LadderHeight, LadderWidth, LadderDepth));
    }

    [Fact]
    public void AKnownPieceIsAdmittedEvenWhenNothingCouldBeMeasured()
    {
        // The measurement is a second opinion, never a veto on the pieces the
        // feature exists for: a ladder whose art has not streamed in must still
        // be the ladder the player just built.
        LadderVerdict verdict = LadderAdmission.Decide(
            "wood_stepladder(Clone)", buildable: true, float.NaN, float.NaN, float.NaN);

        Assert.Equal(LadderVerdict.KnownPiece, verdict);
        Assert.True(verdict.Admits());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(Clone)")]
    [InlineData("some_mod_ladder_nobody_has_heard_of")]
    [InlineData("Karve(Clone)/ladder")]
    public void AnUnknownNameIsNotAnError(string? name)
    {
        Assert.False(LadderAdmission.IsKnown(name));

        // It falls through to the measurement, and a ladder-shaped thing is
        // still climbable.
        Assert.Equal(
            LadderVerdict.MeasuresLikeALadder,
            LadderAdmission.Decide(name, buildable: false, LadderHeight, LadderWidth, LadderDepth));
    }

    [Fact]
    public void APropThatMeasuresLikeALadderIsClimbable()
    {
        LadderVerdict verdict = LadderAdmission.Decide(
            "goblin_stepladder", buildable: false, LadderHeight, LadderWidth, LadderDepth);

        Assert.Equal(LadderVerdict.MeasuresLikeALadder, verdict);
        Assert.True(verdict.Admits());
    }

    [Fact]
    public void AShortPropIsAStepNotALadder()
    {
        LadderVerdict verdict = LadderAdmission.Decide(
            "wood_step", buildable: false, heightMetres: 0.4f, widestMetres: 0.6f, thinnestMetres: 0.2f);

        Assert.Equal(LadderVerdict.TooShort, verdict);
        Assert.False(verdict.Admits());
    }

    [Fact]
    public void ShortnessIsMeasuredAgainstTheDomainsOwnThreshold()
    {
        // One truth about how short is too short, so a piece the survey would
        // refuse to measure is never admitted here.
        Assert.Equal(LadderGeometry.MinimumClimbableHeight, LadderAdmission.MinimumHeightMetres);

        Assert.False(LadderAdmission
            .Decide("x", false, LadderGeometry.MinimumClimbableHeight - 0.01f, 0.6f, 0.2f)
            .Admits());
        Assert.True(LadderAdmission
            .Decide("x", false, LadderGeometry.MinimumClimbableHeight + 0.01f, 0.6f, 0.2f)
            .Admits());
    }

    [Fact]
    public void APropWiderThanAnyLadderIsRefused()
    {
        LadderVerdict verdict = LadderAdmission.Decide(
            "palisade_wall", buildable: false, heightMetres: 3f, widestMetres: 6f, thinnestMetres: 0.2f);

        Assert.Equal(LadderVerdict.TooWide, verdict);
    }

    [Fact]
    public void ABuildablePieceIsNotAskedToProveItIsThinAsWell()
    {
        // A hammer-placed piece that carries a Ladder is something a player put
        // there to go up. Refusing it because it is chunky would break the
        // feature's own promise on a modded ladder.
        Assert.Equal(
            LadderVerdict.MeasuresLikeALadder,
            LadderAdmission.Decide(
                "some_mod_ladder", buildable: true, heightMetres: 2f, widestMetres: 1.5f, thinnestMetres: 1.4f));
    }

    [Fact]
    public void APropDeepEnoughToBeFurnitureIsRefused()
    {
        LadderVerdict verdict = LadderAdmission.Decide(
            "shelf", buildable: false, heightMetres: 2f, widestMetres: 1.2f, thinnestMetres: 1.4f);

        Assert.Equal(LadderVerdict.TooDeep, verdict);
    }

    [Fact]
    public void APropLyingDownIsRefused()
    {
        LadderVerdict verdict = LadderAdmission.Decide(
            "fallen_ladder", buildable: false, heightMetres: 1f, widestMetres: 3f, thinnestMetres: 0.6f);

        Assert.Equal(LadderVerdict.LyingDown, verdict);
        Assert.False(verdict.Admits());
    }

    [Theory]
    [InlineData(float.NaN, 0.6f, 0.2f)]
    [InlineData(2f, float.NaN, 0.2f)]
    [InlineData(2f, 0.6f, float.PositiveInfinity)]
    [InlineData(0f, 0.6f, 0.2f)]
    [InlineData(2f, 0f, 0.2f)]
    public void NumbersThatAreNotAMeasurementAreNotARefusalEither(
        float height, float widest, float thinnest)
    {
        LadderVerdict verdict = LadderAdmission.Decide("mystery", false, height, widest, thinnest);

        Assert.Equal(LadderVerdict.NotMeasured, verdict);
        Assert.False(verdict.Admits());

        // And it is never remembered: a piece whose art was not there yet gets
        // another chance rather than teleporting forever.
        Assert.False(verdict.WorthRemembering());
        Assert.True(LadderVerdict.MeasuresLikeALadder.WorthRemembering());
        Assert.True(LadderVerdict.TooShort.WorthRemembering());
    }

    [Fact]
    public void EveryVerdictExplainsItselfToAPerson()
    {
        foreach (LadderVerdict verdict in Enum.GetValues<LadderVerdict>())
        {
            string explanation = LadderAdmission.Explain(verdict);
            Assert.False(string.IsNullOrWhiteSpace(explanation));
            Assert.DoesNotContain("no reason was recorded", explanation);
        }
    }

    // --- the settings -----------------------------------------------------

    [Fact]
    public void ZeroConfigurationIsTheSpecsTable()
    {
        // LADDERS.md section 8. A player who never opens the config file gets
        // exactly this.
        LadderSettingValues defaults = LadderSettingValues.Defaults;

        Assert.True(defaults.Enabled);
        Assert.True(defaults.AutoMount);
        Assert.Equal(1f, defaults.ClimbSpeed);
        Assert.Equal(0f, defaults.StaminaCost);
        Assert.False(defaults.UseTeleport);
        Assert.False(defaults.NpcClimbing);
    }

    [Fact]
    public void TheDefaultsAreAlreadyTheClimbsOwnDefaults()
    {
        // Binding the section must not change the behaviour a player who never
        // configures anything gets.
        var options = new ClimbOptions();
        LadderSettingValues.Defaults.ApplyTo(options);

        Assert.True(options.Enabled);
        Assert.True(options.AutoMount);
        Assert.Equal(1f, options.ClimbSpeedMultiplier);
        Assert.Equal(0f, options.StaminaPerSecond);
    }

    [Fact]
    public void TheSettingsReachTheClimb()
    {
        var options = new ClimbOptions();
        new LadderSettingValues(
            enabled: false,
            autoMount: false,
            climbSpeed: 1.5f,
            staminaCost: 2.5f,
            useTeleport: true,
            npcClimbing: true).ApplyTo(options);

        Assert.False(options.Enabled);
        Assert.False(options.AutoMount);
        Assert.Equal(1.5f, options.ClimbSpeedMultiplier);
        Assert.Equal(2.5f, options.StaminaPerSecond);

        ClimbLimits limits = options.ToLimits();
        Assert.Equal(1.5f, limits.SpeedMultiplier);
        Assert.Equal(2.5f, limits.StaminaPerSecond);
    }

    [Theory]
    [InlineData(-10f, LadderSettingValues.MinimumClimbSpeed)]
    [InlineData(0f, LadderSettingValues.MinimumClimbSpeed)]
    [InlineData(0.1f, LadderSettingValues.MinimumClimbSpeed)]
    [InlineData(1000f, LadderSettingValues.MaximumClimbSpeed)]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(float.NegativeInfinity, 1f)]
    public void AnOutOfRangeClimbSpeedIsBroughtBackIntoRangeNotThrown(float given, float expected)
    {
        var options = new ClimbOptions();
        new LadderSettingValues(true, true, given, 0f, false, false).ApplyTo(options);

        Assert.Equal(expected, options.ClimbSpeedMultiplier);

        // And the domain, which does throw on a number outside its range,
        // never sees the bad one.
        Assert.Equal(expected, options.ToLimits().SpeedMultiplier);
    }

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(500f, LadderSettingValues.MaximumStaminaCost)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.NegativeInfinity, 0f)]
    public void AnOutOfRangeStaminaCostIsBroughtBackIntoRangeNotThrown(float given, float expected)
    {
        var options = new ClimbOptions();
        new LadderSettingValues(true, true, 1f, given, false, false).ApplyTo(options);

        Assert.Equal(expected, options.StaminaPerSecond);
        Assert.Equal(expected, options.ToLimits().StaminaPerSecond);
    }

    [Fact]
    public void TheConfigRangesAreTheRangesTheDomainValidates()
    {
        // If these ever disagree, a value the config file accepts becomes a
        // throw at the moment somebody walks into a ladder.
        var atTheEdges = new ClimbLimits
        {
            SpeedMultiplier = LadderSettingValues.MinimumClimbSpeed,
            StaminaPerSecond = LadderSettingValues.MaximumStaminaCost,
        };
        atTheEdges.Validate();

        var atTheOtherEdges = new ClimbLimits
        {
            SpeedMultiplier = LadderSettingValues.MaximumClimbSpeed,
            StaminaPerSecond = LadderSettingValues.MinimumStaminaCost,
        };
        atTheOtherEdges.Validate();
    }

    [Fact]
    public void TurningLaddersOffRefusesEveryMount()
    {
        // The other half of "off is off": no patch is installed at startup, and
        // that is only visible in game. This half is visible here - with the
        // setting off, nothing mounts, whatever else happens.
        var options = new ClimbOptions();
        new LadderSettingValues(false, true, 1f, 0f, false, false).ApplyTo(options);

        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(0f, 0f, 0f),
            new ClimbPoint(0f, 4f, 0f),
            ClimbHeading.FromXz(1f, 0f),
            width: 0.8f,
            rungPitch: 0.35f,
            out LadderGeometry geometry));

        var climber = new ClimberState(
            new ClimbPoint(0.5f, 0f, 0f),
            ClimbHeading.FromXz(-1f, 0f),
            canClimbNow: true,
            alreadyClimbing: false);

        MountDecision walkingIn = LadderMount.Decide(
            geometry, climber, ClimbLimits.Default, options.Enabled, byDeliberateUse: false);
        MountDecision pressingUse = LadderMount.Decide(
            geometry, climber, ClimbLimits.Default, options.Enabled, byDeliberateUse: true);

        Assert.False(walkingIn.Allowed);
        Assert.False(pressingUse.Allowed);
    }

    // --- pressing Use -----------------------------------------------------

    [Fact]
    public void WithLaddersOffUseIsVanillasTeleport()
    {
        Assert.Equal(
            LadderUseOutcome.VanillaTeleport,
            LadderUse.Decide(laddersEnabled: false, useTeleport: false, alreadyClimbing: false));
        Assert.Equal(
            LadderUseOutcome.VanillaTeleport,
            LadderUse.Decide(laddersEnabled: false, useTeleport: false, alreadyClimbing: true));
    }

    [Fact]
    public void UseTeleportRestoresVanillaInEveryState()
    {
        foreach (bool climbing in new[] { false, true })
        {
            Assert.Equal(
                LadderUseOutcome.VanillaTeleport,
                LadderUse.Decide(laddersEnabled: true, useTeleport: true, alreadyClimbing: climbing));
        }
    }

    [Fact]
    public void ByDefaultUseStartsAClimb()
    {
        LadderSettingValues defaults = LadderSettingValues.Defaults;

        Assert.Equal(
            LadderUseOutcome.TryToClimb,
            LadderUse.Decide(defaults.Enabled, defaults.UseTeleport, alreadyClimbing: false));
    }

    [Fact]
    public void AClimbersUseIsNeverATeleport()
    {
        // Vanilla's Use on a ladder moves you to the other end of it. Doing
        // that to somebody already on the ladder throws them off their run.
        Assert.Equal(
            LadderUseOutcome.AlreadyClimbing,
            LadderUse.Decide(laddersEnabled: true, useTeleport: false, alreadyClimbing: true));
    }
}
