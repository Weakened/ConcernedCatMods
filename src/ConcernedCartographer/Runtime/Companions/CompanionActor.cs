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

    public AppearanceChoice Chest { get; set; } = AppearanceChoice.None;

    public AppearanceChoice Legs { get; set; } = AppearanceChoice.None;

    public bool ChestAttached { get; set; }

    public bool LegsAttached { get; set; }

    /// <summary>True when a preset was attached and then removed because it was
    /// not being drawn on the head. Reported, because "no hair" and "no hair
    /// because this build draws it in the wrong place" are different answers
    /// and only one of them is a defect.</summary>
    public bool AppearanceRejected { get; set; }

    /// <summary>Where hair and beard were attached, and how that point was
    /// found. Reported because "attached" on its own turned out to be
    /// compatible with a braid hovering over a bald head: the attachment
    /// succeeded and the point was wrong.</summary>
    public string Joint { get; set; } = "<none>";

    /// <summary>The animator state actually used for the pose, or null when
    /// none of the candidates existed and the model kept its default.</summary>
    public string? PoseState { get; set; }

    public CompanionPose Pose { get; set; } = CompanionPose.SitOnGround;

    /// <summary>How far each attached piece was actually drawn from where it
    /// belongs, once he had been posed and could be measured.
    ///
    /// Reported even when everything fits, because "it fits" and "it fits by
    /// four centimetres" are different amounts of evidence, and the second is
    /// the one that distinguishes hair that landed from hair that happened to
    /// be inside a generous tolerance.</summary>
    public string Fit { get; set; } = "<unmeasured>";

    /// <summary>How the player can reach him: whether the game's own hover
    /// raycast has anything to hit, or whether he is reachable only by walking
    /// up and pressing Use.</summary>
    public string Interaction { get; private set; } = "<none>";

    public void NoteInteraction(string how)
    {
        Interaction = how;
    }

    public override string ToString()
    {
        return $"source={SourcePrefab} skeleton={UsedSkeleton} hair={Hair} ({(HairAttached ? "attached" : "not attached")}) " +
            $"beard={Beard} ({(BeardAttached ? "attached" : "not attached")})" +
            (AppearanceRejected ? " REJECTED-BY-FIT-CHECK " : " ") +
            $"joint={Joint} " +
            $"colour={(HairColourApplied ? HairColour.ToString() : "not applied")}" +
            $"{(HairColourObserved ? " (palette)" : " (fallback)")} skin={SkinColourApplied} " +
            $"chest={Chest} ({(ChestAttached ? "worn" : "not worn")}) " +
            $"legs={Legs} ({(LegsAttached ? "worn" : "not worn")}) " +
            $"pose={Pose} state={PoseState ?? "<default>"} fit=[{Fit}] interact={Interaction}";
    }
}

    /// <summary>How a step toward a destination went.</summary>
    internal enum WalkStep
    {
        /// <summary>Still going.</summary>
        Walking = 0,

        /// <summary>Close enough. Stop.</summary>
        Arrived = 1,

        /// <summary>The ground ahead could not be stood on, or the zone is not
        /// loaded. Stop, and stop where he is rather than pushing into
        /// it.</summary>
        Blocked = 2,
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
/// What is kept is then assembled <b>dark</b>. The new root is created
/// inactive, because re-parenting into a live object is exactly what runs
/// <c>Awake</c> - and the game's own scripts inside a character's visual,
/// <c>CharacterAnimEvent</c> first among them, wake straight into a
/// dereference of the <c>Character</c> this figure deliberately does not have.
/// They are destroyed while the object is still inactive, and the light is
/// switched on after they are gone. That is removal, not stripping: not one of
/// them has run.
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

    /// <summary>Bone names to look for, best first. Exact matches are tried
    /// before fragments, because a rig that contains both "Head" and
    /// "HeadTarget" should give up the one the game animates, and a neck is a
    /// last resort rather than an equal alternative.</summary>
    private static readonly string[] HeadBoneNames = { "head", "neck" };

    /// <summary>The shader property Valheim tints skin, hair and beards with.
    /// Cached as an id the way the game caches it.</summary>
    private static readonly int SkinColourProperty = Shader.PropertyToID("_SkinColor");

    /// <summary>The body shader's garment texture slots, named as the game
    /// names them.</summary>
    private static readonly int ChestTexProperty = Shader.PropertyToID("_ChestTex");
    private static readonly int ChestBumpProperty = Shader.PropertyToID("_ChestBumpMap");
    private static readonly int ChestMetalProperty = Shader.PropertyToID("_ChestMetal");
    private static readonly int LegsTexProperty = Shader.PropertyToID("_LegsTex");
    private static readonly int LegsBumpProperty = Shader.PropertyToID("_LegsBumpMap");
    private static readonly int LegsMetalProperty = Shader.PropertyToID("_LegsMetal");

    private readonly ManualLogSource _log;
    private readonly Action _onTalk;
    private readonly Func<string?> _hairOverride;
    private readonly Func<string?> _beardOverride;
    private readonly Func<string?> _chestOverride;
    private readonly Func<string?> _legsOverride;

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

    /// <summary>How the last customization piece was attached. Reported,
    /// because the two modes fail in different ways and the report used to say
    /// only that something was attached. Reset per build, or a build where
    /// nothing attached inherits the previous build's answer.</summary>
    private string _attachMode = "none";

    /// <summary>Pieces attached this build, by slot, so the fit check can take
    /// one off again.</summary>
    private readonly Dictionary<string, GameObject> _attachedPieces =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);

    private bool _verified;

    /// <summary>The seat he was placed on, if any.</summary>
    private SeatOffer _seat;

    /// <summary>The animator parameter currently held true for his pose, so it
    /// can be released before another is set.</summary>
    private string? _poseParameter;

    public CompanionActor(
        ManualLogSource log,
        Action onTalk,
        Func<string?>? hairOverride = null,
        Func<string?>? beardOverride = null,
        Func<string?>? chestOverride = null,
        Func<string?>? legsOverride = null)
    {
        _log = log;
        _onTalk = onTalk;
        _hairOverride = hairOverride ?? (() => null);
        _beardOverride = beardOverride ?? (() => null);
        _chestOverride = chestOverride ?? (() => null);
        _legsOverride = legsOverride ?? (() => null);
    }

    public bool Exists => _root != null;

    /// <summary>The seat he is using, if any. The director re-checks it every
    /// residency pass so a seat that is taken or taken away is given up.</summary>
    public SeatOffer Seat => _seat;

    public ActorReport? Report { get; private set; }

    /// <summary>The pose he is actually in, not the one that was asked for.
    /// The furniture sweep compares against this, so a companion who reported
    /// a seat he had failed to take would silence the sweep permanently.
    /// </summary>
    public CompanionPose Pose => Report?.Pose ?? CompanionPose.SitOnGround;

    /// <summary>The joint a line of speech should appear over, or null when
    /// there is no model. The head bone rather than the root: the game's
    /// in-world text follows the object it is given, and asks a
    /// <c>Character</c> for a head point - which this companion deliberately
    /// does not have, so it would otherwise put his voice at his feet.
    /// </summary>
    public Transform? SpeechAnchor =>
        _root == null ? null : _helmetJoint ?? FindHeadBone(_root.transform);

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
        _verified = false;
        _attachMode = "none";
        _attachedPieces.Clear();

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

        // A seat offered but not usable is not a pose we may claim to be in.
        // He is on the probed ground either way; reporting otherwise would tell
        // the furniture sweep it had succeeded and stop it looking again.
        CompanionPose achieved = pose == CompanionPose.SitOnSeat && !onSeat
            ? CompanionPose.SitOnGround
            : pose;

        _root.transform.position = new Vector3(where.X, where.Y, where.Z);
        _root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        ApplyPose(achieved);
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
    /// <summary><paramref name="record"/> is false when the pose is being set
    /// by hand for a look. What the report says he is doing has to keep meaning
    /// what the PLANNER decided, because the furniture sweep reads it: a hand
    /// pose that wrote "on a seat" would tell the sweep it had nothing left to
    /// look for and stop it finding real chairs, permanently. The animator does
    /// what it is told either way; only the bookkeeping is withheld.</summary>
    private void ApplyPose(CompanionPose pose, bool record = true)
    {
        if (Report != null && record)
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
                if (Report != null && record)
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


    /// <summary>The animator floats the game drives a player's legs with.
    /// Discovered rather than assumed - a controller without them leaves him
    /// sliding, which is worse than leaving him sitting, so the absence is
    /// checked once and reported.</summary>
    private const string ForwardSpeedParameter = "forward_speed";

    private const string SidewaySpeedParameter = "sideway_speed";

    private const string TurnSpeedParameter = "turn_speed";

    /// <summary>How close counts as arrived. Generous on purpose: the
    /// destination is a probed patch of ground, not a doorway, and grinding the
    /// last few centimetres looks worse than stopping short.</summary>
    private const float ArrivalMetres = 0.6f;

    /// <summary>Whether this build can animate a walk at all. When false he
    /// never strolls - he would slide, and a sliding companion reads as a
    /// broken one where a still companion just reads as still.</summary>
    public bool CanWalk =>
        _animator != null && HasFloatParameter(ForwardSpeedParameter);

    /// <summary>Walks him one step toward <paramref name="target"/>, following
    /// the ground under his feet.
    ///
    /// Deliberately not pathfinding. He walks the straight line, checks the
    /// ground he is about to stand on through the same probe that chose his
    /// spot in the first place, and stops if it will not hold him. A companion
    /// pottering around a camp does not need to solve a maze, and the failure
    /// mode of trying is one who walks into a wall forever.</summary>
    public WalkStep StepToward(Vector3 target, float deltaTime, float speed)
    {
        if (_root == null)
        {
            return WalkStep.Blocked;
        }

        Vector3 here = _root.transform.position;
        Vector3 flat = new Vector3(target.x - here.x, 0f, target.z - here.z);
        float distance = flat.magnitude;

        if (distance <= ArrivalMetres)
        {
            return WalkStep.Arrived;
        }

        Vector3 direction = flat / distance;
        float travel = Mathf.Min(speed * deltaTime, distance);
        Vector3 next = here + (direction * travel);

        if (!TryGroundAt(next, out float height))
        {
            return WalkStep.Blocked;
        }

        next.y = height;

        // A step that would climb or drop more than a person steps is a wall
        // or a hole, whichever way it goes.
        if (Mathf.Abs(next.y - here.y) > 0.6f)
        {
            return WalkStep.Blocked;
        }

        _root.transform.position = next;
        _root.transform.rotation = Quaternion.Slerp(
            _root.transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up),
            Mathf.Clamp01(deltaTime * 6f));

        SetLocomotion(speed);
        return WalkStep.Walking;
    }

    /// <summary>Stops the legs. Called whatever ended the stroll, including the
    /// actor being torn down, because an animator left with speed on its
    /// parameters keeps walking on the spot.</summary>
    public void StopWalking()
    {
        SetLocomotion(0f);
    }

    private void SetLocomotion(float speed)
    {
        if (_animator == null)
        {
            return;
        }

        try
        {
            if (HasFloatParameter(ForwardSpeedParameter))
            {
                _animator.SetFloat(ForwardSpeedParameter, speed);
            }

            if (HasFloatParameter(SidewaySpeedParameter))
            {
                _animator.SetFloat(SidewaySpeedParameter, 0f);
            }

            if (HasFloatParameter(TurnSpeedParameter))
            {
                _animator.SetFloat(TurnSpeedParameter, 0f);
            }
        }
        catch (Exception)
        {
            // A controller that will not take a float is not worth a notice
            // every frame of every stroll.
        }
    }

    private static bool TryGroundAt(Vector3 point, out float height)
    {
        height = point.y;

        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.IsZoneLoaded(point))
            {
                return false;
            }

            if (!zones.GetSolidHeight(point + (Vector3.up * 2f), out float found, out Vector3 normal, out GameObject _))
            {
                return false;
            }

            // The same slope limit the placement probe uses. Ground he could
            // not have been placed on is ground he should not walk onto.
            if (Vector3.Dot(normal, Vector3.up) < 0.75f)
            {
                return false;
            }

            height = found;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool HasFloatParameter(string name)
    {
        if (_animator == null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter != null &&
                parameter.type == AnimatorControllerParameterType.Float &&
                string.Equals(parameter.name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }


    /// <summary>Gives him something for the game's hover raycast to hit.
    ///
    /// Without this he has a name and a Talk verb that nobody can ever see: the
    /// extracted model carries no collider - every one of them is stripped,
    /// because a companion who blocks a doorway is a companion who has gone
    /// wrong - and <c>Player.FindHoverObject</c> is a physics raycast. No
    /// collider, no hit, no prompt. Walking up and pressing Use still worked,
    /// through the proximity fallback, which is exactly why this went unnoticed:
    /// the interaction worked and only the invitation was missing.
    ///
    /// The volume is a child rather than the root, so the model's own layers
    /// are left alone, and the raycast still finds the right component because
    /// the game resolves it with <c>GetComponentInParent&lt;Hoverable&gt;</c>.
    /// It sits on <c>piece_nonsolid</c> - a layer that is in the interact mask
    /// and, by the game's own collision matrix, does not stop a body. That is
    /// the same arrangement the Broken Compass already uses.</summary>
    private void BuildInteractionVolume()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            var volume = new GameObject("CC_HulgiInteract");
            volume.transform.SetParent(_root.transform, worldPositionStays: false);
            volume.transform.localPosition = Vector3.zero;
            volume.transform.localRotation = Quaternion.identity;

            var collider = volume.AddComponent<CapsuleCollider>();
            collider.radius = 0.45f;
            collider.height = 1.5f;
            collider.center = new Vector3(0f, 0.75f, 0f);

            int layer = ResolveNonSolidLayer();
            if (layer >= 0)
            {
                volume.layer = layer;
                Report?.NoteInteraction("hover volume on " + LayerMask.LayerToName(layer));
                return;
            }

            // No non-solid layer on this build. A trigger cannot stop a body
            // either, so he still does not block anything; whether the hover
            // raycast reaches it depends on the project's trigger query
            // setting, and the proximity prompt covers the case where it does
            // not.
            collider.isTrigger = true;
            Report?.NoteInteraction("hover volume on a pass-through trigger");
        }
        catch (Exception exception)
        {
            Report?.NoteInteraction("no hover volume");
            _log.LogInfo(
                "The companion could not be given a hover volume, so he is spoken to from the " +
                $"proximity prompt only: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>The first interaction layer this build actually has. Named
    /// rather than numbered: layer indices are project data and have moved
    /// between versions.</summary>
    private static int ResolveNonSolidLayer()
    {
        foreach (string name in new[] { "piece_nonsolid", "item" })
        {
            try
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    return layer;
                }
            }
            catch
            {
                // Try the next name.
            }
        }

        return -1;
    }

    /// <summary>Gets him on his feet for a stroll. Clears the sitting
    /// parameter without touching what the planner thinks his pose is - that
    /// belongs to where he SETTLES, and a stroll is a round trip.</summary>
    public void StandUp()
    {
        ClearPoseParameter();
    }

    /// <summary>Sits him down wherever the stroll left him.
    ///
    /// This one DOES update the report, because it is the truth now: he is on
    /// the ground somewhere other than the spot the planner picked, and the
    /// furniture sweep reading "on the ground" is exactly right - if there is a
    /// seat near where he has wandered to, he should be offered it.</summary>
    public void SettleWhereHeStands()
    {
        ApplyPose(CompanionPose.SitOnGround);
    }

    /// <summary>Puts him in a pose by hand, so he can be looked at in one.
    ///
    /// Presentation only, local only, and not persisted: it writes animator
    /// bools on our own extracted model and touches nothing in the world, no
    /// seat is claimed and nothing is sent anywhere. The next residency pass
    /// that rebuilds him restores the planned pose.
    ///
    /// It exists because clothing has to be checked standing, turning, seated
    /// on the ground and seated on furniture, and the ordinary lifecycle only
    /// ever produces one of those at a time. A garment that is bound to the
    /// skeleton correctly and a garment that merely happens to line up in one
    /// frozen pose look identical until something moves.</summary>
    public string ForcePose(string what)
    {
        if (_root == null || _animator == null)
        {
            return "There is no companion model to pose.";
        }

        switch (what)
        {
            case "stand":
                ClearPoseParameter();
                return "Hulgi is standing. He returns to his idle when he is next rebuilt.";

            case "seat":
                ApplyPose(CompanionPose.SitOnSeat, record: false);
                return "Hulgi is posed on a seat" +
                    (_seat.IsUsable ? "." : " - he has no seat, so this is the ground emote.");

            case "ground":
                ApplyPose(CompanionPose.SitOnGround, record: false);
                return "Hulgi is sitting on the ground.";

            default:
                return "Usage: cc_companion pose <stand|ground|seat>. Presentation only; he goes " +
                    "back to his planned pose when he is next rebuilt.";
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

            // Born dark, and it stays dark until the source's own scripts are
            // out of it. Re-parenting is what WAKES a subtree: the moment this
            // transform lands under an active parent, Unity runs Awake on
            // everything inside it - and the second line of
            // CharacterAnimEvent.Awake dereferences
            // GetComponentInParent<Character>(), which the extraction has just
            // guaranteed is not there. That Awake could only ever throw, and
            // production duly reported it on every single build.
            root = new GameObject("CC_Hulgi");
            root.SetActive(false);

            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            RemovePhysics(root);
            int quieted = RemoveBehaviours(root, out string quietedNames);

            // Nothing inside can run any more, so it is safe to switch on.
            // Everything after this line - the pose, the appearance, the hover
            // - then runs against a live object exactly as it did before.
            root.SetActive(true);

            if (quieted > 0)
            {
                _log.LogInfo(
                    $"[actor] {quieted} of the source character's own script(s) were taken out of the " +
                    $"body before it was ever enabled: {quietedNames}.");
            }

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

    /// <summary>Takes the source character's own scripts out of a subtree that
    /// has not been switched on yet, and says what went.
    ///
    /// This is not the "instantiate it and strip it afterwards" approach the
    /// whole extraction exists to avoid, and the difference is the entire
    /// point: every component here is destroyed while its object is still
    /// INACTIVE, so none of it has run and none of it can have registered
    /// itself anywhere. Stripping removes something that already woke. This
    /// removes something that never will.
    ///
    /// The rule is a type test rather than another name list, because the
    /// problem is not any one class. A MonoBehaviour inside a character's
    /// visual subtree is game code written for a live character - and the
    /// refusal above has just guaranteed this figure has no <c>Character</c>,
    /// no <c>ZNetView</c> and no AI anywhere above it, so that code has
    /// nothing to be written for. <c>CharacterAnimEvent</c> is the one
    /// production found: its <c>Awake</c> dereferences
    /// <c>GetComponentInParent&lt;Character&gt;()</c> immediately, and its
    /// <c>OnEnable</c> puts it in a static list <c>MonoUpdaters</c> walks every
    /// FixedUpdate and LateUpdate - so a half-built one does not merely log
    /// once; it sits inside the game's own update loop for the session.
    ///
    /// Nothing that draws him is a MonoBehaviour. <c>Animator</c>,
    /// <c>Renderer</c>, <c>SkinnedMeshRenderer</c>, <c>LODGroup</c> and
    /// <c>Transform</c> are all built-in components, so this cannot take away
    /// the body, the rig, the mesh or the animation. Anything Unity refuses to
    /// destroy - a <c>[RequireComponent]</c> dependency, which it refuses by
    /// writing to the log and carrying on rather than by throwing - is
    /// disabled instead and named in the summary, so that case is visible
    /// rather than silent - which is also why the count returned covers both
    /// halves: a build that refused every one of them must still say so out
    /// loud rather than look like a build that found nothing.
    /// The cloth family is left to <c>RemoveCloth</c>,
    /// which disables it for exactly that reason and should not have the
    /// decision re-litigated one method later.</summary>
    private static int RemoveBehaviours(GameObject subtree, out string names)
    {
        var removed = new List<string>();
        var refused = new List<string>();

        // Twice. A [RequireComponent] destroy is refused while the component
        // declaring it is still present, and the enumeration order is the
        // prefab's, not the dependency's; one retry clears the ordinary case
        // of a dependent that happened to come second.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (MonoBehaviour script in
                subtree.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (script == null)
                {
                    continue;
                }

                Type type = script.GetType();
                if (type.Namespace != null &&
                    type.Namespace.StartsWith("TheConcernedCat", StringComparison.Ordinal))
                {
                    // Ours, put on a figure we own, on purpose.
                    continue;
                }

                if (type.Name.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // RemoveCloth owns that family, and disables rather than
                    // destroys for a documented reason: those components
                    // declare [RequireComponent] on each other, and Unity
                    // refuses the destroy by writing an error to the player's
                    // log. Attempting it again here would trade one logged
                    // error for another, which is not a fix.
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(script);
                if (script == null)
                {
                    removed.Add(type.Name);
                }
                else if (pass == 1)
                {
                    // Still there, so Unity refused. Disable what can be
                    // disabled - and name it either way, including one that
                    // was already disabled. A game script that survives into
                    // the figure is precisely the thing this is here to make
                    // visible, and a disabled one is not safe by virtue of
                    // being disabled: Unity runs Awake on activation whether a
                    // component is enabled or not.
                    if (script.enabled)
                    {
                        script.enabled = false;
                    }

                    refused.Add(type.Name);
                }
            }
        }

        names = removed.Count == 0 ? "<none>" : string.Join(", ", removed);
        if (refused.Count > 0)
        {
            names += $" (still present, disabled instead because this build refuses to destroy them: " +
                $"{string.Join(", ", refused)})";
        }

        // Both halves, so a build where everything was refused still says so
        // out loud instead of returning zero and logging nothing.
        return removed.Count + refused.Count;
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
            Report.Joint = joint == null
                ? "<none>"
                : PathOf(joint) + (_helmetJoint != null ? " (VisEquipment.m_helmet)" : " (name search)") +
                  DescribeScale(joint);

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
                Report.Joint += " via " + _attachMode;
            }

            Report.SkinColourApplied = TryApplyBodyColours(palette, hairColour);

            // Clothing. The owner asked for a rag tunic and leather pants, and
            // that is all this is: a garment drawn on the body. Nothing is
            // given to him, nothing is taken from anyone, and no armour value
            // is ever read - he has no health to protect.
            Report.Chest = AppearancePlan.Choose(
                catalog.Chest, AppearancePlan.HulgiChest, _chestOverride());
            Report.Legs = AppearancePlan.Choose(
                catalog.Legs, AppearancePlan.HulgiLegs, _legsOverride());
            Report.ChestAttached = TryAttachGarment(Report.Chest.PrefabName, "chest");
            Report.LegsAttached = TryAttachGarment(Report.Legs.PrefabName, "legs");
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
            GameObject? attach = FindAttachChild(prefab, out bool namedForSkin);
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
            int cloth = RemoveCloth(piece);

            // Same rule as the body, and for the same reason: this piece is
            // about to be parented into a LIVE figure, which is what wakes it.
            // Whatever scripts an item's attachment mesh carries, they were
            // written for a character that is wearing it.
            int pieceScripts = RemoveBehaviours(piece, out string pieceScriptNames);

            // Whether the mesh is SKINNED decides how it attaches, and the
            // child's name is only a hint at that. A skinned mesh is drawn by
            // its bones, not by its transform: its vertices are authored in
            // the character's own space, so hanging one off a head joint draws
            // it wherever the body's head would be if the body were standing
            // at the origin. That is what a braid floating a metre over a
            // seated, bald companion looked like in game - the attachment
            // reported success every time, and the mesh was never being drawn
            // where it was attached.
            bool skinned = namedForSkin ||
                piece.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) != null;

            if (skinned && _bodyModel != null)
            {
                // Bound to the body's own skeleton and parented beside it -
                // the game's attach_skin path, which is the only thing that
                // makes a skinned customization mesh follow an animation.
                piece.transform.SetParent(_bodyModel.transform.parent);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localRotation = Quaternion.identity;
                _attachMode = "skinned " + BindToBody(piece);
            }
            else
            {
                // SetParent's default - worldPositionStays: true - is load
                // bearing, and not for the reason its name suggests. Position
                // and rotation are overwritten on the next two lines either
                // way; what it actually preserves here is world SCALE, by
                // recomputing localScale against the joint's own. Valheim's
                // player rig does not hang its attachment points at unit
                // scale, so parenting with `false` leaves the piece at the
                // joint's scale instead of its own - and a hair mesh whose
                // vertices sit away from its pivot then draws that offset
                // multiplied, which is a braid hovering a metre above a bald
                // head. Observed exactly that way in game.
                piece.transform.SetParent(joint);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localRotation = Quaternion.identity;
                _attachMode = skinned ? "joint (no body model to bind to)" : "joint";
            }

            ApplyEquipOffset(prefab, piece.transform);
            Tint(piece, colour);
            _log.LogInfo(
                $"[appearance] {slot} {prefabName}: {DescribePiece(prefab, piece)}" +
                (cloth > 0 ? $" cloth-stripped={cloth}" : string.Empty) +
                (pieceScripts > 0 ? $" scripts-quieted=[{pieceScriptNames}]" : string.Empty));

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            _attachedPieces[slot] = attached;
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

    /// <summary>Checks that what was attached is actually being drawn on the
    /// head, and takes it off if it is not.
    ///
    /// Run a moment after construction rather than during it: a skinned mesh is
    /// drawn by its bones, and on the frame it is created those bones have not
    /// been posed yet, so every piece looks correct no matter how it is bound.
    /// The caller ticks this once the animator has had a frame.
    ///
    /// Removing is the point. An attachment that reports success while the
    /// mesh renders a metre away is the failure this exists to catch, and
    /// leaving it on screen would make a bug out of a gap.</summary>
    public void VerifyAppearance()
    {
        if (_verified || _root == null || Report == null || _attachedPieces.Count == 0)
        {
            return;
        }

        // A hidden actor has no active renderers, so every piece would measure
        // as unmeasurable and be destroyed for it. Wait until he is on screen
        // to judge where his hair is.
        if (!_root.activeInHierarchy)
        {
            return;
        }

        _verified = true;
        Transform? head = _helmetJoint ?? FindHeadBone(_root.transform);
        if (head == null)
        {
            Report.Fit = "no head joint to measure against";
            return;
        }

        var measured = new List<string>();

        foreach (KeyValuePair<string, GameObject> piece in _attachedPieces)
        {
            if (piece.Value == null)
            {
                continue;
            }

            // Hair and beards are measured against the head. A tunic is worn on
            // the whole body, and its centre is legitimately half a torso from
            // any single bone, so it is measured against the body instead.
            bool garment = piece.Key.StartsWith("chest", StringComparison.Ordinal) ||
                piece.Key.StartsWith("legs", StringComparison.Ordinal);
            Vector3 reference = garment && _bodyModel != null
                ? _bodyModel.bounds.center
                : head.position;
            float tolerance = garment
                ? AppearanceFit.GarmentToleranceMetres
                : AppearanceFit.ToleranceMetres;

            float distance = DistanceFrom(piece.Value, reference);
            bool fits = AppearanceFit.Fits(distance, tolerance);
            measured.Add($"{piece.Key} {distance:0.00}m/{tolerance:0.00}{(fits ? "" : " REMOVED")}");
            if (fits)
            {
                continue;
            }

            _log.LogInfo(
                $"The companion's {piece.Key} piece is not being drawn where it was put on this build " +
                $"({distance:0.00} m away), so it has been removed rather than left floating. He keeps " +
                "the model's own look; nothing else is affected.");

            MarkSlotRemoved(piece.Key);
            Report.AppearanceRejected = true;
            UnityEngine.Object.Destroy(piece.Value);
        }

        Report.HairColourApplied = Report.HairAttached || Report.BeardAttached;
        Report.Fit = measured.Count == 0 ? "nothing attached" : string.Join(", ", measured);
    }

    private void MarkSlotRemoved(string slot)
    {
        if (Report == null)
        {
            return;
        }

        if (slot.StartsWith("hair", StringComparison.Ordinal))
        {
            Report.HairAttached = false;
        }
        else if (slot.StartsWith("beard", StringComparison.Ordinal))
        {
            Report.BeardAttached = false;
        }
        else if (slot.StartsWith("chest", StringComparison.Ordinal))
        {
            Report.ChestAttached = false;
        }
        else if (slot.StartsWith("legs", StringComparison.Ordinal))
        {
            Report.LegsAttached = false;
        }
    }

    /// <summary>How far a piece is drawn from the head, using rendered bounds
    /// rather than transforms: for a skinned mesh the transform is exactly the
    /// thing that does not tell you where it ended up.</summary>
    private static float DistanceFrom(GameObject piece, Vector3 reference)
    {
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(includeInactive: false))
        {
            if (renderer == null)
            {
                continue;
            }

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return any ? Vector3.Distance(bounds.center, reference) : float.NaN;
    }

    /// <summary>Binds a skinned customization mesh to the body's skeleton the
    /// way the game binds one.
    ///
    /// <c>VisEquipment.AttachItem</c>'s attach_skin path is three lines long:
    /// parent the instance beside the body model, then for every skinned
    /// renderer under it assign <c>rootBone = m_bodyModel.rootBone</c> and
    /// <c>bones = m_bodyModel.bones</c> — the body's array, verbatim, in the
    /// body's own order. That is the whole contract a vanilla customization
    /// mesh is authored against: its bindposes are indexed against the player
    /// skeleton's array LAYOUT, so slot 17 means whatever the body's slot 17
    /// means.
    ///
    /// This used to remap bone by bone on name, which preserves the PIECE's
    /// ordering instead. Where the two orders agree that yields the identical
    /// array and nothing is wrong — which is why the beard always landed.
    /// Where they disagree, every vertex is weighted to the wrong joint and the
    /// mesh is drawn somewhere else entirely: Long Braid came out 0.99 m away,
    /// at the height an unanimated bind pose puts a head. Same bone count, same
    /// names, a complete match, and the wrong answer (#305).
    ///
    /// Name matching survives only for the case the game never meets — a mesh
    /// whose bindpose count is not the body's bone count, which cannot be handed
    /// the body's array at all.</summary>
    private string BindToBody(GameObject piece)
    {
        if (_bodyModel == null)
        {
            return "(no body model)";
        }

        Transform[] bodyBones = _bodyModel.bones;
        if (bodyBones == null || bodyBones.Length == 0)
        {
            return "(body model has no bones)";
        }

        int adopted = 0;
        int renamed = 0;
        int missed = 0;
        string ordering = string.Empty;
        var strays = new List<Transform>();

        foreach (SkinnedMeshRenderer mesh in
            piece.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            if (mesh == null)
            {
                continue;
            }

            Transform[] original = mesh.bones ?? Array.Empty<Transform>();
            int bindposes = mesh.sharedMesh == null ? -1 : mesh.sharedMesh.bindposes.Length;

            switch (SkinBinding.Decide(bindposes, bodyBones.Length))
            {
                case SkinBindingMode.AdoptBodySkeleton:
                    // The one line the game runs, and the only one that is
                    // right for a mesh authored against the player skeleton.
                    if (ordering.Length == 0)
                    {
                        ordering = DescribeBoneOrder(original, bodyBones);
                    }

                    mesh.rootBone = _bodyModel.rootBone;
                    mesh.bones = bodyBones;

                    // The precomputed local bounds came from the piece's own
                    // armature; against ours they can cull the mesh from
                    // perfectly ordinary angles.
                    mesh.updateWhenOffscreen = true;
                    CollectStrays(original, piece, strays);
                    adopted++;
                    break;

                case SkinBindingMode.MatchByName when RebindByName(mesh, bodyBones, piece, strays):
                    renamed++;
                    break;

                default:
                    missed++;
                    break;
            }
        }

        // The armature the piece arrived with is now referenced by nothing.
        // Left in place it is a second skeleton inside the actor that no
        // Animator drives, and FindBoneNamed would happily hand a later garment
        // one of its joints.
        //
        // Only when EVERY mesh on the piece was rebound, though. A piece can
        // carry more than one, they share the one armature, and a mesh that was
        // refused is still bound to it - tearing it out from under that mesh
        // would trade a second skeleton for a mesh pointing at destroyed
        // transforms, which is a worse bug and a harder one to see.
        if (missed == 0)
        {
            foreach (Transform stray in strays)
            {
                if (stray == null || stray.parent != piece.transform)
                {
                    continue;
                }

                // Some pieces put the mesh INSIDE the armature rather than
                // beside it. Destroying the armature would take the renderer
                // with it, and because Destroy is deferred the piece would
                // vanish a frame later and the fit check would be blamed for
                // it - it measures no active renderer, gets NaN, and reports
                // the piece as "not drawn where it was put".
                if (stray.GetComponentInChildren<Renderer>(includeInactive: true) != null)
                {
                    continue;
                }

                UnityEngine.Object.Destroy(stray.gameObject);
            }
        }

        var text = new System.Text.StringBuilder("(");
        text.Append("adopted ").Append(adopted);
        if (renamed > 0)
        {
            text.Append(", name-matched ").Append(renamed);
        }

        if (missed > 0)
        {
            text.Append(", ").Append(missed).Append(" unbound");
        }

        if (ordering.Length > 0)
        {
            text.Append("; ").Append(ordering);
        }

        return text.Append(')').ToString();
    }

    /// <summary>Whether the piece's own bone array was in the body's order.
    /// Purely diagnostic, and the line that settles #305 in the log rather than
    /// by argument: a piece that reports "order differed at N" is one the old
    /// name-matching rebind would have drawn in the wrong place.</summary>
    private static string DescribeBoneOrder(Transform[] pieceBones, Transform[] bodyBones)
    {
        if (pieceBones == null || pieceBones.Length == 0)
        {
            return "piece carried no bone array";
        }

        if (pieceBones.Length != bodyBones.Length)
        {
            return $"piece bone array was {pieceBones.Length}, body's is {bodyBones.Length}";
        }

        for (int index = 0; index < pieceBones.Length; index++)
        {
            string mine = pieceBones[index] == null ? "<null>" : pieceBones[index].name;
            string theirs = bodyBones[index] == null ? "<null>" : bodyBones[index].name;
            if (!string.Equals(mine, theirs, StringComparison.Ordinal))
            {
                return $"bone order differed from the body's at {index} ({mine} vs {theirs})";
            }
        }

        return "bone order already matched the body's";
    }

    /// <summary>The fallback for a mesh that cannot take the body's array
    /// because it does not have the body's bindpose count: match what can be
    /// matched by name, and refuse the mesh outright rather than half-bind it.
    /// No vanilla customization item takes this path.</summary>
    private bool RebindByName(
        SkinnedMeshRenderer mesh, Transform[] bodyBones, GameObject piece, List<Transform> strays)
    {
        if (mesh.bones == null || mesh.bones.Length == 0)
        {
            return false;
        }

        var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (Transform bone in bodyBones)
        {
            if (bone != null && !byName.ContainsKey(bone.name))
            {
                byName[bone.name] = bone;
            }
        }

        Transform[] source = mesh.bones;
        var mapped = new Transform[source.Length];

        for (int index = 0; index < source.Length; index++)
        {
            Transform bone = source[index];
            if (bone == null || !byName.TryGetValue(bone.name, out Transform? match))
            {
                // Still bound to the armature it arrived with, so none of that
                // armature may be destroyed on its behalf. A mesh pointing at
                // half a skeleton looks like a different bug entirely.
                return false;
            }

            mapped[index] = match!;
        }

        CollectStrays(source, piece, strays);
        mesh.bones = mapped;
        mesh.rootBone = mesh.rootBone != null && byName.TryGetValue(mesh.rootBone.name, out Transform? root)
            ? root!
            : _bodyModel!.rootBone;
        mesh.updateWhenOffscreen = true;
        return true;
    }

    private static void CollectStrays(Transform[] bones, GameObject piece, List<Transform> strays)
    {
        foreach (Transform bone in bones)
        {
            if (bone != null && bone.IsChildOf(piece.transform))
            {
                strays.Add(bone);
            }
        }
    }

    /// <summary>What the attached piece actually is: which child was taken,
    /// what draws it, and what it is bound to. Diagnostic; one line per
    /// attachment, and the only way to tell a mesh that is in the wrong place
    /// from a mesh that is drawn somewhere other than where it was put.</summary>
    private string DescribePiece(GameObject prefab, GameObject piece)
    {
        var text = new System.Text.StringBuilder();
        Transform offset = prefab.transform.Find("equipoffset");
        if (_bodyModel != null)
        {
            text.Append("body bones=").Append(_bodyModel.bones == null ? 0 : _bodyModel.bones.Length)
                .Append(" root=").Append(_bodyModel.rootBone == null ? "<null>" : _bodyModel.rootBone.name)
                .Append(" bodyBindposes=").Append(
                    _bodyModel.sharedMesh == null ? -1 : _bodyModel.sharedMesh.bindposes.Length)
                .Append(" bodyWorld=").Append(_bodyModel.transform.position.ToString("0.###"))
                .Append(" bodyBounds=").Append(_bodyModel.bounds.center.ToString("0.###"))
                .Append(' ');
        }

        text.Append("equipoffset=").Append(
            offset == null ? "<none>" : offset.localPosition.ToString("0.###"));
        text.Append(" children=");
        for (int index = 0; index < prefab.transform.childCount; index++)
        {
            if (index > 0)
            {
                text.Append('|');
            }

            text.Append(prefab.transform.GetChild(index).name);
        }

        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer == null)
            {
                continue;
            }

            text.Append(" [").Append(renderer.GetType().Name).Append(' ').Append(renderer.name);
            if (renderer is SkinnedMeshRenderer skinned)
            {
                text.Append(" bones=").Append(skinned.bones == null ? 0 : skinned.bones.Length);
                text.Append(" root=").Append(
                    skinned.rootBone == null ? "<null>" : skinned.rootBone.name);
                text.Append(" bindposes=").Append(
                    skinned.sharedMesh == null ? -1 : skinned.sharedMesh.bindposes.Length);
            }

            text.Append(" localPos=").Append(renderer.transform.localPosition.ToString("0.###"));
            text.Append(" worldPos=").Append(renderer.transform.position.ToString("0.###"));
            text.Append(" bounds=").Append(renderer.bounds.center.ToString("0.###"));
            text.Append(" parent=").Append(
                renderer.transform.parent == null ? "<root>" : renderer.transform.parent.name);
            text.Append(']');
        }

        return text.ToString();
    }

    /// <summary>Puts one garment on the extracted body.
    ///
    /// Armour attaches differently from a customization preset, and the
    /// difference is the whole reason this is its own method: an armour prefab
    /// can carry SEVERAL <c>attach_*</c> children, and each names the joint it
    /// belongs on. <c>attach_skin</c> is the skinned piece that deforms with
    /// the body; <c>attach_&lt;joint&gt;</c> is a rigid piece - a buckle, a
    /// strap - that hangs off the bone of that name. Transcribed from
    /// <c>VisEquipment.AttachArmor</c>, including which of the two gets its
    /// bones rebound.
    ///
    /// The body's own chest and leg textures come from the item's armour
    /// material, which is the half that makes a tunic look like cloth rather
    /// than like a mesh floating over bare skin.</summary>
    private bool TryAttachGarment(string? prefabName, string slot)
    {
        if (string.IsNullOrEmpty(prefabName) || _root == null)
        {
            return false;
        }

        GameObject? prefab = LocalVisual.FindPrefab(prefabName!);
        if (prefab == null)
        {
            _log.LogInfo(
                $"The companion's {slot} garment \"{prefabName}\" is not in this build's item table, " +
                "so he is not wearing it.");
            return false;
        }

        bool anything = ApplyGarmentTextures(prefab, slot);

        int children = prefab.transform.childCount;
        for (int index = 0; index < children; index++)
        {
            Transform child = prefab.transform.GetChild(index);
            if (child == null || !child.gameObject.name.StartsWith("attach_", StringComparison.Ordinal))
            {
                continue;
            }

            string jointName = child.gameObject.name.Substring("attach_".Length);
            if (AttachGarmentPiece(child.gameObject, jointName, slot, prefabName!))
            {
                anything = true;
            }
        }

        return anything;
    }

    /// <summary>One <c>attach_*</c> child of a garment.</summary>
    private bool AttachGarmentPiece(
        GameObject source, string jointName, string slot, string prefabName)
    {
        GameObject holder = new GameObject("CC_GarmentHarvest");
        holder.SetActive(false);

        GameObject? piece = null;
        try
        {
            piece = UnityEngine.Object.Instantiate(source, holder.transform);
            if (ContainsForbiddenComponent(piece, out string offender))
            {
                _log.LogInfo(
                    $"The companion's {slot} garment \"{prefabName}\" carries {offender}, so it was " +
                    "not used. Nothing was stripped; the garment was refused.");
                return false;
            }

            RemovePhysics(piece);
            RemoveCloth(piece);
            if (RemoveBehaviours(piece, out string garmentScripts) > 0)
            {
                _log.LogInfo(
                    $"[appearance] {slot} {prefabName} ({jointName}): scripts-quieted=[{garmentScripts}]");
            }

            if (string.Equals(jointName, "skin", StringComparison.Ordinal))
            {
                if (_bodyModel == null)
                {
                    return false;
                }

                piece.transform.SetParent(_bodyModel.transform.parent);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localRotation = Quaternion.identity;
                BindToBody(piece);
            }
            else
            {
                Transform? joint = FindBoneNamed(jointName);
                if (joint == null)
                {
                    _log.LogInfo(
                        $"The companion's model has no \"{jointName}\" joint, so part of his {slot} " +
                        "garment was left off.");
                    return false;
                }

                piece.transform.SetParent(joint);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localRotation = Quaternion.identity;
            }

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            _attachedPieces[slot + ":" + jointName] = attached;
            return true;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's {slot} garment could not be attached: {SafeLogText.Brief(exception)}");
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

    /// <summary>Paints the garment's own textures onto the body, the way the
    /// game does when you put armour on. Without it the skin underneath still
    /// reads as bare.</summary>
    private bool ApplyGarmentTextures(GameObject prefab, string slot)
    {
        if (_bodyModel == null)
        {
            return false;
        }

        try
        {
            var drop = prefab.GetComponent<ItemDrop>();
            Material? armour = drop?.m_itemData?.m_shared?.m_armorMaterial;
            if (armour == null)
            {
                return false;
            }

            bool legs = string.Equals(slot, "legs", StringComparison.Ordinal);
            int tex = legs ? LegsTexProperty : ChestTexProperty;
            int bump = legs ? LegsBumpProperty : ChestBumpProperty;
            int metal = legs ? LegsMetalProperty : ChestMetalProperty;

            // material, not sharedMaterial: this writes textures rather than a
            // property block, so it must land on this renderer's own instance
            // and never on the asset every character in the world is drawn
            // with. Unity destroys the instance with the renderer.
            Material body = _bodyModel.material;
            body.SetTexture(tex, armour.GetTexture(tex));
            body.SetTexture(bump, armour.GetTexture(bump));
            body.SetTexture(metal, armour.GetTexture(metal));
            return true;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's {slot} garment texture could not be applied: " +
                SafeLogText.Brief(exception));
            return false;
        }
    }

    /// <summary>A bone by exact name, the animated skeleton first.
    ///
    /// Searching the whole subtree was fine until something else got attached
    /// into it. An armature that arrives with a customization piece has the
    /// same joint names as the real one and is driven by nothing, so a plain
    /// name search could hand a garment a dead LeftHand and leave a sleeve
    /// standing still. The body model's own bone array is the skeleton the
    /// Animator actually moves; the subtree walk stays only as the fallback for
    /// a joint the body model does not list, such as an attachment point.</summary>
    private Transform? FindBoneNamed(string name)
    {
        if (_bodyModel != null && _bodyModel.bones != null)
        {
            foreach (Transform bone in _bodyModel.bones)
            {
                if (bone != null && string.Equals(bone.name, name, StringComparison.Ordinal))
                {
                    return bone;
                }
            }
        }

        if (_root == null)
        {
            return null;
        }

        foreach (Transform bone in _root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (bone != null && string.Equals(bone.name, name, StringComparison.Ordinal))
            {
                return bone;
            }
        }

        return null;
    }

    /// <summary>Strips cloth simulation off an attached piece.
    ///
    /// Cloth needs colliders and a bone map that the game binds for it out of a
    /// live <c>VisEquipment</c>. A static presentation figure has neither, so
    /// an unbound cloth component is simulation nobody asked for on somebody
    /// who never moves - and on 1.0.12 it is worse than useless: MagicaCloth
    /// builds itself when the object is enabled, against the transforms it was
    /// serialized with. On a piece whose armature we have just swapped for the
    /// body's, that is a mesh being driven by a skeleton that is no longer
    /// there. Hair and beards get the same treatment as garments for exactly
    /// that reason; a braid is one of the things the game simulates.
    ///
    /// Matched on type NAME rather than by referencing the type, because it
    /// lives in a third-party dependency the game ships and a hard reference
    /// would stop this build loading on any version that ships a different
    /// one. That match is deliberately broad: half a cloth system is worse
    /// than all of it, so everything in the family goes quiet together.
    /// </summary>
    private static int RemoveCloth(GameObject piece)
    {
        int removed = 0;

        foreach (Cloth cloth in piece.GetComponentsInChildren<Cloth>(includeInactive: true))
        {
            if (cloth != null)
            {
                UnityEngine.Object.DestroyImmediate(cloth);
                removed++;
            }
        }

        foreach (Component component in
            piece.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null || component is Transform || component is Renderer)
            {
                continue;
            }

            string name = component.GetType().Name;
            if (name.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // Disabled rather than destroyed. Destroying is what Unity refuses
            // when another component declares [RequireComponent] on this one -
            // and it refuses by writing an error to the player's log and
            // carrying on, not by throwing, so a try/catch around it catches
            // nothing and the count comes out wrong. Disabling cannot be
            // refused, cannot break a dependency, and is enough: a cloth
            // component that never receives OnEnable never builds itself.
            if (component is Behaviour behaviour)
            {
                if (behaviour.enabled)
                {
                    behaviour.enabled = false;
                    removed++;
                }

                continue;
            }

            UnityEngine.Object.DestroyImmediate(component);
            if (component == null)
            {
                removed++;
            }
        }

        return removed;
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

        // Skin first, and each half in its own try. They are independent
        // writes to different submaterials, and a model with only one of them
        // should lose only the tint it cannot take - not both because the
        // other threw first.
        bool skinApplied = false;
        ColourTriple? skin = AppearanceColour.HulgiSkin(palette);
        if (skin.HasValue)
        {
            skinApplied = TrySetBodyColour(0, skin.Value, "skin tone");
        }

        TrySetBodyColour(1, hairColour, "hair tint");
        return skinApplied;
    }

    /// <summary>One tinted submaterial, through a property block so the shared
    /// vanilla material is never written to.</summary>
    private bool TrySetBodyColour(int materialIndex, ColourTriple colour, string what)
    {
        if (_bodyModel == null)
        {
            return false;
        }

        try
        {
            var block = new MaterialPropertyBlock();
            _bodyModel.GetPropertyBlock(block, materialIndex);
            block.SetColor(SkinColourProperty, new Color(colour.R, colour.G, colour.B, 1f));
            _bodyModel.SetPropertyBlock(block, materialIndex);
            return true;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                $"The companion's {what} could not be applied on this build; he keeps the source " +
                $"model's own: {SafeLogText.Brief(exception)}");
            return false;
        }
    }

    /// <summary>The joint's world scale, when it is not 1. Silent otherwise,
    /// because a unit-scale joint says nothing; a joint that is not unit scale
    /// is exactly why attachment has to be parented the way the game parents
    /// it.</summary>
    private static string DescribeScale(Transform joint)
    {
        Vector3 scale = joint.lossyScale;
        bool unit = Mathf.Abs(scale.x - 1f) < 0.01f &&
            Mathf.Abs(scale.y - 1f) < 0.01f &&
            Mathf.Abs(scale.z - 1f) < 0.01f;
        return unit ? "" : $" scale=({scale.x:0.###}, {scale.y:0.###}, {scale.z:0.###})";
    }

    /// <summary>A transform's path under the actor root. Diagnostic only, and
    /// the thing that turns "the hair is in the wrong place" into a fact about
    /// which object it is parented to.</summary>
    private static string PathOf(Transform transform)
    {
        string path = transform.name;
        Transform? parent = transform.parent;
        int guard = 0;
        while (parent != null && guard++ < 12)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private Transform? FindHeadBone(Transform root)
    {
        // The animated skeleton first, for the same reason FindBoneNamed does
        // it: an armature that arrives with an attached piece has a Head too,
        // nothing drives it, and this transform is the reference every fit
        // measurement is taken against. Measuring against a bind-pose head
        // fails every piece at once and blames the pieces.
        if (_bodyModel != null && _bodyModel.bones != null)
        {
            foreach (string wanted in HeadBoneNames)
            {
                foreach (Transform bone in _bodyModel.bones)
                {
                    if (bone != null &&
                        string.Equals(bone.name, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return bone;
                    }
                }
            }
        }

        Transform[] bones = root.GetComponentsInChildren<Transform>(includeInactive: true);

        foreach (string wanted in HeadBoneNames)
        {
            foreach (Transform bone in bones)
            {
                if (bone != null && string.Equals(bone.name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return bone;
                }
            }
        }

        foreach (string wanted in HeadBoneNames)
        {
            foreach (Transform bone in bones)
            {
                if (bone != null && !string.IsNullOrEmpty(bone.name) &&
                    bone.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
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
            BuildInteractionVolume();
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
