using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>Explicit-use player traversal. The owner's motor runs one swept, bounded physics step at a time.</summary>
internal sealed class LadderRuntime : IDisposable
{
    private enum Mode { Off, Mounting, Climbing, Exiting }

    /// <summary>How far below the run's top an upward climb starts looking for a floor to walk onto.</summary>
    private const float TopPlanBand = 0.6f;

    /// <summary>A body further than this from the motor's last command at release has been moved by something
    /// else; the release then makes no physics or fall-reference writes.</summary>
    private const float TakeoverDistance = 0.5f;

    /// <summary>A blocked upward climb this close to the top also tries the top exit: a lip overhead.</summary>
    private const float TopBlockedBand = 2.2f;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly Action<string> _log;
    private readonly LadderMotor _motor = new LadderMotor();
    private readonly LadderWalkPose _pose;
    private readonly LadderClimbAnimation _animation;
    private readonly LadderSkill _skill;
    private readonly ClimbLedger _ledger = new ClimbLedger();
    private readonly List<LadderSegment> _segments = new List<LadderSegment>();
    private readonly List<Vector3> _positions = new List<Vector3>();
    private readonly List<Quaternion> _rotations = new List<Quaternion>();
    private readonly List<LadderSegment> _hoverParts = new List<LadderSegment>();
    private LadderSegment? _hoverSegment;
    private LadderGeometry _hoverRun;
    private bool _hoverValid;
    private float _hoverTime;
    private int _hoverRevision = -1;
    private float _plannedScale = 1f;
    private Player? _player;
    private Player? _sessionOwner;
    private Rigidbody? _body;
    private ZNetView? _view;
    private LadderClearance? _clearance;
    private LadderGeometry _run;
    private Mode _mode;
    private LadderFace _face;
    private ClimbGait _gait;
    private Vector3[] _route = Array.Empty<Vector3>();
    private int _routeIndex;
    private float _laneDepth;
    private float _plane;
    private float _pitch;
    private float _gripHeight;
    private float _hangLowest;
    private float _transferStartHeight;
    private bool _gravityBefore;
    private bool _wrotePhysics;
    private float _cooldown;
    private float _poseSpeed;
    private float _topPlanTimer;
    private float _lastControlledHeight;
    private Vector3 _lastControlledPosition;
    private float _noticeCooldown;
    private float _compatibilityTimer;
    private int _mountFrame = -1;
    private bool _foremanConflict;
    private string _lastNotice = string.Empty;
    private bool _enabled = true;

