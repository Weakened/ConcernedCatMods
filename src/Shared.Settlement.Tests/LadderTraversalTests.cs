using TheConcernedCat.Ladders;

namespace Shared.Settlement.Tests;

/// <summary>CF-LAD-002: the climb's decision seams, with the world faked.
///
/// The Valheim side of the climb reads the world and moves a body; every rule
/// about where a climber is, when a climb must end and what has to be handed
/// back lives in <see cref="ClimbSession"/> and is proved here. In particular
/// there is one test per <see cref="ClimbEndReason"/>, because "the character
/// got its body back" is the requirement this feature cannot fail.</summary>
public sealed class LadderTraversalTests
{
    private const float Step = 1f / 50f;

    // A four metre ladder on the world origin, climbed from its +X face.
    private static LadderRun Run(float height = 4f)
    {
        Assert.True(LadderGeometry.TryMeasure(
            new ClimbPoint(0f, 0f, 0f),
            new ClimbPoint(0f, height, 0f),
            ClimbHeading.FromXz(1f, 0f),
            width: 0.8f,
            rungPitch: 0.35f,
            out LadderGeometry geometry));
        return LadderRun.Single(geometry);
    }

    private static ClimberState AtTheFoot(float reach = 0.5f) => new ClimberState(
        new ClimbPoint(reach, 0f, 0f),
        ClimbHeading.FromXz(-1f, 0f),
        canClimbNow: true,
        alreadyClimbing: false);

    private static ClimbSession Mounted(LadderRun run, ClimbLimits? limits = null, ClimberState? climber = null)
    {
        Assert.True(ClimbSession.TryStart(
            run,
            climber ?? AtTheFoot(),
            limits ?? ClimbLimits.Default,
            laddersEnabled: true,
            byDeliberateUse: false,
            out ClimbSession session,
            out MountRefusal refusal));
        Assert.Equal(MountRefusal.Unspecified, refusal);
        return session;
    }

    private static ClimbStep Drive(
        ClimbSession session,
        float input,
        float seconds,
        TopLanding landing = default,
        bool letGo = false)
    {
        ClimbStep step = default;
        for (float elapsed = 0f; elapsed < seconds; elapsed += Step)
        {
            step = session.Step(new ClimbFrame(Step, ClimbConditions.Fine(), input, letGo, landing));
            if (!step.Continues)
            {
                return step;
            }
        }

        return step;
    }

    // ---- survey to mount -------------------------------------------------

    [Fact]
    public void A_character_in_front_of_a_ladder_may_climb_it()
    {
        ClimbSession session = Mounted(Run());

        Assert.Equal(ClimbPhase.Aligning, session.Phase);
        Assert.Equal(0f, session.Progress, 3);
    }

    [Fact]
    public void A_character_behind_the_ladder_is_refused_with_the_reason()
    {
        var behind = new ClimberState(
            new ClimbPoint(-0.5f, 0f, 0f),
            ClimbHeading.FromXz(1f, 0f),
            canClimbNow: true,
            alreadyClimbing: false);

        Assert.False(ClimbSession.TryStart(
            Run(), behind, ClimbLimits.Default, true, false, out _, out MountRefusal refusal));
        Assert.Equal(MountRefusal.WrongSide, refusal);
    }

    [Fact]
    public void Walking_past_a_ladder_does_not_grab_it_but_pressing_use_does()
    {
        var passingBy = new ClimberState(
            new ClimbPoint(0.5f, 0f, 0f),
            ClimbHeading.FromXz(0f, 1f),
            canClimbNow: true,
            alreadyClimbing: false);

        Assert.False(ClimbSession.TryStart(
            Run(), passingBy, ClimbLimits.Default, true, byDeliberateUse: false, out _, out MountRefusal refusal));
        Assert.Equal(MountRefusal.LookingAway, refusal);

        Assert.True(ClimbSession.TryStart(
            Run(), passingBy, ClimbLimits.Default, true, byDeliberateUse: true, out _, out _));
    }

