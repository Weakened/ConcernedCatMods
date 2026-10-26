using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>Custom climbing animation on the installed player rig. The player's humanoid Animator already
/// runs an IK pass (vanilla's CharacterAnimEvent.OnAnimatorIK places the feet); after it, this sets the
/// hands and feet onto the ladder's real rungs, the elbows and knees to climbing bends and the gaze up the
/// ladder. Which rung each limb holds follows <see cref="LimbCycle"/>, so the cadence is the body's actual
/// travel and holding still is a held pose. Nothing here moves the body: the ladder motor owns movement and
/// collision. IK goals are re-set every frame, so stopping simply returns the native pose.
///
/// Observers see the same animation without any new network state: every client's ladder registry plus
/// the climber's replicated position and facing identify a body standing in a climbing lane (see
/// <see cref="Observe"/>). Their own animator then runs this same pass locally.</summary>
internal sealed class LadderClimbAnimation : IDisposable
{
    private const float FadeRate = 6f;
    private const float ObserveInterval = 0.2f;
    private const float RungDepth = 0.06f;
    private static LadderClimbAnimation? _current;

    private readonly Action<string> _log;
    private readonly Dictionary<Character, Climber> _climbers = new Dictionary<Character, Climber>();
    private readonly List<Character> _expired = new List<Character>();
    private readonly List<LadderSegment> _scratch = new List<LadderSegment>();
    private readonly RaycastHit[] _ground = new RaycastHit[16];
    private Harmony? _harmony;
    private float _observeTimer;
    private bool _failed;
    private bool _restoring;
    private int _groundMask;

    internal LadderClimbAnimation(Action<string> log) => _log = log;

    internal bool Ready { get; private set; }

    internal int Count => _climbers.Count;