    internal static LadderRuntime? Current { get; private set; }
    internal bool IsClimbing => _mode != Mode.Off;
    internal bool Ready => _motor.Ready;
    internal bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value && IsClimbing) { Release(Departure.Controlled); } }
    }

    internal LadderRuntime(Action<string> log, LadderSkill skill, LadderClimbAnimation animation)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _skill = skill ?? throw new ArgumentNullException(nameof(skill));
        _animation = animation ?? throw new ArgumentNullException(nameof(animation));
        _pose = new LadderWalkPose(log);
    }

    internal bool Install(string harmonyId)
    {
        if (Current != null && !ReferenceEquals(Current, this))
        {
            _log("Ladder climbing unavailable: another Concerned Ladders runtime is already active.");
            return false;
        }

        Current = this;
        return _motor.Install(harmonyId, _log);
    }

    internal void Update(float deltaTime)
    {
        float delta = LadderRules.FrameSeconds(deltaTime);
        _cooldown = Mathf.Max(0f, _cooldown - delta);
        _noticeCooldown = Mathf.Max(0f, _noticeCooldown - delta);
        _topPlanTimer = Mathf.Max(0f, _topPlanTimer - delta);
        // The carried skill fraction belongs to one character in one world session.
        if (!ReferenceEquals(Player.m_localPlayer, _sessionOwner))
        {
            _ledger.Reset();
            _skill.Forget();
            _sessionOwner = Player.m_localPlayer;
        }

        _animation.Update(delta);
        if (!IsClimbing) { return; }
        try
        {
            // Update also releases when the physics loop has stopped during world unload.
            if (_player == null || Player.m_localPlayer != _player || ZNetScene.instance == null || !Enabled ||
                _view == null || !_view.IsValid() || !_view.IsOwner() || !Free(_player, true))
            {
                Release(Departure.External);
                return;
            }

            _compatibilityTimer -= delta;
            if (_compatibilityTimer <= 0f)
            {
                _compatibilityTimer = 1f;
                _foremanConflict = _motor.HasForemanMotor();
                if (_foremanConflict)
                {
                    Notice("$cl_msg_foreman");
                    Release(Departure.Controlled);
                    return;
                }
            }

            bool takesInput = _motor.TakesInput(_player);
            // Vanilla routes Use only to the hovered object. A climber who looks away from the rungs (at a wall,
            // the sky, a blocked landing) would otherwise have no way to ask for a safe dismount. The player's own
            // bindings are read; a hovered interactable (including this ladder) keeps vanilla's routing. Jump wins
            // a press that arrives in the same frame, and the press that mounted cannot also dismount.
            if (_mode == Mode.Climbing && takesInput && UsePressed() && !JumpPressed() && Time.frameCount != _mountFrame &&
                !HoversInteractable(_player))
            {
                if (!TrySafeDismount()) { Notice("$cl_msg_nolanding"); }
            }

            // UpdateMotion no longer runs, so nothing else resets the locomotion floats: under the custom IK
            // pose the base layer is held at idle; without it, the walking fallback follows actual travel.
            _pose.Apply(_animation.Ready || !takesInput ? 0f : _poseSpeed);
        }
        catch (Exception exception) { Fault(exception); }
    }

    private static bool UsePressed() =>
        (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) && !Hud.InRadial();

    private static bool JumpPressed() => ZInput.GetButtonDown("Jump") || ZInput.GetButtonDown("JoyJump");

    private static bool HoversInteractable(Player player)
    {
        GameObject? hover = player.GetHoverObject();
        return hover != null && hover.GetComponentInParent<Interactable>() != null;
    }

    /// <summary>The same reach test the Use press applies, for the hover prompt: chest to the connected
    /// run's usable span. The run is rebuilt only when the hovered section changes or every half second.</summary>
    internal bool InReach(LadderSegment segment)
    {
        try
        {
            Player? player = Player.m_localPlayer;
            if (player == null || segment == null) { return false; }
            if (!ReferenceEquals(segment, _hoverSegment) || _hoverRevision != LadderSegment.Revision || Time.time - _hoverTime > 0.5f)
            {
                _hoverSegment = segment;
                _hoverRevision = LadderSegment.Revision;
                _hoverTime = Time.time;
                _hoverValid = LadderSegment.TryRun(segment, _hoverParts, out _hoverRun);
                _hoverParts.Clear();
            }

            CapsuleCollider capsule = player.GetCollider();
            if (!_hoverValid || capsule == null) { return false; }
            float height = capsule.height * Mathf.Abs(capsule.transform.lossyScale.y);
            // Native ground contact stands in for the swept support test here: hover runs every frame.
            return ClimbPolicy.InReach(Reach(_hoverRun, segment, player.transform.position, height, player.IsOnGround()));
        }
        catch { return false; }
    }

    private static float Reach(in LadderGeometry geometry, LadderSegment segment, Vector3 feet, float capsuleHeight, bool supported)
    {
        Vector3 chest = feet + Vector3.up * (capsuleHeight * ClimbPolicy.ChestFraction);
        float plane = (segment.BackFaceDepth + segment.FrontFaceDepth) * 0.5f;
        return ClimbPolicy.MountReach(geometry, LadderSegment.Point(feet), LadderSegment.Point(chest), plane,
            segment.RungPitch * 0.5f, supported);
    }

    internal bool Use(LadderSegment segment)
    {
        try
        {
            Player? player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) { return false; }
            if (!_motor.Ready)
            {
                Notice("$cl_msg_startup");
                return true;
            }

            if (!Enabled)
            {
                // Observed in game: a deliberate setting change was being reported as a startup failure.
                Notice("$cl_msg_disabled");
                return true;
            }

            if (!_motor.TakesInput(player)) { return false; }
            if (IsClimbing)
            {
                if (Time.frameCount != _mountFrame && !JumpPressed() && !TrySafeDismount()) { Notice("$cl_msg_nolanding"); }
                return true;
            }

            // Jump takes precedence over a Use press that arrives in the same frame.
            if (JumpPressed()) { return true; }

            _foremanConflict = _motor.HasForemanMotor();
            if (_foremanConflict)
            {
                Notice("$cl_msg_foreman");
                return true;
            }

            Rigidbody body = player.GetComponent<Rigidbody>();
            ZNetView view = player.GetComponent<ZNetView>();
            CapsuleCollider capsule = player.GetCollider();
            if (body == null || body.isKinematic || view == null || !view.IsValid() || capsule == null ||
                !ClimbPolicy.MayAdmit(view.IsOwner(), Free(player), false, _cooldown))
            {
                Notice(player.InPlaceMode() ? "$cl_msg_buildtool" : "$cl_msg_unavailable");
                return true;
            }

            if (!LadderSegment.TryRun(segment, _segments, out LadderGeometry run))
            {
                Notice("$cl_msg_invalidrun");
                return true;
            }

            var clearance = new LadderClearance(player, capsule, body.position);
            clearance.SetRun(_segments);
            if (!TryPlanMount(player, body, clearance, run, segment, out Plan plan, out ClimbRefusal refusal, out string detail))
            {
                Refuse(refusal, clearance);
                _log("Ladder mount refused: " + refusal + " (" + detail + ").");
                _segments.Clear();
                return true;
            }

            Commit(player, body, view, clearance, run, segment, plan);
            return true;
        }
        catch (Exception exception) { Fault(exception); return true; }
    }

    private struct Plan
    {
        internal LadderFace Face;
        internal float Lane;
        internal float Height;
        internal Vector3[] Route;
        internal bool Airborne;
        internal float Reach;
    }

    /// <summary>Every check happens here, before anything about the player changes: a refused grab leaves
    /// velocity, gravity, fall reference, pose, stamina and skill untouched.</summary>
    private bool TryPlanMount(Player player, Rigidbody body, LadderClearance clearance, in LadderGeometry run,
        LadderSegment segment, out Plan plan, out ClimbRefusal refusal, out string detail)
    {
        plan = default;
        refusal = ClimbRefusal.None;
        Vector3 feet = body.position;
        // Standing or airborne is the game's own ground contact, the same reading the hover prompt uses, so a
        // body a few centimetres above a floor is airborne here too and keeps the chest reach and air limits.
        bool supported = player.IsOnGround();
        float reach = Reach(run, segment, feet, clearance.Height, supported);
        float grip = clearance.Height * ClimbPolicy.GripFraction;
        float firstRung = segment.RungPitch * 0.5f;
        float hangLowest = ClimbPolicy.LowestHang(run.Bottom.Y, firstRung, grip);
        float depth = run.ReachFrom(LadderSegment.Point(feet));
        LadderFace approach = ClimbPolicy.FaceOf(depth, segment.BackFaceDepth, segment.FrontFaceDepth);
        detail = "reach=" + reach.ToString("0.00", Invariant) + " supported=" + supported + " vy=" +
                 body.linearVelocity.y.ToString("0.00", Invariant) + " face=" + approach + " height=" +
                 (feet.y - run.Bottom.Y).ToString("0.00", Invariant);
        if (!ClimbPolicy.InReach(reach))
        {
            refusal = ClimbRefusal.OutOfReach;
            return false;
        }

        // Standing at the top on a landing, the usable face is the one with room to climb down: the approach
        // side there is usually the floor itself. Everywhere else the climber stays on the face they reached.
        bool topEntry = supported && feet.y >= run.Top.Y - ClimbPolicy.TopEntryBelow;
        LadderFace[] faces = topEntry ? new[] { approach, Opposite(approach) } : new[] { approach };
        Vector3 chest = feet + Vector3.up * (clearance.Height * ClimbPolicy.ChestFraction);
        ClimbRefusal first = ClimbRefusal.None;
        foreach (LadderFace face in faces)
        {
            clearance.ClearBlocker();
            float lane = ClimbPolicy.LaneDepth(face, clearance.Radius, segment.BackFaceDepth, segment.FrontFaceDepth);
            float target = supported ? Mathf.Clamp(feet.y, hangLowest, run.Top.Y) : ClimbPolicy.GrabHeight(feet.y, hangLowest, run.Top.Y);
            Vector3 line = LanePoint(run, lane, target);
            if (supported)
            {
                if (target - feet.y > LadderRules.MaximumStepUpToFoot) { first = Keep(first, ClimbRefusal.OutOfReach); continue; }
                // On rising ground the climb line's own floor can be above the start height; end the route on it, never inside it.
                if (clearance.TryFloorBelow(line + Vector3.up * LadderRules.StepLift, LadderRules.StepLift + 0.2f, out float lineFloor) &&
                    lineFloor > line.y && lineFloor <= run.Top.Y)
                {
                    line.y = lineFloor;
                }
            }
            else
            {
                float horizontal = new Vector2(line.x - feet.x, line.z - feet.z).magnitude;
                if (!ClimbPolicy.AirAlignmentAllowed(horizontal, target - feet.y)) { first = Keep(first, ClimbRefusal.OutOfReach); continue; }
            }

            // The hands must reach the rungs without passing through a wall or floor.
            Vector3 rung = LanePoint(run, (segment.BackFaceDepth + segment.FrontFaceDepth) * 0.5f, Mathf.Clamp(chest.y, run.Bottom.Y + firstRung, run.Top.Y - firstRung));
            if (!clearance.HasLineOfSight(chest, rung)) { first = Keep(first, FromBlocker(clearance.LastBlocker)); continue; }

            if (topEntry && !clearance.HasRoom(LanePoint(run, lane, Mathf.Max(run.Bottom.Y, run.Top.Y - 1f))))
            {
                first = Keep(first, FromBlocker(clearance.LastBlocker));
                continue;
            }

            // Beside or behind the rails (the owner's photographed approaches), a straight leg onto the lane
            // clips the rails' end. The second shape first steps out to the lane's depth at the current
            // sideways offset, then slides across onto the lane; both are swept with the real capsule.
            Vector3 aside = DepthFirst(run, lane, feet, line);
            Vector3[]? route = null;
            if (supported)
            {
                // Observed in game: an off-centre approach over ground that rises toward the wall blocked the 6 cm
                // lateral leg. A step-height lift is tried next; every leg is still swept with the real capsule.
                foreach (float lift in new[] { LadderRules.RouteLift, LadderRules.StepLift })
                {
                    float raised = Mathf.Max(feet.y, line.y) + lift;
                    Vector3[] direct =
                    {
                        new Vector3(feet.x, raised, feet.z),
                        new Vector3(line.x, raised, line.z),
                        line,
                    };
                    Vector3[] around =
                    {
                        new Vector3(feet.x, raised, feet.z),
                        new Vector3(aside.x, raised, aside.z),
                        new Vector3(line.x, raised, line.z),
                        line,
                    };
                    if (clearance.CanFollow(feet, direct)) { route = direct; break; }
                    if (clearance.CanFollow(feet, around)) { route = around; break; }
                }
            }
            else if (clearance.CanMove(feet, line))
            {
                route = new[] { line };
            }
            else if (clearance.CanFollow(feet, new[] { new Vector3(aside.x, feet.y, aside.z), line }))
            {
                route = new[] { new Vector3(aside.x, feet.y, aside.z), line };
            }

            if (route == null) { first = Keep(first, FromBlocker(clearance.LastBlocker)); continue; }
            plan = new Plan { Face = face, Lane = lane, Height = target, Route = route, Airborne = !supported, Reach = reach };
            return true;
        }

        refusal = first == ClimbRefusal.None ? ClimbRefusal.Obstacle : first;
        detail += " blocker=" + clearance.LastBlocker + ":" + clearance.LastBlockerName;
        return false;
    }

    /// <summary>The point at the lane's depth with the body's current sideways offset from the run.</summary>
    private static Vector3 DepthFirst(in LadderGeometry run, float lane, Vector3 feet, Vector3 line)
    {
        Vector3 outward = new Vector3(run.StandingSide.X, 0f, run.StandingSide.Z);
        Vector3 offset = feet - line;
        offset.y = 0f;
        Vector3 sideways = offset - outward * Vector3.Dot(offset, outward);
        return new Vector3(line.x + sideways.x, line.y, line.z + sideways.z);
    }

    private static ClimbRefusal Keep(ClimbRefusal first, ClimbRefusal next) => first == ClimbRefusal.None ? next : first;

    private static LadderFace Opposite(LadderFace face) => face == LadderFace.Front ? LadderFace.Rear : LadderFace.Front;

    private static ClimbRefusal FromBlocker(BlockerKind kind) => kind switch
    {
        BlockerKind.Building => ClimbRefusal.Building,
        BlockerKind.OwnLadder => ClimbRefusal.OwnLadder,
        BlockerKind.Terrain => ClimbRefusal.Terrain,
        BlockerKind.Actor => ClimbRefusal.Actor,
        BlockerKind.Overflow => ClimbRefusal.Unchecked,
        _ => ClimbRefusal.Obstacle,
    };

    private static Vector3 LanePoint(in LadderGeometry run, float lane, float height)
    {
        Vector3 point = LadderSegment.Vector(run.ClimbPositionAt(height - run.Bottom.Y, lane));
        point.y = height;
        return point;
    }

    private void Commit(Player player, Rigidbody body, ZNetView view, LadderClearance clearance, in LadderGeometry run,
        LadderSegment segment, Plan plan)
    {
        _player = player;
        _body = body;
        _view = view;
        _clearance = clearance;
        _run = run;
        _face = plan.Face;
        _laneDepth = plan.Lane;
        _plane = (segment.BackFaceDepth + segment.FrontFaceDepth) * 0.5f;
        _pitch = segment.RungPitch;
        _gripHeight = clearance.Height * ClimbPolicy.GripFraction;
        _hangLowest = ClimbPolicy.LowestHang(run.Bottom.Y, segment.RungPitch * 0.5f, _gripHeight);
        _gait = ClimbGait.Hold;
        _gravityBefore = body.useGravity;
        _wrotePhysics = false;
        _lastControlledHeight = body.position.y;
        _lastControlledPosition = body.position;
        _positions.Clear();
        _rotations.Clear();
        foreach (LadderSegment part in _segments)
        {
            _positions.Add(part.transform.position);
            _rotations.Add(part.transform.rotation);
        }

        _route = plan.Route;
        _routeIndex = 0;
        _mode = Mode.Mounting;
        _mountFrame = Time.frameCount;
        _compatibilityTimer = 1f;
        _ledger.Discard();
        if (plan.Airborne)
        {
            // The catch arrests this fall once; a later release falls from where the climber actually is.
            _motor.SetFallReference(player, body.position.y);
        }

        _log("Ladder mounted: " + _segments.Count + " section(s), " + run.Height.ToString("0.00", Invariant) +
             " m run, " + plan.Face + " face, " + (plan.Airborne ? "air catch" : "from a landing") + " at " +
             (plan.Height - run.Bottom.Y).ToString("0.00", Invariant) + " m, reach " + plan.Reach.ToString("0.00", Invariant) + " m.");
        _pose.Begin(player);
        Present(true);
    }

    internal bool Drive(Character character, float seconds)
    {
        if (!IsClimbing || _player == null || !ReferenceEquals(character, _player)) { return false; }
        try
        {
            Player player = _player;
            Rigidbody? body = _body;
            LadderClearance? clearance = _clearance;
            bool owned = _view != null && _view.IsValid() && _view.IsOwner();
            bool worldPresent = ZNetScene.instance != null && ReferenceEquals(Player.m_localPlayer, player);
            bool present = _segments.Count > 0;
            bool unmoved = true;
            for (int index = 0; index < _segments.Count; index++)
            {
                LadderSegment segment = _segments[index];
                if (segment == null || !segment.TryGeometry(out _)) { present = false; break; }
                if (Vector3.Distance(segment.transform.position, _positions[index]) > 0.01f ||
                    Quaternion.Angle(segment.transform.rotation, _rotations[index]) > 0.25f)
                {
                    unmoved = false;
                    break;
                }
            }

            float drift = body != null ? Vector3.Distance(body.position, _lastControlledPosition) : float.NaN;
            bool free = Free(player, true);
            if (body == null || body.isKinematic || clearance == null || !clearance.IsUnchanged() ||
                LadderRules.MustRelease(Enabled && !_foremanConflict, owned, !player.IsDead(), free, worldPresent,
                    present, unmoved, drift))
            {
                // A teleport, knockback, death or authority change owns its new velocity/fall state.
                bool external = LadderRules.IsExternalRelease(owned, !player.IsDead(), free, worldPresent,
                    body == null || body.isKinematic, drift);
                Release(external ? Departure.External : Departure.Controlled);
                return false;
            }

            float delta = LadderRules.FrameSeconds(seconds);
            if (delta <= 0f) { Release(Departure.Controlled); return false; }
            SettlePending(player, body.position.y, delta);
            // Opening a door or hatch from the ladder plays the interact animation, a minor action. The
            // climber holds still on the rungs for its duration rather than being let go to fall.
            if (!_motor.TakesInput(player) || player.InMinorAction())
            {
                Hold(body.position, seconds, 0f, LadderRules.Speed);
                Present(true);
                return true;
            }

            if (_mode == Mode.Mounting || _mode == Mode.Exiting)
            {
                if (_mode == Mode.Exiting && ReadInput(player) < -0.15f) { ReturnToLadder(); }
                MoveRoute(delta, seconds);
                Present(_mode != Mode.Exiting);
                return IsClimbing;
            }

            float input = ReadInput(player);
            float level = _skill.LearnedLevel(player, delta);
            float scale = StaminaScale(player);
            ClimbGait gait = ClimbPolicy.SelectGait(input, _motor.RunIntent(player), level, player.GetStamina(), delta,
                scale * Game.m_staminaRate);
            float velocity = ClimbPolicy.StepSpeed(gait, input);
            _gait = gait;
            // Without footing the hands can still hold the lowest rung: the body hangs that far below the foot.
            float lowest = _hangLowest;
            if (velocity < 0f && body.position.y <= _run.Bottom.Y + LadderRules.BottomExitBand &&
                clearance.TryFloorBelow(body.position, LadderRules.MaximumStepUpToFoot + 0.3f, out float floorBelow) &&
                floorBelow >= _run.Bottom.Y - LadderRules.MaximumStepUpToFoot)
            {
                lowest = Mathf.Max(lowest, floorBelow);
            }

            float nextHeight = LadderRules.Advance(body.position.y, velocity, delta, lowest, _run.Top.Y);
            Vector3 target = LanePoint(_run, _laneDepth, nextHeight);
            if (velocity > 0f && body.position.y >= _run.Top.Y - TopPlanBand && _topPlanTimer <= 0f)
            {
                _topPlanTimer = 0.2f;
                if (TryExitTop()) { MoveRoute(delta, seconds); Present(false); return IsClimbing; }
            }

            if (velocity > 0f && body.position.y >= _run.Top.Y - 0.03f)
            {
                Hold(body.position, seconds, 0f, LadderRules.Speed);
                Notice("$cl_msg_notop");
                Present(true);
                return true;
            }

            // Descending onto real support near the foot is the bottom exit. A standing player's feet rest on the
            // floor, which physics keeps above the run's nominal foot, so an exact-height test alone never fires.
            if (velocity < 0f && body.position.y <= _run.Bottom.Y + LadderRules.BottomExitBand &&
                clearance.Supported(body.position) && clearance.HasRoom(body.position))
            {
                Release(Departure.Controlled);
                return false;
            }

            if (velocity < 0f && body.position.y <= lowest + 0.03f)
            {
                Hold(body.position, seconds, 0f, LadderRules.Speed);
                Notice("$cl_msg_nofooting");
                Present(true);
                return true;
            }

            clearance.ClearBlocker();
            if (!clearance.CanMove(body.position, target))
            {
                // Uneven ground can rise above the run's foot. Already standing on it is the bottom exit.
                if (velocity < 0f && clearance.Supported(body.position) && clearance.HasRoom(body.position))
                {
                    Release(Departure.Controlled);
                    return false;
                }

                BlockerKind blocker = clearance.LastBlocker;
                string name = clearance.LastBlockerName;
                // A lip or roof edge overhead near the top: the floor beyond it may still be walkable.
                if (velocity > 0f && body.position.y >= _run.Top.Y - TopBlockedBand && _topPlanTimer <= 0f)
                {
                    _topPlanTimer = 0.2f;
                    if (TryExitTop()) { MoveRoute(delta, seconds); Present(false); return IsClimbing; }
                }

                Hold(body.position, seconds, 0f, LadderRules.Speed);
                Refuse(FromBlocker(blocker), blocker, name);
                Present(true);
                return true;
            }

            _ledger.Plan(gait, body.position.y, nextHeight - body.position.y);
            _plannedScale = scale; // The step is charged at the basis it was budgeted with.
            Hold(target, seconds, (target.y - body.position.y) / seconds, Mathf.Max(LadderRules.Speed, ClimbPolicy.SpeedOf(gait)));
            Present(true);
            return true;
        }
        catch (Exception exception) { Fault(exception); return false; }
    }

    /// <summary>Pays the previous paid step once, from the body's observed travel; skill metres go to the owner.
    /// Every voluntary exit, release and jump settles first, so completed travel is never free.</summary>
    private void SettlePending(Player player, float height, float delta)
    {
        if (!_ledger.Pending) { return; }
        bool owned = _view != null && _view.IsValid() && _view.IsOwner();
        Rigidbody? body = _body;
        // Only the motor's own step is paid for: a body moved away from where that step was sent (a shove,
        // a teleport, a takeover), a kinematic body or a dead one settles nothing.
        bool ours = body != null && !body.isKinematic && !player.IsDead() &&
                    Vector3.Distance(body.position, _lastControlledPosition) <= 0.25f;
        if (!owned || !ours) { _ledger.Discard(); return; }
        _ledger.Settle(height, _skill.LearnedLevel(player, delta), out float stamina, out int metres);
        // UseStamina applies the world stamina rate itself; the movement rate and status effects apply here,
        // at the basis the step was budgeted with.
        float charge = stamina * _plannedScale;
        if (ClimbPolicy.Finite(charge) && charge > 0f) { player.UseStamina(charge); }
        if (metres > 0) { _skill.Raise(player, metres); }
    }

    /// <summary>The native multipliers a climb charge sees before UseStamina's own world rate: the world's
    /// movement stamina rate and the run-drain status effects (potions, set bonuses), applied once.</summary>
    private static float StaminaScale(Player player)
    {
        float factor = 1f;
        try { player.GetSEMan()?.ModifyRunStaminaDrain(1f, ref factor, Vector3.up); }
        catch { factor = 1f; }
        float scale = Game.m_moveStaminaRate * factor;
        return ClimbPolicy.Finite(scale) && scale >= 0f ? scale : 1f;
    }

    private void Present(bool attached)
    {
        if (_player == null) { return; }
        _animation.SetLocal(_player, _run, _face, _plane, _pitch, _gripHeight, _gait == ClimbGait.Slide && _mode == Mode.Climbing, attached);
        if (_animation.Ready) { _poseSpeed = 0f; }
    }

    internal bool Jump(Character character)
    {
        if (!IsClimbing || _player == null || !ReferenceEquals(character, _player)) { return false; }
        try
        {
            if (_motor.TakesInput(_player)) { JumpOff(_player); }
            return true;
        }
        catch (Exception exception) { Fault(exception); return true; }
    }

    /// <summary>Jump from every attached mode: settle the climb, choose a camera-relative departure (neutral is
    /// away from the rails), let go, then apply the native jump once. Without the stamina for a jump the climber
    /// still lets go, pushed just clear of the rails, and falls normally.</summary>
    private void JumpOff(Player player)
    {
        Rigidbody body = _body!;
        SettlePending(player, body.position.y, Time.fixedDeltaTime);
        Vector3 away = Away();
        Vector3 move = _motor.MoveDirection(player);
        ClimbPolicy.DepartureDirection(move.x, move.z, away.x, away.z, out float x, out float z);
        Vector3 direction = new Vector3(x, 0f, z);
        if (direction != away && _clearance != null &&
            !_clearance.CanMove(body.position, body.position + direction * 0.5f + Vector3.up * 0.3f))
        {
            direction = away; // That way is immediately blocked: depart away from the rails instead.
        }

        bool powered = !player.IsEncumbered() && player.HaveStamina(player.m_jumpStaminaUsage) && player.GetSEMan() != null;
        if (!powered && Hud.instance != null) { Hud.instance.StaminaBarEmptyFlash(); }
        Release(powered ? Departure.Jump : Departure.Push, direction);
    }

    private Vector3 Away()
    {
        Vector3 standing = new Vector3(_run.StandingSide.X, 0f, _run.StandingSide.Z);
        return _face == LadderFace.Front ? standing : -standing;
    }

    private Vector3 Toward() => -Away();

    /// <summary>A bounded set of floors around the run's top: straight over the rails first, then a landing to
    /// either side (a ladder set in a slot between two floors, as the owner photographed), then behind the
    /// climber. Each candidate needs room, support and a fully swept rise/cross/settle route.</summary>
    private bool TryExitTop()
    {
        Vector3 inward = Toward();
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3 top = LadderSegment.Vector(_run.Top);
        Vector3 lane = LanePoint(_run, _laneDepth, _run.Top.Y);
        float radius = _clearance!.Radius;
        foreach (float distance in new[] { radius + 0.45f, radius + 0.85f })
        {
            if (TryTopLanding(top + inward * distance)) { return true; }
        }

        float sideways = _run.Width * 0.5f + radius + 0.15f;
        foreach (float sign in new[] { 1f, -1f })
        {
            foreach (float distance in new[] { sideways, sideways + 0.4f })
            {
                if (TryTopLanding(lane + side * (sign * distance)) || TryTopLanding(top + side * (sign * distance))) { return true; }
            }
        }

        return TryTopLanding(lane + Away() * 0.6f);
    }

    private bool TryLandingNear(Vector3 desired) => _clearance!.TryNearbySurface(desired, out Vector3 floor) && PlanExitTo(floor);

    private bool TryTopLanding(Vector3 desired) => _clearance!.TryTopSurface(desired, out Vector3 floor) && PlanExitTo(floor);

    private bool TrySafeDismount()
    {
        if (_player != null) { SettlePending(_player, _body!.position.y, Time.fixedDeltaTime); }
        Vector3 feet = _body!.position;
        if (_clearance!.Supported(feet) && _clearance.HasRoom(feet)) { Release(Departure.Controlled); return true; }
        if (feet.y >= _run.Top.Y - 0.5f && TryExitTop()) { return true; }
        // A landing beside or behind the climber, at about the height the feet are at now. The candidates
        // are the top exit's: far enough to the side that a whole standing footprint clears the rails (a
        // fixed 0.75 m probe put the footprint half over the rails, so a real side landing was refused -
        // observed in game on 0.2.0, T16), then out from the face. Each still needs room, a supported
        // footprint and a fully swept route.
        Vector3 outward = Away();
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        float radius = _clearance.Radius;
        float sideways = _run.Width * 0.5f + radius + 0.15f;
        foreach (float sign in new[] { 1f, -1f })
        {
            foreach (float distance in new[] { sideways, sideways + 0.4f })
            {
                if (TryLandingNear(feet + side * (sign * distance))) { return true; }
            }
        }

        foreach (float distance in new[] { radius + 0.45f, radius + 0.85f })
        {
            if (TryLandingNear(feet + outward * distance)) { return true; }
        }

        return false;
    }

    /// <summary>Rise, cross, settle: each leg swept with the real capsule, two lift heights tried. Where a lip
    /// or roof edge overhangs the climb lane, a clearance leg first steps back from the rails (up to half a
    /// metre) so the head can rise past the edge before crossing onto the floor. The destination needs room
    /// and a supported footprint, not a single support ray.</summary>
    private bool PlanExitTo(Vector3 floor)
    {
        if (!_clearance!.HasRoom(floor) || !_clearance.SupportedFootprint(floor)) { return false; }
        Vector3 feet = _body!.position;
        Vector3 away = Away();
        foreach (float back in new[] { 0f, 0.25f, 0.5f })
        foreach (float lift in new[] { LadderRules.RouteLift, LadderRules.StepLift })
        {
            float raised = Mathf.Max(feet.y, floor.y) + lift;
            Vector3 clear = feet + away * back;
            Vector3[] path = back <= 0f
                ? new[] { new Vector3(feet.x, raised, feet.z), new Vector3(floor.x, raised, floor.z), floor }
                : new[] { clear, new Vector3(clear.x, raised, clear.z), new Vector3(floor.x, raised, floor.z), floor };
            if (!_clearance.CanFollow(feet, path)) { continue; }
            _route = path;
            _routeIndex = 0;
            _transferStartHeight = Mathf.Clamp(feet.y, _hangLowest, _run.Top.Y);
            _mode = Mode.Exiting;
            _ledger.Discard();
            return true;
        }

        return false;
    }

    private void ReturnToLadder()
    {
        Vector3 current = _body!.position;
        Vector3 line = LanePoint(_run, _laneDepth, _transferStartHeight);
        float raised = Mathf.Max(current.y, line.y) + LadderRules.RouteLift;
        Vector3[] route =
        {
            new Vector3(current.x, raised, current.z),
            new Vector3(line.x, raised, line.z),
            line,
        };
        if (!_clearance!.CanFollow(current, route)) { return; }
        _mode = Mode.Mounting;
        _route = route;
        _routeIndex = 0;
    }

    /// <summary>The last leg of a route lowers the body onto its destination. Ground contact holds a standing
    /// capsule a few centimetres above a computed point, so that leg ends once the body is horizontally there
    /// and within the route's own lift; requiring 2 cm vertically left a mount on terrain unfinishable.</summary>
    private static bool Arrived(Vector3 body, Vector3 waypoint, bool last)
    {
        float dx = body.x - waypoint.x, dz = body.z - waypoint.z;
        float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
        return last
            ? horizontal < 0.02f && Mathf.Abs(body.y - waypoint.y) <= LadderRules.RouteLift + 0.02f
            : Vector3.Distance(body, waypoint) < 0.02f;
    }

    private void MoveRoute(float delta, float physicsSeconds)
    {
        Rigidbody body = _body!;
        LadderClearance clearance = _clearance!;
        if (_mode == Mode.Exiting && !clearance.SupportedFootprint(_route[_route.Length - 1]))
        {
            Release(Departure.Controlled); // A removed platform is never replaced by its cached destination.
            return;
        }
        while (_routeIndex < _route.Length && Arrived(body.position, _route[_routeIndex], _routeIndex == _route.Length - 1))
        {
            _routeIndex++;
        }

        if (_routeIndex >= _route.Length)
        {
            if (_mode == Mode.Exiting)
            {
                Release(Departure.Controlled);
                return;
            }

            _mode = Mode.Climbing;
            Hold(body.position, physicsSeconds, 0f, LadderRules.Speed);
            return;
        }

        Vector3 target = Vector3.MoveTowards(body.position, _route[_routeIndex], LadderRules.Speed * delta);
        clearance.ClearBlocker();
        if (!clearance.CanMove(body.position, target))
        {
            Hold(body.position, physicsSeconds, 0f, LadderRules.Speed);
            Refuse(FromBlocker(clearance.LastBlocker), clearance.LastBlocker, clearance.LastBlockerName);
            return;
        }

        // Transfer legs are mostly sideways: drive the walk cycle from the real path speed, not its vertical part.
        float pathSpeed = Vector3.Distance(target, body.position) / physicsSeconds;
        Hold(target, physicsSeconds, target.y < body.position.y - 0.001f ? -pathSpeed : pathSpeed, LadderRules.Speed);
    }

    private void Hold(Vector3 target, float delta, float poseSpeed, float maximumSpeed)
    {
        Rigidbody body = _body!;
        if (!LadderSegment.Finite(target) || !LadderSegment.Finite(body.position))
        {
            throw new InvalidOperationException("The climb target was not finite.");
        }

        Vector3 velocity = Vector3.ClampMagnitude((target - body.position) / delta, maximumSpeed);
        body.useGravity = false;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        _wrotePhysics = true;
        body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(Toward()), 360f * LadderRules.FrameSeconds(delta)));
        _lastControlledHeight = body.position.y;
        _lastControlledPosition = target;
        _motor.SetFallReference(_player!, body.position.y);
        _poseSpeed = poseSpeed;
    }

    /// <summary>Forward/backward relative to the camera's horizontal view: forward climbs, whatever the pitch.</summary>
    private float ReadInput(Player player)
    {
        Vector3 move = _motor.MoveDirection(player);
        Vector3 look = player.GetLookDir();
        look.y = 0f;
        if (!LadderSegment.Finite(move) || !LadderSegment.Finite(look)) { return 0f; }
        if (look.sqrMagnitude < 0.001f) { look = Toward(); }
        return LadderRules.Input(Vector3.Dot(move, look.normalized), false);
    }

    /// <summary>Mounting needs a completely free player. An active climb also survives a minor action
    /// (the interact animation of opening a door or hatch), which holds the climb instead.</summary>
    private static bool Free(Player player, bool minorActionAllowed = false) =>
        !player.IsDead() && !player.IsTeleporting() && !player.IsAttached() && !player.InBed() &&
        !player.IsRiding() && !player.InCutscene() && !player.IsSwimming() && !player.IsDebugFlying() &&
        !player.InPlaceMode() && !player.InAttack() && !player.InDodge() && !player.IsKnockedBack() &&
        !player.IsStaggering() && (minorActionAllowed || !player.InMinorAction());

    private enum Departure { Controlled, External, Jump, Push }

    private void Release(Departure departure) => Release(departure, Vector3.zero);

    private void Release(Departure departure, Vector3 direction)
    {
        Player? player = _player;
        Rigidbody? body = _body;
        LadderClearance? clearance = _clearance;
        Mode was = _mode;
        _mode = Mode.Off; // Release the motor before querying any native state that might already be destroyed.
        if (was != Mode.Off)
        {
            // One line per release, never per frame: which kind of release, from which stage.
            try { _log("Ladder released: " + departure + " from " + was + " (" + _gait + ")."); }
            catch { /* Logging cannot keep the motor captured. */ }
        }
        bool owned = false;
        try { owned = _view != null && _view.IsValid() && _view.IsOwner(); } catch { /* No verified authority, no writes. */ }
        // Whatever asked for this release (a setting change, a dismount, a jump), a body that something else has
        // already moved away from the motor's last command, made kinematic or killed belongs to that controller:
        // its velocity, gravity and fall reference are left exactly as they are (Codex review of 4926903).
        if (departure != Departure.External)
        {
            try
            {
                if (body == null || body.isKinematic || player == null || player.IsDead() ||
                    Vector3.Distance(body.position, _lastControlledPosition) > TakeoverDistance)
                {
                    departure = Departure.External;
                }
            }
            catch { departure = Departure.External; }
        }
        _cooldown = ClimbPolicy.RemountDelay;
        _poseSpeed = 0f;
        _gait = ClimbGait.Hold;
        if (departure == Departure.External || !owned || player == null || body == null) { _ledger.Discard(); }
        else
        {
            try { SettlePending(player, body.position.y, Time.fixedDeltaTime); }
            catch { _ledger.Discard(); }
        }
        try
        {
            if (owned && body != null && (_wrotePhysics || departure == Departure.Jump || departure == Departure.Push))
            {
                // An external release writes nothing at all, gravity included: vanilla's own motor sets gravity
                // on every tick it runs (UpdateWalking/UpdateSwimming), and whatever took over owns the rest.
                if (_wrotePhysics && departure != Departure.External) { body.useGravity = _gravityBefore; }
                if (departure != Departure.External && player != null)
                {
                    body.angularVelocity = Vector3.zero;
                    _motor.SetFallReference(player, Mathf.Max(body.position.y, _lastControlledHeight));
                    if (departure == Departure.Jump)
                    {
                        PoweredJump(player, body, direction);
                    }
                    else
                    {
                        Vector3 push = departure == Departure.Push ? direction * ClimbPolicy.ReleasePushSpeed : Vector3.zero;
                        body.linearVelocity = push;
                        bool standing = departure == Departure.Controlled && clearance != null && clearance.Supported(body.position);
                        if (!standing) { _motor.BeginAirborne(player, push); }
                    }
                }
            }
        }
        finally
        {
            try { _pose.Clear(owned && departure != Departure.External); } catch { /* Never overwrite a takeover's animation state. */ }
            try { _animation.ReleaseLocal(player); } catch { /* Presentation cannot keep the motor captured. */ }
            _player = null;
            _body = null;
            _view = null;
            _clearance = null;
            _wrotePhysics = false;
            _segments.Clear();
            _positions.Clear();
            _rotations.Clear();
            _route = Array.Empty<Vector3>();
        }
    }

    /// <summary>Native jump strength, skill bonus, status-effect modifiers, stamina and effects, applied once:
    /// vanilla's own ForceJump runs OnJump, which pays the jump stamina, and sets the body airborne.</summary>
    private void PoweredJump(Player player, Rigidbody body, Vector3 direction)
    {
        float bonus = 1f + player.GetSkillFactor(Skills.SkillType.Jump) * 0.4f;
        Vector3 jump = Vector3.up * (player.m_jumpForce * bonus) +
                       direction * ((ClimbPolicy.JumpDepartureSpeed + player.m_jumpForceForward) * bonus);
        player.GetSEMan().ApplyStatusEffectJumpMods(ref jump);
        if (!LadderSegment.Finite(jump)) { body.linearVelocity = Vector3.zero; _motor.BeginAirborne(player, Vector3.zero); return; }
        player.RaiseSkill(Skills.SkillType.Jump);
        player.ForceJump(jump);
        _motor.SetWalkVelocity(player, jump);
        _log("Ladder jump: up " + jump.y.ToString("0.0", Invariant) + " m/s, away " +
             new Vector2(jump.x, jump.z).magnitude.ToString("0.0", Invariant) + " m/s.");
    }

    private void Fault(Exception exception)
    {
        try { Release(Departure.External); }
        catch { _mode = Mode.Off; _player = null; _body = null; }
        try { _log("Ladder traversal stopped (" + SafeFailure.Brief(exception) + "). Normal control was released."); }
        catch { /* Logging cannot keep the motor captured. */ }
    }

    private void Refuse(ClimbRefusal refusal, LadderClearance clearance) =>
        Refuse(refusal, clearance.LastBlocker, clearance.LastBlockerName);

    /// <summary>One message per reason. Only a hit on another real building piece names a building.</summary>
    private void Refuse(ClimbRefusal refusal, BlockerKind blocker, string name)
    {
        switch (refusal)
        {
            case ClimbRefusal.OutOfReach: Notice("$cl_msg_reach"); break;
            case ClimbRefusal.Unavailable: Notice("$cl_msg_unavailable"); break;
            case ClimbRefusal.Building:
                Notice(blocker == BlockerKind.Building && name.Length > 0 ? "$cl_msg_building " + name : "$cl_msg_building");
                break;
            case ClimbRefusal.OwnLadder: Notice("$cl_msg_ownladder"); break;
            case ClimbRefusal.Terrain: Notice("$cl_msg_terrain"); break;
            case ClimbRefusal.Actor: Notice("$cl_msg_actor"); break;
            case ClimbRefusal.Unchecked: Notice("$cl_msg_unchecked"); break;
            case ClimbRefusal.NoLanding: Notice("$cl_msg_nolanding"); break;
            default: Notice("$cl_msg_obstacle"); break;
        }
    }

    private void Notice(string message)
    {
        if (message == _lastNotice && _noticeCooldown > 0f) { return; }
        _lastNotice = message;
        _noticeCooldown = 2f;
        MessageHud? hud = MessageHud.instance;
        if (hud == null) { return; }
        string text = Localization.instance != null ? Localization.instance.Localize(message) : message;
        hud.ShowMessage(MessageHud.MessageType.Center, text);
    }

    public void Dispose()
    {
        try { Release(Departure.Controlled); }
        finally
        {
            _motor.Dispose();
            if (ReferenceEquals(Current, this)) { Current = null; }
        }
    }
}
