using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>Explicit-use player traversal. The owner's motor runs one swept, bounded physics step at a time.</summary>
internal sealed class LadderRuntime : IDisposable
{
    private enum Mode { Off, Mounting, Climbing, Exiting }
    private readonly Action<string> _log;
    private readonly LadderMotor _motor = new LadderMotor();
    private readonly LadderWalkPose _pose;
    private readonly List<LadderSegment> _segments = new List<LadderSegment>();
    private readonly List<Vector3> _positions = new List<Vector3>();
    private readonly List<Quaternion> _rotations = new List<Quaternion>();
    private Player? _player;
    private Rigidbody? _body;
    private ZNetView? _view;
    private LadderClearance? _clearance;
    private LadderGeometry _run;
    private Mode _mode;
    private Vector3[] _route = Array.Empty<Vector3>();
    private int _routeIndex;
    private float _bodyOffset;
    private float _transferStartHeight;
    private bool _gravityBefore;
    private bool _wrotePhysics;
    private float _cooldown;
    private float _poseSpeed;
    private float _lastControlledHeight;
    private Vector3 _lastControlledPosition;
    private float _noticeCooldown;
    private float _compatibilityTimer;
    private bool _foremanConflict;
    private string _lastNotice = string.Empty;
    private bool _enabled = true;

