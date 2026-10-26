using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>Scoped IK inputs. Before native IK runs again, restore only inputs still equal to our last write.
/// All getters/setters here are called inside the rig's IK pass. End never calls an IK API from Update.</summary>
internal sealed class NativeIKLease
{
    internal static readonly AvatarIKGoal[] Goals = { AvatarIKGoal.LeftHand, AvatarIKGoal.RightHand, AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot };
    internal static readonly AvatarIKHint[] Hints = { AvatarIKHint.LeftElbow, AvatarIKHint.RightElbow, AvatarIKHint.LeftKnee, AvatarIKHint.RightKnee };
    private readonly Goal[] _goals = new Goal[4];
    private readonly Hint[] _hints = new Hint[4];
    private bool _bodyOwned;
    private Vector3 _bodyBefore, _bodyLast;
    private Quaternion _rotationBefore, _rotationLast;
    internal bool Pending { get; private set; }

    /// <summary>Codex review A2: each owned field is restored on its own when it still holds our last write. A field
    /// something else changed since is left as it is, but that never stops the other owned fields from restoring.</summary>
    internal void RestoreInIK(Animator animator)
    {
        for (int i = 0; i < 4; i++)
        {
            Goal goal = _goals[i];
            if (goal.Owned)
            {
                AvatarIKGoal id = Goals[i];
                if (Same(animator.GetIKPosition(id), goal.LastPosition)) { animator.SetIKPosition(id, goal.BeforePosition); }
                if (Same(animator.GetIKRotation(id), goal.LastRotation)) { animator.SetIKRotation(id, goal.BeforeRotation); }
                if (Mathf.Abs(animator.GetIKPositionWeight(id) - goal.LastWeight) < .00001f) { animator.SetIKPositionWeight(id, goal.BeforeWeight); }
                if (Mathf.Abs(animator.GetIKRotationWeight(id) - goal.LastRotationWeight) < .00001f) { animator.SetIKRotationWeight(id, goal.BeforeRotationWeight); }
            }

            _goals[i] = default;
            Hint hint = _hints[i];
            if (hint.Owned)
            {
                AvatarIKHint id = Hints[i];
                if (Same(animator.GetIKHintPosition(id), hint.LastPosition)) { animator.SetIKHintPosition(id, hint.BeforePosition); }
                if (Mathf.Abs(animator.GetIKHintPositionWeight(id) - hint.LastWeight) < .00001f) { animator.SetIKHintPositionWeight(id, hint.BeforeWeight); }
            }

            _hints[i] = default;
        }

        if (_bodyOwned)
        {
            if (Same(animator.bodyPosition, _bodyLast)) { animator.bodyPosition = _bodyBefore; }
            if (Same(animator.bodyRotation, _rotationLast)) { animator.bodyRotation = _rotationBefore; }
        }

        _bodyOwned = false; Pending = false;
    }

    internal void GoalInIK(Animator animator, int limb, Vector3 position, Quaternion rotation, float weight)
    {
        if (weight <= 0f) { return; }
        AvatarIKGoal id = Goals[limb];
        _goals[limb] = new Goal { Owned = true,
            BeforePosition = animator.GetIKPosition(id), BeforeRotation = animator.GetIKRotation(id),
            BeforeWeight = animator.GetIKPositionWeight(id), BeforeRotationWeight = animator.GetIKRotationWeight(id),
            LastPosition = position, LastRotation = rotation, LastWeight = weight, LastRotationWeight = weight };
        animator.SetIKPosition(id, position); animator.SetIKRotation(id, rotation);
        animator.SetIKPositionWeight(id, weight); animator.SetIKRotationWeight(id, weight);
        Pending = true;
    }

    internal void HintInIK(Animator animator, int limb, Vector3 position, float weight)
    {
        if (weight <= 0f) { return; }
        AvatarIKHint id = Hints[limb];
        _hints[limb] = new Hint { Owned = true, BeforePosition = animator.GetIKHintPosition(id),
            BeforeWeight = animator.GetIKHintPositionWeight(id), LastPosition = position, LastWeight = weight };
        animator.SetIKHintPosition(id, position); animator.SetIKHintPositionWeight(id, weight);
        Pending = true;
    }

    internal void BodyInIK(Animator animator, Vector3 position, Quaternion rotation, float weight)
    {
        if (weight <= 0f) { return; }
        _bodyBefore = animator.bodyPosition; _rotationBefore = animator.bodyRotation;
        _bodyLast = Vector3.Lerp(_bodyBefore, position, weight); _rotationLast = Quaternion.Slerp(_rotationBefore, rotation, weight);
        animator.bodyPosition = _bodyLast; animator.bodyRotation = _rotationLast;
        _bodyOwned = true; Pending = true;
    }

    private static bool Same(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .00000001f;
    private static bool Same(Quaternion a, Quaternion b) => Mathf.Abs(Quaternion.Dot(a,b)) > .999999f;
    private struct Goal
    {
        internal bool Owned;
        internal Vector3 BeforePosition, LastPosition;
        internal Quaternion BeforeRotation, LastRotation;
        internal float BeforeWeight, BeforeRotationWeight, LastWeight, LastRotationWeight;
    }
    private struct Hint
    {
        internal bool Owned;
        internal Vector3 BeforePosition, LastPosition;
        internal float BeforeWeight, LastWeight;
    }
}
