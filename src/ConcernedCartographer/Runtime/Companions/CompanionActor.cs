using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>What the presentation adapter managed to do, in the words the
/// console tool reports and the pending-evidence rows are written against.</summary>
internal sealed class ActorReport
{
    public ActorReport(string sourcePrefab, bool usedSkeleton)
    {
        SourcePrefab = sourcePrefab;
        UsedSkeleton = usedSkeleton;
    }

    /// <summary>Which prefab the visuals were extracted from.</summary>
    public string SourcePrefab { get; }

    /// <summary>True when a real animated skeleton was extracted; false when
    /// the adapter fell back to a static stand-in.</summary>
    public bool UsedSkeleton { get; }

    public AppearanceChoice Hair { get; set; } = AppearanceChoice.None;

    public AppearanceChoice Beard { get; set; } = AppearanceChoice.None;

    public bool HairAttached { get; set; }

    public bool BeardAttached { get; set; }

    public bool HairColourApplied { get; set; }

    /// <summary>The animator state actually used for the pose, or null when
    /// none of the candidates existed and the model kept its default.</summary>
    public string? PoseState { get; set; }

    public CompanionPose Pose { get; set; } = CompanionPose.SitOnGround;

    public override string ToString()
    {
        return $"source={SourcePrefab} skeleton={UsedSkeleton} hair={Hair} ({(HairAttached ? "attached" : "not attached")}) " +
            $"beard={Beard} ({(BeardAttached ? "attached" : "not attached")}) colour={HairColourApplied} " +
            $"pose={Pose} state={PoseState ?? "<default>"}";
    }
}

/// <summary>Builds and owns Hulgi's body.
///
/// Nothing from a game prefab is ever allowed to wake, and nothing is ever
/// stripped after the fact. The construction is an <b>extraction</b>: the
/// source prefab is instantiated under an inactive holder, its animated visual
/// subtree is re-parented out, and the remainder — which is where
/// <c>Player</c>, <c>ZNetView</c>, <c>Character</c> and <c>BaseAI</c> live — is
/// destroyed without ever having been enabled.
///
/// The extraction then <b>refuses</b> rather than cleans up. If a networking or
/// AI component turns out to be inside the subtree on some build, the whole
/// attempt is abandoned and the next candidate is tried; it is never removed
/// and carried on with. That keeps a simple property true and auditable: a
/// finished actor has never contained one of those components, rather than
/// having contained one and been tidied.
///
/// Every step below it has a fallback, and the last fallback is "no actor,
/// with a notice". None of them touches the player's access, progress or
/// data.</summary>
internal sealed class CompanionActor
{
    /// <summary>Components whose presence inside an extracted subtree
    /// invalidates the whole extraction. Resolved by name so a build that does
    /// not have one of these types cannot fail the check by its absence.</summary>
    private static readonly string[] ForbiddenComponents =
    {
        "ZNetView",
        "ZSyncAnimation",
        "Player",
        "Character",
        "Humanoid",
        "BaseAI",
        "MonsterAI",
        "AnimalAI",
        "NpcTalk",
        "Tameable",
    };

    /// <summary>Animator states to try for a seated idle, best first. Every one
    /// is checked against the live controller with <c>Animator.HasState</c>
    /// before it is used, so none of them is an assumption — the audit was
    /// explicit that no animation key is a constant on this build.</summary>
    private static readonly string[] SitStateCandidates =
    {
        "attach_chair",
        "Sitting",
        "sit",
        "attach_bed",
        "idle",
        "Idle",
    };

    private static readonly string[] HeadBoneFragments = { "head", "neck" };

    private readonly ManualLogSource _log;

    private GameObject? _root;
    private Animator? _animator;

    public CompanionActor(ManualLogSource log)
    {
        _log = log;
    }

    public bool Exists => _root != null;

    public ActorReport? Report { get; private set; }

    /// <summary>The home point this actor was built against, so the residency
    /// rule can tell whether it has moved.</summary>
    public CompanionAnchor PlacedAnchor { get; private set; }

    public Vector3 Position => _root != null ? _root.transform.position : Vector3.zero;

