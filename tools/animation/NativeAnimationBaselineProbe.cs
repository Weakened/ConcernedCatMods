using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>
/// Explicit, bounded, read-only observation of an already selected player Animator.
/// The lead calls Begin, then SampleLateFrame once per rendered update, then Finish.
/// It never changes an Animator, bone, controller, input, physics, equipment or game state.
/// This is diagnostic tooling, not replacement animation or evidence of a completed baseline.
/// </summary>
internal sealed class NativeAnimationBaselineProbe
{
    private const int MaximumFrames = 3600;
    private const int MaximumCharacters = 16 * 1024 * 1024;
    private const double MaximumSeconds = 20;
    private const int MaximumLayers = 16;
    private const int MaximumClips = 512;
    private const int MaximumClipReferences = 2048;
    private readonly Player _player;
    private readonly Animator _animator;
    private readonly Transform _root;
    private AnimatorControllerParameter[] _parameters = Array.Empty<AnimatorControllerParameter>();
    private readonly List<string> _records = new List<string>();
    private readonly List<AnimatorClipInfo> _clipScratch = new List<AnimatorClipInfo>();
    private readonly Dictionary<int, Transform> _bones = new Dictionary<int, Transform>();
    private readonly Stopwatch _clock = new Stopwatch();
    private int _lastFrame = -1;
    private int _frames;
    private int _characters;
    private bool _finished;

    private NativeAnimationBaselineProbe(Player player, Animator animator)
    {
        _player = player;
        _animator = animator;
        _root = player.transform;
    }

    /// <summary>
    /// All identity/evidence strings are caller claims, not verified hashes or native-isolation proof.
    /// The selected Animator must be the actual local player rig, cross-checked by the lead against
    /// CharacterAnimEvent and ZSyncAnimation. No first-child Animator selection is performed here.
    /// </summary>
    internal static NativeAnimationBaselineProbe Begin(Player player, Animator animator,
        string action, string gameVersion, string buildIdentity, string isolationEvidence)
    {
        if (!LocalOwner(player) || animator == null ||
            (animator.transform != player.transform && !animator.transform.IsChildOf(player.transform)))
        {
            throw new ArgumentException("Select the actual player Animator under the actual player root.");
        }
        if (string.IsNullOrWhiteSpace(isolationEvidence))
        {
            throw new ArgumentException("A lead-recorded native-isolation evidence reference is required.");
        }

        // A disabled climb setting alone does not remove this candidate's rejected observer IK hook.
        MethodInfo? ik = AccessTools.Method(typeof(CharacterAnimEvent), "OnAnimatorIK", new[] { typeof(int) });
        Patches? patches = ik != null ? Harmony.GetPatchInfo(ik) : null;
        string[] owners = patches != null ? new List<string>(patches.Owners).ToArray() : Array.Empty<string>();
        foreach (string owner in owners)
        {
            if (owner == "com.theconcernedcat.valheim.concernedladders.pose" ||
                owner == "com.theconcernedcat.valheim.concernedladders.presentation" ||
                owner == "com.theconcernedcat.valheim.concernedladders.original-ik-cleanup")
            {
                throw new InvalidOperationException("Native baseline refused: a known ladder presentation/cleanup hook is installed.");
            }
        }

        var probe = new NativeAnimationBaselineProbe(player, animator);
        probe.Inventory(action, gameVersion, buildIdentity, isolationEvidence, owners);
        probe._clock.Start();
        return probe;
    }

    internal bool Finished => _finished;

