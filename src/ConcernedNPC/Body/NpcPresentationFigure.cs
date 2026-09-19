using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>The outcome of one attempt to extract a local figure from a game
/// prefab: either a live root and the handles a role needs to dress and pose
/// it, or a refusal naming what was in the way.
///
/// There is no third state. A partially assembled figure is never returned,
/// because the whole extraction exists to keep one property true: a finished
/// figure has never contained a networking or AI component, rather than having
/// contained one and been tidied.</summary>
internal readonly struct NpcPresentationFigure
{
    private NpcPresentationFigure(
        GameObject? root,
        Animator? animator,
        Transform? helmetJoint,
        Transform? rightHandJoint,
        SkinnedMeshRenderer? bodyModel,
        int quietedScripts,
        string quietedNames,
        string refusal)
    {
        Root = root;
        Animator = animator;
        HelmetJoint = helmetJoint;
        RightHandJoint = rightHandJoint;
        BodyModel = bodyModel;
        QuietedScripts = quietedScripts;
        QuietedNames = quietedNames ?? string.Empty;
        Refusal = refusal ?? string.Empty;
    }

    /// <summary>The finished figure, active, with nothing inside it that can
    /// run. Null on a refusal.</summary>
    internal GameObject? Root { get; }

    /// <summary>The animator the figure is posed through.</summary>
    internal Animator? Animator { get; }

    /// <summary>Where the source rig hung a helmet, if it is inside the part
    /// that was kept. Null is normal and means the role goes without.</summary>
    internal Transform? HelmetJoint { get; }

    /// <summary>Where the source rig hung a right-hand item, if it is inside
    /// the part that was kept.</summary>
    internal Transform? RightHandJoint { get; }

    /// <summary>The body renderer, for a role that tints skin or swaps
    /// garment textures.</summary>
    internal SkinnedMeshRenderer? BodyModel { get; }

    /// <summary>How many of the source character's own scripts were taken out
    /// before the figure was ever switched on. Reported even when it is zero,
    /// so a build that found none is distinguishable from a build that refused
    /// every one.</summary>
    internal int QuietedScripts { get; }

    /// <summary>Their names, for the log line.</summary>
    internal string QuietedNames { get; }

    /// <summary>Why this candidate was refused, or empty when it was
    /// built.</summary>
    internal string Refusal { get; }

    internal bool IsBuilt => Root != null;

    internal static NpcPresentationFigure Built(
        GameObject root,
        Animator animator,
        Transform? helmetJoint,
        Transform? rightHandJoint,
        SkinnedMeshRenderer? bodyModel,
        int quietedScripts,
        string quietedNames) =>
        new NpcPresentationFigure(
            root, animator, helmetJoint, rightHandJoint, bodyModel, quietedScripts, quietedNames, string.Empty);

    internal static NpcPresentationFigure Refused(string reason) =>
        new NpcPresentationFigure(
            null, null, null, null, null, 0, string.Empty,
            string.IsNullOrEmpty(reason) ? "no reason was given" : reason);

    public override string ToString() => IsBuilt ? "built" : "refused: " + Refusal;
}