    /// <summary>Builds the actor at <paramref name="position"/>. Returns false
    /// when nothing could be built; the caller then disables presentation with
    /// a notice and changes nothing else.</summary>
    public bool TryBuild(
        WorldPoint position,
        CompanionPose pose,
        CompanionAnchor anchor,
        IReadOnlyList<string> sourceCandidates,
        ManualLogSource log)
    {
        Release();

        foreach (string candidate in sourceCandidates)
        {
            GameObject? prefab = LocalVisual.FindPrefab(candidate);
            if (prefab == null)
            {
                continue;
            }

            GameObject? extracted = TryExtractVisual(prefab, candidate);
            if (extracted == null)
            {
                continue;
            }

            _root = extracted;
            Report = new ActorReport(candidate, usedSkeleton: _animator != null) { Pose = pose };
            Place(position, pose);
            ApplyAppearance();
            AttachHover();
            return true;
        }

        // No humanoid source worked. A plain marker is still better than a
        // player being told their companion exists and seeing nothing, and it
        // keeps the anchor and seating behaviour observable.
        LocalVisual.Result stand = LocalVisual.Build(
            "Hulgi", new[] { "Wishbone", "SilverNecklace", "Amber" }, 1.6f, log);
        _root = stand.Root;
        _animator = null;
        Report = new ActorReport(stand.Source, usedSkeleton: false) { Pose = pose };
        Place(position, pose);
        AttachHover();

        _log.LogInfo(
            "No animated humanoid source was available for the companion on this build, so a simple " +
            "stand-in marks his place. Your tools, progress and data are unaffected.");
        return true;
    }

    private void Place(WorldPoint position, CompanionPose pose)
    {
        PlacedAnchorSet(position);
        if (_root == null)
        {
            return;
        }

        _root.transform.position = new Vector3(position.X, position.Y, position.Z);
        _root.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        ApplyPose(pose);
    }

    private void PlacedAnchorSet(WorldPoint position)
    {
        // The actor remembers where it was put, not where home is; the caller
        // records the anchor separately through RememberAnchor.
        _ = position;
    }

    public void RememberAnchor(CompanionAnchor anchor)
    {
        PlacedAnchor = anchor;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
        {
            _root.SetActive(visible);
        }
    }