    [Fact]
    public void Ladders_switched_off_refuse_every_mount()
    {
        Assert.False(ClimbSession.TryStart(
            Run(), AtTheFoot(), ClimbLimits.Default, laddersEnabled: false, false, out _, out MountRefusal refusal));
        Assert.Equal(MountRefusal.Disabled, refusal);
    }

    // ---- alignment -------------------------------------------------------

    [Fact]
    public void The_body_moves_onto_the_ladder_over_the_alignment_time_and_never_snaps()
    {
        LadderRun run = Run();
        ClimbLimits limits = ClimbLimits.Default;
        ClimbSession session = Mounted(run, limits);

        ClimbStep first = session.Step(new ClimbFrame(Step, ClimbConditions.Fine(), 1f, false, default));
        Assert.True(first.Continues);
        Assert.Equal(ClimbPhase.Aligning, session.Phase);

        // One physics step in, the body has barely left where it was standing:
        // the alignment is a move, not a teleport onto the ladder.
        float travelled = first.BodyTarget.HorizontalDistanceTo(AtTheFoot().Feet);
        Assert.True(travelled < 0.05f, "alignment jumped " + travelled + " m in one step");

        ClimbStep settled = Drive(session, 0f, limits.AlignSeconds + Step);
        Assert.Equal(ClimbPhase.OnTheLadder, session.Phase);
        Assert.Equal(1f, settled.Telemetry.AlignFraction, 3);
        Assert.Equal(run.AsOne.ClimbPositionAt(0f, limits.BodyOffsetMetres), settled.BodyTarget);
        Assert.Equal(run.AsOne.FacingWhileClimbing, settled.BodyFacing);
    }

    // ---- the climb itself ------------------------------------------------

    [Fact]
    public void Climbing_up_forever_stops_at_the_head_of_the_ladder()
    {
        LadderRun run = Run();
        ClimbSession session = Mounted(run);

        // Ten seconds of holding "up" on a four metre ladder: a clamp that only
        // worked for sensible inputs would have thrown the climber off the top.
        ClimbStep step = Drive(session, 1f, 10f);

        Assert.True(step.Continues);
        Assert.Equal(run.Height, session.Progress, 3);
        Assert.Equal(ClimbMotion.PressingAgainstTheTop, step.Telemetry.Motion);
        Assert.Equal(0f, step.Telemetry.Velocity, 3);
    }

    [Fact]
    public void Releasing_the_input_rests_on_the_ladder_without_creeping()
    {
        ClimbSession session = Mounted(Run());
        Drive(session, 1f, 1f);
        float reached = session.Progress;

        ClimbStep resting = Drive(session, 0f, 2f);

        Assert.True(resting.Continues);
        Assert.Equal(reached, session.Progress, 3);
        Assert.Equal(ClimbMotion.Resting, resting.Telemetry.Motion);
        Assert.Equal(0f, resting.Telemetry.AnimationSpeed, 3);
    }

    [Fact]
    public void Reversing_reverses_the_motion_and_the_animation()
    {
        ClimbSession session = Mounted(Run());
        ClimbStep up = Drive(session, 1f, 1f);
        Assert.True(up.Telemetry.AnimationSpeed > 0f);

        ClimbStep down = session.Step(new ClimbFrame(Step, ClimbConditions.Fine(), -1f, false, default));

        Assert.Equal(ClimbMotion.Descending, down.Telemetry.Motion);
        Assert.True(down.Telemetry.AnimationSpeed < 0f);
    }

    [Fact]
    public void The_telemetry_follows_the_body_up_the_rungs()
    {
        ClimbSession session = Mounted(Run());

        ClimbStep step = Drive(session, 1f, 1f);

        Assert.Equal(ClimbPhase.OnTheLadder, step.Telemetry.Phase);
        Assert.True(step.Telemetry.IsClimbing);
        Assert.Equal(4f, step.Telemetry.Height, 3);
        Assert.Equal(session.Progress, step.Telemetry.Progress, 3);
        Assert.Equal((int)(session.Progress / 0.35f), step.Telemetry.Rung);
        Assert.Equal(Run().AsOne.FacingWhileClimbing, step.Telemetry.Facing);
    }

