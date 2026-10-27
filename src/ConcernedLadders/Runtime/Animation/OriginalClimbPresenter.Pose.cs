using System;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Animation;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

internal sealed partial class OriginalClimbPresenter
{
    private enum NativeKind { None, Attack, Minor }

    private void PoseInIK(Actor actor)
    {
        OriginalRigBinding rig = actor.Rig;
        if (!rig.Valid || !actor.Gait.Ready) { actor.Active = false; return; }
        rig.BindBodyInIK();
        Animator animator = rig.Animator;
        ClimbPresentationFrame frame = actor.Frame;
        Vector3 outward = new Vector3(frame.Run.StandingSide.X, 0f, frame.Run.StandingSide.Z) * (float)frame.Face;
        Vector3 toward = -outward;
        Vector3 right = Vector3.Cross(Vector3.up, toward);
        Vector3 feet = rig.Player.transform.position; //rendered/interpolated root; never written by presentation.
        NativeActionParts native = frame.NativeAction;
        // A native action can start after the last producer tick: reconcile current/next native tags at this actual
        // IK pass. Owner decision 2026-10-26: with the building tool out, only the hammer (right) arm swings natively,
        // from the first frame; the body, legs and left hand keep climbing. Any other action (an interact) owns both
        // arms, the upper body and the head.
        NativeKind kind = NativeActionNow(animator);
        if (kind == NativeKind.Attack && frame.HammerEquipped) { native |= NativeActionParts.RightArm; }
        else if (kind != NativeKind.None) { native |= NativeActionParts.LeftArm | NativeActionParts.RightArm | NativeActionParts.UpperBody | NativeActionParts.Head; }

        actor.Fit.Begin(this, frame.Motion, Time.time);
        // The authored transition playing now (owner spec 2026-10-27 section 5), if any: an entry (mount, catch, top
        // entry) plays from the attachment until it completes; an exit (top, side, bottom) plays over the runtime's own
        // route progress, so visual contacts never lead the real capsule.
        TransitionAction? action = actor.Transition;
        float progress = actor.TransitionProgress;
        bool exit = action != null && (action.Kind == TransitionKind.TopExit || action.Kind == TransitionKind.SideExit || action.Kind == TransitionKind.BottomStepOff);
        float bodyWeight = action != null ? action.BodyWeightAt(progress) : 1f;
        if ((native & NativeActionParts.UpperBody) != 0) { bodyWeight = 0f; }
        float sway = actor.Gait.Sway * (1f - actor.SlideBlend);
        float lean = Mathf.Lerp(ClimbTuning.Lean, ClimbTuning.SlideLean, actor.SlideBlend);
        if (action != null) { lean += action.LeanAt(progress); }
        Vector3 landing = Vector(frame.TransitionTo), start = Vector(frame.TransitionFrom);
        Vector3 across = landing - start; across.y = 0f;
        Vector3 travel = across.sqrMagnitude > .0004f ? across.normalized : toward;
        // The authored body curve: the visible body rises more slowly while a foot recovers and catches up as the leg
        // extends (bounded, zero mean, presentation only; the gameplay body moves steadily). Faded in and out with travel
        // so a stop or a reversal never jumps.
        bool moving = frame.Motion == PresentedMotion.Climb || frame.Motion == PresentedMotion.Fast;
        actor.Gait.Body(frame.Feet.Y, out float bodyUp, out float bodyToward, out float bodyLean);
        actor.BodyBlend = Mathf.MoveTowards(actor.BodyBlend, moving ? 1f : 0f, Time.deltaTime / .25f);
        float blend = LimbGait.Ease(actor.BodyBlend);
        lean += bodyLean * blend;
        float actionUp = action != null ? action.BodyUpAt(progress) : 0f, actionToward = action != null ? action.BodyTowardAt(progress) : 0f;
        // Over the top (in game 2026-10-27: hands ran out of reach on the last rungs): the run leaves nothing above the
        // hands, so the climber crouches and folds the chest forward over the top while the hands keep the rail tops or
        // the floor lip; a descending entry starts from the same crouch on the floor. By feet height, so it is continuous
        // across climb, hold, exit and entry; faded with the body weight like every body offset.
        float overTop = Smooth01((feet.y - (frame.Run.Top.Y - ClimbTuning.TopApproachFrom)) / ClimbTuning.TopApproachSpan);
        actionUp -= ClimbTuning.TopApproachDrop * overTop;
        actionToward += ClimbTuning.TopApproachToward * overTop;
        lean += ClimbTuning.TopApproachLean * overTop;
        Vector3 pelvis = feet + Vector3.up * (rig.PelvisHeight - ClimbTuning.PelvisDrop + bodyUp * blend + actionUp) +
                         toward * (ClimbTuning.PelvisToward + bodyToward * blend + actionToward) +
                         right * (ClimbTuning.SwayShift * sway);
        Quaternion bodyRotation = Quaternion.LookRotation(toward, Vector3.up) *
            Quaternion.AngleAxis(lean, Vector3.right) * Quaternion.AngleAxis(-sway * ClimbTuning.SwayTilt, Vector3.forward) * rig.BodyInPlayer;
        Vector3 bodyPosition = pelvis + bodyRotation * rig.BodyFromPelvis;
        Vector3 nativeBody = animator.bodyPosition;
        Quaternion nativeRotation = animator.bodyRotation;
        actor.Lease.BodyInIK(animator, bodyPosition, bodyRotation, bodyWeight);
        Vector3 posedBody = animator.bodyPosition;
        Quaternion bodyDelta = animator.bodyRotation * Quaternion.Inverse(nativeRotation);

        for (int limb = 0; limb < 4; limb++) { actor.Asked[limb] = false; actor.Record[limb] = default; }
        for (int limb = 0; limb < 4; limb++)
        {
            bool hand = limb < 2;
            if ((limb == 0 && (native & (NativeActionParts.LeftArm | NativeActionParts.UpperBody)) != 0) ||
                (limb == 1 && (native & (NativeActionParts.RightArm | NativeActionParts.UpperBody)) != 0))
            {
                actor.Record[limb] = new ContactRecord { Native = true };
                continue;
            }
            OriginalLimbContact contact = actor.Gait.Contact(limb);
            float side = limb == 0 || limb == 2 ? -1f : 1f;
            Vector3 root = posedBody + bodyDelta * (rig.Roots[limb].position - nativeBody);
            float span = Math.Min(ClimbPresentationFrame.HalfWidth - .04f, hand ? ClimbTuning.HandSpan : ClimbTuning.FootSpan);
            // Fit the palm / sole to this section's real rung: a wooden box rung (calibrated offsets) or a round metal
            // rung (palm wrapped over its top-front, sole on its crown).
            RungProfile profile = frame.ProfileAt(contact.Height);
            float above, outOffset;
            if (!profile.Round)
            {
                above = hand ? ClimbTuning.HandAbove + profile.HalfHeight - ClimbPresentationFrame.HalfHeight : ClimbTuning.FootAbove + profile.HalfHeight - ClimbPresentationFrame.HalfHeight;
                outOffset = hand ? ClimbTuning.HandOut : ClimbTuning.FootOut;
            }
            else if (hand)
            {
                float wrap = ClimbTuning.HandWrapDegrees * Mathf.Deg2Rad, reachOut = profile.Radius + ClimbTuning.PalmThickness;
                above = reachOut * Mathf.Cos(wrap);
                outOffset = reachOut * Mathf.Sin(wrap);
            }
            else
            {
                above = profile.Radius + ClimbTuning.FootAbove - ClimbPresentationFrame.HalfHeight;
                outOffset = ClimbTuning.FootOut;
            }
            Vector3 surface = RungSurface(frame, contact.Height + above, outOffset, span * side);
            if (contact.Swing > 0f)
            {
                // The authored swing arc for this limb: toward its own side, away from the rungs, upward.
                actor.Gait.Arc(limb, contact.Swing, out float arcLat, out float arcOut, out float arcUp);
                surface += right * (side * arcLat) + outward * arcOut + Vector3.up * arcUp;
            }
            float railBlend = 0f;
            if (hand)
            {
                // Near the top there is no rung left within reach above: the hand takes the rail instead, at a height
                // the arm reaches, never above the rail's real top.
                float rail = Smooth01((root.y - contact.Height - ClimbTuning.RailFromBelow) / ClimbTuning.RailBlend);
                if (rail > 0f)
                {
                    float railY = Mathf.Min(frame.Run.Top.Y - .04f, root.y - ClimbTuning.RailBelowShoulder);
                    Vector3 railGrip = RungSurface(frame, railY, ClimbTuning.RailOut, side * ClimbTuning.RailSide);
                    surface = Vector3.Lerp(surface, railGrip, rail);
                    railBlend = rail;
                }
            }

            if (actor.SlideBlend > 0f)
            {
                // A distinct rail-braking pose: these contacts slide by design, not fake planted rung grips.
                float height = feet.y + (hand ? rig.ShoulderHeight - .16f : .24f);
                Vector3 rail = RungSurface(frame, height, ClimbPresentationFrame.HalfDepth + (hand ? .02f : .11f), side * (hand ? .32f : .25f));
                surface = Vector3.Lerp(surface, rail, LimbGait.Ease(actor.SlideBlend));
            }

            float weight = 1f, toDestination = 0f;
            if (action != null)
            {
                // Authored transition channels: how much this limb is ours, how far from its ladder contact to its
                // destination contact, and its extra arc. Destinations come only from the runtime's validated route:
                // feet stand on the landing (or start on the floor for a top entry), hands press onto the floor beyond
                // the lip on a top exit. No floor query or landing choice here.
                weight = action.WeightAt(limb, progress);
                toDestination = action.ReachAt(limb, progress);
                if (toDestination > 0f)
                {
                    Vector3 destination;
                    if (hand)
                    {
                        // Palms flat on the floor just past the lip, where the ladder meets it (16-59-05, 17-01-40).
                        float floorY = action.Kind == TransitionKind.TopEntry ? start.y : landing.y;
                        destination = RungSurface(frame, floorY + ClimbTuning.PalmThickness, -ClimbTuning.LipOnto, side * ClimbTuning.HandSpan);
                    }
                    else if (action.Kind == TransitionKind.TopExit || action.Kind == TransitionKind.TopEntry)
                    {
                        // Feet step onto (or off) the floor just past the lip; the game's walk carries the rest.
                        float floorY = action.Kind == TransitionKind.TopEntry ? start.y : landing.y;
                        destination = RungSurface(frame, floorY, -(ClimbTuning.LipOnto + ClimbTuning.FootOnto), side * ClimbTuning.FootSpan);
                    }
                    else
                    {
                        destination = (action.Kind == TransitionKind.TopEntry ? start : landing) +
                                      Vector3.Cross(Vector3.up, travel) * (side * -ClimbTuning.FootSpan);
                    }
                    surface = Vector3.Lerp(surface, destination, toDestination);
                }

                surface += right * (side * action.ArcAt(limb, progress, 0)) + outward * action.ArcAt(limb, progress, 1) + Vector3.up * action.ArcAt(limb, progress, 2);
            }

            Quaternion rotation;
            Vector3 pivot;
            if (hand) { pivot = GripGoal(surface, Vector3.Slerp(toward, travel, toDestination), Mathf.Lerp(ClimbTuning.HandFingerPitch, 90f, toDestination), out rotation); }
            else
            {
                // A humanoid foot goal: forward is the toes, up is up. The ankle sits behind and above the ball of the foot.
                // On a landing the foot turns to the walking direction.
                Vector3 forward = Vector3.Slerp(toward, travel, toDestination);
                rotation = Quaternion.LookRotation(forward, Vector3.up);
                pivot = surface - forward * ClimbTuning.FootBallForward + Vector3.up * ClimbTuning.FootAnkleHeight;
            }

            float reach = Vector3.Distance(root, pivot);
            // A contact is planted when the gait holds it on a rung and nothing else (entry, exit, rail, slide) blends
            // it: that is when the 2 cm error / 1 cm drift targets apply.
            bool planted = contact.Planted && action == null && railBlend <= 0f && actor.SlideBlend <= 0f && weight >= .999f;
            var record = new ContactRecord
            {
                Has = true, Target = surface, Planted = planted, Requested = weight, Ratio = reach / rig.Reach[limb],
                Root = root, Chain = rig.Reach[limb], Progress = action != null ? progress : -1f,
                RungCentre = RungSurface(frame, contact.Height, 0f, span * side), Hand = hand, Outward = outward, Profile = profile,
            };
            if (weight <= .01f) { actor.Record[limb] = record; continue; } // Handed to the native pose on purpose (mount/exit windows).
            actor.Fit.Sample(limb, reach / rig.Reach[limb], root, feet, frame, outward);
            if (reach > rig.Reach[limb] * ClimbTuning.ReachLimit)
            {
                actor.Fit.Skip(limb, planted);
                record.Skipped = true;
                actor.Record[limb] = record;
                continue; //never stretch a limb or pretend the unreachable target is a successful plant.
            }

            // A contact nearing the limit hands over to the native pose gradually instead of popping off at it.
            weight *= 1f - Smooth01((reach / rig.Reach[limb] - ClimbTuning.FadeFrom) / (ClimbTuning.ReachLimit - ClimbTuning.FadeFrom));
            record.Effective = weight;
            actor.Record[limb] = record;

            actor.Lease.GoalInIK(animator, limb, pivot, rotation, weight);
            if (weight >= .999f) { actor.Asked[limb] = true; actor.LastPivot[limb] = pivot; }
            Vector3 hint = hand ? root + outward * ClimbTuning.ElbowOut + right * (side * ClimbTuning.ElbowSide) - Vector3.up * ClimbTuning.ElbowDown :
                root + toward * ClimbTuning.KneeToward + right * (side * ClimbTuning.KneeSide) - Vector3.up * ClimbTuning.KneeDown;
            actor.Lease.HintInIK(animator, limb, hint, weight * .75f);
        }

        actor.Fit.Report(this, Time.time, false);
        // Native look-at and equipment remain owned by the game. No controller parameters, animation speed,
        // root transforms, rigidbodies, capsule, gravity, input, skill or stamina are written.
    }

