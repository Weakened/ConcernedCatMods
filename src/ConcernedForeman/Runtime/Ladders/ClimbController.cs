using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>The local player climbing a ladder (CF-LAD-002).
///
/// It owns one climb at a time, for one body: <c>Player.m_localPlayer</c>. It
/// finds the ladder through <see cref="LadderSurvey"/>, asks
/// <see cref="LadderMount"/> whether the climb may start, moves the body along
/// <see cref="ClimbTrack"/> through the character's own rigidbody, leaves
/// through <see cref="ClimbExit"/>, and checks <see cref="ClimbSafety.MustEnd"/>
/// before it moves anything at all, every frame.
///
/// The rule it exists to keep is LADDERS.md L5: <b>there is no path out of a
/// climb that does not hand the character back whole</b>. A destroyed ladder, a
/// death, a teleport, a world unload, the plugin stopping, the setting being
/// switched off, and an exception inside this class all end the same way, in the
/// same frame, with gravity, the motor and the fall reference restored and the
/// climb pose cleared.
///
/// Multiplayer: the climber's own client owns the climb and the body travels the
/// way every character's does. No RPC is sent, no ZDO key is written, and no
/// ladder is reserved — two players may climb the same one.</summary>
internal sealed class ClimbController
{
    /// <summary>Further than a step-off should ever be. A placement bigger than
    /// this is a bug in the measurement, and the climber is let go where they
    /// are rather than moved somewhere the probe only thinks is floor.</summary>
    private const float FurthestStepOffMetres = 2f;

    private static ClimbController? _active;

    private readonly ClimbOptions _options;
    private readonly Action<string> _log;
    private readonly LadderSurvey _survey;
    private readonly MountGate _gate = new MountGate();
    private readonly List<Ladder> _pieces = new List<Ladder>();
    private readonly List<Vector3> _piecesWere = new List<Vector3>();

    private ClimbSession? _session;
    private Player? _climber;
    private Rigidbody? _body;
    private CapsuleCollider? _capsule;
    private ClimbLimits _limits;
    private float _limitsFromSpeed = float.NaN;
    private float _limitsFromStamina = float.NaN;
    private bool _letGo;
    private bool _running;
    private bool _saidUnavailable;

    internal ClimbController(ClimbOptions options, Action<string> log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _survey = new LadderSurvey(_options, log);
        _limitsFromSpeed = _options.ClimbSpeedMultiplier;
        _limitsFromStamina = _options.StaminaPerSecond;
        _limits = _options.ToLimits();
    }

    /// <summary>What the climb is doing, for the presentation layer
    /// (CF-LAD-003). It is a value and it is always current: the pose and the
    /// sound read it rather than reaching into the climb, and
    /// <see cref="ClimbTelemetry.Idle"/> — published on every ending, including
    /// the ones nobody chose — is how they are told to stop.</summary>
    internal static ClimbTelemetry Telemetry { get; private set; } = ClimbTelemetry.Idle;

    /// <summary>Raised whenever <see cref="Telemetry"/> changes, including the
    /// idle one at the end of a climb.</summary>
    internal static event Action<ClimbTelemetry>? TelemetryChanged;

    /// <summary>The live settings. The lead binds Foreman's `Ladders` config
    /// section onto this object; every value is read at the moment it is used,
    /// so switching ladders off ends a climb on the next frame.</summary>
    internal ClimbOptions Options => _options;

    internal bool IsClimbing => _session != null;

    internal LadderSurvey Survey => _survey;

    /// <summary>Starts the feature: binds the motor and installs its two
    /// prefixes. False means the game did not fit and ladders quietly stay
    /// vanilla; <see cref="ClimbMotor.Unavailable"/> says why.</summary>
    internal bool Install(string harmonyId)
    {
        if (!ClimbMotor.Install(harmonyId, _log))
        {
            return false;
        }

        _active = this;
        _running = true;
        return true;
    }

