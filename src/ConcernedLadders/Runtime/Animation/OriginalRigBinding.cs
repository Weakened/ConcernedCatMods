using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>Per-player humanoid binding. All dimensions come from this live rig; no imported game asset,
/// global controller override, skeleton replacement, guessed bone Euler axis or player transform write.</summary>
internal sealed class OriginalRigBinding
{
    internal readonly Player Player;
    internal readonly Animator Animator;
    internal readonly CharacterAnimEvent Events;
    internal readonly Transform Hips;
    internal readonly Transform[] Ends = new Transform[4];
    internal readonly Transform[] Joints = new Transform[4];
    internal readonly Transform[] Roots = new Transform[4];
    internal readonly float[] Reach = new float[4];
    internal readonly Vector3[] ContactOffset = new Vector3[4];
    internal readonly Quaternion[] ContactFrame = new Quaternion[4];
    internal readonly float PelvisHeight;
    internal readonly float ShoulderHeight;
    private readonly int _controller, _avatar;
    private readonly Vector3 _rootScale;
    internal bool BodyBound { get; private set; }
    internal Vector3 BodyFromPelvis { get; private set; }
    internal Quaternion BodyInPlayer { get; private set; }

    /// <summary>Degrees between the root and the native body rotation when the climb was bound (diagnostic only).</summary>
    internal float LiveBodyTwist { get; private set; }
    internal Vector3 NativePelvisInBody { get; private set; }