    internal bool Install(string id)
    {
        try
        {
            var target = AccessTools.Method(typeof(CharacterAnimEvent), "OnAnimatorIK", new[] { typeof(int) });
            if (target == null)
            {
                _log("Custom climbing animation unavailable: the installed rig has no IK pass. The walking fallback is used.");
                return false;
            }

            _groundMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain", "vehicle");
            _harmony = new Harmony(id);
            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(LadderClimbAnimation), nameof(AfterIK)));
            _current = this;
            Ready = true;
            return true;
        }
        catch (Exception exception)
        {
            _log("Custom climbing animation unavailable (" + SafeFailure.Brief(exception) + "). The walking fallback is used.");
            Dispose();
            return false;
        }
    }

    /// <summary>The local climber's state, set by the ladder runtime every physics step it drives.</summary>
    internal void SetLocal(Player player, in LadderGeometry run, LadderFace face, float plane, float pitch,
        float handHeight, bool slide, bool attached, bool nativeAction = false)
    {
        if (!Ready || _restoring || player == null) { return; }
        Climber climber = Get(player, true, run, face, plane, pitch, handHeight);
        climber.Slide = slide;
        // A native action (hammer swing, interact) takes the limbs at once, not after a fade: the pose's weight
        // drops to zero this step and the next IK pass clears what it wrote, once (Codex re-review of 85713d6).
        if (nativeAction && !climber.Yielding) { climber.Weight = 0f; climber.Yielding = true; climber.YieldCleared = false; }
        else if (!nativeAction) { climber.Yielding = false; }
        climber.Target = attached && !nativeAction ? 1f : 0f;
        climber.Seen = 0f;
    }

    /// <summary>The local climb ended: its pose fades out over a few frames and is then forgotten.</summary>
    internal void ReleaseLocal(Player? player)
    {
        if (player != null && _climbers.TryGetValue(player, out Climber climber)) { climber.Target = 0f; climber.Released = true; }
    }

    internal void Update(float delta)
    {
        if (!Ready) { return; }
        try
        {
            _observeTimer -= delta;
            if (_observeTimer <= 0f && !_restoring)
            {
                _observeTimer = ObserveInterval;
                Observe();
            }

            _expired.Clear();
            foreach (KeyValuePair<Character, Climber> pair in _climbers)
            {
                Climber climber = pair.Value;
                climber.Weight = Mathf.MoveTowards(climber.Weight, climber.Target, FadeRate * delta);
                // Only a released pose is forgotten after its restoration pass. A candidate still waiting for its
                // second observation stays (with no IK written) until it matches or misses.
                if (pair.Key == null || (climber.Released && climber.Weight <= 0f && climber.ClearedFrames > 1))
                {
                    _expired.Add(pair.Key!);
                }
            }

            foreach (Character character in _expired) { _climbers.Remove(character); }
            if (_restoring && _climbers.Count == 0) { Ready = false; }
        }
        catch (Exception exception) { Fail(exception); }
    }

    internal void Clear() => _climbers.Clear();

    /// <summary>Observers: a remote player standing in a ladder's climbing lane, facing the rails, with no
    /// floor directly under the feet, is climbing. Replicated position and facing are enough; the climber
    /// sends nothing new. Anyone with a floor underfoot (an intermediate landing, the ground at the foot,
    /// a climber who has just stepped off) is standing, so ambiguity resolves to the native pose. A match is
    /// required on two consecutive checks to start and missing on two to stop.</summary>
    private void Observe()
    {
        Player local = Player.m_localPlayer;
        foreach (Player player in Player.GetAllPlayers())
        {
            if (player == null || player == local) { continue; }
            bool matched = TryLane(player, out LadderGeometry run, out LadderFace face, out float plane, out float pitch);
            _climbers.TryGetValue(player, out Climber? climber);
            if (matched)
            {
                bool fresh = climber == null || climber.Local;
                climber = Get(player, false, run, face, plane, pitch, Grip(player)); // Refreshes a changed run.
                if (fresh) { climber.Target = 0f; }
                climber.Matches++;
                climber.Misses = 0;
                climber.Slide = climber.VerticalSpeed < -ClimbPolicy.FastSpeed - 0.3f;
                if (climber.Matches >= 2) { climber.Target = 1f; }
            }
            else if (climber != null && !climber.Local)
            {
                climber.Matches = 0;
                if (++climber.Misses >= 2) { climber.Target = 0f; climber.Released = true; }
            }
        }
    }

    private bool TryLane(Player player, out LadderGeometry run, out LadderFace face, out float plane, out float pitch)
    {
        run = default;
        face = LadderFace.Front;
        plane = 0f;
        pitch = 1f / 3f;
        CapsuleCollider capsule = player.GetCollider();
        if (capsule == null) { return false; }
        Vector3 feet = player.transform.position;
        Vector3 forward = player.transform.forward;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Abs(capsule.transform.lossyScale.z));
        LadderSegment? found = LadderSegment.FindLane(feet, forward, radius, out face);
        if (found == null || !LadderSegment.TryRun(found, _scratch, out run)) { return false; }
        plane = (found.BackFaceDepth + found.FrontFaceDepth) * 0.5f;
        pitch = found.RungPitch;
        // Anything under any part of the capsule's footprint (a floor, its edge, a beam) reads as standing.
        return !FloorUnder(feet, radius);
    }

    /// <summary>A sphere of nine tenths of the capsule radius swept a few centimetres below the feet: it meets a
    /// floor under any part of the footprint, edges and diagonals included (Codex review of 7726fe3). Ladders
    /// and characters are ignored; an overflowing query keeps the native pose.</summary>
    private bool FloorUnder(Vector3 feet, float radius)
    {
        float sphere = radius * 0.9f;
        Vector3 origin = feet + Vector3.up * (sphere + 0.15f);
        int found = Physics.SphereCastNonAlloc(origin, sphere, Vector3.down, _ground, 0.3f, _groundMask, QueryTriggerInteraction.Ignore);
        if (found >= _ground.Length) { return true; }
        for (int index = 0; index < found; index++)
        {
            Collider collider = _ground[index].collider;
            if (collider == null || collider.isTrigger) { continue; }
            if (collider.GetComponentInParent<LadderSegment>() != null || collider.GetComponentInParent<Character>() != null) { continue; }
            return true;
        }

        return false;
    }

    private Climber Get(Player player, bool local, in LadderGeometry run, LadderFace face, float plane, float pitch, float handHeight)
    {
        if (!_climbers.TryGetValue(player, out Climber? climber) || climber.Local != local ||
            climber.Face != face || climber.Run.Bottom.HorizontalDistanceTo(run.Bottom) > 0.05f ||
            Math.Abs(climber.Run.Bottom.Y - run.Bottom.Y) > 0.05f || Math.Abs(climber.Run.Top.Y - run.Top.Y) > 0.05f ||
            climber.Released)
        {
            float weight = climber?.Weight ?? 0f;
            climber = new Climber(player, local) { Weight = weight };
            _climbers[player] = climber;
            climber.Run = run;
            climber.Face = face;
            climber.Plane = plane;
            climber.Pitch = pitch;
            climber.HandHeight = handHeight;
            climber.Cycle.Reset(player.transform.position.y, run.Bottom.Y, pitch, run.Height, handHeight, 0.05f);
            climber.LastFeet = player.transform.position.y;
        }

        return climber;
    }

    private static float Grip(Player player)
    {
        CapsuleCollider capsule = player.GetCollider();
        float height = capsule != null ? capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) : 1.85f;
        return ClimbPolicy.Finite(height) && height > 0.5f && height < 4f ? height * ClimbPolicy.GripFraction : 1.57f;
    }

    private static void AfterIK(CharacterAnimEvent __instance, int layerIndex)
    {
        LadderClimbAnimation? self = _current;
        if (self == null || !self.Ready || self._climbers.Count == 0) { return; }
        try { self.Pose(__instance); }
        catch (Exception exception) { self.Fail(exception); }
    }

    private void Pose(CharacterAnimEvent events)
    {
        Character character = events.GetComponentInParent<Character>();
        if (character == null || !_climbers.TryGetValue(character, out Climber climber)) { return; }
        Animator animator = events.GetComponent<Animator>();
        if (animator == null || !animator.isHuman) { return; }
        // Native action ownership is checked here, at the IK pass itself: a hammer swing or interact that started
        // after the last physics sample owns the limbs from its first evaluated frame (Codex review of d913e26).
        if (climber.Local && character is Player owner && NativeActionNow(owner, animator) && !climber.Yielding)
        {
            climber.Weight = 0f;
            climber.Target = 0f;
            climber.Yielding = true;
            climber.YieldCleared = false;
        }

        float weight = climber.Yielding ? 0f : Mathf.Clamp01(climber.Weight);
        if (weight <= 0f)
        {
            // A candidate that never posed writes nothing, so vanilla's own foot IK and look-at stand. A released
            // pose gets a restoration pass: every goal, hint and the look-at weight this class wrote are set back
            // to zero before the climber is forgotten.
            if (climber.Released && climber.Posed)
            {
                ZeroOwned(animator);
                climber.ClearedFrames++;
            }
            else if (climber.Yielding && climber.Posed && !climber.YieldCleared)
            {
                ZeroOwned(animator); // Once: afterwards the native pass's own foot IK and look-at stand.
                climber.YieldCleared = true;
            }
            else if (climber.Released)
            {
                climber.ClearedFrames = 2;
            }

            return;
        }

        climber.Posed = true;

        climber.ClearedFrames = 0;
        float feet = character.transform.position.y;
        if (climber.Frame != Time.frameCount)
        {
            float seconds = Time.deltaTime;
            if (seconds > 0f) { climber.VerticalSpeed = (feet - climber.LastFeet) / seconds; }
            climber.LastFeet = feet;
            climber.Frame = Time.frameCount;
            climber.Cycle.Step(feet, seconds);
        }

        LadderGeometry run = climber.Run;
        float sign = (float)climber.Face;
        Vector3 standing = new Vector3(run.StandingSide.X, 0f, run.StandingSide.Z);
        Vector3 away = standing * sign; // From the rails toward this climber.
        Vector3 toward = -away;
        Vector3 right = Vector3.Cross(Vector3.up, toward);
        Vector3 line = new Vector3(run.Bottom.X, 0f, run.Bottom.Z) + standing * climber.Plane + away * RungDepth;

        Vector3 Contact(float height, float side) => new Vector3(line.x, height, line.z) + right * side;

        Vector3 leftHand, rightHand, leftFoot, rightFoot;
        if (climber.Slide)
        {
            // Sliding: hands loosely on the rails, feet on the rails' outsides, no rung changes.
            float hands = feet + climber.HandHeight - 0.2f;
            leftHand = Contact(hands, -0.34f) + away * 0.03f;
            rightHand = Contact(hands, 0.34f) + away * 0.03f;
            leftFoot = Contact(feet + 0.2f, -0.3f) + away * 0.05f;
            rightFoot = Contact(feet + 0.2f, 0.3f) + away * 0.05f;
        }
        else
        {
            LimbCycle cycle = climber.Cycle;
            leftHand = Contact(cycle.Height(LimbCycle.LeftHand) + 0.02f, -0.2f) + away * (0.14f * cycle.Swing(LimbCycle.LeftHand));
            rightHand = Contact(cycle.Height(LimbCycle.RightHand) + 0.02f, 0.2f) + away * (0.14f * cycle.Swing(LimbCycle.RightHand));
            leftFoot = Contact(cycle.Height(LimbCycle.LeftFoot) + 0.05f, -0.14f) + away * (0.02f + 0.12f * cycle.Swing(LimbCycle.LeftFoot));
            rightFoot = Contact(cycle.Height(LimbCycle.RightFoot) + 0.05f, 0.14f) + away * (0.02f + 0.12f * cycle.Swing(LimbCycle.RightFoot));
        }

        Vector3 body = character.transform.position;
        Quaternion footRotation = Quaternion.LookRotation(toward, Vector3.up);
        Goal(animator, AvatarIKGoal.LeftHand, leftHand, weight, null);
        Goal(animator, AvatarIKGoal.RightHand, rightHand, weight, null);
        Goal(animator, AvatarIKGoal.LeftFoot, leftFoot, weight, footRotation);
        Goal(animator, AvatarIKGoal.RightFoot, rightFoot, weight, footRotation);
        float chest = climber.HandHeight * 0.8f;
        Hint(animator, AvatarIKHint.LeftElbow, body + Vector3.up * chest - right * 0.55f + away * 0.15f, weight);
        Hint(animator, AvatarIKHint.RightElbow, body + Vector3.up * chest + right * 0.55f + away * 0.15f, weight);
        Hint(animator, AvatarIKHint.LeftKnee, body + Vector3.up * 0.7f + toward * 0.6f - right * 0.2f, weight);
        Hint(animator, AvatarIKHint.RightKnee, body + Vector3.up * 0.7f + toward * 0.6f + right * 0.2f, weight);
        animator.SetLookAtPosition(Contact(feet + climber.HandHeight + (climber.VerticalSpeed < -0.1f ? -0.6f : 0.6f), 0f));
        animator.SetLookAtWeight(0.6f * weight, 0f, 0.7f, 0.7f, 0.5f);
    }

    private static readonly int AttackTag = ZSyncAnimation.GetHash("attack");

    /// <summary>Reads the animator being evaluated right now: the current state, or the next state while a
    /// transition runs, on every layer, for the game's attack tag. Player.InAttack caches its answer per update
    /// tick and can be stale at this pass (Codex review of 825f16c). In build mode no combat attack starts, so an
    /// attack there is the hammer's. Interact animations are minor actions.</summary>
    private static bool NativeActionNow(Player player, Animator animator)
    {
        try
        {
            if (player.InMinorAction()) { return true; }
            if (!player.InPlaceMode()) { return false; }
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                int tag = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer).tagHash : animator.GetCurrentAnimatorStateInfo(layer).tagHash;
                if (tag == AttackTag) { return true; }
            }

            return false;
        }
        catch { return true; } // Unknown state: leave the limbs to the game.
    }

    private static readonly AvatarIKGoal[] Goals = { AvatarIKGoal.LeftHand, AvatarIKGoal.RightHand, AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot };
    private static readonly AvatarIKHint[] Hints = { AvatarIKHint.LeftElbow, AvatarIKHint.RightElbow, AvatarIKHint.LeftKnee, AvatarIKHint.RightKnee };

    private static void ZeroOwned(Animator animator)
    {
        foreach (AvatarIKGoal goal in Goals)
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
        }

        foreach (AvatarIKHint hint in Hints) { animator.SetIKHintPositionWeight(hint, 0f); }
        animator.SetLookAtWeight(0f);
    }

    private static void Goal(Animator animator, AvatarIKGoal goal, Vector3 position, float weight, Quaternion? rotation)
    {
        animator.SetIKPosition(goal, position);
        animator.SetIKPositionWeight(goal, weight);
        if (rotation.HasValue)
        {
            animator.SetIKRotation(goal, rotation.Value);
            animator.SetIKRotationWeight(goal, 0.6f * weight);
        }
        else
        {
            animator.SetIKRotationWeight(goal, 0f);
        }
    }

    private static void Hint(Animator animator, AvatarIKHint hint, Vector3 position, float weight)
    {
        animator.SetIKHintPosition(hint, position);
        animator.SetIKHintPositionWeight(hint, weight);
    }

    /// <summary>A presentation fault never touches movement. The first fault keeps the patch for one
    /// restoration pass (every tracked climber's owned weights written to zero), then stops; a second fault
    /// stops at once.</summary>
    private void Fail(Exception exception)
    {
        if (_failed)
        {
            _climbers.Clear();
            Ready = false;
            return;
        }

        _failed = true;
        _restoring = true;
        foreach (Climber climber in _climbers.Values) { climber.Target = 0f; climber.Weight = 0f; climber.Released = true; }
        try { _log("Custom climbing animation stopped (" + SafeFailure.Brief(exception) + "); the walking fallback is used."); }
        catch { /* Presentation failure never affects movement. */ }
    }

    /// <summary>BepInEx destroys plugins only when the application quits, so this runs after the last frame
    /// any climber would be drawn in; there is no later IK pass to restore. Releases during play (exits, jumps,
    /// deaths, a disabled setting, faults) all go through the restoration pass above instead.</summary>
    public void Dispose()
    {
        Ready = false;
        _climbers.Clear();
        try { _harmony?.UnpatchSelf(); } catch { /* Own patch only. */ }
        _harmony = null;
        if (ReferenceEquals(_current, this)) { _current = null; }
    }

    private sealed class Climber
    {
        internal Climber(Player player, bool local) { Player = player; Local = local; }
        internal Player Player { get; }
        internal bool Local { get; }
        internal LadderGeometry Run;
        internal LadderFace Face;
        internal float Plane;
        internal float Pitch;
        internal float HandHeight;
        internal readonly LimbCycle Cycle = new LimbCycle();
        internal float Weight;
        internal float Target;
        internal bool Slide;
        internal bool Released;
        internal bool Posed;
        internal float Seen;
        internal float LastFeet;
        internal float VerticalSpeed;
        internal int Frame = -1;
        internal int Matches;
        internal int Misses;
        internal int ClearedFrames;
        internal bool Yielding;
        internal bool YieldCleared;
    }
}
