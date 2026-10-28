# Native animation baseline — required before original motion implementation

**Status: NOT RUN in this cloud handoff.** The owner requires the ladder motion
to have timing and fluidity consistent with vanilla Valheim. Complete this
analysis on the installed game before authoring or integrating replacement clips
or procedural poses. Read-only inspection tooling and planning are allowed now.
Claude remains the only game/build/profile operator; Codex performs the analysis
and records findings with an uninvolved reviewer.

## 1. Identify genuine native animation

Record game/Unity versions, player rig/avatar/controller, active animation mods,
source SHA and the capture setup. Inspect the native controller before ladder
presentation overrides, or use an observed clean baseline with those overrides
disabled by the lead. Verify normal animation has restored. Do not label an
existing mod's replacement animation as vanilla.

Locate the actual loaded player Animator/controller through the current licensed
assemblies and observed object hierarchy. Unity exposes the controller's clips,
clip sampling-rate metadata and duration. This establishes a viable inspection
route, not proof that this game instance or every compressed curve is accessible.
Do not guess internal field names or require a wholesale game-asset extraction.

## 2. Inspect metadata and actual motion together

Study native idle/standing, walking, running, jump start/air/landing, crouch,
swimming and hammer/build/repair motions that actually exist in this build.
Record actual state/clip names; do not invent a native climbing animation.

For each available representative motion record:

| Field | Required observation |
|---|---|
| Actual clip/state and controller | Identify the native source and any blend/override |
| Clip length and looping | Native duration; loop versus transition |
| Clip frameRate metadata | Authored/imported sampling rate, not screen FPS |
| Effective runtime playback | Animator/state speed and observed cycle time where accessible |
| Render and capture FPS | Separate measured/displayed values and any dropped frames |
| Blend behavior | Start/stop/transition duration and pose continuity |
| Limb cadence | Steps or contacts per observed cycle; left/right relationship |
| Body motion | Pelvis/torso bob, lateral movement, shoulder reach and bend behavior |
| Contact behavior | Foot plants/sliding, hand action ownership and interruption |
| Update/interpolation | Observed Animator update mode and character interpolation where accessible |
| Curve access | Available locally or unavailable; never invent key data |

AnimationClip.frameRate is the source sampling rate. It does not establish the
render rate, actual unique key count, runtime state speed or perceived smoothness.
Do not assume length multiplied by frameRate is an exact curve-key count.

Inspect curves/key spacing only if supported by available local editor tooling
and legitimate access. Unity's AnimationUtility curve APIs are editor APIs, not
a runtime extraction guarantee. Keep original game clips, rigs and binaries out
of commits, uploads and mod packages. Store only derived measurements, notes and
permitted gameplay evidence under the existing evidence policy.

## 3. Controlled visual comparison

Claude records representative native movements at ordinary game speed in a
disposable course, with a stable character/camera view and the actual frame rate
noted. Use several cycles where needed to distinguish a loop from a dropped frame
or transition. Codex reviews normal playback and frame stepping, looking at hands,
feet, knees, elbows, pelvis and torso. Video timing and clip metadata must not be
conflated. Match the game's animation style; a denser key bake alone is not proof.

Record a practical authoring recommendation: suitable clip sampling/export rate,
blend/easing strategy, body-motion scale and acceptable contact cadence, with the
native observations supporting each choice. Do not hardcode 30 or 60 FPS without
that inspection. Preserve natural interpolation; do not update limb poses only
once per numbered reference row.

## 4. Resolve climbing cadence before finalizing the pose plan

Read LIMB_FRAME_REFERENCE_2026_10_26.md and its CSVs as provisional authored
contact plans. Their 60 phase intervals are not a one-second or 60-FPS clip.
Compare the candidate contact order and movement speed on the actual rig before
selecting the final gait. Use the observed rung spacing and current motor speed.

The audited cloud geometry has rung pitch 1/3 m, while the active design defaults
are 1.8 m/s normal and 2.43 m/s fast. A two-rung cycle would take about 0.370 s or
0.274 s respectively. Re-observe these values locally. A rigid four-beat gait
would make each limb swing very quickly; do not prescribe it at all speeds or
hide flailing with more samples. Evaluate the paired diagonal gait and contact
blending, then document the chosen plan. Animation cannot silently change motor
speed, costs, skill or collision to make the clip look better. Report an actual
remaining cadence conflict to the lead with measured evidence and an explicit
proposal instead of guessing a gameplay change.

## 5. Completion gate

Before authoring replacement motion, produce a baseline report containing the
observed metadata, native playback observations, source/capture identity, chosen
cadence/sampling/blend approach, and identified unknowns. An uninvolved reviewer
checks that the selected approach follows the evidence. The lead can then
integrate the original motion work without another owner scope approval.

Unavailable raw curves alone do not block the gate if actual metadata and native
playback are sufficient and the limitation is recorded. If native rig/clip access
or real playback evidence is unavailable, mark the relevant baseline work BLOCKED
and explain the specific local prerequisite. Do not fabricate native FPS or skip
this gate by using the Meshy previews. Continue unrelated authorized runtime work.

## Primary API references

- [RuntimeAnimatorController.animationClips](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RuntimeAnimatorController-animationClips.html)
- [AnimationClip.frameRate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationClip-frameRate.html)
- [AnimationClip.length](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationClip-length.html)
- [Animator.updateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-updateMode.html)
- [AnimationUtility.GetCurveBindings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationUtility.GetCurveBindings.html)

The local API signatures and actual assets remain to be verified. This document
contains a plan and known API capabilities, not observed Valheim measurements.