    /// <summary>One frame, from the plugin's own Update. It surveys, and it
    /// decides whether walking into a ladder starts a climb. The motion itself
    /// happens in the physics step, through the motor prefix.</summary>
    internal void Update(float deltaTime)
    {
        try
        {
            Player? player = Player.m_localPlayer;
            if (!_running || !_options.Enabled || player == null)
            {
                if (_session != null)
                {
                    End(_session.Stop(player == null ? ClimbEndReason.WorldUnloading : ClimbEndReason.RuntimeStopped));
                }

                if (player == null || !_options.Enabled)
                {
                    _survey.Forget();
                }

                return;
            }

            _gate.Tick(deltaTime);
            if (_session != null)
            {
                // A climber already has their run; nothing else needs finding.
                return;
            }

            _survey.Look(deltaTime, player.transform.position);
            if (!_options.AutoMount || !_gate.AllowsAutoMount)
            {
                return;
            }

            TryStart(player, byDeliberateUse: false, null);
        }
        catch (Exception exception)
        {
            _log("Ladder climbing failed and let go: " + SafeFailure.Brief(exception));
            Fault();
        }
    }

    /// <summary>Pressing Use on a ladder. CF-LAD-004's interaction patch calls
    /// this and suppresses vanilla's teleport when it returns true; when it
    /// returns false the teleport is vanilla's to do, exactly as before.</summary>
    internal bool TryMountByUse(Ladder ladder)
    {
        try
        {
            Player? player = Player.m_localPlayer;
            if (!_running || !_options.Enabled || player == null || ladder == null || _session != null)
            {
                return false;
            }

            if (!_gate.AllowsDeliberateMount)
            {
                return false;
            }

            // A ladder built or repaired a moment ago may not be in the survey
            // yet, and Use must not be the one thing that fails.
            _survey.Look(0f, player.transform.position, force: true);
            return TryStart(player, byDeliberateUse: true, ladder);
        }
        catch (Exception exception)
        {
            _log("Ladder climbing failed and let go: " + SafeFailure.Brief(exception));
            Fault();
            return false;
        }
    }

    /// <summary>The world went away. Everything measured against it goes with
    /// it, and a climber is standing in no world at all.</summary>
    internal void OnWorldUnloaded()
    {
        if (_session != null)
        {
            End(_session.Stop(ClimbEndReason.WorldUnloading));
        }

        _survey.Forget();
        _gate.Reset();
    }