    /// <summary>A palm resting on a rung, fingers tilted from vertical toward the ladder and the palm facing it.
    /// Humanoid hand goals are in the avatar's normalized space, not the hand bone's. Measured in game 2026-10-26
    /// (solved finger bones against the goal): the goal's forward runs along the fingers and its up is the back of
    /// the hand. Returns the wrist pivot for a palm centre at <paramref name="contact"/>.</summary>
    private static Vector3 GripGoal(Vector3 contact, Vector3 toward, float fingerPitch, out Quaternion rotation)
    {
        float pitch = fingerPitch * Mathf.Deg2Rad;
        Vector3 fingers = Vector3.up * Mathf.Cos(pitch) + toward * Mathf.Sin(pitch);
        Vector3 palm = toward * Mathf.Cos(pitch) - Vector3.up * Mathf.Sin(pitch);
        rotation = Quaternion.LookRotation(fingers, -palm * ClimbTuning.HandNormalSign);
        return contact - fingers * ClimbTuning.HandPalmAlong - palm * ClimbTuning.HandPalmDepth;
    }

    private static Vector3 RungSurface(in ClimbPresentationFrame frame, float worldY, float faceOffset, float lateral) =>
        Vector(OriginalRungSurface.At(frame.Run, frame.Face, frame.RungPlane, worldY, faceOffset, lateral));