    /// <summary>
    /// Call once from the lead's explicit diagnostic LateUpdate. This records that callback's
    /// evaluated pose; it does not claim to execute after every other native/mod pose writer.
    /// No console/file I/O occurs during sampling. Collection stops on identity changes or caps.
    /// </summary>
    internal void SampleLateFrame()
    {
        if (_finished || Time.frameCount == _lastFrame) { return; }
        try
        {
            long started = Stopwatch.GetTimestamp();
            if (!Available()) { return; }
            _lastFrame = Time.frameCount;
            var layers = new LayerRecord[Math.Min(MaximumLayers, _animator.layerCount)];
            for (int layer = 0; layer < layers.Length; layer++)
            {
                bool transition = _animator.IsInTransition(layer);
                AnimatorTransitionInfo transitionInfo = transition ? _animator.GetAnimatorTransitionInfo(layer) : default;
                layers[layer] = new LayerRecord
                {
                    index = layer,
                    name = _animator.GetLayerName(layer),
                    weight = _animator.GetLayerWeight(layer),
                    transitioning = transition,
                    transitionNormalizedTime = transition ? transitionInfo.normalizedTime : 0,
                    transitionDuration = transition ? transitionInfo.duration : 0,
                    transitionDurationUnit = transition ? transitionInfo.durationUnit.ToString() : "none",
                    current = State(_animator.GetCurrentAnimatorStateInfo(layer)),
                    currentClips = ActiveClips(layer, false),
                    next = transition ? State(_animator.GetNextAnimatorStateInfo(layer)) : null,
                    nextClips = transition ? ActiveClips(layer, true) : Array.Empty<WeightedClip>()
                };
            }

            var pose = new List<BonePose>(_bones.Count);
            foreach (KeyValuePair<int, Transform> entry in _bones)
            {
                // Inventory all mapped bones, but sample the body/limbs rather than every finger.
                bool bodyBone = entry.Key <= (int)HumanBodyBones.RightToes ||
                    entry.Key == (int)HumanBodyBones.UpperChest;
                if (bodyBone && entry.Value != null) { pose.Add(Pose(entry.Key, entry.Value)); }
            }

            var frame = new FrameRecord
            {
                kind = "frame",
                stage = "host_late_update",
                frame = _lastFrame,
                elapsedSeconds = _clock.Elapsed.TotalSeconds,
                unityUnscaledDeltaSeconds = Time.unscaledDeltaTime,
                unityDeltaSeconds = Time.deltaTime,
                timeScale = Time.timeScale,
                animatorSpeed = _animator.speed,
                animatorEnabled = _animator.enabled,
                animatorActive = _animator.gameObject.activeInHierarchy,
                parameters = Parameters(),
                rootPosition = _root.position,
                rootRotation = _root.rotation,
                layers = layers,
                bones = pose.ToArray(),
                // Includes metadata and transform reads, not JSON serialization or later output.
                readMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency
            };
            Add(frame);
            _frames++;
        }
        catch (Exception error)
        {
            End("read_failed_" + error.GetType().Name);
        }
    }

    /// <summary>Read-only IK inputs must be captured INSIDE the native OnAnimatorIK callback.</summary>
    internal void SampleNativeIK(Animator animator, int layerIndex, string stage)
    {
        if (_finished || animator != _animator) { return; }
        if (stage != "native_ik_prefix" && stage != "native_ik_postfix")
        {
            throw new ArgumentException("Identify the observed native callback phase explicitly.");
        }
        try
        {
            if (!Available()) { return; }
            var goals = new GoalRecord[4];
            for (int i = 0; i < goals.Length; i++)
            {
                var goal = (AvatarIKGoal)i;
                goals[i] = new GoalRecord
                {
                    goal = goal.ToString(),
                    position = animator.GetIKPosition(goal),
                    rotation = animator.GetIKRotation(goal),
                    positionWeight = animator.GetIKPositionWeight(goal),
                    rotationWeight = animator.GetIKRotationWeight(goal)
                };
            }
            var hints = new HintRecord[4];
            for (int i = 0; i < hints.Length; i++)
            {
                var hint = (AvatarIKHint)i;
                hints[i] = new HintRecord { hint = hint.ToString(),
                    position = animator.GetIKHintPosition(hint), weight = animator.GetIKHintPositionWeight(hint) };
            }
            Add(new IKRecord
            {
                kind = "ik",
                stage = stage,
                frame = Time.frameCount,
                elapsedSeconds = _clock.Elapsed.TotalSeconds,
                layer = layerIndex,
                // Unity body getters, like IK getters, require the Animator IK pass.
                humanoidBodyPositionRoot = animator.isHuman ? _root.InverseTransformPoint(animator.bodyPosition) : Vector3.zero,
                humanoidBodyRotationRoot = animator.isHuman ? Quaternion.Inverse(_root.rotation) * animator.bodyRotation : Quaternion.identity,
                goals = goals,
                hints = hints
            });
        }
        catch (Exception error) { End("ik_read_failed_" + error.GetType().Name); }
    }