    /// <summary>The plugin is stopping. The climber is handed back first, then
    /// the patches come out: in that order, so nothing is left holding a body
    /// that no longer has a motor patch to release it.</summary>
    internal void Stop()
    {
        if (_session != null)
        {
            End(_session.Stop(ClimbEndReason.RuntimeStopped));
        }

        _running = false;
        _survey.Forget();
        _gate.Reset();
        ClimbMotor.Remove();
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    /// <summary>The motor prefix's question: is this body on a ladder this
    /// frame, and has the climb already moved it? Deliberately cheap for every
    /// character that is not climbing, which is all of them nearly all of the
    /// time.</summary>
    internal static bool DrivesMotion(Character character, float deltaTime)
    {
        ClimbController? controller = _active;
        if (controller == null || controller._session == null || character == null)
        {
            return false;
        }

        if (!ReferenceEquals(character, controller._climber))
        {
            return false;
        }

        return controller.FixedStep(deltaTime);
    }

    /// <summary>The jump prefix's question. A climber is off the ground, so
    /// vanilla's jump would refuse; this turns the press into letting go.</summary>
    internal static bool LetsGoOnJump(Character character)
    {
        ClimbController? controller = _active;
        if (controller == null || controller._session == null || character == null)
        {
            return false;
        }

        if (!ReferenceEquals(character, controller._climber))
        {
            return false;
        }

        controller._letGo = true;
        return true;
    }

    private bool TryStart(Player player, bool byDeliberateUse, Ladder? only)
    {
        if (!ClimbMotor.Ready)
        {
            if (!_saidUnavailable)
            {
                _saidUnavailable = true;
                _log("Ladder climbing is not running: " + ClimbMotor.Unavailable + ".");
            }

            return false;
        }

        _limits = Limits();
        ClimberState climber = Read(player);
        if (!_survey.TryPlanMount(
                climber,
                _limits,
                _options.Enabled,
                byDeliberateUse,
                only,
                out LadderRun run,
                out IReadOnlyList<Ladder> pieces,
                out MountRefusal refusal))
        {
            if (MountGate.LeavesTheBand(refusal))
            {
                _gate.OutOfTheBand();
            }

            return false;
        }

        if (!ClimbSession.TryStart(
                run, climber, _limits, _options.Enabled, byDeliberateUse, out ClimbSession session, out refusal))
        {
            return false;
        }

        _session = session;
        _climber = player;
        _body = player.GetComponent<Rigidbody>();
        _capsule = player.GetCollider();
        _letGo = false;
        _pieces.Clear();
        _piecesWere.Clear();
        for (int index = 0; index < pieces.Count; index++)
        {
            Ladder piece = pieces[index];
            _pieces.Add(piece);
            _piecesWere.Add(piece == null ? Vector3.zero : piece.transform.position);
        }

        if (_body == null)
        {
            // No rigidbody means nothing to drive. Give up before the body is
            // touched rather than half way up.
            End(session.Stop(ClimbEndReason.ControllerFault));
            return false;
        }

        return true;
    }

    /// <summary>One physics step of a climb. Everything in here is inside a
    /// try/catch, because this is the method an exception would strand a player
    /// in.</summary>
    private bool FixedStep(float deltaTime)
    {
        ClimbSession session = _session!;
        try
        {
            var frame = new ClimbFrame(deltaTime, Conditions(session), Input(session), _letGo, Landing(session));
            _letGo = false;

            ClimbStep step = session.Step(frame);
            if (!step.Continues)
            {
                End(step);
                return false;
            }

            Hold(step, deltaTime);
            Publish(step.Telemetry);
            return true;
        }
        catch (Exception exception)
        {
            _log("Ladder climbing failed and let go: " + SafeFailure.Brief(exception));
            Fault();
            return false;
        }
    }

    /// <summary>What the world says about this climb, read honestly. Nothing
    /// here is assumed true because it was true when the climb started.</summary>
    private ClimbConditions Conditions(ClimbSession session)
    {
        Player player = _climber!;
        bool ladderAlive = _pieces.Count > 0;
        bool ladderLoaded = true;
        bool ladderStill = true;

        for (int index = 0; index < _pieces.Count; index++)
        {
            Ladder piece = _pieces[index];
            if (piece == null)
            {
                ladderAlive = false;
                break;
            }

            ZNetView view = piece.GetComponentInParent<ZNetView>();
            if (view != null && !view.IsValid())
            {
                ladderLoaded = false;
            }

            if ((piece.transform.position - _piecesWere[index]).sqrMagnitude > 0.0025f)
            {
                ladderStill = false;
            }
        }

        ClimbPoint held = session.HeldAt;
        Vector3 body = _body != null ? _body.position : player.transform.position;
        float drift = Vector3.Distance(body, new Vector3(held.X, held.Y, held.Z));

        return new ClimbConditions(
            ladderAlive,
            ladderLoaded && ZNetScene.instance != null,
            ladderStill,
            characterAlive: !player.IsDead(),
            characterFree: IsFree(player),
            worldUp: ZNetScene.instance != null && ReferenceEquals(Player.m_localPlayer, player),
            runtimeRunning: _running && _options.Enabled,
            distanceFromLadder: drift);
    }

    /// <summary>Nothing else has hold of this character. Anything that does —
    /// a bed, a chair, a ship's rudder, a teleport, a cutscene, deep water —
    /// owns the body, and a climb must let go of it in the same frame.</summary>
    private static bool IsFree(Player player) =>
        !player.IsTeleporting() &&
        !player.IsAttached() &&
        !player.InBed() &&
        !player.IsRiding() &&
        !player.InCutscene() &&
        !player.IsSwimming() &&
        !player.IsDebugFlying();

    /// <summary>The climber's intent along the ladder, from vanilla's own move
    /// direction. Taken along the character's own look, not along the ladder, so
    /// that forward is always up the ladder however the camera is turned.</summary>
    private float Input(ClimbSession session)
    {
        Player player = _climber!;
        if (!ClimbMotor.TryReadMoveDir(player, out Vector3 move) || move.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        Vector3 look = player.GetLookDir();
        look.y = 0f;
        if (look.sqrMagnitude <= 0.0001f)
        {
            ClimbHeading facing = session.Run.AsOne.FacingWhileClimbing;
            look = new Vector3(facing.X, 0f, facing.Z);
        }

        look.Normalize();
        return Mathf.Clamp(Vector3.Dot(move, look), -1f, 1f);
    }

    /// <summary>Whether there is somewhere to stand at the top, asked only when
    /// the climber is nearly there: the probe costs a ray and a capsule test,
    /// and its answer is not used anywhere else.</summary>
    private TopLanding Landing(ClimbSession session)
    {
        if (!session.NearTheTop(_options.TopProbeWithinMetres))
        {
            return TopLanding.None;
        }

        return _survey.ProbeTop(session.Run.AsOne, _limits, _capsule);
    }

    /// <summary>Holds the body on the ladder for one physics step.
    ///
    /// The velocity is the one that closes the gap between where the body is and
    /// where the climb says it belongs, which both carries the climber up the
    /// rungs and cancels any sideways drift in the same step. It is capped, so a
    /// body that snagged is pulled back at a climbing speed rather than flung;
    /// past <see cref="ClimbLimits.LostContactMetres"/> the climb ends instead of
    /// pulling harder.</summary>
    private void Hold(in ClimbStep step, float deltaTime)
    {
        Rigidbody body = _body!;
        Player player = _climber!;

        var target = new Vector3(step.BodyTarget.X, step.BodyTarget.Y, step.BodyTarget.Z);
        Vector3 wanted = target - body.position;
        Vector3 velocity = deltaTime > 0f ? wanted / deltaTime : Vector3.zero;
        float cap = Mathf.Max(_limits.EffectiveClimbSpeed, _options.MaxCorrectionSpeed);
        if (velocity.sqrMagnitude > cap * cap)
        {
            velocity = velocity.normalized * cap;
        }

        body.useGravity = false;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;

        if (step.BodyFacing.IsKnown)
        {
            body.rotation = Quaternion.LookRotation(new Vector3(step.BodyFacing.X, 0f, step.BodyFacing.Z));
        }

        // The climber is in the air for the whole climb as far as vanilla is
        // concerned; without this a descent would land as a fall.
        ClimbMotor.PinFallHeight(player);

        if (step.StaminaUsed > 0f)
        {
            player.UseStamina(step.StaminaUsed);
        }
    }

    /// <summary>An exception anywhere in the climb. The character is handed
    /// back on the domain's own fault path, and if even that throws the body is
    /// released by hand: there is no failure here that is allowed to end with a
    /// player stuck on a ladder.</summary>
    private void Fault()
    {
        try
        {
            if (_session != null)
            {
                End(_session.Fault());
                return;
            }
        }
        catch (Exception exception)
        {
            _log("Ladder climbing could not end cleanly: " + SafeFailure.Brief(exception));
        }

        Release(ClimbSafety.Restoration, ClimbExitKind.LetGo);
    }

    /// <summary>The one way out. Places the character only when the domain said
    /// to, then gives back everything <see cref="ClimbSafety.Restoration"/>
    /// lists, whatever ended the climb.</summary>
    private void End(in ClimbStep step)
    {
        Player? player = _climber;
        ClimbExitKind kind = step.HasExit ? step.Exit.Kind : ClimbExitKind.LetGo;

        if (step.HasExit && step.Exit.PlaceCharacter && player != null && _body != null)
        {
            var landing = new Vector3(step.Exit.Landing.X, step.Exit.Landing.Y, step.Exit.Landing.Z);
            if (IsSane(landing) && Vector3.Distance(player.transform.position, landing) <= FurthestStepOffMetres)
            {
                player.transform.position = landing;
                _body.position = landing;
                if (step.Exit.Facing.IsKnown)
                {
                    var facing = new Vector3(step.Exit.Facing.X, 0f, step.Exit.Facing.Z);
                    player.transform.rotation = Quaternion.LookRotation(facing);
                    _body.rotation = player.transform.rotation;
                }

                Physics.SyncTransforms();
            }
            else
            {
                // The measurement produced somewhere the character should not be
                // put. Letting go where they stand is always safe; placing them
                // somewhere wrong is not.
                _log("Ladder step-off refused: the landing was not where a body can be put. Let go instead.");
            }
        }

        if (step.EndReason != ClimbEndReason.Unspecified)
        {
            _log("Ladder climb ended: " + ClimbSafety.Explain(step.EndReason));
        }

        Release(step.Restoration, kind);
    }

    /// <summary>Everything the climb took, given back. The list is the domain's,
    /// so a new exit path cannot quietly forget one of them.</summary>
    private void Release(in ClimbRestoration restoration, ClimbExitKind kind)
    {
        Player? player = _climber;
        Rigidbody? body = _body;

        _session = null;
        _climber = null;
        _body = null;
        _capsule = null;
        _letGo = false;
        _pieces.Clear();
        _piecesWere.Clear();
        _gate.ClimbEnded(kind, _options.ReMountCooldownSeconds);

        if (body != null)
        {
            if (restoration.RestoreGravity)
            {
                body.useGravity = true;
            }

            if (restoration.RestoreMotor)
            {
                // The motor takes the body back from the next physics step,
                // because nothing is holding it any more. It starts from rest:
                // the servo velocity that was carrying the climber up the rungs
                // is not a velocity anybody should inherit.
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        // Collisions are never taken away by this feature — a climber is a
        // solid body on a ladder, which is what keeps them from being pushed
        // through the world — so restoring them is checking that nothing was
        // taken. The assertion is the point: if a later change does disable a
        // collider, this is where it has to be put back.
        if (restoration.RestoreCollisions && player != null)
        {
            CapsuleCollider capsule = player.GetCollider();
            if (capsule != null && !capsule.enabled)
            {
                capsule.enabled = true;
            }
        }

        if (restoration.RestoreMotor && player != null)
        {
            // Last thing before the motor takes over: the climb is not a fall,
            // whichever way it ended.
            ClimbMotor.PinFallHeight(player);
        }

        if (restoration.ClearClimbPose || restoration.StopClimbSound)
        {
            Publish(ClimbTelemetry.Idle);
        }
    }

    /// <summary>The tuned numbers with the player's settings applied, built
    /// again only when one of those settings has actually changed.
    ///
    /// The idle path runs every frame, and a fresh limits object every frame
    /// would be an allocation per frame for a feature that is supposed to cost
    /// nothing when nobody is climbing (LADDERS.md G6).</summary>
    private ClimbLimits Limits()
    {
        float speed = _options.ClimbSpeedMultiplier;
        float stamina = _options.StaminaPerSecond;
        if (speed.Equals(_limitsFromSpeed) && stamina.Equals(_limitsFromStamina))
        {
            return _limits;
        }

        _limitsFromSpeed = speed;
        _limitsFromStamina = stamina;
        return _options.ToLimits();
    }

    private static bool IsSane(Vector3 point) =>
        !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) &&
        !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);

    private ClimberState Read(Player player)
    {
        Vector3 feet = player.transform.position;
        ClimbHeading moving = ClimbHeading.None;
        if (ClimbMotor.TryReadMoveDir(player, out Vector3 move) && move.sqrMagnitude > 0.01f)
        {
            moving = ClimbHeading.FromXz(move.x, move.z);
        }

        return new ClimberState(
            new ClimbPoint(feet.x, feet.y, feet.z),
            moving,
            canClimbNow: !player.IsDead() && IsFree(player) && !player.InPlaceMode(),
            alreadyClimbing: _session != null);
    }

    private static void Publish(ClimbTelemetry telemetry)
    {
        Telemetry = telemetry;
        try
        {
            TelemetryChanged?.Invoke(telemetry);
        }
        catch
        {
            // The pose is a listener, not a partner: a presentation failure
            // must never be able to end, stall or corrupt a climb.
        }
    }
}