    /// <summary>Applies a seated idle if this build has a state for one.
    ///
    /// Every candidate is checked with <c>Animator.HasState</c> first, so an
    /// absent state is a fact this build told us rather than an exception we
    /// caught. When none exists the model keeps its default idle and the report
    /// says <c>PoseState = null</c> — which is how seating stays honestly
    /// "pending" instead of quietly claimed.</summary>
    private void ApplyPose(CompanionPose pose)
    {
        if (Report != null)
        {
            Report.Pose = pose;
        }

        if (_animator == null)
        {
            return;
        }

        try
        {
            _animator.applyRootMotion = false;

            foreach (string candidate in SitStateCandidates)
            {
                int hash = Animator.StringToHash(candidate);
                if (!_animator.HasState(0, hash))
                {
                    continue;
                }

                _animator.Play(hash, 0, 0f);
                if (Report != null)
                {
                    Report.PoseState = candidate;
                }

                return;
            }
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "The companion's idle pose could not be applied on this build; he keeps the model's " +
                $"default: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>Extracts the animated visual subtree, or returns null.</summary>
    private GameObject? TryExtractVisual(GameObject prefab, string candidateName)
    {
        GameObject holder = new GameObject("CC_ActorHarvest");
        holder.SetActive(false);

        GameObject? clone = null;
        GameObject? root = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(prefab, holder.transform);

            Animator? animator = clone.GetComponentInChildren<Animator>(includeInactive: true);
            if (animator == null)
            {
                return null;
            }

            GameObject visual = animator.gameObject;
            if (visual == clone)
            {
                // The animator sits on the same object as the networking and AI
                // components. Extracting it would mean extracting them, so this
                // candidate is refused rather than cleaned up.
                _log.LogInfo(
                    $"\"{candidateName}\" keeps its animator on the same object as its game components, " +
                    "so it cannot be used as a local visual source. Trying the next candidate.");
                return null;
            }

            if (ContainsForbiddenComponent(visual, out string offender))
            {
                _log.LogInfo(
                    $"\"{candidateName}\" carries {offender} inside its visual subtree, so it was not " +
                    "used. Nothing was stripped; the candidate was refused.");
                return null;
            }

            root = new GameObject("CC_Hulgi");
            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            RemovePhysics(root);
            _animator = animator;

            GameObject? result = root;
            root = null;
            return result;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"Could not build a local visual from \"{candidateName}\": {SafeLogText.Brief(exception)}");
            return null;
        }
        finally
        {
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            if (clone != null)
            {
                // Whatever is left holds Player/ZNetView/Character/BaseAI. It
                // never woke, and now it never will.
                UnityEngine.Object.DestroyImmediate(clone);
            }

            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    private static bool ContainsForbiddenComponent(GameObject subtree, out string offender)
    {
        offender = "";
        foreach (Component component in subtree.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null)
            {
                continue;
            }

            string name = component.GetType().Name;
            foreach (string forbidden in ForbiddenComponents)
            {
                if (string.Equals(name, forbidden, StringComparison.Ordinal))
                {
                    offender = forbidden;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Physics components are removed rather than refused, and the
    /// difference is deliberate. A collider carries no registration — it is not
    /// visible to anything until something touches it — so removing one cannot
    /// leave a trace the way waking a <c>ZNetView</c> would. Leaving one in
    /// place, on the other hand, would mean a player walking into an invisible
    /// wall where their companion stands.</summary>
    private static void RemovePhysics(GameObject subtree)
    {
        foreach (Collider collider in subtree.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        foreach (Rigidbody body in subtree.GetComponentsInChildren<Rigidbody>(includeInactive: true))
        {
            if (body != null)
            {
                UnityEngine.Object.DestroyImmediate(body);
            }
        }
    }

    private void ApplyAppearance()
    {
        if (_root == null || Report == null)
        {
            return;
        }

        try
        {
            AppearanceCatalog catalog = AppearanceCatalog.Read();
            Report.Hair = AppearancePlan.Choose(
                catalog.Hair, AppearancePlan.HairPreferences, AppearanceCatalog.HairPrefix);
            Report.Beard = AppearancePlan.Choose(
                catalog.Beards, AppearancePlan.BeardPreferences, AppearanceCatalog.BeardPrefix);

            Transform? head = FindHeadBone(_root.transform);
            if (head == null)
            {
                _log.LogInfo(
                    "The companion's head bone could not be found, so hair and beard presets were not " +
                    "attached. He keeps the source model's own appearance.");
                return;
            }

            Report.HairAttached = TryAttach(Report.Hair.PrefabName, head, "hair");
            Report.BeardAttached = TryAttach(Report.Beard.PrefabName, head, "beard");
            Report.HairColourApplied = TryApplyHairColour(head);
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "The companion's appearance could not be customised on this build; he keeps the source " +
                $"model's own look: {SafeLogText.Brief(exception)}");
        }
    }

    private bool TryAttach(string? prefabName, Transform head, string slot)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            return false;
        }

        try
        {
            LocalVisual.Result piece = LocalVisual.Build(
                "Hulgi" + slot, new[] { prefabName! }, 1f, _log);
            if (piece.Source == "primitive")
            {
                // A primitive cylinder is not a beard. Better to have none.
                UnityEngine.Object.DestroyImmediate(piece.Root);
                return false;
            }

            piece.Root.transform.SetParent(head, worldPositionStays: false);
            piece.Root.transform.localPosition = Vector3.zero;
            piece.Root.transform.localRotation = Quaternion.identity;
            return true;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's {slot} preset could not be attached: {SafeLogText.Brief(exception)}");
            return false;
        }
    }

    /// <summary>Tints the attached hair through a property block, so the
    /// vanilla material every other character shares is never written to. Two
    /// property names are tried because which one this build's hair shader uses
    /// is not knowable from metadata.</summary>
    private bool TryApplyHairColour(Transform head)
    {
        try
        {
            var colour = new Color(
                AppearancePlan.StrawberryBlondR,
                AppearancePlan.StrawberryBlondG,
                AppearancePlan.StrawberryBlondB,
                1f);

            bool applied = false;
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in head.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", colour);
                block.SetColor("_HairColor", colour);
                renderer.SetPropertyBlock(block);
                applied = true;
            }

            return applied;
        }
        catch
        {
            return false;
        }
    }

    private static Transform? FindHeadBone(Transform root)
    {
        foreach (Transform bone in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (bone == null || string.IsNullOrEmpty(bone.name))
            {
                continue;
            }

            foreach (string fragment in HeadBoneFragments)
            {
                if (bone.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return bone;
                }
            }
        }

        return null;
    }

    /// <summary>Hover text only. The companion is not
    /// <c>Interactable</c> in this slice: talking to him is CC-NPC-005, and an
    /// interactable that does nothing would be worse than none.</summary>
    private void AttachHover()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            CompanionHover.Attach(_root);
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's hover label could not be attached: {SafeLogText.Brief(exception)}");
        }
    }

    public void Release()
    {
        _animator = null;
        Report = null;
        PlacedAnchor = CompanionAnchor.None;

        if (_root == null)
        {
            return;
        }

        GameObject doomed = _root;
        _root = null;
        try
        {
            UnityEngine.Object.Destroy(doomed);
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not remove the companion actor: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>Drops the reference without destroying anything. For scene
    /// teardown, where Unity has already destroyed the GameObject and calling
    /// Destroy again would be talking to a corpse.</summary>
    public void Forget()
    {
        _root = null;
        _animator = null;
        Report = null;
        PlacedAnchor = CompanionAnchor.None;
    }
}

/// <summary>A name on hover, and nothing else. No <c>Interactable</c>, no
/// collider of its own — it rides whatever the visual subtree offers, so it
/// appears when vanilla's raycast happens to reach the model and is silent
/// otherwise.</summary>
internal sealed class CompanionHover : MonoBehaviour, Hoverable
{
    public static CompanionHover Attach(GameObject root)
    {
        return root.AddComponent<CompanionHover>();
    }

    public string GetHoverName()
    {
        return AtlasStrings.Get("companion.hulgi.name");
    }

    public string GetHoverText()
    {
        return AtlasStrings.Get("companion.hulgi.name");
    }

    public float GetHoverOffset()
    {
        return 1.6f;
    }
}
