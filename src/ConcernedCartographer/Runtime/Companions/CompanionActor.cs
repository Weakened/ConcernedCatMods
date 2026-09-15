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

    /// <summary>True when the colour came from the game's own customization
    /// palette rather than the documented fallback tuning value.</summary>
    public bool HairColourObserved { get; set; }

    /// <summary>The colour actually applied, for the acceptance row.</summary>
    public ColourTriple HairColour { get; set; } = AppearanceColour.HairFallback;

    /// <summary>True when the extracted model's own skin was tinted. Only
    /// possible with an observed palette; never guessed.</summary>
    public bool SkinColourApplied { get; set; }

    /// <summary>The animator state actually used for the pose, or null when
    /// none of the candidates existed and the model kept its default.</summary>
    public string? PoseState { get; set; }

    public CompanionPose Pose { get; set; } = CompanionPose.SitOnGround;

    public override string ToString()
    {
        return $"source={SourcePrefab} skeleton={UsedSkeleton} hair={Hair} ({(HairAttached ? "attached" : "not attached")}) " +
            $"beard={Beard} ({(BeardAttached ? "attached" : "not attached")}) " +
            $"colour={(HairColourApplied ? HairColour.ToString() : "not applied")}" +
            $"{(HairColourObserved ? " (palette)" : " (fallback)")} skin={SkinColourApplied} " +
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

    /// <summary>The animator parameter the game sets to sit somebody on the
    /// ground: <c>Player.StartEmote("sit", oneshot: false)</c> ends in
    /// <c>SetBool("emote_" + emote, true)</c>. It is a parameter, not a state
    /// name, which is the whole reason the previous state-name search never
    /// found anything.</summary>
    private const string GroundSitParameter = "emote_sit";

    /// <summary>What a seat asks for when it does not name its own animation.
    /// <c>Chair.m_attachAnimation</c> defaults to exactly this, and
    /// <c>Player.AttachStart</c> passes it straight to
    /// <c>SetBool(attachAnimation, true)</c>.</summary>
    private const string DefaultSeatParameter = "attach_chair";

    private static readonly string[] HeadBoneFragments = { "head", "neck" };

    /// <summary>The shader property Valheim tints skin, hair and beards with.
    /// Cached as an id the way the game caches it.</summary>
    private static readonly int SkinColourProperty = Shader.PropertyToID("_SkinColor");

    private readonly ManualLogSource _log;
    private readonly Action _onTalk;
    private readonly Func<string?> _hairOverride;
    private readonly Func<string?> _beardOverride;

    private GameObject? _root;
    private Animator? _animator;

    /// <summary>The attachment point the source model's own <c>VisEquipment</c>
    /// names for hair and beards, captured during extraction. It is a bone
    /// inside the visual subtree, so the reference survives the re-parent - and
    /// using it means the presets sit exactly where the game puts them instead
    /// of on whichever bone a name search happened to hit first.</summary>
    private Transform? _helmetJoint;

    /// <summary>The extracted model's body renderer, for the skin tint and for
    /// re-binding a skinned customization mesh to the right bones.</summary>
    private SkinnedMeshRenderer? _bodyModel;

    /// <summary>The seat he was placed on, if any.</summary>
    private SeatOffer _seat;

    /// <summary>The animator parameter currently held true for his pose, so it
    /// can be released before another is set.</summary>
    private string? _poseParameter;

    public CompanionActor(
        ManualLogSource log,
        Action onTalk,
        Func<string?>? hairOverride = null,
        Func<string?>? beardOverride = null)
    {
        _log = log;
        _onTalk = onTalk;
        _hairOverride = hairOverride ?? (() => null);
        _beardOverride = beardOverride ?? (() => null);
    }

    public bool Exists => _root != null;

    /// <summary>The seat he is using, if any. The director re-checks it every
    /// residency pass so a seat that is taken or taken away is given up.</summary>
    public SeatOffer Seat => _seat;

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
        ManualLogSource log,
        SeatOffer seat = default)
    {
        Release();
        _seat = seat;

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

    /// <summary>Puts the body where it belongs.
    ///
    /// A seat is not "the ground near a chair": the game puts a sitter on the
    /// chair's own <c>m_attachPoint</c>, at the chair's own heading, and so
    /// does this. Using the probed ground point instead is what makes a seated
    /// figure look like it is standing through the furniture.</summary>
    private void Place(WorldPoint position, CompanionPose pose)
    {
        PlacedAnchorSet(position);
        if (_root == null)
        {
            return;
        }

        bool onSeat = pose == CompanionPose.SitOnSeat && _seat.IsUsable;
        WorldPoint where = onSeat ? _seat.Position : position;
        float yaw = onSeat ? _seat.YawDegrees : 200f;

        _root.transform.position = new Vector3(where.X, where.Y, where.Z);
        _root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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

    /// <summary>Sits him down, the way the game sits anybody down.
    ///
    /// Valheim does not play a sitting state by name. It sets an animator
    /// <b>bool parameter</b> - <c>emote_sit</c> for the ground emote,
    /// <c>attach_chair</c> (or whatever the piece names) for furniture - and
    /// lets the controller do the rest. Setting the parameter is a local write
    /// to our own animator: no attachment message is sent, no seat is claimed,
    /// and nothing about the piece or the player changes.
    ///
    /// Every parameter is checked against the live controller first, so an
    /// absent one is a fact this build told us rather than an exception we
    /// caught. When none exists the model keeps its default idle and the report
    /// says <c>PoseState = null</c>.</summary>
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

            // A pose that is being replaced must be cleared first, or a
            // companion who gives up a chair stays folded into a sitting shape
            // on the grass.
            ClearPoseParameter();

            foreach (string candidate in PoseParameters(pose))
            {
                if (!HasBoolParameter(candidate))
                {
                    continue;
                }

                _animator.SetBool(candidate, true);
                _poseParameter = candidate;
                if (Report != null)
                {
                    Report.PoseState = candidate;
                }

                return;
            }

            _log.LogInfo(
                "This build has no sitting animation parameter for the companion, so he keeps the " +
                "model's own idle. Nothing else is affected.");
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "The companion's idle pose could not be applied on this build; he keeps the model's " +
                $"default: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>The parameters to try for a pose, best first. A seat's own
    /// animation leads when there is one; the ground emote is the fallback for
    /// everything, which is what makes "sit on the ground" the behaviour that
    /// always works.</summary>
    private IEnumerable<string> PoseParameters(CompanionPose pose)
    {
        if (pose == CompanionPose.SitOnSeat && _seat.IsUsable)
        {
            yield return _seat.AttachAnimation ?? DefaultSeatParameter;
            yield return DefaultSeatParameter;
        }

        yield return GroundSitParameter;
    }

    private void ClearPoseParameter()
    {
        if (_animator == null || _poseParameter == null)
        {
            return;
        }

        try
        {
            if (HasBoolParameter(_poseParameter))
            {
                _animator.SetBool(_poseParameter, false);
            }
        }
        catch
        {
            // A controller that will not answer is not worth a notice here.
        }

        _poseParameter = null;
    }

    /// <summary>Whether the live controller actually has this bool parameter.
    /// <c>Animator.SetBool</c> on an absent one logs a Unity error every call,
    /// so asking first is both honest and quiet.</summary>
    private bool HasBoolParameter(string name)
    {
        if (_animator == null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter != null &&
                parameter.type == AnimatorControllerParameterType.Bool &&
                string.Equals(parameter.name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

            // Read the source's own attachment point and body renderer BEFORE
            // anything is destroyed. Both live inside the visual subtree, so
            // the references stay valid once it is re-parented out, and the
            // component they were read from never wakes.
            Transform? helmet = null;
            SkinnedMeshRenderer? body = null;
            var vis = clone.GetComponentInChildren<VisEquipment>(includeInactive: true);
            if (vis != null)
            {
                helmet = vis.m_helmet;
                body = vis.m_bodyModel;
                if (helmet != null && !helmet.IsChildOf(visual.transform))
                {
                    // The joint is outside the subtree we keep; it would dangle.
                    helmet = null;
                }

                if (body != null && !body.transform.IsChildOf(visual.transform))
                {
                    body = null;
                }
            }

            root = new GameObject("CC_Hulgi");
            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            RemovePhysics(root);
            _animator = animator;
            _helmetJoint = helmet;
            _bodyModel = body;

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

    /// <summary>Gives the extracted model the owner's reference appearance.
    ///
    /// Everything here follows what the game itself does in
    /// <c>VisEquipment</c>: the customization item's <c>attach</c> child is
    /// what gets instantiated (not the whole item prefab), it goes on the
    /// model's own helmet joint, and the tint is a
    /// <c>MaterialPropertyBlock</c> on <c>_SkinColor</c>. Using the game's own
    /// mechanism rather than a plausible-looking one is the difference between
    /// a beard and an untinted lump floating near a neck bone.
    ///
    /// Every step is allowed to fail on its own. A missing preset changes how
    /// Hulgi looks and nothing else.</summary>
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
                catalog.Hair, AppearancePlan.HulgiHair, _hairOverride());
            Report.Beard = AppearancePlan.Choose(
                catalog.Beards, AppearancePlan.HulgiBeard, _beardOverride());

            if (!Report.Hair.MatchesReference || !Report.Beard.MatchesReference)
            {
                _log.LogInfo(
                    "The companion's reference appearance could not be matched exactly on this build " +
                    $"(hair {Report.Hair}, beard {Report.Beard}). He wears the closest available " +
                    "presets. Nothing else is affected.");
            }

            CustomizationPalette palette = CustomizationPaletteReader.Read(_log);
            CustomizationPaletteReader.NoteFallbackOnce(_log);
            ColourTriple hairColour = AppearanceColour.HulgiHair(palette);
            Report.HairColour = hairColour;
            Report.HairColourObserved = palette.Observed;

            Transform? joint = _helmetJoint ?? FindHeadBone(_root.transform);
            if (joint == null)
            {
                _log.LogInfo(
                    "The companion's head attachment point could not be found, so hair and beard " +
                    "presets were not attached. He keeps the source model's own appearance.");
            }
            else
            {
                Report.HairAttached = TryAttachCustomization(
                    Report.Hair.PrefabName, joint, "hair", hairColour);
                Report.BeardAttached = TryAttachCustomization(
                    Report.Beard.PrefabName, joint, "beard", hairColour);
                Report.HairColourApplied = Report.HairAttached || Report.BeardAttached;
            }

            Report.SkinColourApplied = TryApplyBodyColours(palette, hairColour);
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "The companion's appearance could not be customised on this build; he keeps the source " +
                $"model's own look: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>Attaches one customization preset the way the game attaches
    /// hair and beards.
    ///
    /// The clone is made under an inactive holder and inspected before it is
    /// ever allowed to wake, exactly as the body extraction is: a customization
    /// item is a mesh and a material, and if one on some build turns out to
    /// carry a component from the forbidden list, the attachment is abandoned
    /// rather than cleaned up.</summary>
    private bool TryAttachCustomization(
        string? prefabName, Transform joint, string slot, ColourTriple colour)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            return false;
        }

        GameObject? prefab = LocalVisual.FindPrefab(prefabName!);
        if (prefab == null)
        {
            _log.LogInfo(
                $"The companion's {slot} preset \"{prefabName}\" is not in this build's item table, " +
                "so it was not attached.");
            return false;
        }

        GameObject holder = new GameObject("CC_CustomizationHarvest");
        holder.SetActive(false);

        GameObject? piece = null;
        try
        {
            // The game instantiates the item's "attach" child, never the item
            // prefab itself: the prefab root is an ItemDrop with a ZNetView on
            // it, and the visible mesh is one level down.
            GameObject? attach = FindAttachChild(prefab, out bool skinned);
            if (attach == null)
            {
                _log.LogInfo(
                    $"The companion's {slot} preset \"{prefabName}\" has no attachment mesh on this " +
                    "build, so it was not attached.");
                return false;
            }

            piece = UnityEngine.Object.Instantiate(attach, holder.transform);
            if (ContainsForbiddenComponent(piece, out string offender))
            {
                _log.LogInfo(
                    $"The companion's {slot} preset \"{prefabName}\" carries {offender}, so it was " +
                    "not used. Nothing was stripped; the preset was refused.");
                return false;
            }

            RemovePhysics(piece);

            if (skinned && _bodyModel != null)
            {
                // A skinned customization mesh deforms with the body, so it is
                // bound to the body's bones and parented beside it - the game's
                // own attach_skin path.
                piece.transform.SetParent(_bodyModel.transform.parent, worldPositionStays: false);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localRotation = Quaternion.identity;
                foreach (SkinnedMeshRenderer mesh in
                    piece.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
                {
                    if (mesh != null)
                    {
                        mesh.rootBone = _bodyModel.rootBone;
                        mesh.bones = _bodyModel.bones;
                    }
                }
            }
            else
            {
                piece.transform.SetParent(joint, worldPositionStays: false);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localRotation = Quaternion.identity;
            }

            ApplyEquipOffset(prefab, piece.transform);
            Tint(piece, colour);

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            return true;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's {slot} preset could not be attached: {SafeLogText.Brief(exception)}");
            return false;
        }
        finally
        {
            if (piece != null)
            {
                UnityEngine.Object.DestroyImmediate(piece);
            }

            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    /// <summary>Finds the child the game would attach, matching its own search:
    /// a child named <c>attach</c> or <c>attach_skin</c>, first one
    /// wins.</summary>
    private static GameObject? FindAttachChild(GameObject prefab, out bool skinned)
    {
        skinned = false;
        int children = prefab.transform.childCount;
        for (int index = 0; index < children; index++)
        {
            Transform child = prefab.transform.GetChild(index);
            if (child == null)
            {
                continue;
            }

            if (child.gameObject.name == "attach")
            {
                return child.gameObject;
            }

            if (child.gameObject.name == "attach_skin")
            {
                skinned = true;
                return child.gameObject;
            }
        }

        return null;
    }

    /// <summary>The optional <c>equipoffset</c> nudge the game applies after
    /// parenting. Transcribed from <c>VisEquipment.AttachItem</c>, including its
    /// use of the prefab child's world transform - a prefab asset sits at the
    /// origin, so that is its local offset.</summary>
    private static void ApplyEquipOffset(GameObject prefab, Transform attached)
    {
        Transform offset = prefab.transform.Find("equipoffset");
        if (offset == null)
        {
            return;
        }

        attached.localPosition += offset.position;
        attached.localRotation *= offset.rotation;
    }

    /// <summary>Tints hair and beard the way the game does: a property block on
    /// <c>_SkinColor</c>.
    ///
    /// The property name matters and is not interchangeable. Valheim's hair
    /// shader reads <c>_SkinColor</c>; writing <c>_Color</c> sets a property
    /// the shader never samples, which looks exactly like a colour that was
    /// applied and had no effect. A property block is used rather than the
    /// material so the vanilla asset every other character shares is never
    /// written to.</summary>
    private static void Tint(GameObject piece, ColourTriple colour)
    {
        var tint = new Color(colour.R, colour.G, colour.B, 1f);
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(block);
            block.SetColor(SkinColourProperty, tint);
            renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>Tints the extracted body: skin on material 0 and the model's
    /// own hair on material 1, which is the split the game itself uses.
    ///
    /// The skin half only happens with a palette read off the live game. There
    /// is no defensible fallback for a body colour - a guessed one is worse
    /// than the model's own - so an unobserved palette leaves the skin alone
    /// and says so.</summary>
    private bool TryApplyBodyColours(CustomizationPalette palette, ColourTriple hairColour)
    {
        if (_bodyModel == null)
        {
            return false;
        }

        try
        {
            var block = new MaterialPropertyBlock();
            var hairTint = new Color(hairColour.R, hairColour.G, hairColour.B, 1f);
            _bodyModel.GetPropertyBlock(block, 1);
            block.SetColor(SkinColourProperty, hairTint);
            _bodyModel.SetPropertyBlock(block, 1);

            ColourTriple? skin = AppearanceColour.HulgiSkin(palette);
            if (!skin.HasValue)
            {
                return false;
            }

            var skinTint = new Color(skin.Value.R, skin.Value.G, skin.Value.B, 1f);
            block = new MaterialPropertyBlock();
            _bodyModel.GetPropertyBlock(block, 0);
            block.SetColor(SkinColourProperty, skinTint);
            _bodyModel.SetPropertyBlock(block, 0);
            return true;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "The companion's skin tone could not be applied on this build; he keeps the source " +
                $"model's own: {SafeLogText.Brief(exception)}");
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

    /// <summary>Name on hover, and a line when spoken to.</summary>
    private void AttachHover()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            CompanionHover.Attach(_root, _onTalk, _log);
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's hover label could not be attached: {SafeLogText.Brief(exception)}");
        }
    }

    public void Release()
    {
        ClearPoseParameter();
        _seat = SeatOffer.None;
        _animator = null;
        _helmetJoint = null;
        _bodyModel = null;
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
        _poseParameter = null;
        _seat = SeatOffer.None;
        _helmetJoint = null;
        _bodyModel = null;
        Report = null;
        PlacedAnchor = CompanionAnchor.None;
    }
}

/// <summary>A name on hover and a line when spoken to.
///
/// It has no collider of its own — it rides whatever the extracted visual
/// subtree offers, so it answers when vanilla's raycast happens to reach the
/// model and is silent otherwise. The director watches the Use key nearby for
/// the same reason it does for the compass: whether that raycast reaches a
/// mod-made object on this build is still unobserved, and a companion nobody
/// can talk to would be a poor companion.</summary>
internal sealed class CompanionHover : MonoBehaviour, Hoverable, Interactable
{
    private Action? _onTalk;
    private ManualLogSource? _log;

    public static CompanionHover Attach(GameObject root, Action onTalk, ManualLogSource log)
    {
        var component = root.AddComponent<CompanionHover>();
        component._onTalk = onTalk;
        component._log = log;
        return component;
    }

    public string GetHoverName()
    {
        return AtlasStrings.Get("companion.hulgi.name");
    }

    public string GetHoverText()
    {
        return AtlasStrings.Get("companion.hulgi.name") +
            "\n[<color=yellow><b>$KEY_Use</b></color>] " + AtlasStrings.Get("companion.talkVerb");
    }

    public float GetHoverOffset()
    {
        return 1.6f;
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        // A held key repeats every frame. He is talkative, not that talkative.
        if (hold)
        {
            return false;
        }

        Talk();
        return true;
    }

    /// <summary>Nothing may be used on him. He is not a container, a station or
    /// a trader, and an item interaction that did something would be a surface
    /// this design never asked for.</summary>
    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        return false;
    }

    public void Talk()
    {
        try
        {
            _onTalk?.Invoke();
        }
        catch (Exception exception)
        {
            _log?.LogWarning($"The companion could not speak: {SafeLogText.Brief(exception)}");
        }
    }
}
