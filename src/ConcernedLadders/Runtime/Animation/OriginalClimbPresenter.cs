using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Animation;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>Original procedural contact/body animation on the player's installed humanoid. The only input
/// is the motor's accepted read-only frame. No input, movement, collision, cost, skill or equipment authority.
/// Observer publication requires a separately reviewed transport; proximity inference is deliberately absent.</summary>
internal sealed partial class OriginalClimbPresenter : IClimbPresenter
{
    private static OriginalClimbPresenter? _current;
    private readonly Action<string> _log;
    private readonly Dictionary<CharacterAnimEvent, Actor> _actors = new Dictionary<CharacterAnimEvent, Actor>();
    private readonly List<CharacterAnimEvent> _remove = new List<CharacterAnimEvent>();
    private Harmony? _harmony;
    private bool _ready, _disposing;
    private string? _failure;
    private Player? _closedPlayer;
    private int _closedSession;

    internal OriginalClimbPresenter(Action<string> log) => _log = log;

    public bool Install(string harmonyId)
    {
        if (_harmony != null || _disposing) { return false; }
        try
        {
            if (_current != null) { throw new InvalidOperationException("Another original presenter is still restoring its rig."); }
            var target = AccessTools.Method(typeof(CharacterAnimEvent), "OnAnimatorIK", new[] { typeof(int) });
            if (target == null) { throw new InvalidOperationException("Installed player has no supported native IK callback."); }
            Patches? patches = Harmony.GetPatchInfo(target);
            if (patches != null)
            {
                foreach (string owner in patches.Owners)
                { if (owner == "com.theconcernedcat.valheim.concernedladders.pose") { throw new InvalidOperationException("Retire the rejected ladder pose before installing the original presenter."); } }
            }
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(typeof(OriginalClimbPresenter), nameof(BeforeNativeIK)),
                postfix: new HarmonyMethod(typeof(OriginalClimbPresenter), nameof(AfterNativeIK)));
            _current = this; _ready = true;
            return true;
        }
        catch (Exception error) { Log("Original climbing presentation unavailable: " + SafeFailure.Brief(error)); Dispose(); return false; }
    }

    public void Publish(Player player, in ClimbPresentationFrame frame)
    {
        if (_failure != null) { throw new InvalidOperationException("Original presentation disabled after an IK fault: " + _failure); }
        if (!_ready || _disposing || !LocalOwner(player)) { return; }
        if (ReferenceEquals(player, _closedPlayer) && frame.Session == _closedSession) { return; }
        try
        {
            Validate(frame);
            Actor? actor = null;
            foreach (Actor existing in _actors.Values) { if (ReferenceEquals(existing.Rig.Player, player)) { actor = existing; break; } }
            if (actor == null)
            {
                if (_actors.Count >= 2) { throw new InvalidOperationException("Unexpected local presentation actor count."); }
                var rig = OriginalRigBinding.Create(player);
                actor = new Actor(rig); _actors.Add(rig.Events, actor);
            }
            if (!actor.Rig.Valid) { throw new InvalidOperationException("Player rig/controller/scale changed; a fresh binding is required."); }
            bool fresh = !actor.Active || actor.Frame.Session != frame.Session || actor.Frame.Discontinuity != frame.Discontinuity ||
                actor.TuningVersion != ClimbTuning.Version;
            if (!fresh && frame.Sample <= actor.Frame.Sample) { return; } //one accepted sample at most once.
            float dt = fresh ? 0f : frame.SampleTime - actor.Frame.SampleTime;
            if (dt < 0f || dt > .5f) { throw new InvalidOperationException("Accepted presentation clock changed or stalled."); }
            float travel = fresh ? 0f : frame.Travel - actor.Frame.Travel;
            bool wasSlide = !fresh && actor.Frame.Motion == PresentedMotion.Slide;
            bool slide = frame.Motion == PresentedMotion.Slide;
            if (fresh || (wasSlide && !slide))
            {
                float fitFeet = frame.Motion == PresentedMotion.GroundMount || frame.Motion == PresentedMotion.AirCatch ? frame.TransitionTo.Y : frame.Feet.Y;
                if (!actor.Gait.Bind(ClimbTuning.Gait, frame.Run.Bottom.Y + frame.RungOrigin, frame.Run.RungPitch, 0, frame.LastRung,
                        actor.Rig.ShoulderHeight - ClimbTuning.PelvisDrop, fitFeet))
                { throw new InvalidOperationException("No real rung span can bind the original contacts."); }
                if (fresh)
                {
                    if (actor.Frame.Session != frame.Session) { actor.Fit.Reset(); }
                    actor.Age = 0f; actor.SlideBlend = 0f; actor.TuningVersion = ClimbTuning.Version;
                }
            }
            else if (frame.GeometryRevision != actor.Frame.GeometryRevision &&
                !actor.Gait.RefreshBounds(frame.Run.Bottom.Y + frame.RungOrigin, 0, frame.LastRung))
            { throw new InvalidOperationException("Run refresh removed a contact or changed rung alignment."); }
            bool loop = frame.Motion == PresentedMotion.Climb || frame.Motion == PresentedMotion.Fast || frame.Motion == PresentedMotion.Hold;
            if (loop && !actor.Gait.Update(frame.Feet.Y, dt, (frame.MenuHold || frame.Motion == PresentedMotion.Hold) && Mathf.Abs(travel) < .000001f))
            { throw new InvalidOperationException("Invalid completed-travel sample for original contacts."); }
            actor.SlideBlend = Mathf.MoveTowards(actor.SlideBlend, slide ? 1f : 0f, dt / (slide ? .08f : .12f));
            actor.Age += dt; actor.Frame = frame; actor.Active = true; actor.Seen = Time.time;
        }
        catch (Exception error)
        {
            End(player, ClimbEnd.Fault);
            // The motor's presenter boundary handles this and keeps gameplay running. No motor call here.
            throw new InvalidOperationException("Original animation binding/frame failed: " + SafeFailure.Brief(error));
        }
    }

    public void End(Player? player, ClimbEnd reason)
    {
        foreach (Actor actor in _actors.Values)
        {
            if (ReferenceEquals(player, null) || ReferenceEquals(actor.Rig.Player, player))
            {
                if (actor.Active) { actor.Fit.Report(this, Time.time, true); }
                actor.Active = false;
                _closedPlayer = actor.Rig.Player; _closedSession = actor.Frame.Session;
            }
        }
        // Stops new pose writes synchronously. Pending IK inputs restore in the next supported IK callback,
        // never through illegal Update-time setters, and no animation completion delays the motor's impulse.
    }

    public void Update(float deltaTime)
    {
        _remove.Clear();
        foreach (KeyValuePair<CharacterAnimEvent, Actor> pair in _actors)
        {
            Actor actor = pair.Value;
            if (!actor.Rig.Valid || !LocalOwner(actor.Rig.Player) || (Time.time - actor.Seen > 1f)) { actor.Active = false; }
            if (pair.Key == null || actor.Rig.Player == null || (!actor.Active && !actor.Lease.Pending)) { _remove.Add(pair.Key!); } // Unity's destroyed-object null; the dictionary key is still removable.
        }
        foreach (CharacterAnimEvent key in _remove) { _actors.Remove(key); }
        if (_closedPlayer == null) { _closedPlayer = null; }
    }

    private static void BeforeNativeIK(CharacterAnimEvent __instance, int layerIndex)
    {
        OriginalClimbPresenter? self = _current;
        if (self == null || layerIndex != 0 || !self._actors.TryGetValue(__instance, out Actor actor)) { return; }
        try { actor.Lease.RestoreInIK(actor.Rig.Animator); }
        catch (Exception error) { self.Fail("Original IK restoration failed", error); }
    }

    private static void AfterNativeIK(CharacterAnimEvent __instance, int layerIndex)
    {
        OriginalClimbPresenter? self = _current;
        if (self == null || !self._actors.TryGetValue(__instance, out Actor actor)) { return; }
        if (layerIndex != 0)
        {
            // A later layer's IK pass: the game's foot IK just ran again. Keep this frame's owned goals.
            if (actor.Active && self._ready && !self._disposing)
            {
                try { actor.Lease.ReassertInIK(actor.Rig.Animator); }
                catch (Exception error) { self.Fail("Original climbing pose stopped", error); }
            }

            return;
        }

        if (actor.Active && self._ready && !self._disposing && LocalOwner(actor.Rig.Player))
        {
            try { self.PoseInIK(actor); }
            catch (Exception error)
            {
                self.Fail("Original climbing pose stopped", error);
                // Undo this pass's partial presentation before the solver consumes it.
                try { actor.Lease.RestoreInIK(actor.Rig.Animator); } catch { }
            }
        }
        if (!actor.Active && !actor.Lease.Pending) { self._actors.Remove(__instance); }
    }

    private static bool LocalOwner(Player player)
    {
        if (player == null || player != Player.m_localPlayer) { return false; }
        ZNetView view = player.GetComponent<ZNetView>();
        return view != null && view.IsValid() && view.IsOwner();
    }

    private static void Validate(in ClimbPresentationFrame frame)
    {
        if (frame.Sample < 0 || !SameSideGait.Finite(frame.SampleTime) || !SameSideGait.Finite(frame.Travel) ||
            !SameSideGait.Finite(frame.StepTravel) || !frame.Feet.IsFinite || !frame.Run.IsClimbable ||
            !frame.Run.Bottom.IsFinite || !frame.Run.Top.IsFinite || !frame.TransitionFrom.IsFinite || !frame.TransitionTo.IsFinite ||
            !SameSideGait.Finite(frame.RungPlane) || !SameSideGait.Finite(frame.RungOrigin) ||
            !SameSideGait.Finite(frame.Transition) || frame.LastRung < 1 || frame.LastRung > 4096 ||
            (frame.Face != LadderFace.Front && frame.Face != LadderFace.Rear) ||
            (int)frame.Motion < 0 || (int)frame.Motion > (int)PresentedMotion.StepOff ||
            ((int)frame.NativeAction & ~15) != 0)
        { throw new InvalidOperationException("Non-finite or unsupported accepted presentation frame."); }
    }

    public void Dispose()
    {
        if (_disposing) { return; }
        _ready = false; _disposing = true; End(null, ClimbEnd.Disabled);
        foreach (Actor actor in _actors.Values)
        {
            if (actor.Rig.Animator == null || !actor.Lease.Pending) { continue; }
            try { OriginalIKCleanup.Schedule(actor.Rig.Animator, actor.Lease); }
            catch (Exception error) { Log("Original rig cleanup could not be scheduled: " + SafeFailure.Brief(error)); }
        }
        _actors.Clear(); _remove.Clear(); _closedPlayer = null;
        // The old presenter releases all global registration immediately, even if its rig will never
        // evaluate again. Pending values belong only to the small rig-lifetime cleanup component.
        try { _harmony?.UnpatchSelf(); } catch { }
        _harmony = null;
        if (ReferenceEquals(_current, this)) { _current = null; }
    }

    private void Fail(string context, Exception error)
    {
        if (_failure != null) { return; }
        _failure = SafeFailure.Brief(error); _ready = false;
        End(null, ClimbEnd.Fault);
        Log(context + ": " + _failure);
    }

    private void Log(string message) { try { _log(message); } catch { } }

    private sealed class Actor
    {
        internal Actor(OriginalRigBinding rig) { Rig = rig; }
        internal readonly OriginalRigBinding Rig;
        internal readonly SameSideGait Gait = new SameSideGait();
        internal readonly FitCounters Fit = new FitCounters();
        internal readonly bool[] Asked = new bool[4];
        internal readonly Vector3[] LastPivot = new Vector3[4];
        internal int TuningVersion = -1;
        internal readonly NativeIKLease Lease = new NativeIKLease();
        internal ClimbPresentationFrame Frame;
        internal bool Active;
        internal float Seen, Age, SlideBlend;
    }
}