    /// <summary>Flush buffered metadata only after measurement; the lead owns its local evidence sink.</summary>
    internal void Finish(Action<string> writeLine)
    {
        if (writeLine == null) { throw new ArgumentNullException(nameof(writeLine)); }
        if (!_finished) { End("lead_finished"); }
        foreach (string record in _records) { writeLine(record); }
        _records.Clear();
    }

    private int _controllerId;
    private int _avatarId;

    private void Inventory(string action, string gameVersion, string buildIdentity, string isolationEvidence, string[] ikOwners)
    {
        RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
        if (controller == null) { throw new InvalidOperationException("Selected Animator has no controller."); }
        _controllerId = controller.GetInstanceID();
        Avatar avatar = _animator.avatar;
        _avatarId = avatar != null ? avatar.GetInstanceID() : 0;
        Rigidbody body = _root.GetComponent<Rigidbody>();
        _parameters = _animator.parameters;
        if (_parameters.Length > 256) { throw new InvalidOperationException("Parameter inventory exceeds cap."); }
        if (_animator.layerCount > MaximumLayers) { throw new InvalidOperationException("Layer inventory exceeds cap."); }
        Add(new HeaderRecord
        {
            kind = "header",
            schema = 1,
            action = Limited(action),
            utc = DateTime.UtcNow.ToString("O"),
            gameVersionClaim = Limited(gameVersion),
            buildIdentityClaim = Limited(buildIdentity),
            isolationEvidenceClaim = Limited(isolationEvidence),
            unityVersion = Application.unityVersion,
            animatorPath = Path(_animator.transform),
            animatorId = _animator.GetInstanceID(),
            controllerId = _controllerId,
            controller = controller.name,
            controllerType = controller.GetType().FullName ?? "",
            avatar = avatar != null ? avatar.name : "",
            avatarValid = avatar != null && avatar.isValid,
            avatarHuman = avatar != null && avatar.isHuman,
            animatorHuman = _animator.isHuman,
            humanScale = _animator.isHuman ? _animator.humanScale : 0,
            applyRootMotion = _animator.applyRootMotion,
            hasRootMotion = _animator.hasRootMotion,
            updateMode = _animator.updateMode.ToString(),
            cullingMode = _animator.cullingMode.ToString(),
            rigidbodyInterpolation = body != null ? body.interpolation.ToString() : "unavailable",
            layerCount = _animator.layerCount,
            playerRootScale = _root.lossyScale,
            targetFrameRateSetting = Application.targetFrameRate,
            vSyncCountSetting = QualitySettings.vSyncCount,
            unityCaptureFramerateSetting = Time.captureFramerate,
            nativeIKPatchOwners = ikOwners,
            // Runtime state hashes are recorded, never presented as readable state names.
            limitations = "Current evaluated poses, not rest/T-pose. No raw curves or game assets exported. " +
                "Settings are not measured render/capture FPS. Host LateUpdate ordering must be documented. " +
                "Cadence is probe-instrumented; read cost excludes serialization/GC, not total diagnostic overhead. " +
                "Native isolation and source/hash strings are external claims requiring lead evidence."
        });

        AnimationClip[] clips = controller.animationClips;
        if (clips.Length > MaximumClipReferences) { throw new InvalidOperationException("Controller clip inventory exceeds diagnostic cap."); }
        var seen = new HashSet<int>();
        foreach (AnimationClip clip in clips)
        {
            if (clip == null || !seen.Add(clip.GetInstanceID())) { continue; }
            if (seen.Count > MaximumClips) { throw new InvalidOperationException("Distinct clip count exceeds cap."); }
            Add(new ClipRecord
            {
                kind = "clip",
                id = clip.GetInstanceID(),
                name = clip.name,
                lengthSeconds = clip.length,
                authoredSamplingRate = clip.frameRate,
                looping = clip.isLooping,
                legacy = clip.legacy,
                humanMotion = clip.humanMotion
            });
        }
        if (controller is AnimatorOverrideController overrides)
        {
            if (overrides.overridesCount > MaximumClipReferences) { throw new InvalidOperationException("Override inventory exceeds cap."); }
            var mappings = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(mappings);
            foreach (KeyValuePair<AnimationClip, AnimationClip> mapping in mappings)
            {
                Add(new OverrideRecord
                {
                    kind = "override",
                    originalId = mapping.Key != null ? mapping.Key.GetInstanceID() : 0,
                    replacementId = mapping.Value != null ? mapping.Value.GetInstanceID() : 0,
                    original = mapping.Key != null ? mapping.Key.name : "",
                    replacement = mapping.Value != null ? mapping.Value.name : ""
                });
            }
        }

        if (avatar != null && avatar.isValid && avatar.isHuman && _animator.isHuman)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = _animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) { _bones.Add(i, bone); }
                Add(new BoneRecord
                {
                    kind = "bone",
                    id = i,
                    humanoidName = ((HumanBodyBones)i).ToString(),
                    mapped = bone != null,
                    path = bone != null ? Path(bone) : "",
                    parentPath = bone != null && bone.parent != null ? Path(bone.parent) : "",
                    observedPose = bone != null ? Pose(i, bone) : null
                });
            }
        }
        CharacterAnimEvent animationEvents = _animator.GetComponent<CharacterAnimEvent>();
        if (animationEvents != null)
        {
            Add(new FootSettingsRecord { kind = "native_foot_settings", footIK = animationEvents.m_footIK,
                perFootValues = animationEvents.m_useFeetValues, offset = animationEvents.m_footOffset,
                maxDown = animationEvents.m_footDownMax, stepHeight = animationEvents.m_footStepHeight,
                stabilizeDistance = animationEvents.m_stabalizeDistance });
            CharacterAnimEvent.Foot[] nativeFeet = animationEvents.m_feets ?? Array.Empty<CharacterAnimEvent.Foot>();
            if (nativeFeet.Length > 8) { throw new InvalidOperationException("Native foot inventory exceeds cap."); }
            foreach (CharacterAnimEvent.Foot foot in nativeFeet)
            {
                if (foot == null) { continue; }
                Add(new FootSettingsRecord { kind = "native_foot", goal = foot.m_ikHandle.ToString(),
                    path = foot.m_transform != null ? Path(foot.m_transform) : "",
                    perFootValues = animationEvents.m_useFeetValues, offset = foot.m_footOffset,
                    maxDown = foot.m_footDownMax, stepHeight = foot.m_footStepHeight,
                    stabilizeDistance = foot.m_stabalizeDistance });
            }
        }
        // Imported reference transforms are clearly separated from evaluated bone poses. These are
        // local diagnostic numbers only, not rig/mesh assets or replacement motion to package.
        if (avatar != null && avatar.isValid && avatar.isHuman)
        {
            SkeletonBone[] reference = avatar.humanDescription.skeleton;
            if (reference != null)
            {
                if (reference.Length > 256) { throw new InvalidOperationException("Avatar reference inventory exceeds cap."); }
                var requiredNames = new HashSet<string>();
                foreach (Transform bone in _bones.Values) { if (bone != null) { requiredNames.Add(bone.name); } }
                foreach (SkeletonBone bone in reference)
                {
                    if (!requiredNames.Contains(bone.name)) { continue; }
                    Add(new ReferenceBoneRecord { kind = "avatar_import_reference", name = bone.name,
                        localPosition = bone.position, localRotation = bone.rotation, localScale = bone.scale });
                }
            }
        }
        MonoBehaviour[] scripts = _root.GetComponentsInChildren<MonoBehaviour>(true);
        if (scripts.Length > 128) { throw new InvalidOperationException("Component inventory exceeds cap."); }
        for (int i = 0; i < scripts.Length; i++)
        {
            MonoBehaviour script = scripts[i];
            if (script == null) { continue; }
            Add(new ComponentRecord
            {
                kind = "component",
                path = Path(script.transform),
                type = script.GetType().FullName ?? "",
                enabled = script.enabled,
                active = script.gameObject.activeInHierarchy
            });
        }
    }

    // Validity checks are called only inside sampling exception containment.
    private bool Available()
    {
        if (_clock.Elapsed.TotalSeconds >= MaximumSeconds || _frames >= MaximumFrames)
        { End("capture_limit"); return false; }
        if (!LocalOwner(_player) || _animator == null || _root == null || !_root.gameObject.activeInHierarchy ||
            (_animator.transform != _root && !_animator.transform.IsChildOf(_root)) || _animator.runtimeAnimatorController == null)
        { End("actor_or_controller_unavailable"); return false; }
        if (_animator.runtimeAnimatorController.GetInstanceID() != _controllerId ||
            (_animator.avatar != null ? _animator.avatar.GetInstanceID() : 0) != _avatarId)
        { End("controller_or_avatar_changed"); return false; }
        return true;
    }

    private static bool LocalOwner(Player player)
    {
        if (player == null || player != Player.m_localPlayer) { return false; }
        ZNetView view = player.GetComponent<ZNetView>();
        return view != null && view.IsValid() && view.IsOwner();
    }

    /// <summary>Lead forwards observed native triggers; this never sets or suppresses one.</summary>
    internal void ObserveNativeTrigger(Animator animator, string trigger)
    {
        if (_finished || animator != _animator) { return; }
        try
        {
            if (!Available()) { return; }
            Add(new TriggerRecord { kind = "native_trigger", frame = Time.frameCount,
                elapsedSeconds = _clock.Elapsed.TotalSeconds, trigger = Limited(trigger) });
        }
        catch (Exception error) { End("trigger_read_failed_" + error.GetType().Name); }
    }

    private ParameterRecord[] Parameters()
    {
        var result = new ParameterRecord[_parameters.Length];
        for (int i = 0; i < result.Length; i++)
        {
            AnimatorControllerParameter p = _parameters[i];
            var record = new ParameterRecord { name = p.name, type = p.type.ToString() };
            if (p.type == AnimatorControllerParameterType.Float) { record.number = _animator.GetFloat(p.nameHash); }
            else if (p.type == AnimatorControllerParameterType.Int) { record.number = _animator.GetInteger(p.nameHash); }
            else if (p.type == AnimatorControllerParameterType.Bool) { record.boolean = _animator.GetBool(p.nameHash); }
            else { record.available = false; } // Trigger reads are not inferred from a bool.
            result[i] = record;
        }
        return result;
    }

    private WeightedClip[] ActiveClips(int layer, bool next)
    {
        _clipScratch.Clear();
        if (next) { _animator.GetNextAnimatorClipInfo(layer, _clipScratch); }
        else { _animator.GetCurrentAnimatorClipInfo(layer, _clipScratch); }
        if (_clipScratch.Count > MaximumClips) { throw new InvalidOperationException("Blend clip count exceeds cap."); }
        var result = new WeightedClip[_clipScratch.Count];
        for (int i = 0; i < result.Length; i++)
        {
            AnimatorClipInfo info = _clipScratch[i];
            result[i] = new WeightedClip { id = info.clip != null ? info.clip.GetInstanceID() : 0,
                name = info.clip != null ? info.clip.name : "", weight = info.weight };
        }
        return result;
    }

    private static StateRecord State(AnimatorStateInfo value) => new StateRecord
    {
        fullPathHash = value.fullPathHash, shortNameHash = value.shortNameHash,
        normalizedTime = value.normalizedTime, lengthSeconds = value.length,
        speed = value.speed, speedMultiplier = value.speedMultiplier, looping = value.loop
    };

    private BonePose Pose(int id, Transform bone) => new BonePose
    {
        id = id, rootPosition = _root.InverseTransformPoint(bone.position),
        rootRotation = Quaternion.Inverse(_root.rotation) * bone.rotation,
        worldPosition = bone.position, localPosition = bone.localPosition,
        localRotation = bone.localRotation, lossyScale = bone.lossyScale
    };

    private string Path(Transform node)
    {
        string value = node.name;
        for (int i = 0; i < 32 && node != _root && node.parent != null; i++)
        {
            node = node.parent;
            value = node.name + "/" + value;
        }
        return value;
    }

    private static string Limited(string value) => value == null ? "" : value.Substring(0, Math.Min(256, value.Length));

    private void Add(object value)
    {
        if (_finished) { return; }
        string json = JsonUtility.ToJson(value);
        if (_characters + json.Length > MaximumCharacters)
        {
            End("buffer_limit");
            return;
        }
        _records.Add(json);
        _characters += json.Length;
    }

    private void End(string reason)
    {
        if (_finished) { return; }
        _clock.Stop();
        // The final record is small and deliberately bypasses Add's cap recursion.
        _records.Add(JsonUtility.ToJson(new EndRecord { kind = "end", reason = reason,
            sampledFrames = _frames, elapsedSeconds = _clock.Elapsed.TotalSeconds }));
        _finished = true;
    }

    [Serializable] private sealed class HeaderRecord
    {
        public string kind = "", action = "", utc = "", gameVersionClaim = "", buildIdentityClaim = "",
            isolationEvidenceClaim = "", unityVersion = "", animatorPath = "", controller = "", controllerType = "",
            avatar = "", updateMode = "", cullingMode = "", rigidbodyInterpolation = "", limitations = "";
        public int schema, animatorId, controllerId, layerCount, targetFrameRateSetting, vSyncCountSetting, unityCaptureFramerateSetting;
        public bool avatarValid, avatarHuman, animatorHuman, applyRootMotion, hasRootMotion;
        public float humanScale;
        public Vector3 playerRootScale;
        public string[] nativeIKPatchOwners = Array.Empty<string>();
    }
    [Serializable] private sealed class ClipRecord
    {
        public string kind = "", name = "";
        public int id;
        public float lengthSeconds, authoredSamplingRate;
        public bool looping, legacy, humanMotion;
    }
    [Serializable] private sealed class OverrideRecord { public string kind = "", original = "", replacement = ""; public int originalId, replacementId; }
    [Serializable] private sealed class ComponentRecord { public string kind = "", path = "", type = ""; public bool enabled, active; }
    [Serializable] private sealed class BoneRecord
    {
        public string kind = "", humanoidName = "", path = "", parentPath = "";
        public int id;
        public bool mapped;
        public BonePose? observedPose;
    }
    [Serializable] private sealed class BonePose
    {
        public int id;
        public Vector3 rootPosition, worldPosition, localPosition, lossyScale;
        public Quaternion rootRotation, localRotation;
    }
    [Serializable] private sealed class WeightedClip { public int id; public string name = ""; public float weight; }
    [Serializable] private sealed class StateRecord
    {
        public int fullPathHash, shortNameHash;
        public float normalizedTime, lengthSeconds, speed, speedMultiplier;
        public bool looping;
    }
    [Serializable] private sealed class LayerRecord
    {
        public int index;
        public string name = "", transitionDurationUnit = "";
        public float weight, transitionNormalizedTime, transitionDuration;
        public bool transitioning;
        public StateRecord current = new StateRecord();
        public StateRecord? next;
        public WeightedClip[] currentClips = Array.Empty<WeightedClip>(), nextClips = Array.Empty<WeightedClip>();
    }
    [Serializable] private sealed class FrameRecord
    {
        public string kind = "", stage = "";
        public int frame;
        public double elapsedSeconds, readMilliseconds;
        public float unityUnscaledDeltaSeconds, unityDeltaSeconds, timeScale, animatorSpeed;
        public bool animatorEnabled, animatorActive;
        public ParameterRecord[] parameters = Array.Empty<ParameterRecord>();
        public Vector3 rootPosition;
        public Quaternion rootRotation;
        public LayerRecord[] layers = Array.Empty<LayerRecord>();
        public BonePose[] bones = Array.Empty<BonePose>();
    }
    [Serializable] private sealed class GoalRecord
    {
        public string goal = "";
        public Vector3 position;
        public Quaternion rotation;
        public float positionWeight, rotationWeight;
    }
    [Serializable] private sealed class IKRecord
    {
        public string kind = "", stage = "";
        public int frame, layer;
        public double elapsedSeconds;
        public Vector3 humanoidBodyPositionRoot;
        public Quaternion humanoidBodyRotationRoot;
        public GoalRecord[] goals = Array.Empty<GoalRecord>();
        public HintRecord[] hints = Array.Empty<HintRecord>();
    }
    [Serializable] private sealed class FootSettingsRecord
    {
        public string kind = "", goal = "", path = "";
        public bool footIK, perFootValues;
        public float offset, maxDown, stepHeight, stabilizeDistance;
    }
    [Serializable] private sealed class ReferenceBoneRecord
    {
        public string kind = "", name = "";
        public Vector3 localPosition, localScale;
        public Quaternion localRotation;
    }
    [Serializable] private sealed class HintRecord { public string hint = ""; public Vector3 position; public float weight; }
    [Serializable] private sealed class TriggerRecord
    {
        public string kind = "", trigger = "";
        public int frame;
        public double elapsedSeconds;
    }
    [Serializable] private sealed class ParameterRecord
    {
        public string name = "", type = "";
        public double number;
        public bool boolean, available = true;
    }
    [Serializable] private sealed class EndRecord
    {
        public string kind = "", reason = "";
        public int sampledFrames;
        public double elapsedSeconds;
    }
}