    private static float Smooth01(float value) { float t = Mathf.Clamp01(value); return t * t * (3f - 2f * t); }

    private static float Window(float progress, float from, float to) => LimbGait.Ease((progress - from) / (to - from));
    private static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);

    private static NativeKind NativeActionNow(Animator animator)
    {
        // Player.InAttack caches by MonoUpdaters.UpdateCount and can be stale at this boundary.
        // These actual native tags were inspected in Humanoid/Player; do not touch their cached fields.
        NativeKind kind = NativeKind.None;
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            kind = Strongest(kind, Tag(animator.GetCurrentAnimatorStateInfo(layer)));
            if (animator.IsInTransition(layer)) { kind = Strongest(kind, Tag(animator.GetNextAnimatorStateInfo(layer))); }
        }

        return kind;
    }

    private static NativeKind Tag(AnimatorStateInfo state) =>
        state.IsTag("minoraction") || state.IsTag("minoraction_fast") ? NativeKind.Minor : state.IsTag("attack") ? NativeKind.Attack : NativeKind.None;

    private static NativeKind Strongest(NativeKind a, NativeKind b) => a == NativeKind.Minor || b == NativeKind.Minor ? NativeKind.Minor :
        a == NativeKind.Attack || b == NativeKind.Attack ? NativeKind.Attack : NativeKind.None;

    /// <summary>Bounded fitting counters (owner correction 2026-10-26): per limb, how many IK passes evaluated a contact,
    /// how many skipped it as unreachable and the worst and mean fraction of the measured chain it needed, plus where
    /// the posed shoulders and hips sat relative to the root and the rung plane. One summary line at most every
    /// three seconds while presenting, and one at the end; at most 40 lines per attachment.</summary>
    internal sealed class FitCounters
    {
        private readonly int[] _count = new int[4], _skips = new int[4];
        private readonly float[] _worst = new float[4], _sum = new float[4], _rootUp = new float[4], _rootOut = new float[4];
        private readonly float[] _error = new float[4], _errorSum = new float[4];
        private readonly int[] _errorCount = new int[4];
        private readonly int[] _planted = new int[4], _plantedSkips = new int[4], _plantedFades = new int[4];
        private readonly float[] _patchWorst = new float[4], _patchSum = new float[4], _gapMin = new float[4], _gapMax = new float[4], _drift = new float[4];
        private PresentedMotion _motion;
        private float _since;
        private int _lines;

        internal void Reset()
        {
            Clear();
            _lines = 0;
        }

        /// <summary>Counters cover one motion at a time: a change of motion first reports what was gathered.</summary>
        internal void Begin(OriginalClimbPresenter owner, PresentedMotion motion, float now)
        {
            if (motion == _motion && _since > 0f) { return; }
            if (Total > 0) { Report(owner, now, true); }
            _motion = motion;
            _since = now;
        }

        internal void Sample(int limb, float ratio, Vector3 root, Vector3 feet, in ClimbPresentationFrame frame, Vector3 outward)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) { return; }
            _count[limb]++;
            _sum[limb] += ratio;
            _worst[limb] = Math.Max(_worst[limb], ratio);
            _rootUp[limb] = root.y - feet.y;
            Vector3 plane = Vector(OriginalRungSurface.At(frame.Run, frame.Face, frame.RungPlane, root.y, 0f, 0f));
            _rootOut[limb] = Vector3.Dot(root - plane, outward);
        }

        internal void Skip(int limb, bool planted)
        {
            _skips[limb]++;
            if (planted) { _plantedSkips[limb]++; }
        }

        /// <summary>A planted contact measured after the solve: patch error to its target, surface gap (negative =
        /// penetration), drift since it was planted, and whether reach fading weakened it.</summary>
        internal void Planted(int limb, float patchError, float gap, float drift, bool faded)
        {
            if (float.IsNaN(patchError) || float.IsNaN(gap) || float.IsNaN(drift)) { return; }
            if (_planted[limb] == 0) { _gapMin[limb] = gap; _gapMax[limb] = gap; }
            _planted[limb]++;
            _patchSum[limb] += patchError;
            _patchWorst[limb] = Math.Max(_patchWorst[limb], patchError);
            _gapMin[limb] = Math.Min(_gapMin[limb], gap);
            _gapMax[limb] = Math.Max(_gapMax[limb], gap);
            _drift[limb] = Math.Max(_drift[limb], drift);
            if (faded) { _plantedFades[limb]++; }
        }

        /// <summary>Distance between the solved end bone and the goal asked for at full weight on the previous pass.</summary>
        internal void Solved(int limb, float error)
        {
            if (float.IsNaN(error) || float.IsInfinity(error)) { return; }
            _errorCount[limb]++;
            _errorSum[limb] += error;
            _error[limb] = Math.Max(_error[limb], error);
        }

        /// <summary>One line at most every three seconds of a long motion, one when the motion ends; 40 per attachment.</summary>
        internal void Report(OriginalClimbPresenter owner, float now, bool final)
        {
            if (Total == 0 || (!final && now - _since < 3f)) { return; }
            if (_lines < 40)
            {
                _lines++;
                var text = new System.Text.StringBuilder("Climb fit ").Append(_motion)
                    .Append(' ').Append((now - _since).ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
                string[] names = { "LH", "RH", "LF", "RF" };
                for (int limb = 0; limb < 4; limb++)
                {
                    float mean = _count[limb] == 0 ? 0f : _sum[limb] / _count[limb];
                    text.Append(" | ").Append(names[limb]).Append(" n=").Append(_count[limb]).Append(" skip=").Append(_skips[limb])
                        .Append(" worst=").Append(_worst[limb].ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" mean=").Append(mean.ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" up=").Append(_rootUp[limb].ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" out=").Append(_rootOut[limb].ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" err=").Append(_error[limb].ToString("0.000", CultureInfo.InvariantCulture))
                        .Append('/').Append((_errorCount[limb] == 0 ? 0f : _errorSum[limb] / _errorCount[limb]).ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(" planted=").Append(_planted[limb])
                        .Append(" patch=").Append(_patchWorst[limb].ToString("0.000", CultureInfo.InvariantCulture))
                        .Append('/').Append((_planted[limb] == 0 ? 0f : _patchSum[limb] / _planted[limb]).ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(" gap=").Append(_gapMin[limb].ToString("0.000", CultureInfo.InvariantCulture))
                        .Append("..").Append(_gapMax[limb].ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(" drift=").Append(_drift[limb].ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(" pfade=").Append(_plantedFades[limb]).Append(" pskip=").Append(_plantedSkips[limb]);
                }

                owner.Log(text.ToString());
            }

            Clear();
            _since = now;
        }

        private int Total => _count[0] + _count[1] + _count[2] + _count[3];

        private void Clear()
        {
            Array.Clear(_count, 0, 4); Array.Clear(_skips, 0, 4); Array.Clear(_worst, 0, 4); Array.Clear(_sum, 0, 4);
            Array.Clear(_error, 0, 4); Array.Clear(_errorSum, 0, 4); Array.Clear(_errorCount, 0, 4);
            Array.Clear(_planted, 0, 4); Array.Clear(_plantedSkips, 0, 4); Array.Clear(_plantedFades, 0, 4);
            Array.Clear(_patchWorst, 0, 4); Array.Clear(_patchSum, 0, 4); Array.Clear(_gapMin, 0, 4); Array.Clear(_gapMax, 0, 4); Array.Clear(_drift, 0, 4);
        }
    }

    /// <summary>What one limb was asked for in the last IK pass (measurement only).</summary>
    internal struct ContactRecord
    {
        internal bool Has, Planted, Skipped, Native, Hand;
        internal Vector3 Target, RungCentre, Outward, Root;
        internal RungProfile Profile;
        internal float Requested, Effective, Ratio, Chain, Progress;
    }
}
