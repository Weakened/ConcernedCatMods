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
public readonly struct NpcPresentationFigure
{
    private NpcPresentationFigure(
        GameObject? root,
        Animator? animator,
        Transform? helmetJoint,
        Transform? rightHandJoint,
        SkinnedMeshRenderer? bodyModel,
        int quietedScripts,
        string quietedNames,
        string refusal,
        Mesh?[]? modelMeshes = null,
        Material?[]? modelMaterials = null)
    {
        Root = root;
        Animator = animator;
        HelmetJoint = helmetJoint;
        RightHandJoint = rightHandJoint;
        BodyModel = bodyModel;
        QuietedScripts = quietedScripts;
        QuietedNames = quietedNames ?? string.Empty;
        Refusal = refusal ?? string.Empty;
        _modelMeshes = modelMeshes;
        _modelMaterials = modelMaterials;
    }

    // The source character's own body list, copied out of its equipment
    // component before that component was destroyed with the rest of the
    // source. Asset references only: a mesh and a base material per body,
    // never anything that was instantiated. Read by the dresser, which is the
    // only thing that ever swaps a figure's body.
    private readonly Mesh?[]? _modelMeshes;
    private readonly Material?[]? _modelMaterials;

    /// <summary>The finished figure, active, with nothing inside it that can
    /// run. Null on a refusal.</summary>
    public GameObject? Root { get; }

    /// <summary>The animator the figure is posed through.</summary>
    public Animator? Animator { get; }

    /// <summary>Where the source rig hung a helmet, if it is inside the part
    /// that was kept. Null is normal and means the role goes without.</summary>
    public Transform? HelmetJoint { get; }

    /// <summary>Where the source rig hung a right-hand item, if it is inside
    /// the part that was kept.</summary>
    public Transform? RightHandJoint { get; }

    /// <summary>The body renderer, for a role that tints skin or swaps
    /// garment textures.</summary>
    public SkinnedMeshRenderer? BodyModel { get; }

    /// <summary>How many of the source character's own scripts were taken out
    /// before the figure was ever switched on. Reported even when it is zero,
    /// so a build that found none is distinguishable from a build that refused
    /// every one.</summary>
    public int QuietedScripts { get; }

    /// <summary>Their names, for the log line.</summary>
    public string QuietedNames { get; }

    /// <summary>Why this candidate was refused, or empty when it was
    /// built.</summary>
    public string Refusal { get; }

    public bool IsBuilt => Root != null;

    /// <summary>The mesh and base material of the source's body number
    /// <paramref name="index"/>, if the source offered one.</summary>
    internal bool TryGetModel(int index, out Mesh? mesh, out Material? baseMaterial)
    {
        mesh = null;
        baseMaterial = null;
        if (_modelMeshes == null || _modelMaterials == null || index < 0
            || index >= _modelMeshes.Length || index >= _modelMaterials.Length)
        {
            return false;
        }

        mesh = _modelMeshes[index];
        baseMaterial = _modelMaterials[index];
        return mesh != null && baseMaterial != null;
    }

    internal static NpcPresentationFigure Built(
        GameObject root,
        Animator animator,
        Transform? helmetJoint,
        Transform? rightHandJoint,
        SkinnedMeshRenderer? bodyModel,
        int quietedScripts,
        string quietedNames,
        Mesh?[]? modelMeshes = null,
        Material?[]? modelMaterials = null) =>
        new NpcPresentationFigure(
            root, animator, helmetJoint, rightHandJoint, bodyModel, quietedScripts, quietedNames, string.Empty,
            modelMeshes, modelMaterials);

    internal static NpcPresentationFigure Refused(string reason) =>
        new NpcPresentationFigure(
            null, null, null, null, null, 0, string.Empty,
            string.IsNullOrEmpty(reason) ? "no reason was given" : reason);

    public override string ToString() => IsBuilt ? "built" : "refused: " + Refusal;
}