    private OriginalRigBinding(Player player, Animator animator, CharacterAnimEvent events)
    {
        Player = player; Animator = animator; Events = events;
        _controller = animator.runtimeAnimatorController.GetInstanceID();
        _avatar = animator.avatar.GetInstanceID(); _rootScale = player.transform.lossyScale;
        Hips = Bone(HumanBodyBones.Hips);
        HumanBodyBones[] roots = { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
        HumanBodyBones[] joints = { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
        HumanBodyBones[] ends = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        for (int i = 0; i < 4; i++)
        {
            Roots[i] = Bone(roots[i]); Joints[i] = Bone(joints[i]); Ends[i] = Bone(ends[i]);
            Reach[i] = Vector3.Distance(Roots[i].position, Joints[i].position) + Vector3.Distance(Joints[i].position, Ends[i].position);
            if (Reach[i] < 0.2f || Reach[i] > 2f) { throw new InvalidOperationException("Live humanoid limb dimensions are incompatible."); }
        }
        // A dimension-based initial stance, subject to actual-rig fitting. Native male leg chain ~.896m,
        // neutral pelvis ~.839m. The .93 ratio does not copy an evaluated jump/crouch as a rest pose.
        PelvisHeight = Math.Min(Reach[2], Reach[3]) * .93f;
        ShoulderHeight = PelvisHeight + Vector3.Distance(Hips.position, (Roots[0].position + Roots[1].position) * .5f);
        BindPalm(0, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal);
        BindPalm(1, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal);
        BindSole(2, HumanBodyBones.LeftToes); BindSole(3, HumanBodyBones.RightToes);
    }

    internal static OriginalRigBinding Create(Player player)
    {
        if (player == null) { throw new ArgumentException("No current player to bind."); }
        Animator? selected = null;
        CharacterAnimEvent? events = null;
        foreach (Animator candidate in player.GetComponentsInChildren<Animator>(true))
        {
            CharacterAnimEvent native = candidate.GetComponent<CharacterAnimEvent>();
            if (native == null || !candidate.isHuman || candidate.avatar == null || !candidate.avatar.isValid) { continue; }
            if (selected != null) { throw new InvalidOperationException("Multiple native humanoid Animators; explicit binding is required."); }
            selected = candidate; events = native;
        }
        if (selected == null || events == null || selected.runtimeAnimatorController == null)
        { throw new InvalidOperationException("No valid native player humanoid/controller was found."); }
        return new OriginalRigBinding(player, selected, events);
    }

    internal bool Valid => Player != null && Animator != null && Animator.isHuman && Animator.avatar != null &&
        Animator.avatar.isValid && Animator.avatar.GetInstanceID() == _avatar && Animator.runtimeAnimatorController != null &&
        Animator.runtimeAnimatorController.GetInstanceID() == _controller &&
        (Player.transform.lossyScale - _rootScale).sqrMagnitude < .000001f;

    /// <summary>Only inside this Animator's IK pass. Humanoid body getters are not legal in arbitrary Update.</summary>
    internal void BindBodyInIK()
    {
        if (BodyBound) { return; }
        Quaternion root = Player.transform.rotation;
        // In game 2026-10-26: copying the live body rotation baked whatever native pose ran first (the hammer idle,
        // a jump) into every climbing frame, turning the hips about 30 degrees off the ladder. A humanoid's neutral
        // body rotation faces its root, so the climbing pose is built from that, and only the measured centre-of-mass
        // offset is kept.
        BodyInPlayer = Quaternion.identity;
        LiveBodyTwist = Quaternion.Angle(root, Animator.bodyRotation);
        BodyFromPelvis = Quaternion.Inverse(Animator.bodyRotation) * (Animator.bodyPosition - Hips.position);
        NativePelvisInBody = Quaternion.Inverse(Animator.bodyRotation) * (Hips.position - Animator.bodyPosition);
        BodyBound = true;
    }

    /// <summary>The hand or foot rotation that puts this limb's measured contact frame (forward = fingers or toes,
    /// up = palm or sole normal) onto <paramref name="surface"/>.</summary>
    internal Quaternion GoalRotation(int limb, Quaternion surface) => surface * Quaternion.Inverse(ContactFrame[limb]);

    internal Vector3 PivotForContact(int limb, Vector3 contact, Quaternion rotation) => contact - rotation * ContactOffset[limb];

    private void BindPalm(int limb, HumanBodyBones middleBone, HumanBodyBones indexBone, HumanBodyBones littleBone)
    {
        Transform hand = Ends[limb], middle = Bone(middleBone), index = Bone(indexBone), little = Bone(littleBone);
        Vector3 fingers = (middle.position - hand.position).normalized;
        Vector3 across = (index.position - little.position).normalized;
        Vector3 normal = Vector3.Cross(across, fingers).normalized * (limb == 0 ? 1f : -1f);
        if (fingers.sqrMagnitude < .9f || normal.sqrMagnitude < .9f) { throw new InvalidOperationException("Native palm landmarks are degenerate."); }
        Vector3 palm = Vector3.Lerp(hand.position, middle.position, .6f);
        ContactOffset[limb] = Quaternion.Inverse(hand.rotation) * (palm - hand.position); //world metres in hand orientation, preserving imported bone scale.
        ContactFrame[limb] = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(fingers, normal);
    }

    private void BindSole(int limb, HumanBodyBones toeBone)
    {
        Transform foot = Ends[limb], toe = Bone(toeBone);
        Vector3 forward = (toe.position - foot.position).normalized;
        Vector3 up = Vector3.ProjectOnPlane(Player.transform.up, forward).normalized;
        if (forward.sqrMagnitude < .9f || up.sqrMagnitude < .9f) { throw new InvalidOperationException("Native sole landmarks need a neutral binding observation."); }
        // Toe/ankle pivots are landmarks, not a measured sole mesh. This initial contact is an explicit
        // prototype fit; evidence must confirm sole height/palm normal with equipment on both faces.
        Vector3 sole = Vector3.Lerp(foot.position, toe.position, .7f) - up * .025f;
        ContactOffset[limb] = Quaternion.Inverse(foot.rotation) * (sole - foot.position);
        ContactFrame[limb] = Quaternion.Inverse(foot.rotation) * Quaternion.LookRotation(forward, up);
    }

    private Transform Bone(HumanBodyBones bone)
    {
        Transform result = Animator.GetBoneTransform(bone);
        if (result == null) { throw new InvalidOperationException("Required humanoid bone missing: " + bone + "."); }
        return result;
    }
}
