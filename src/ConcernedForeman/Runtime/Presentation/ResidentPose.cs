using System;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.Settlement.Housing;
using UnityEngine;
namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Cosmetic pose ONLY on this extraction's animator. No Player,
/// Humanoid, ZSyncAnimation, arbitrary animator parameter or movement input.
/// Parameters were observed in the installed player's 2026-10-26 animdump;
/// compatibility is checked again on the actual extracted controller.</summary>
internal sealed class ResidentPose
{
    private readonly Animator _animator;
    private ResidentRoutineStage _stage = ResidentRoutineStage.Paused;
    internal ResidentPose(NpcPresentationFigure figure)
    {
        if (!figure.IsBuilt || figure.Animator == null) throw new ArgumentException("An extracted resident figure is required.");
        _animator = figure.Animator;
        if (_animator.applyRootMotion) throw new InvalidOperationException("A resident visual must not apply root motion.");
        foreach (string required in new[] { "lying_down", "emote_sit", "onGround" })
        {
            bool known = false;
            foreach (var parameter in _animator.parameters)
                if (parameter.name == required && parameter.type == AnimatorControllerParameterType.Bool) known = true;
            if (!known) throw new InvalidOperationException("Resident pose parameter unavailable: " + required);
        }
    }
    internal ResidentRoutineStage Stage => _stage;
    internal void Apply(ResidentRoutineStage stage)
    {
        if (stage == ResidentRoutineStage.Paused || !Enum.IsDefined(typeof(ResidentRoutineStage), stage) || stage == _stage) return;
        // Native blends perform sit/stand transitions. No trigger, clip timing,
        // root travel, worker motor or native sleeping authority is involved.
        _animator.SetBool("onGround", true);
        _animator.SetBool("lying_down", stage == ResidentRoutineStage.Rest);
        _animator.SetBool("emote_sit", stage == ResidentRoutineStage.Morning || stage == ResidentRoutineStage.Evening);
        _stage = stage;
    }
}
