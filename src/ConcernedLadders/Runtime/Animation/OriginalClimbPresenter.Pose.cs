using System;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Animation;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

internal sealed partial class OriginalClimbPresenter
{
    private void PoseInIK(Actor actor)
    {
        OriginalRigBinding rig = actor.Rig;
        if (!rig.Valid || !actor.Track.Ready) { actor.Active = false; return; }
        rig.BindBodyInIK();
        Animator animator = rig.Animator;
        ClimbPresentationFrame frame = actor.Frame;
        Vector3 outward = new Vector3(frame.Run.StandingSide.X, 0f, frame.Run.StandingSide.Z) * (float)frame.Face;
        Vector3 toward = -outward;
        Vector3 right = Vector3.Cross(Vector3.up, toward);
        Vector3 feet = rig.Player.transform.position; //rendered/interpolated root; never written by presentation.
        NativeActionParts native = frame.NativeAction;
        // A native action can start after the last producer tick: reconcile current/next native attack tags
        // at this actual IK pass, without reading input or inventing trigger names.
        if (NativeActionNow(animator))
        { native |= NativeActionParts.LeftArm | NativeActionParts.RightArm | NativeActionParts.UpperBody | NativeActionParts.Head; }
        float entry = Mathf.Clamp01(actor.Age / .15f);
        bool mount = frame.Motion == PresentedMotion.GroundMount || frame.Motion == PresentedMotion.AirCatch;
        bool exit = frame.Motion == PresentedMotion.TopFinish || frame.Motion == PresentedMotion.StepOff;
        float route = Mathf.Clamp01(frame.Transition);
        if (mount || exit) { entry = 1f; }
        float bodyWeight = OriginalContactTrack.Ease(entry);
        if (mount) { bodyWeight *= Window(route, 0f, frame.Motion == PresentedMotion.AirCatch ? .35f : .7f); }
        if (exit) { bodyWeight *= 1f - Window(route, .65f, 1f); }
        if ((native & NativeActionParts.UpperBody) != 0) { bodyWeight = 0f; }
        float sway = .025f * actor.Track.SupportShift * (1f - actor.SlideBlend);
        float lean = Mathf.Lerp(12f, 7f, actor.SlideBlend);
        Vector3 pelvis = feet + Vector3.up * (rig.PelvisHeight - .035f) + toward * .055f + right * sway;
        Quaternion bodyRotation = Quaternion.LookRotation(toward, Vector3.up) *
            Quaternion.AngleAxis(lean, Vector3.right) * Quaternion.AngleAxis(-actor.Track.SupportShift * 2.5f, Vector3.forward) * rig.BodyInPlayer;
        Vector3 bodyPosition = pelvis + bodyRotation * rig.BodyFromPelvis;
        Vector3 nativeBody = animator.bodyPosition;
        Quaternion nativeRotation = animator.bodyRotation;
        actor.Lease.BodyInIK(animator, bodyPosition, bodyRotation, bodyWeight);
        Vector3 posedBody = animator.bodyPosition;
        Quaternion bodyDelta = animator.bodyRotation * Quaternion.Inverse(nativeRotation);

        for (int limb = 0; limb < 4; limb++)
        {
            bool hand = limb < 2;
            if ((limb == 0 && (native & (NativeActionParts.LeftArm | NativeActionParts.UpperBody)) != 0) ||
                (limb == 1 && (native & (NativeActionParts.RightArm | NativeActionParts.UpperBody)) != 0)) { continue; }
            OriginalLimbContact contact = actor.Track.Contact(limb);
            float side = limb == 0 || limb == 2 ? -1f : 1f;
            float halfSpan = Math.Min(ClimbPresentationFrame.HalfWidth - .04f, hand ? .18f : .13f);
            float planeOffset = hand ? ClimbPresentationFrame.HalfDepth : ClimbPresentationFrame.HalfDepth * .25f;
            float surfaceY = contact.Height + (hand ? ClimbPresentationFrame.HalfHeight * .3f : ClimbPresentationFrame.HalfHeight);
            Vector3 surface = RungSurface(frame, surfaceY, planeOffset, halfSpan * side, outward, right);
            surface += outward * contact.Clearance * (hand ? .09f : .12f);
            if (actor.SlideBlend > 0f)
            {
                // A distinct rail-braking pose: these contacts slide by design, not fake planted rung grips.
                float height = feet.y + (hand ? rig.ShoulderHeight - .16f : .24f);
                Vector3 rail = RungSurface(frame, height, ClimbPresentationFrame.HalfDepth + (hand ? .02f : .11f),
                    side * (hand ? .32f : .25f), outward, right);
                surface = Vector3.Lerp(surface, rail, OriginalContactTrack.Ease(actor.SlideBlend));
            }
            float weight = OriginalContactTrack.Ease(entry);
            if (mount)
            {
                if (frame.Motion == PresentedMotion.AirCatch)
                { weight *= hand ? Window(route, 0f, .25f) : Window(route, .1f, .9f); }
                else
                {
                    float start = limb == 0 ? 0f : limb == 1 ? .1f : limb == 2 ? .25f : .15f;
                    float finish = hand ? start + .45f : 1f;
                    weight *= Window(route, start, finish);
                }
            }
            if (exit)
            {
                if (hand) { weight *= 1f - Window(route, limb == 0 ? .45f : .6f, limb == 0 ? .8f : .95f); }
                else
                {
                    // Only the motor's accepted supported destination is used. No floor query or landing choice.
                    float step = Window(route, limb == 2 ? .08f : .35f, limb == 2 ? .65f : .95f);
                    Vector3 supported = Vector(frame.TransitionTo) + right * (side * .13f);
                    surface = Vector3.Lerp(surface, supported, step);
                    surface += Vector3.up * .09f * Mathf.Sin(Mathf.PI * step);
                    weight *= 1f - Window(route, .9f, 1f);
                }
            }
            Quaternion rotation = rig.GoalRotation(limb, toward);
            Vector3 pivot = rig.PivotForContact(limb, surface, rotation);
            Vector3 root = posedBody + bodyDelta * (rig.Roots[limb].position - nativeBody);
            float reach = Vector3.Distance(root, pivot);
            if (reach > rig.Reach[limb] * 1.01f)
            {
                if (!actor.ReachWarning[limb])
                {
                    actor.ReachWarning[limb] = true;
                    Log("Original contact needs rig/route fitting: limb=" + limb + ", required=" + reach.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                        ", reach=" + rig.Reach[limb].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                        ", motion=" + frame.Motion + ". That limb yields to native; no movement change.");
                }
                continue; //never stretch a limb or pretend the unreachable target is a successful plant.
            }
            actor.Lease.GoalInIK(animator, limb, pivot, rotation, weight);
            Vector3 hint = hand ? root + outward * .2f + right * (side * .18f) - Vector3.up * .25f :
                root + toward * .28f + right * (side * .08f) - Vector3.up * .28f;
            actor.Lease.HintInIK(animator, limb, hint, weight * .75f);
        }
        // Native look-at and equipment remain owned by the game. No controller parameters, animation speed,
        // root transforms, rigidbodies, capsule, gravity, input, skill or stamina are written.
    }

    private static Vector3 RungSurface(in ClimbPresentationFrame frame, float worldY, float faceOffset,
        float lateral, Vector3 outward, Vector3 right)
    {
        return Vector(OriginalRungSurface.At(frame.Run, frame.Face, frame.RungPlane, worldY, faceOffset, lateral));
    }

    private static float Window(float progress, float from, float to) => OriginalContactTrack.Ease((progress - from) / (to - from));
    private static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);
    private static bool NativeActionNow(Animator animator)
    {
        // Player.InAttack caches by MonoUpdaters.UpdateCount and can be stale at this boundary.
        // These actual native tags were inspected in Humanoid/Player; do not touch their cached fields.
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            if (ActionTag(animator.GetCurrentAnimatorStateInfo(layer))) { return true; }
            if (animator.IsInTransition(layer) && ActionTag(animator.GetNextAnimatorStateInfo(layer))) { return true; }
        }
        return false;
    }
    private static bool ActionTag(AnimatorStateInfo state) => state.IsTag("attack") ||
        state.IsTag("minoraction") || state.IsTag("minoraction_fast");
}