    // ---- exits -----------------------------------------------------------

    [Fact]
    public void Over_the_top_puts_the_character_on_the_floor_that_was_found()
    {
        LadderRun run = Run();
        ClimbSession session = Mounted(run);
        var landing = new TopLanding(hasStandingSpace: true, new ClimbPoint(0f, 4f, 0f));

        ClimbStep step = Drive(session, 1f, 10f, landing);

        Assert.False(step.Continues);
        Assert.True(step.HasExit);
        Assert.Equal(ClimbExitKind.Top, step.Exit.Kind);
        Assert.True(step.Exit.PlaceCharacter);
        Assert.Equal(ClimbEndReason.Unspecified, step.EndReason);
        Assert.True(step.Restoration.IsComplete);

        // Inwards over the edge, and above the floor rather than in it.
        Assert.Equal(-ClimbLimits.Default.TopStepInMetres, step.Exit.Landing.X, 3);
        Assert.Equal(4f + ClimbLimits.Default.TopStepUpMetres, step.Exit.Landing.Y, 3);
        Assert.Equal(ClimbTelemetry.Idle.Phase, step.Telemetry.Phase);
    }

    [Fact]
    public void A_ladder_that_ends_under_a_roof_holds_the_climber_instead_of_pushing_them_into_it()
    {
        ClimbSession session = Mounted(Run());

        ClimbStep step = Drive(session, 1f, 10f, TopLanding.None);

        Assert.True(step.Continues);
        Assert.Equal(ClimbPhase.OnTheLadder, session.Phase);
        Assert.Equal(ClimbMotion.PressingAgainstTheTop, step.Telemetry.Motion);
    }

    [Fact]
    public void Off_the_bottom_stands_the_character_in_the_open_air_it_was_hanging_in()
    {
        LadderRun run = Run();
        ClimbSession session = Mounted(run);
        Drive(session, 1f, 1f);

        ClimbStep step = Drive(session, -1f, 10f);

        Assert.False(step.Continues);
        Assert.Equal(ClimbExitKind.Bottom, step.Exit.Kind);
        Assert.True(step.Exit.PlaceCharacter);
        Assert.Equal(ClimbLimits.Default.BodyOffsetMetres, step.Exit.Landing.X, 3);
        Assert.Equal(0f, step.Exit.Landing.Y, 3);
        Assert.True(step.Restoration.IsComplete);
    }

    [Fact]
    public void Jumping_lets_go_where_the_climber_is_and_never_moves_the_body()
    {
        ClimbSession session = Mounted(Run());
        Drive(session, 1f, 1f);

        ClimbStep step = session.Step(new ClimbFrame(Step, ClimbConditions.Fine(), 0f, letGoRequested: true, default));

        Assert.False(step.Continues);
        Assert.Equal(ClimbExitKind.JumpedOff, step.Exit.Kind);
        Assert.False(step.Exit.PlaceCharacter);
        Assert.Equal(ClimbEndReason.Unspecified, step.EndReason);
        Assert.True(step.Restoration.IsComplete);
    }

    // ---- safety: one test per reason a climb must end --------------------