    internal static LadderRuntime? Current { get; private set; }
    internal bool IsClimbing => _mode != Mode.Off;
    internal bool Ready => _motor.Ready;
    internal bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value && IsClimbing) { Release(false, false); } }
    }

    internal LadderRuntime(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
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
        if (!IsClimbing) { return; }
        try
        {
            // Update also releases when the physics loop has stopped during world unload.
            if (_player == null || Player.m_localPlayer != _player || ZNetScene.instance == null || !Enabled ||
                _view == null || !_view.IsValid() || !_view.IsOwner() || !Free(_player))
            {
                Release(false, true);
                return;
            }

            _compatibilityTimer -= delta;
            if (_compatibilityTimer <= 0f)
            {
                _compatibilityTimer = 1f;
                _foremanConflict = _motor.HasForemanMotor();
                if (_foremanConflict)
                {
                    Notice("Foreman's ladder motor is active. Disable Foreman's Ladders/Enabled and restart to use these ladders.");
                    Release(false, false);
                    return;
                }
            }

            _pose.Apply(_motor.TakesInput(_player) ? _poseSpeed : 0f);
        }
        catch (Exception exception) { Fault(exception); }
    }

    internal bool Use(LadderSegment segment)
    {
        try
        {
            Player? player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) { return false; }
            if (!_motor.Ready || !Enabled)
            {
                Notice("Ladder climbing is unavailable. Check Concerned Ladders' startup log.");
                return true;
            }

            if (!_motor.TakesInput(player)) { return false; }
            if (IsClimbing)
            {
                if (!TrySafeDismount()) { Notice("There is no safe landing within reach. Keep climbing, or jump to let go."); }
                return true;
            }

            _foremanConflict = _motor.HasForemanMotor();
            if (_foremanConflict)
            {
                Notice("Foreman's ladder motor is active. Disable Foreman's Ladders/Enabled and restart to use these ladders.");
                return true;
            }

            Rigidbody body = player.GetComponent<Rigidbody>();
            ZNetView view = player.GetComponent<ZNetView>();
            CapsuleCollider capsule = player.GetCollider();
            if (body == null || body.isKinematic || view == null || !view.IsValid() || !view.IsOwner() ||
                capsule == null || !Free(player) || _cooldown > 0f)
            {
                Notice("You cannot climb right now.");
                return true;
            }

            if (!LadderSegment.TryRun(segment, _segments, out LadderGeometry run))
            {
                Notice("This ladder has an invalid or ambiguous connection. Check the segment ends.");
                return true;
            }

            var clearance = new LadderClearance(player, capsule, body.position);
            if (!clearance.HasRoom(body.position) || !LadderRules.CanMount(run, LadderSegment.Point(body.position), clearance.Supported(body.position),
                    body.linearVelocity.y, _motor.FallHeight(player), true, Free(player), false, _cooldown))
            {
                Notice("Stand on a nearby landing in front of the ladder. Midair grabs are unavailable.");
                _segments.Clear();
                return true;
            }

            float offset = LadderRules.BodyOffset(clearance.Radius, segment.FrontFaceDepth);
            float height = Mathf.Clamp(body.position.y, run.Bottom.Y, run.Top.Y);
            Vector3 line = LadderSegment.Vector(run.ClimbPositionAt(height - run.Bottom.Y, offset));
            float raised = Mathf.Max(body.position.y, line.y) + 0.06f;
            Vector3[] route =
            {
                new Vector3(body.position.x, raised, body.position.z),
                new Vector3(line.x, raised, line.z),
                line,
            };
            if (!clearance.CanFollow(body.position, route))
            {
                Notice("The route onto the ladder is blocked.");
                _segments.Clear();
                return true;
            }

            _player = player;
            _body = body;
            _view = view;
            _clearance = clearance;
            _run = run;
            _bodyOffset = offset;
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

            _route = route;
            _routeIndex = 0;
            _mode = Mode.Mounting;
            _compatibilityTimer = 1f;
            _pose.Begin(player);
            return true;
        }
        catch (Exception exception) { Fault(exception); return true; }
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
            bool free = Free(player);
            if (body == null || body.isKinematic || clearance == null || !clearance.IsUnchanged() ||
                LadderRules.MustRelease(Enabled && !_foremanConflict, owned, !player.IsDead(), free, worldPresent,
                    present, unmoved, drift))
            {
                // A teleport, knockback, death or authority change owns its new velocity/fall state.
                Release(false, !owned || !free || !worldPresent || player.IsDead());
                return false;
            }

            float delta = LadderRules.FrameSeconds(seconds);
            if (delta <= 0f) { Release(false, false); return false; }
            if (!_motor.TakesInput(player))
            {
                Hold(body.position, seconds, 0f);
                return true;
            }

            if (_mode == Mode.Mounting || _mode == Mode.Exiting)
            {
                if (_mode == Mode.Exiting && ReadInput(player) < -0.15f) { ReturnToLadder(); }
                MoveRoute(delta, seconds);
                return IsClimbing;
            }

            float input = ReadInput(player);
            float nextHeight = LadderRules.Advance(body.position.y, input, delta, _run.Bottom.Y, _run.Top.Y);
            Vector3 target = LadderSegment.Vector(_run.ClimbPositionAt(nextHeight - _run.Bottom.Y, _bodyOffset));
            if (input > 0f && body.position.y >= _run.Top.Y - 0.03f)
            {
                if (TryExitTop()) { MoveRoute(delta, seconds); }
                else { Hold(body.position, seconds, 0f); Notice("There is no clear landing at the top. Descend or jump to let go."); }
                return IsClimbing;
            }

            if (input < 0f && body.position.y <= _run.Bottom.Y + 0.03f)
            {
                if (clearance.Supported(body.position)) { Release(false, false); return false; }
                Hold(body.position, seconds, 0f);
                Notice("There is no footing below the ladder. Climb back up, or jump to let go.");
                return true;
            }

            if (!clearance.CanMove(body.position, target))
            {
                Hold(body.position, seconds, 0f);
                Notice("The ladder route is blocked. Reverse or jump to let go.");
                return true;
            }

            Hold(target, seconds, (target.y - body.position.y) / seconds);
            return true;
        }
        catch (Exception exception) { Fault(exception); return false; }
    }

    internal bool Jump(Character character)
    {
        if (!IsClimbing || _player == null || !ReferenceEquals(character, _player)) { return false; }
        try
        {
            if (_motor.TakesInput(_player)) { Release(true, false); }
            return true;
        }
        catch (Exception exception) { Fault(exception); return true; }
    }

    private bool TryExitTop()
    {
        Vector3 inward = new Vector3(_run.FacingWhileClimbing.X, 0f, _run.FacingWhileClimbing.Z);
        Vector3 desired = LadderSegment.Vector(_run.Top) + inward * (_clearance!.Radius + 0.45f);
        return _clearance.TryTopSurface(desired, out Vector3 floor) && PlanExitTo(floor);
    }

    private bool TrySafeDismount()
    {
        Vector3 feet = _body!.position;
        if (_clearance!.Supported(feet) && _clearance.HasRoom(feet)) { Release(false, false); return true; }
        if (feet.y >= _run.Top.Y - 0.5f && TryExitTop()) { return true; }
        Vector3 outward = new Vector3(_run.StandingSide.X, 0f, _run.StandingSide.Z);
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        foreach (Vector3 direction in new[] { outward, side, -side, -outward })
        {
            Vector3 desired = feet + direction * 0.75f;
            if (_clearance.TryNearbySurface(desired, out Vector3 floor) && PlanExitTo(floor)) { return true; }
        }

        return false;
    }

    private bool PlanExitTo(Vector3 floor)
    {
        if (!_clearance!.HasRoom(floor)) { return false; }
        Vector3 feet = _body!.position;
        float raised = Mathf.Max(feet.y, floor.y) + 0.06f;
        Vector3[] path =
        {
            new Vector3(feet.x, raised, feet.z),
            new Vector3(floor.x, raised, floor.z),
            floor,
        };
        if (!_clearance.CanFollow(feet, path)) { return false; }
        _route = path;
        _routeIndex = 0;
        _transferStartHeight = Mathf.Clamp(feet.y, _run.Bottom.Y, _run.Top.Y);
        _mode = Mode.Exiting;
        return true;
    }

    private void ReturnToLadder()
    {
        Vector3 current = _body!.position;
        Vector3 line = LadderSegment.Vector(_run.ClimbPositionAt(_transferStartHeight - _run.Bottom.Y, _bodyOffset));
        float raised = Mathf.Max(current.y, line.y) + 0.06f;
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

    private void MoveRoute(float delta, float physicsSeconds)
    {
        Rigidbody body = _body!;
        LadderClearance clearance = _clearance!;
        if (_mode == Mode.Exiting && !clearance.Supported(_route[_route.Length - 1]))
        {
            Release(false, false); // A removed platform is never replaced by its cached destination.
            return;
        }
        while (_routeIndex < _route.Length && Vector3.Distance(body.position, _route[_routeIndex]) < 0.02f)
        {
            _routeIndex++;
        }

        if (_routeIndex >= _route.Length)
        {
            if (_mode == Mode.Exiting)
            {
                Release(false, false);
                return;
            }

            _mode = Mode.Climbing;
            Hold(body.position, physicsSeconds, 0f);
            return;
        }

        Vector3 target = Vector3.MoveTowards(body.position, _route[_routeIndex], LadderRules.Speed * delta);
        if (!clearance.CanMove(body.position, target))
        {
            Hold(body.position, physicsSeconds, 0f);
            Notice("The transfer is blocked. Reverse, seek a safe dismount with Use, or jump to let go.");
            return;
        }

        Hold(target, physicsSeconds, (target.y - body.position.y) / physicsSeconds);
    }

    private void Hold(Vector3 target, float delta, float poseSpeed)
    {
        Rigidbody body = _body!;
        if (!LadderSegment.Finite(target) || !LadderSegment.Finite(body.position))
        {
            throw new InvalidOperationException("The climb target was not finite.");
        }

        Vector3 velocity = Vector3.ClampMagnitude((target - body.position) / delta, LadderRules.Speed);
        body.useGravity = false;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        _wrotePhysics = true;
        Vector3 facing = new Vector3(_run.FacingWhileClimbing.X, 0f, _run.FacingWhileClimbing.Z);
        body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(facing), 360f * LadderRules.FrameSeconds(delta)));
        _lastControlledHeight = body.position.y;
        _lastControlledPosition = target;
        _motor.SetFallReference(_player!, body.position.y);
        _poseSpeed = poseSpeed;
    }

    private float ReadInput(Player player)
    {
        Vector3 move = _motor.MoveDirection(player);
        Vector3 look = player.GetLookDir();
        look.y = 0f;
        if (!LadderSegment.Finite(move) || !LadderSegment.Finite(look)) { return 0f; }
        if (look.sqrMagnitude < 0.001f)
        {
            look = new Vector3(_run.FacingWhileClimbing.X, 0f, _run.FacingWhileClimbing.Z);
        }

        return LadderRules.Input(Vector3.Dot(move, look.normalized), false);
    }

    private static bool Free(Player player) =>
        !player.IsDead() && !player.IsTeleporting() && !player.IsAttached() && !player.InBed() &&
        !player.IsRiding() && !player.InCutscene() && !player.IsSwimming() && !player.IsDebugFlying() &&
        !player.InPlaceMode() && !player.InAttack() && !player.InDodge() && !player.IsKnockedBack() &&
        !player.IsStaggering() && !player.InMinorAction();

    private void Release(bool jump, bool externalControl)
    {
        Player? player = _player;
        Rigidbody? body = _body;
        _mode = Mode.Off; // Release the motor before querying any native state that might already be destroyed.
        bool owned = false;
        try { owned = _view != null && _view.IsValid() && _view.IsOwner(); } catch { /* No verified authority, no writes. */ }
        Vector3 outward = new Vector3(_run.StandingSide.X, 0f, _run.StandingSide.Z);
        _cooldown = LadderRules.RemountDelay;
        _poseSpeed = 0f;
        try
        {
            if (owned && body != null && _wrotePhysics)
            {
                body.useGravity = _gravityBefore;
                if (!externalControl)
                {
                    body.linearVelocity = jump ? outward * LadderRules.Speed : Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    if (player != null)
                    {
                        _motor.SetFallReference(player, Mathf.Max(body.position.y, _lastControlledHeight));
                    }
                }
            }
        }
        finally
        {
            try { _pose.Clear(owned && !externalControl); } catch { /* Never overwrite a takeover's animation state. */ }
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

    private void Fault(Exception exception)
    {
        try { Release(false, true); }
        catch { _mode = Mode.Off; _player = null; _body = null; }
        try { _log("Ladder traversal stopped (" + SafeFailure.Brief(exception) + "). Normal control was released."); }
        catch { /* Logging cannot keep the motor captured. */ }
    }

    private void Notice(string message)
    {
        if (message == _lastNotice && _noticeCooldown > 0f) { return; }
        _lastNotice = message;
        _noticeCooldown = 2f;
        Player? player = Player.m_localPlayer;
        if (player != null) { player.Message(MessageHud.MessageType.Center, message); }
    }

    public void Dispose()
    {
        try { Release(false, false); }
        finally
        {
            _motor.Dispose();
            if (ReferenceEquals(Current, this)) { Current = null; }
        }
    }
}
