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
    private const float TopPlanBand = 1.3f;

    /// <summary>Descending entry ends this far below the run's top (hands on the top rungs, feet on real rungs).</summary>
    private const float TopEntryDrop = 1.3f;

    /// <summary>A descending entry checks its line of sight to this height above the run's top (above a floor that
    /// sits a little over the rails, measured 0.12 m in game).</summary>
    private const float TopEntrySightAbove = 0.2f;

    /// <summary>Intermediate-floor dismount search: a landing from this far below the feet to this far above them.</summary>
    private const float IntermediateAbove = 1.15f;
    private const float IntermediateBelow = 0.45f;

    /// <summary>A body further than this from the motor's last command at release has been moved by something
    /// else; the release then makes no physics or fall-reference writes.</summary>
    private const float TakeoverDistance = 0.5f;

    /// <summary>A blocked upward climb this close to the top also tries the top exit: a lip overhead.</summary>
    private const float TopBlockedBand = 2.2f;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly Action<string> _log;
    private readonly LadderMotor _motor = new LadderMotor();
    private readonly LadderWalkPose _pose;
    private readonly LadderSkill _skill;
    private readonly ClimbLedger _ledger = new ClimbLedger();
    private readonly List<LadderSegment> _segments = new List<LadderSegment>();
    private readonly List<Vector3> _positions = new List<Vector3>();
    private readonly List<Quaternion> _rotations = new List<Quaternion>();
    private readonly List<float> _spanBottoms = new List<float>();
    private readonly List<float> _spanTops = new List<float>();
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
    private readonly DropTrigger _drop = new DropTrigger();
    private int _departFrame = -1;
    private int _runRevision = -1;
    private Vector3 _commanded;

    // Read-only presentation feed (IClimbPresenter). Counters are per attachment unless noted.
    private IClimbPresenter? _presenter;
    private int _session;
    private int _discontinuity;
    private int _geometryRevision;
    private RungProfile[]? _profiles;
    private int _profilesRevision = -1;
    private int _profilesSession = -1;
    private long _sample;
    private float _travel;
    private float _lastSampleY;
    private Mode _lastSampleMode;
    private bool _mountAirborne;
    private bool _exitIsTop;
    private bool _exitIsBottom;
    private bool _mountFromTop;
    private Vector3 _routeStart;
    private float _routeRise;
    private float _routeLength;
    private ClimbEnd _endHint;

    internal static LadderRuntime? Current { get; private set; }
    internal bool IsClimbing => _mode != Mode.Off;
    internal bool Ready => _motor.Ready;
    internal bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value && IsClimbing) { Release(Departure.Controlled); } }
    }

    internal LadderRuntime(Action<string> log, LadderSkill skill)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _skill = skill ?? throw new ArgumentNullException(nameof(skill));
        _pose = new LadderWalkPose(log);
    }

    /// <summary>The presenter that receives the read-only climb frames; null keeps only the built-in pose.</summary>
    internal void SetPresenter(IClimbPresenter? presenter) => _presenter = presenter;

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

        try { _presenter?.Update(delta); } catch (Exception exception) { PresenterFault(exception); }
        if (!IsClimbing)
        {
            TryBuildModeMount();
            return;
        }
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
            bool focus = GameplayFocus(_player);
            _drop.Observe(CrouchHeld(), focus);
            _drop.Press(CrouchPressed(), focus);
            // Vanilla routes Use only to the hovered object. A climber who looks away from the rungs (at a wall,
            // the sky, a blocked landing) would otherwise have no way to ask for a safe dismount. The player's own
            // bindings are read; a hovered interactable (including this ladder) keeps vanilla's routing. Jump and
            // Drop win a press that arrives in the same frame, the press that mounted cannot also dismount, and in
            // build mode the hammer owns the shared Use binding (snap cycling), so it never dismounts there (N08).
            ClimbAction action = ClimbControls.Resolve(focus, JumpPressed(), CrouchPressed(), UsePressed(), _player.InPlaceMode());
            if (_mode == Mode.Climbing && action == ClimbAction.Use && Time.frameCount != _mountFrame && !HoversInteractable(_player))
            {
                if (!TrySafeDismount()) { Notice("$cl_msg_nolanding"); }
            }

            // UpdateMotion no longer runs, so nothing else resets the locomotion floats: under the custom IK
            // pose the base layer is held at idle; without it, the walking fallback follows actual travel.
            // A top or side exit walks onto the landing: the game's own walk cycle follows the real path speed there,
            // presenter or not (in game 2026-10-26 the climbing contacts have nothing left to hold at a flush top).
            _pose.Apply(_mode == Mode.Exiting ? _poseSpeed : _presenter != null || !takesInput ? 0f : _poseSpeed);
        }
        catch (Exception exception) { Fault(exception); }
    }

    private static bool UsePressed() =>
        (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) && !Hud.InRadial();

    private static bool JumpPressed() => ZInput.GetButtonDown("Jump") || ZInput.GetButtonDown("JoyJump");

    private readonly RaycastHit[] _buildHover = new RaycastHit[64]; // The game's own hover buffer size.

    /// <summary>Mounting with the hammer equipped (owner decision 2026-10-26). In build mode the game clears its
    /// hover target, so Use never reaches the ladder. This repeats the game's own hover test (camera ray, the game's
    /// interaction mask, nearest hit, within the interact distance of the eyes) and, on a Use press with gameplay
    /// focus, sends the hovered ladder through the same Use path as any other mount: reach, room, route and every
    /// refusal are unchanged. Use is also the hammer's snap-cycle key; that native action still happens.</summary>
    private void TryBuildModeMount()
    {
        try
        {
            Player? player = Player.m_localPlayer;
            if (player == null || !player.InPlaceMode() || _cooldown > 0f || !_motor.Ready || !UsePressed() || JumpPressed()) { return; }
            if (!GameplayFocus(player) || GameCamera.instance == null) { return; }
            int mask = _motor.InteractMask(player);
            if (mask == -1) { return; }
            Transform camera = GameCamera.instance.transform;
            int count = Physics.RaycastNonAlloc(camera.position, camera.forward, _buildHover, 50f, mask);
            // A full buffer need not hold the nearest hits; never mount past an object normal hover would select.
            if (count >= _buildHover.Length) { return; }
            float nearest = float.MaxValue;
            RaycastHit? first = null;
            for (int index = 0; index < count && index < _buildHover.Length; index++)
            {
                RaycastHit hit = _buildHover[index];
                if (hit.collider == null) { continue; }
                Rigidbody attached = hit.collider.attachedRigidbody;
                if (attached != null && attached.gameObject == player.gameObject) { continue; }
                if (hit.distance < nearest) { nearest = hit.distance; first = hit; }
            }

            if (first == null) { return; }
            LadderSegment? segment = first.Value.collider.GetComponentInParent<LadderSegment>();
            if (segment == null || Vector3.Distance(player.GetEyePoint(), first.Value.point) >= player.m_maxInteractDistance) { return; }
            Use(segment);
        }
        catch (Exception exception) { Fault(exception); }
    }

    private static bool CrouchPressed() => ZInput.GetButtonDown("Crouch") || ZInput.GetButtonDown("JoyCrouch");

    private static bool CrouchHeld() => ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch");

    /// <summary>The native controls for one input tick, before the game applies them (Player.SetControls).
    /// While attached with gameplay input, Jump wins and departs; otherwise an armed fresh Crouch edge drops.
    /// Both consume the crouch edge so the native sneak toggle never flips while attached, and a Jump never
    /// becomes the game's crouch-dodge. Nothing is changed for a player who is not attached.</summary>
    internal void Controls(Player player, Vector3 movedir, ref bool jump, ref bool crouch)
    {
        if (!IsClimbing || _player == null || !ReferenceEquals(player, _player)) { return; }
        try
        {
            bool focus = GameplayFocus(player);
            // Lost focus seen here too clears a pending press, whichever of the frame's updates saw the menu first.
            if (!focus) { _drop.Observe(CrouchHeld(), false); }
            // The native edge, or a fresh press seen on a rendered frame whose native edge went stale behind a menu.
            bool drop = focus && _drop.Take(crouch);
            ClimbAction action = ClimbControls.Resolve(focus, jump, drop, false, false);
            jump = false;
            crouch = false;
            // This tick's movement, the way SetControls is about to derive it, so Jump+direction pressed together
            // departs that way (the stored move direction is still the previous tick's).
            if (action == ClimbAction.Jump) { JumpOff(player, CameraRelative(player, movedir)); }
            else if (action == ClimbAction.Drop) { DropOff(player); }
        }
        catch (Exception exception) { Fault(exception); }
    }

    private static Vector3 CameraRelative(Player player, Vector3 movedir)
    {
        Vector3 look = player.GetLookDir();
        look.y = 0f;
        if (!LadderSegment.Finite(movedir) || !LadderSegment.Finite(look) || look.sqrMagnitude < 1e-6f) { return Vector3.zero; }
        look.Normalize();
        return movedir.z * look + movedir.x * Vector3.Cross(Vector3.up, look);
    }

    /// <summary>Gameplay has the player's input: no menu, console, inventory, radial or build palette has focus.
    /// Player.TakeInput alone does not exclude the radial or the piece-selection palette.</summary>
    private bool GameplayFocus(Player player) =>
        _motor.TakesInput(player) && !InventoryGui.IsVisible() && !Hud.InRadial() && !Hud.IsPieceSelectionVisible();

    /// <summary>A world interaction is not delivered in the frame of a Jump or Drop departure, nor while attached
    /// when a Jump or fresh Crouch press arrives in the same frame: the game reads Use in its Update, which can run
    /// before the physics tick that departs, and the departure wins (Jump &gt; Drop &gt; Use).</summary>
    internal bool SuppressInteract()
    {
        if (Time.frameCount == _departFrame) { return true; }
        if (!IsClimbing || _player == null || !GameplayFocus(_player)) { return false; }
        return JumpPressed() || (CrouchPressed() && (_drop.Armed || _drop.Pending));
    }

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
            return ClimbPolicy.InReach(Reach(_hoverRun, segment, player.transform.position, height, player.IsOnGround() && !StandsOnLadder(player)));
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
                Notice("$cl_msg_unavailable");
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
        internal bool TopEntry;
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
        // A jump beside the ladder can brush a rung, which the game reports as ground contact (in game 2026-10-27: a
        // midair grab 1.6 m up planned as a standing mount). Standing on the ladder itself is not standing.
        // The game's contact can also linger a moment after leaving a rung; the feet must have a real (non-ladder)
        // surface just under them.
        bool supported = player.IsOnGround() && !StandsOnLadder(player) && clearance.TryFloorBelow(body.position, 0.35f, out _);
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
            // Owner spec 2026-10-27: a descending entry from an upper floor lowers onto the ladder (hands on the top
            // rungs, feet onto real rungs below the lip) instead of standing on the run's top.
            float entryLow = Mathf.Max(hangLowest, run.Top.Y - TopEntryDrop);
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
                bool aligned = player.IsSwimming()
                    ? ClimbPolicy.SwimAlignmentAllowed(horizontal, target - feet.y)
                    : ClimbPolicy.AirAlignmentAllowed(horizontal, target - feet.y);
                if (!aligned) { first = Keep(first, ClimbRefusal.OutOfReach); continue; }
            }

            // The hands must reach the rungs without passing through a wall or floor.
            // From a floor at the top the hands go to the lip, not a rung under it: in game 2026-10-27 the line from a
            // chest a step back from the edge down to the top rung passed through the floor and refused the mount,
            // while a step closer it cleared the edge.
            float sightY = topEntry ? run.Top.Y + TopEntrySightAbove : Mathf.Clamp(chest.y, run.Bottom.Y + firstRung, run.Top.Y - firstRung);
            Vector3 rung = LanePoint(run, (segment.BackFaceDepth + segment.FrontFaceDepth) * 0.5f, sightY);
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
                    if (topEntry && entryLow < line.y - 0.05f)
                    {
                        Vector3 lowered = LanePoint(run, lane, entryLow);
                        Vector3[] down = { direct[0], direct[1], line, lowered };
                        Vector3[] downAround = { around[0], around[1], around[2], line, lowered };
                        if (clearance.CanFollow(feet, down)) { route = down; break; }
                        if (clearance.CanFollow(feet, downAround)) { route = downAround; break; }
                    }

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
            bool entering = topEntry && route.Length > 0 && route[route.Length - 1].y < line.y - 0.05f;
            if (topEntry && entryLow < line.y - 0.05f && !entering)
            {
                // The lip sight line clears a closed floor or hatch over the ladder, but the body cannot lower past it
                // (review 2026-10-27): refuse as before instead of attaching the climber on top of the floor.
                first = Keep(first, FromBlocker(clearance.LastBlocker));
                continue;
            }
            plan = new Plan { Face = face, Lane = lane, Height = entering ? entryLow : target, Route = route, Airborne = !supported, Reach = reach, TopEntry = entering };
            return true;
        }

        refusal = first == ClimbRefusal.None ? ClimbRefusal.Obstacle : first;
        detail += " blocker=" + clearance.LastBlocker + ":" + clearance.LastBlockerName;
        return false;
    }

    private static bool StandsOnLadder(Player player)
    {
        Collider? ground = player.GetLastGroundCollider();
        return ground != null && ground.GetComponentInParent<LadderSegment>() != null;
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
        CaptureSegments();
        _route = plan.Route;
        _routeIndex = 0;
        _mode = Mode.Mounting;
        _mountFrame = Time.frameCount;
        _compatibilityTimer = 1f;
        _runRevision = LadderSegment.Revision;
        _commanded = Vector3.zero;
        _drop.Arm(CrouchHeld()); // A Crouch already held while grabbing is not a request to drop.
        _session++;
        _discontinuity = 0;
        _geometryRevision = 0;
        _travel = 0f;
        _lastSampleY = body.position.y;
        _lastSampleMode = Mode.Mounting;
        _mountAirborne = plan.Airborne;
        _mountFromTop = plan.TopEntry;
        _endHint = ClimbEnd.None;
        BeginRoute(body.position);
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
        try
        {
            // The controls once per mount, in the game's top-left message area, with the current bindings.
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                Localization.instance != null ? Localization.instance.Localize("$cl_ladder_hint") : "$cl_ladder_hint");
        }
        catch { /* A hint never affects the climb. */ }
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
            // A ladder section was built or removed somewhere (N08): rebuild this shaft's connected run once and
            // keep climbing on it if it is the same shaft and the section under the hands still stands; otherwise
            // let go. A takeover in progress (knockback, stagger, teleport, authority loss) is classified first, so
            // a refresh failure never writes over it (Codex review of 075f14b).
            if (_runRevision != LadderSegment.Revision && body != null && clearance != null)
            {
                float driftNow = Vector3.Distance(body.position, _lastControlledPosition);
                if (LadderRules.IsExternalRelease(owned, !player.IsDead(), Free(player, true), worldPresent, body.isKinematic, driftNow))
                {
                    Release(Departure.External);
                    return false;
                }

                if (!RefreshRun(body.position, clearance))
                {
                    _endHint = ClimbEnd.Destroyed;
                    Release(Departure.Controlled);
                    return false;
                }
            }

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
                if (!present || !unmoved) { _endHint = ClimbEnd.Destroyed; }
                Release(external ? Departure.External : Departure.Controlled);
                return false;
            }

            float delta = LadderRules.FrameSeconds(seconds);
            if (delta <= 0f) { Release(Departure.Controlled); return false; }
            SettlePending(player, body.position.y, delta);
            // Opening a door or hatch from the ladder plays the interact animation, a minor action; a hammer
            // placement, repair or removal plays the tool swing (N08). The climber holds still on the rungs for
            // their duration rather than being let go to fall. Holding costs no stamina and earns no skill.
            if (!GameplayFocus(player) || player.InMinorAction() || BuildAction(player))
            {
                Hold(body.position, seconds, 0f, LadderRules.Speed);
                Present(true);
                return true;
            }

            if (_mode == Mode.Mounting || _mode == Mode.Exiting)
            {
                // Input back toward the ladder abandons an exit: Down for a top or side exit, Up for a bottom step-off
                // (in game 2026-10-27: Down, which starts a step-off, cancelled it every frame).
                if (_mode == Mode.Exiting && (_exitIsBottom ? ReadInput(player) : -ReadInput(player)) > 0.15f) { ReturnToLadder(); }
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

            if (velocity > 0f && body.position.y >= _run.Top.Y - NoLandingStop && body.position.y < _run.Top.Y - 0.03f)
            {
                // The last chance to find a landing before the hands would run out of rungs, at most every 0.2 s
                // (review 2026-10-27: it ran every physics step on a ladder with no landing).
                if (_topPlanTimer <= 0f)
                {
                    _topPlanTimer = 0.2f;
                    if (TryExitTop()) { MoveRoute(delta, seconds); Present(false); return IsClimbing; }
                }

                Hold(body.position, seconds, 0f, LadderRules.Speed);
                Notice("$cl_msg_notop");
                Present(true);
                return true;
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
                // Owner spec 2026-10-27: a supported step off onto the ground (lead foot, support transfer, second
                // foot, last hand), not a release where the climber stands; without a reachable spot behind, release.
                if (TryBottomStepOff()) { MoveRoute(delta, seconds); Present(false); return IsClimbing; }
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
                    if (TryBottomStepOff()) { MoveRoute(delta, seconds); Present(false); return IsClimbing; }
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

    /// <summary>Bounded refresh of the session's run after a registry change: anchor on the section the hands
    /// hold (or the nearest one still standing), rebuild the connected run from it, and adopt it only for the
    /// same shaft with the climber still on it. Face, lane, anchor height and the gait ledger are unchanged.
    /// Removing the section the hands are on, or leaving the climber off the end, fails and releases.</summary>
    private bool RefreshRun(Vector3 feet, LadderClearance clearance)
    {
        _runRevision = LadderSegment.Revision;
        // Every section that is still standing must be where it was: a moved section ends the climb, it is never
        // hidden by re-capturing its new pose (Codex review of 075f14b).
        for (int index = 0; index < _segments.Count; index++)
        {
            LadderSegment old = _segments[index];
            if (old == null) { continue; }
            if (Vector3.Distance(old.transform.position, _positions[index]) > 0.01f ||
                Quaternion.Angle(old.transform.rotation, _rotations[index]) > 0.25f)
            {
                return false;
            }
        }

        // The section the hands hold, from the spans recorded when the run was accepted; if that one is gone the
        // support is gone, even when a neighbouring section survives.
        int holding = ClimbControls.SupportingSection(feet.y + _gripHeight, _run.Bottom.Y, _run.Top.Y,
            _spanBottoms.ToArray(), _spanTops.ToArray());
        if (holding < 0) { return false; }
        LadderSegment anchor = _segments[holding];
        if (anchor == null || !anchor.TryGeometry(out _)) { return false; }
        var refreshed = new List<LadderSegment>();
        if (!LadderSegment.TryRun(anchor, refreshed, out LadderGeometry run)) { return false; }
        float lowest = ClimbPolicy.LowestHang(run.Bottom.Y, anchor.RungPitch * 0.5f, _gripHeight);
        if (!ClimbControls.CanAdoptRun(_run, run, feet.y, lowest)) { return false; }
        bool changed = refreshed.Count != _segments.Count;
        for (int index = 0; !changed && index < refreshed.Count; index++) { changed = !_segments.Contains(refreshed[index]); }
        if (!changed) { return true; }
        _segments.Clear();
        _segments.AddRange(refreshed);
        CaptureSegments();
        _run = run;
        _hangLowest = lowest;
        _geometryRevision++; // The span changed; the climber, face and planted contacts did not.
        clearance.SetRun(_segments);
        _log("Ladder run refreshed while climbing: " + _segments.Count + " section(s), " + run.Height.ToString("0.00", Invariant) + " m.");
        return true;
    }

    /// <summary>Records each accepted section's pose and vertical span, so later checks can tell a moved or
    /// removed section from the ones still standing.</summary>
    private void CaptureSegments()
    {
        _positions.Clear();
        _rotations.Clear();
        _spanBottoms.Clear();
        _spanTops.Clear();
        foreach (LadderSegment part in _segments)
        {
            _positions.Add(part.transform.position);
            _rotations.Add(part.transform.rotation);
            bool measured = part.TryGeometry(out LadderGeometry span);
            _spanBottoms.Add(measured ? span.Bottom.Y : float.NaN);
            _spanTops.Add(measured ? span.Top.Y : float.NaN);
        }
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
        // With the original presenter installed the walking contingency stays idle; it drives the legs only when
        // no presenter is bound (install failed or a presentation fault removed it). Exit routes publish with
        // attached=false only through their PresentedMotion; the frame carries everything the presenter uses.
        if (_presenter != null && _mode != Mode.Exiting) { _poseSpeed = 0f; }
        _ = attached;
        Publish(_player);
    }

    /// <summary>One read-only frame per motor step for the presenter. Completed travel is the body's observed
    /// vertical change since the previous sample, counted only between two climbing samples (mount, catch,
    /// exit and route motion are excluded). A non-finite value withholds the frame.</summary>
    private void Publish(Player player)
    {
        IClimbPresenter? presenter = _presenter;
        Rigidbody? body = _body;
        if (presenter == null || body == null) { return; }
        try
        {
            Vector3 feet = body.position;
            float step = _mode == Mode.Climbing && _lastSampleMode == Mode.Climbing ? feet.y - _lastSampleY : 0f;
            if (!ClimbPolicy.Finite(step) || !LadderSegment.Finite(feet)) { return; }
            _travel += step;
            _lastSampleY = feet.y;
            _lastSampleMode = _mode;
            PresentedMotion motion = _mode switch
            {
                Mode.Mounting => _mountAirborne ? PresentedMotion.AirCatch : _mountFromTop ? PresentedMotion.TopEntry : PresentedMotion.GroundMount,
                Mode.Exiting => _exitIsTop ? PresentedMotion.TopFinish : _exitIsBottom ? PresentedMotion.BottomStepOff : PresentedMotion.StepOff,
                _ => _gait switch
                {
                    ClimbGait.Normal => PresentedMotion.Climb,
                    ClimbGait.Fast => PresentedMotion.Fast,
                    ClimbGait.Slide => PresentedMotion.Slide,
                    _ => PresentedMotion.Hold,
                },
            };
            bool routed = _mode == Mode.Mounting || _mode == Mode.Exiting;
            Vector3 end = routed && _route.Length > 0 ? _route[_route.Length - 1] : feet;
            float origin = _pitch * 0.5f;
            NativeActionParts action = NativeOwnership(player);
            if (_profilesRevision != _geometryRevision || _profilesSession != _session) { _profiles = BuildProfiles(); _profilesRevision = _geometryRevision; _profilesSession = _session; }
            var frame = new ClimbPresentationFrame(_session, _discontinuity, _geometryRevision, ++_sample, Time.fixedTime,
                motion, _gait, _travel, step, LadderSegment.Point(feet), _run, _face, _plane, origin,
                ClimbPresentationFrame.LastRungOf(_run.Height, origin, _pitch), routed ? PresentedProgress(feet) : -1f,
                LadderSegment.Point(routed ? _routeStart : feet), LadderSegment.Point(end), action, player.InPlaceMode(),
                !GameplayFocus(player), _profiles);
            presenter.Publish(player, frame);
        }
        catch (Exception exception) { PresenterFault(exception); }
    }

    /// <summary>Each section's rung shape (material art), bottom to top, for presentation only.</summary>
    private RungProfile[] BuildProfiles()
    {
        var profiles = new RungProfile[_segments.Count];
        for (int i = 0; i < _segments.Count; i++)
        {
            LadderSegment segment = _segments[i];
            float bottom = segment != null ? segment.transform.TransformPoint(segment.LocalBottom).y : 0f;
            float top = segment != null ? segment.transform.TransformPoint(segment.LocalTop).y : 0f;
            profiles[i] = segment == null ? RungProfile.Wood :
                new RungProfile(bottom, top, segment.RungHalfHeight, segment.RungHalfDepth, segment.RungRadius);
        }

        return profiles;
    }

    /// <summary>Which limbs the game's own animation owns while attached (owner decision 2026-10-26):
    /// <list type="bullet">
    /// <item>With the building tool out (build mode), only the hammer arm, the right one: it shows the native
    /// hammer hold and swings, while the body, legs and left hand keep the climbing pose on the rungs.</item>
    /// <item>An interact (door, hatch) takes both arms and the upper body for its short animation.</item>
    /// </list></summary>
    private static NativeActionParts NativeOwnership(Player player) =>
        ClimbControls.Ownership(player.InMinorAction(), HoldsBuildTool(player));

    /// <summary>Player.InPlaceMode is the game's own test for an equipped building tool (its piece table is set).
    /// Humanoid.GetRightItem is protected in the shipped game: publicized build references compile it, the game
    /// then throws MethodAccessException (observed in game on 0.2.2 b90b345).</summary>
    private static bool HoldsBuildTool(Player player) => player.InPlaceMode();

    /// <summary>A presenter fault disables presentation only; the climb and the built-in pose continue.</summary>
    private void PresenterFault(Exception exception)
    {
        IClimbPresenter? presenter = _presenter;
        _presenter = null;
        try { presenter?.Dispose(); } catch { /* Presentation cleanup only. */ }
        try { _log("Climbing presentation stopped (" + SafeFailure.Brief(exception) + "); climbing continues."); }
        catch { /* Logging cannot affect the climb. */ }
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
    private void JumpOff(Player player, Vector3? thisTick = null)
    {
        Rigidbody body = _body!;
        SettlePending(player, body.position.y, Time.fixedDeltaTime);
        Vector3 away = Away();
        Vector3 move = thisTick ?? _motor.MoveDirection(player);
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

    /// <summary>Drop (N07): let go where the body is, with no impulse, kick, landing requirement or stamina
    /// cost. The preceding step's earned charge and progress settle first, once; the release then removes only
    /// the motion the ladder commanded and the body falls normally from the release height.</summary>
    private void DropOff(Player player)
    {
        SettlePending(player, _body!.position.y, Time.fixedDeltaTime);
        Release(Departure.Drop);
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
        // Straight over the rails first, then the same distances shifted along the lip (in game 2026-10-27: the owner's
        // ladder stands at a floor's corner, so every point straight in from it was on the floor's edge).
        foreach (float shift in new[] { 0f, 0.4f, -0.4f, 0.8f, -0.8f })
        {
            foreach (float distance in new[] { radius + 0.45f, radius + 0.85f })
            {
                if (TryTopLanding(top + inward * distance + side * shift)) { return true; }
            }
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

    private bool TryLandingNear(Vector3 desired)
    {
        _exitIsTop = false;
        _exitIsBottom = false;
        if (!_clearance!.TryLandingSurface(desired, IntermediateAbove, IntermediateBelow, out Vector3 floor)) { Landing("side", desired, "no surface"); return false; }
        return PlanExitTo(floor);
    }

    /// <summary>A step back off the foot of the ladder onto supported ground behind the climber.</summary>
    private bool TryBottomStepOff()
    {
        Vector3 feet = _body!.position;
        Vector3 away = Away();
        float radius = _clearance!.Radius;
        foreach (float distance in new[] { radius + 0.25f, radius + 0.45f })
        {
            _exitIsTop = false;
            _exitIsBottom = true;
            if (!_clearance.TryLandingSurface(feet + away * distance, 0.35f, 0.45f, out Vector3 floor)) { Landing("bottom", feet + away * distance, "no surface"); continue; }
            if (PlanExitTo(floor)) { return true; }
        }

        _exitIsBottom = false;
        return false;
    }

    private bool TryTopLanding(Vector3 desired)
    {
        _exitIsTop = true;
        _exitIsBottom = false;
        return _clearance!.TryTopSurface(desired, out Vector3 floor) && PlanExitTo(floor);
    }

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
        // Owner spec 2026-10-27 (intermediate floors beside a ladder that continues above them): the landing may be
        // from a step below the feet to about waist height above them, and the side the camera faces is tried first.
        Vector3 outward = Away();
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        float radius = _clearance.Radius;
        float sideways = _run.Width * 0.5f + radius + 0.15f;
        Vector3 look = GameCamera.instance != null ? GameCamera.instance.transform.forward : outward;
        look.y = 0f;
        var candidates = new List<(Vector3 Point, float Score)>();
        foreach (float sign in new[] { 1f, -1f })
        {
            // Three distances, each also shifted away from and in toward the ladder's plane (in game 2026-10-27: a 2x2
            // floor beside the ladder was only found at its edge, half the footprint off it).
            foreach (float distance in new[] { sideways, sideways + 0.4f, sideways + 0.8f })
            {
                foreach (float depth in new[] { 0f, 0.4f, -0.3f })
                {
                    candidates.Add((feet + side * (sign * distance) + outward * depth,
                        Vector3.Dot(side * sign, look) - distance * 0.01f - Mathf.Abs(depth) * 0.02f));
                }
            }
        }

        foreach (float distance in new[] { radius + 0.45f, radius + 0.85f })
        {
            candidates.Add((feet + outward * distance, Vector3.Dot(outward, look) - 0.05f - distance * 0.01f));
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (var candidate in candidates)
        {
            if (TryLandingNear(candidate.Point)) { return true; }
        }

        return false;
    }

    /// <summary>Rise, cross, settle: each leg swept with the real capsule, two lift heights tried. Where a lip
    /// or roof edge overhangs the climb lane, a clearance leg first steps back from the rails (up to half a
    /// metre) so the head can rise past the edge before crossing onto the floor. The destination needs room
    /// and a supported footprint, not a single support ray.</summary>
    private static bool LogLandings => Animation.ClimbTuning.LogLandingQueries;

    private void Landing(string kind, Vector3 desired, string outcome)
    {
        if (!LogLandings) { return; }
        Vector3 feet = _body != null ? _body.position : Vector3.zero;
        try { _log("Ladder landing " + kind + " at " + desired.ToString("F2") + " (feet " + feet.ToString("F2") + "): " + outcome); }
        catch { /* Diagnostics only. */ }
    }

    private bool PlanExitTo(Vector3 floor)
    {
        string kind = _exitIsTop ? "top" : _exitIsBottom ? "bottom" : "side";
        if (!_clearance!.HasRoom(floor)) { Landing(kind, floor, "refused: no standing room"); return false; }
        // Ground at the foot of the ladder (in game 2026-10-27: a grass bump) may rise a step across the footprint, as
        // the game itself walks; floors keep the strict tolerance.
        if (!_clearance.SupportedFootprint(floor, _exitIsBottom ? LadderRules.StepLift : 0.15f, out string why))
        {
            if (LogLandings) { Landing(kind, floor, "refused: unsupported footprint (" + why + ")"); }
            return false;
        }
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
            BeginRoute(feet);
            _transferStartHeight = Mathf.Clamp(feet.y, _hangLowest, _run.Top.Y);
            _mode = Mode.Exiting;
            _ledger.Discard();
            if (LogLandings) { Landing(kind, floor, "accepted (back " + back.ToString("F2") + ", lift " + lift.ToString("F2") + ")"); }
            return true;
        }

        Landing(kind, floor, "refused: no clear path");
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
        _mountAirborne = false;
        _mountFromTop = false;
        _route = route;
        _routeIndex = 0;
        BeginRoute(current);
    }

    /// <summary>Owner 2026-10-27 (exits "act weird for a bit", reference finish about 2.5 s): an exit or a descending
    /// entry takes a set time, so the authored support transfer has room; at the general route speed (1.8 m/s) a side
    /// exit was over in 0.7 s; later the same day the owner asked for every mount and dismount to be quick.</summary>
    private float RouteSpeed()
    {
        float seconds = _mode == Mode.Exiting ? (_exitIsTop ? TopExitSeconds : _exitIsBottom ? BottomStepSeconds : SideExitSeconds)
            : _mode == Mode.Mounting ? (_mountFromTop ? TopEntrySeconds : QuickSeconds) : 0f;
        if (seconds <= 0f || _routeLength <= 0.01f) { return LadderRules.Speed; }
        return Mathf.Clamp(_routeLength / seconds, 0.35f, seconds <= QuickSeconds ? QuickRouteSpeed : LadderRules.Speed);
    }

    private const float TopExitSeconds = QuickSeconds, SideExitSeconds = QuickSeconds, BottomStepSeconds = QuickSeconds, TopEntrySeconds = QuickSeconds;

    /// <summary>Owner 2026-10-27: on and off at the top and at an intermediate floor "super fast" - not a teleport,
    /// the capsule still follows its swept route, at up to QuickRouteSpeed.</summary>
    private const float QuickSeconds = 0.45f, QuickRouteSpeed = 6f;

    /// <summary>Climbing with no landing at the top stops with the feet this far below the run's top: the hands still
    /// hold the top rungs there (owner 2026-10-27: "stop climbing right before the hand contacts leave the rungs").</summary>
    private const float NoLandingStop = 1.3f;

    /// <summary>Remembers where an accepted mount/exit move started and its full length, for transition progress.</summary>
    private void BeginRoute(Vector3 start)
    {
        _routeStart = start;
        float length = 0f;
        Vector3 from = start;
        foreach (Vector3 point in _route) { length += Vector3.Distance(from, point); from = point; }
        _routeLength = length;
        // The lead: the leading legs of one kind - an exit lifts onto the floor (more up than across) then walks; a
        // descending entry walks over the lip (more across than down) then lowers onto the ladder.
        float rise = 0f;
        from = start;
        bool? vertical = null;
        bool leading = true;
        foreach (Vector3 point in _route)
        {
            Vector3 leg = point - from;
            float across = new Vector2(leg.x, leg.z).magnitude;
            if (leg.magnitude < 0.001f) { from = point; continue; }
            // The small leading lift or back-step leg belongs to whatever follows it (review 2026-10-27).
            if (leading && leg.magnitude <= LadderRules.StepLift + 0.21f) { rise += leg.magnitude; from = point; leading = false; continue; }
            leading = false;
            bool kind = Mathf.Abs(leg.y) > across;
            if (vertical.HasValue && vertical.Value != kind) { break; }
            vertical = kind;
            rise += leg.magnitude;
            from = point;
        }

        _routeRise = rise;
    }

    /// <summary>Exit and descending-entry progress as published to presentation: the route's lead (an exit's lift, an
    /// entry's walk over the lip) always spans the first RiseShare and the rest the remainder, whatever the route's
    /// proportions, so an authored transition lines its contacts up with the real capsule.</summary>
    private float PresentedProgress(Vector3 position)
    {
        float plain = RouteProgress(position);
        bool shaped = _mode == Mode.Exiting || (_mode == Mode.Mounting && _mountFromTop);
        if (!shaped || _routeRise < 0.1f || _routeRise >= _routeLength - 0.05f) { return plain; }
        float travelled = plain * _routeLength;
        return travelled <= _routeRise
            ? ClimbPresentationFrame.RiseShare * travelled / _routeRise
            : ClimbPresentationFrame.RiseShare + (1f - ClimbPresentationFrame.RiseShare) * (travelled - _routeRise) / (_routeLength - _routeRise);
    }

    /// <summary>Route progress 0..1 from the remaining path, not from elapsed time.</summary>
    private float RouteProgress(Vector3 position)
    {
        if (_routeLength <= 0.001f || _route.Length == 0) { return 1f; }
        float remaining = 0f;
        Vector3 from = position;
        for (int index = Mathf.Min(_routeIndex, _route.Length); index < _route.Length; index++)
        {
            remaining += Vector3.Distance(from, _route[index]);
            from = _route[index];
        }

        return Mathf.Clamp01(1f - remaining / _routeLength);
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
        if (_mode == Mode.Exiting && !clearance.SupportedFootprint(_route[_route.Length - 1], _exitIsBottom ? LadderRules.StepLift : 0.15f, out _))
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

        Vector3 target = Vector3.MoveTowards(body.position, _route[_routeIndex], RouteSpeed() * delta);
        clearance.ClearBlocker();
        if (!clearance.CanMove(body.position, target))
        {
            Hold(body.position, physicsSeconds, 0f, LadderRules.Speed);
            Refuse(FromBlocker(clearance.LastBlocker), clearance.LastBlocker, clearance.LastBlockerName);
            return;
        }

        // Transfer legs are mostly sideways: drive the walk cycle from the real path speed, not its vertical part.
        float pathSpeed = Vector3.Distance(target, body.position) / physicsSeconds;
        Hold(target, physicsSeconds, target.y < body.position.y - 0.001f ? -pathSpeed : pathSpeed, Mathf.Max(LadderRules.Speed, RouteSpeed()));
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
        _commanded = velocity;
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

    /// <summary>Mounting needs a completely free player. An active climb also survives a minor action (the
    /// interact animation of opening a door or hatch), build mode with the hammer, and the hammer's own native
    /// place/repair/remove swing (N08); all of these hold the climb instead. Combat attacks, dodges, knockback
    /// and stagger still release it.</summary>
    private static bool Free(Player player, bool attached = false) =>
        !player.IsDead() && !player.IsTeleporting() && !player.IsAttached() && !player.InBed() &&
        // Swimming does not refuse a grab (owner 2026-10-27: mount from the water like boarding a boat), and while
        // attached the ladder motor replaces the game's swim movement, so a body at the waterline is not released.
        !player.IsRiding() && !player.InCutscene() && !player.IsDebugFlying() &&
        // Build mode with the hammer never refuses a mount or ends a climb (owner decision 2026-10-26).
        (!player.InAttack() || (attached && BuildAction(player))) &&
        !player.InDodge() && !player.IsKnockedBack() && !player.IsStaggering() && (attached || !player.InMinorAction());

    /// <summary>The native hammer swing: build mode, a building tool in the right hand, the tool's attack
    /// animation playing. The game never starts a combat attack in build mode.</summary>
    private static bool BuildAction(Player player)
    {
        return ClimbControls.IsNativeBuildAction(player.InPlaceMode(), HoldsBuildTool(player), player.InAttack());
    }

    private enum Departure { Controlled, External, Jump, Push, Drop }

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
        if (was != Mode.Off && departure != Departure.External && departure != Departure.Controlled) { _departFrame = Time.frameCount; }
        if (was != Mode.Off)
        {
            // The presenter hears about the end first, synchronously, before any impulse or physics write below.
            ClimbEnd end = EndReason(departure, player);
            IClimbPresenter? presenter = _presenter;
            try { presenter?.End(player, end); }
            catch (Exception exception) { PresenterFault(exception); }
        }
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
            if (owned && body != null && (_wrotePhysics || departure == Departure.Jump || departure == Departure.Push || departure == Departure.Drop))
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
                    else if (departure == Departure.Drop)
                    {
                        // Only the motion this motor commanded is taken away; anything applied independently since
                        // (a hit, a push) stays. No impulse, no kick: gravity takes over from here.
                        // Only what physics kept of that command, along its direction: a command a landing or
                        // collision already cancelled is never subtracted again (Codex review of 075f14b).
                        Vector3 v = body.linearVelocity;
                        Vector3 c = _wrotePhysics && LadderSegment.Finite(_commanded) ? _commanded : Vector3.zero;
                        ClimbControls.SurvivingCommand(v.x, v.y, v.z, c.x, c.y, c.z, out float sx, out float sy, out float sz);
                        Vector3 left = v - new Vector3(sx, sy, sz);
                        if (!LadderSegment.Finite(left)) { left = Vector3.zero; }
                        body.linearVelocity = left;
                        _motor.BeginAirborne(player, left);
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
            _player = null;
            _body = null;
            _view = null;
            _clearance = null;
            _wrotePhysics = false;
            _segments.Clear();
            _positions.Clear();
            _rotations.Clear();
            _spanBottoms.Clear();
            _spanTops.Clear();
            _route = Array.Empty<Vector3>();
        }
    }

    private ClimbEnd EndReason(Departure departure, Player? player)
    {
        ClimbEnd hint = _endHint;
        _endHint = ClimbEnd.None;
        if (hint != ClimbEnd.None) { return hint; }
        switch (departure)
        {
            case Departure.Jump:
            case Departure.Push:
                return ClimbEnd.Jump;
            case Departure.Drop:
                return ClimbEnd.Drop;
            case Departure.External:
                try
                {
                    if (player == null || ZNetScene.instance == null) { return ClimbEnd.External; }
                    if (player.IsDead()) { return ClimbEnd.Death; }
                    if (player.IsTeleporting()) { return ClimbEnd.Teleport; }
                }
                catch { /* A destroyed player is an external end. */ }

                return ClimbEnd.External;
            default:
                return Enabled ? ClimbEnd.Landed : ClimbEnd.Disabled;
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
        _endHint = ClimbEnd.Fault;
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