    [Fact]
    public void A_destroyed_ladder_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.LadderDestroyed);

    [Fact]
    public void An_unloaded_ladder_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.LadderUnloaded);

    [Fact]
    public void A_ladder_that_moved_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.LadderMoved);

    [Fact]
    public void A_character_thrown_off_the_ladder_is_handed_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.LostContact);

    [Fact]
    public void Dying_on_a_ladder_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.CharacterDied);

    [Fact]
    public void Something_else_taking_hold_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.CharacterTaken);

    [Fact]
    public void A_world_going_away_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.WorldUnloading);

    [Fact]
    public void The_runtime_stopping_hands_the_character_back_whole() =>
        AssertHandedBackWhole(ClimbEndReason.RuntimeStopped);

    private static void AssertHandedBackWhole(ClimbEndReason reason)
    {
        ClimbSession session = Mounted(Run());
        Drive(session, 1f, 1f);

        ClimbStep step = session.Step(new ClimbFrame(Step, Broken(reason), 1f, false, default));

        Assert.False(step.Continues);
        Assert.True(step.HasExit);
        Assert.Equal(reason, step.EndReason);
        Assert.Equal(ClimbEndReason.Unspecified, ClimbSafety.MustEnd(ClimbConditions.Fine(), ClimbLimits.Default));
        Assert.True(step.Restoration.IsComplete);

        // Never placed: a killed, thrown, unloaded or teleported body is left
        // exactly where the world put it.
        Assert.False(step.Exit.PlaceCharacter);
        Assert.Equal(ClimbExitKind.LetGo, step.Exit.Kind);
        Assert.Equal(ClimbPhase.Ended, session.Phase);
        Assert.Equal(ClimbTelemetry.Idle.Phase, step.Telemetry.Phase);
    }

    [Fact]
    public void A_fault_inside_the_controller_hands_the_character_back_whole()
    {
        ClimbSession session = Mounted(Run());
        Drive(session, 1f, 1f);

        ClimbStep step = session.Fault();

        Assert.False(step.Continues);
        Assert.True(step.HasExit);
        Assert.Equal(ClimbEndReason.ControllerFault, step.EndReason);
        Assert.Equal(ClimbEndReason.ControllerFault, session.EndReason);
        Assert.False(step.Exit.PlaceCharacter);
        Assert.True(step.Restoration.IsComplete);
        Assert.Equal(ClimbPhase.Ended, session.Phase);
    }

    [Fact]
    public void An_ended_climb_stays_ended_and_still_asks_for_the_full_restoration()
    {
        ClimbSession session = Mounted(Run());
        Drive(session, 1f, 1f);
        session.Stop(ClimbEndReason.WorldUnloading);

        ClimbStep again = session.Step(new ClimbFrame(Step, ClimbConditions.Fine(), 1f, false, default));
        ClimbStep stoppedTwice = session.Stop(ClimbEndReason.RuntimeStopped);

        Assert.False(again.Continues);
        Assert.False(again.HasExit);
        Assert.True(again.Restoration.IsComplete);
        Assert.Equal(ClimbEndReason.WorldUnloading, again.EndReason);
        Assert.Equal(ClimbEndReason.WorldUnloading, stoppedTwice.EndReason);
    }

    [Fact]
    public void Safety_is_checked_before_the_climber_is_moved()
    {
        ClimbSession session = Mounted(Run());
        Drive(session, 1f, 1f);
        float reached = session.Progress;

        ClimbStep step = session.Step(
            new ClimbFrame(Step, Broken(ClimbEndReason.LadderDestroyed), 1f, false, default));

        Assert.False(step.Continues);
        Assert.Equal(reached, session.Progress, 3);
    }

    /// <summary>A world in which exactly one thing a climb depends on has gone
    /// wrong.</summary>
    private static ClimbConditions Broken(ClimbEndReason reason) => new ClimbConditions(
        ladderAlive: reason != ClimbEndReason.LadderDestroyed,
        ladderLoaded: reason != ClimbEndReason.LadderUnloaded,
        ladderStill: reason != ClimbEndReason.LadderMoved,
        characterAlive: reason != ClimbEndReason.CharacterDied,
        characterFree: reason != ClimbEndReason.CharacterTaken,
        worldUp: reason != ClimbEndReason.WorldUnloading,
        runtimeRunning: reason != ClimbEndReason.RuntimeStopped,
        distanceFromLadder: reason == ClimbEndReason.LostContact
            ? ClimbLimits.Default.LostContactMetres + 1f
            : 0f);

    // ---- letting go, and not being caught again --------------------------

    [Fact]
    public void Letting_go_is_not_undone_by_falling_back_through_the_mounting_band()
    {
        var gate = new MountGate();

        gate.ClimbEnded(ClimbExitKind.JumpedOff, cooldownSeconds: 0.35f);
        Assert.False(gate.AllowsAutoMount);
        Assert.False(gate.AllowsDeliberateMount);

        gate.Tick(0.5f);
        Assert.False(gate.AllowsAutoMount);

        // Pressing Use is a deliberate act: it only waits out the cooldown.
        Assert.True(gate.AllowsDeliberateMount);

        gate.OutOfTheBand();
        Assert.True(gate.AllowsAutoMount);
    }

    [Fact]
    public void Falling_past_a_ladder_you_let_go_of_is_not_leaving_its_band()
    {
        // The frames of a fall past the ladder say "you are not moving into it",
        // and that must not count as having left it.
        Assert.False(MountGate.LeavesTheBand(MountRefusal.LookingAway));
        Assert.False(MountGate.LeavesTheBand(MountRefusal.CharacterBusy));
        Assert.False(MountGate.LeavesTheBand(MountRefusal.AlreadyClimbing));
        Assert.False(MountGate.LeavesTheBand(MountRefusal.Disabled));

        // Walking away does.
        Assert.True(MountGate.LeavesTheBand(MountRefusal.TooFar));
        Assert.True(MountGate.LeavesTheBand(MountRefusal.OffToTheSide));
        Assert.True(MountGate.LeavesTheBand(MountRefusal.WrongSide));
        Assert.True(MountGate.LeavesTheBand(MountRefusal.OutOfSpan));
        Assert.True(MountGate.LeavesTheBand(MountRefusal.NotClimbable));
        Assert.True(MountGate.LeavesTheBand(MountRefusal.Unspecified));
    }

    [Fact]
    public void Stepping_off_at_either_end_lets_the_ladder_be_climbed_again_at_once()
    {
        var gate = new MountGate();

        gate.ClimbEnded(ClimbExitKind.Bottom, cooldownSeconds: 0.35f);
        Assert.False(gate.AllowsAutoMount);
        gate.Tick(0.4f);

        Assert.True(gate.AllowsAutoMount);
    }

    // ---- stamina and settings -------------------------------------------

    [Fact]
    public void Climbing_costs_no_stamina_by_default_and_costs_it_when_asked_to()
    {
        ClimbSession free = Mounted(Run());
        Assert.Equal(0f, Drive(free, 1f, 1f).StaminaUsed, 4);

        ClimbLimits charged = new ClimbOptions { StaminaPerSecond = 2f }.ToLimits();
        ClimbSession costly = Mounted(Run(), charged);
        Drive(costly, 1f, charged.AlignSeconds + Step);

        Assert.Equal(2f * Step, Drive(costly, 1f, Step).StaminaUsed, 4);
    }

    [Fact]
    public void A_nonsense_setting_is_clamped_rather_than_throwing_a_player_off_a_ladder()
    {
        ClimbLimits limits = new ClimbOptions
        {
            ClimbSpeedMultiplier = float.NaN,
            StaminaPerSecond = -12f,
        }.ToLimits();

        Assert.Equal(1f, limits.SpeedMultiplier, 3);
        Assert.Equal(0f, limits.StaminaPerSecond, 3);
        Assert.Equal(ClimbLimits.Default.ClimbSpeedMetresPerSecond, limits.EffectiveClimbSpeed, 3);
    }

    [Fact]
    public void The_defaults_are_the_documented_ones()
    {
        var options = new ClimbOptions();

        Assert.True(options.Enabled);
        Assert.True(options.AutoMount);
        Assert.Equal(1f, options.ClimbSpeedMultiplier, 3);
        Assert.Equal(0f, options.StaminaPerSecond, 3);
    }
}
