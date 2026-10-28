# Codex continuation prompt — author the original ladder animations

Owner decision: 2026-10-26. Paste this complete prompt into the existing local
Codex session. This replaces its former default read-only animation-review role.

---

Continue Concerned Ladders in `Weakened/ConcernedCatMods`. You own original
animation implementation while Claude continues the ladder runtime and remains
integration lead. The owner chose the supplied GIFs as visual references and
does not want to buy Meshy or use its stock animation exports. Author our own
motion against the actual installed Valheim character. Do not stop at a plan,
a generic mannequin demonstration or a walking-speed parameter change.

The previous arm/leg presentation was rejected. Its candidate identity is not
established by the uploaded image; inspect current local code before diagnosing
it. The references are sufficient to begin. Additional owner references and
paid assets are not prerequisites.

## 0. Mandatory native analysis and written frame reference

The owner requires native animation analysis BEFORE replacement animation
implementation. Complete VANILLA_ANIMATION_BASELINE_2026_10_26.md first. Inspect
the actual loaded native rig/controller and available clip metadata, then have
Claude capture native walk/run/jump/crouch/swim/hammer behavior. Distinguish clip
sampling rate, effective state speed, rendered FPS and capture FPS. Inspect
curves only if available safely in local editor tooling; metadata plus real
playback can suffice when raw curves are unavailable. Never invent native FPS
or copy game clips into the mod. Read-only diagnostic tooling may be written now.
If native access or actual playback evidence is missing, report
BLOCKED_NATIVE_ANIMATION_BASELINE rather than silently skipping this gate.

Produce NATIVE_ANIMATION_BASELINE.md and native_animation_baseline.csv with
observed metadata, measured playback, inferences and unknowns clearly separated.
Record the supported choice of authoring sampling, easing, cadence and blending.
An uninvolved reviewer checks the evidence; then proceed without another owner
scope confirmation. Match actual vanilla fluidity, not just a nominal FPS label.

Read LIMB_FRAME_REFERENCE_2026_10_26.md and both animation-reference CSVs. They
provide every 0–60 ascent/descent sample for paired and three-contact plans,
hand/foot contact targets and body progression. They are provisional original
plans, not measurements of Meshy's or Valheim's skeleton. Frame numbers are
normalized distance phases, not 60-FPS playback. Resolve actual rig dimensions,
contacts, easing and the chosen gait after the native baseline. The one-limb-at-
a-time variant is not mandatory at full speed; compare cadence before committing
to it. Never apply the body-travel reference again as root movement.

Maintain a frame-to-capture comparison recording LH/RH/LF/RF contacts, knee/elbow
bends and pelvis/torso behavior. Interpolate between reference samples, preserve
unwrapped rung indices at loop seams and retain planted contacts on stop/reverse.
Mount/catch/top-finish use validated transition progress. The first good loop is
an internal checkpoint; finish the complete motion set below.

## 1. Reconcile ongoing work and establish ownership

Read AGENTS.md, CLAUDE.md, naming conventions, this product's PROJECT,
ARCHITECTURE, OWNER_REVISION_2026_10_25, ANIMATION_REVIEW_2026_10_26,
DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25, TEST_PLAN and active STATUS. Reconcile
the actual local branch, full SHA, worktrees and dirty work. The cloud audit is
historical, not a reset target. Preserve Claude's work and all earlier mechanics.

Use a separate animation branch/worktree from the reconciled integration point.
Establish a narrow file allowlist from the current tree. You own original motion
sources, rig binding, scoped presentation, contacts and animation validation.
A product-local Runtime/Animation area and original authoring sources are
reasonable if they fit the current structure.

Claude retains LadderRuntime/motor, admission, inputs, collisions, landing
choices, stamina, skill, build actions, lifecycle authority, shared project
resource declarations, versioning, package scripts, profile imports and game
operation. Supply proposed hook/resource changes for Claude to integrate; do
not concurrently edit those files or create a second gameplay controller.
Ordinary agent coordination needs no new owner scope approval.

Use subagents for bounded authoring/review in separate worktrees with explicit
file ownership. An uninvolved reviewer must assess your animation; you cannot
provide its sole independent acceptance. Preserve general review duties in
CODEX_REVIEW_CHECKLIST_2026_10_26.md. You may independently review Claude's motor
where you did not author it.

## 2. Reference and target inspection

Visual reference: https://www.meshy.ai/animation-library/body-movements/climbing

Use Ladder Mount Start, Ladder Climb Loop and Ladder Climb Finish to understand
the grounded approach/hop, coordinated climbing and upper-floor transition.
Fast Ladder Climb is an additional cadence reference. Create original keyframes
or original procedural motion; do not download or extract Meshy skeletal assets.

The supplied files are valheim_0YqMtj2yNo.gif (rejected), chrome_CCFS3rFAiU.gif
(mount), chrome_PwrevvFic4.gif (loop), chrome_IhKvXoS72v.gif (fast reference), and
chrome_1IXDLInCG2.gif (finish/loop/mount). In the cloud copies the first three
are single-frame PNGs despite their filenames; the final two contain animation.
Use task attachments or their observed local location, not an assumed cloud
path on the PC. A still proves no motion. The previews contain no ladder, so
fit contacts to our actual geometry.

Inspect and record the installed player rig/avatar type, required bones, rest
pose, local axes, scale, limb proportions, Animator/controller, equipment and
other pose writers. Inspect rung spacing, usable span, both physical faces and
physical anchor. Missing bones or incompatible binding must fail cleanly.
Do not guess a skeleton from names or package extracted game/player assets.

Choose a compatible original authoring path from installed tools and observed
rig facts. Prefer editable authored clips with limited contact correction where
useful. Scoped original procedural motion is acceptable if it meets the same
visual standard; arbitrary limb oscillations or blind bone rotations do not.
Retain editable keyframes/curves or authoring projects and reproducible export
instructions. Do not invent a runtime non-legacy SetCurve pipeline: Unity
supports that method at runtime only for legacy clips.

## 3. Small read-only presentation interface

Agree concrete names/types with Claude from current code. This is a semantic
contract, not an asserted native API or permission to implement another motor:

| Accepted runtime fact | Presentation use |
|---|---|
| Attachment/session and discontinuity/reset identity | Bind once and clear stale presentation |
| Ground mount, accepted air catch, hold, up/down, fast, slide, transfer | Select original motion |
| Actual completed signed travel with sample identity or cumulative measure | Advance once, not on every repeated render frame |
| Physical face, stable ladder basis, rung geometry and anchor | Fit contacts without moving the player |
| Validated mount/catch/top-exit state and progress | Blend along accepted gameplay |
| Equipment, native hammer/action ownership and focused-menu hold | Preserve native arm/action animation and stable hold |
| Release, death, destruction, teleport, disable, owner loss or takeover | Restore only presentation state still owned by this mod |

Only traversal-loop phase follows actual signed distance. Mount, catch and top
finish also need accepted transition progress; do not reduce all modes to one
vertical-speed float. A timer cannot infer a successful landing. Do not retain
an obsolete 1.8 speed clamp when the motor supports faster travel.

Presentation does not consume inputs, mount/release the player, move the capsule,
alter gravity, select collision paths, reset falls, spend stamina, award skill
or write remote movement. Validate finite data and session identity before
presentation writes. Claude supplies the accepted gameplay outcomes.

## 4. Complete original motion set

| State | Required presentation |
|---|---|
| Ground mount | Reach into a believable grip; a small hop only follows the admitted motor transition |
| Upward loop | Coordinated hand/foot contacts and natural torso motion on actual rungs |
| Hold | Stable planted contacts at arbitrary stops; no walking in place, drift or rung change to finish a clip |
| Descent | Deliberate downward movement and smooth reversal; adapted upward motion must pass actual-character inspection |
| Fast travel | Cadence follows real faster travel; blocked players never cycle through imaginary movement |
| Learned slide | Intentional slide pose and clean transitions to/from hold or climbing |
| Midair catch | Blend from accepted airborne pose to a reachable grip without replaying a ground launch or adding a hoist |
| Top finish | Step onto the validated supported floor; blocked or missing landings retain responsive hold |
| Jump / Crouch Drop | Immediate handoff to powered jump versus unpowered drop when gameplay has focus; no waiting for clip completion |
| Hammer/scaffold work | Hold the body while required native hammer arm/upper-body actions remain visible and functional |

First fit one full climb loop on the real player and ladder, then finish all
transitions/modes. This is an internal implementation sequence, not a separate
unfinished proof of concept for the owner.

Normalize authored travel for the physics-owned character. Inspect root and
skeletal displacement so the visible body cannot leave its capsule, double its
movement or teleport through a lip. Use coherent pose/blending ownership per
body part. Scope changes per character; never mutate shared donor controllers.

Require consistent knee/elbow bends, no joint flips, stretched limbs, repeated
rung penetration or obvious sliding through planted contacts. Use bounded
correction only for reachable contacts. Derive tolerances from actual geometry;
no metre values come from the GIFs. Both faces work without negative-scale
limb inversion. Keep phase through mixed-material seams and newly built
extensions. Stop on real blockage and reverse coherently.

The motor's normal/fast/slide defaults remain 1.8/2.43/4.5 m/s. Animation sets
none of the speeds, costs or unlocks. Preserve all accounting and input rules.
During hammer actions release the required hand/upper-body grip influence while
retaining a credible supported hold. Do not freeze the hammer, cancel actions,
change inventory/equipment ownership or require stowing it to resume climbing.
Focused menus remain a hold and do not replay queued input.

## 5. Cleanup, observers and packaging

Restore only owned animation resources on every release, destruction, death,
teleport, disable, logout, ownership change or external takeover. Do not restore
stale state over a newer controller. Ordinary walking, combat, jump, swimming,
crouch and equipment must remain correct afterward.

Prove actual observer presentation. Prefer sufficient verified native replicated
state. Any new presentation-only protocol needs independent design review,
bounded values and stale/late-join cleanup under the existing constraints.
No remote motor, stamina or XP authority. Dedicated operation must not require
graphics loading. Local playback alone is not multiplayer evidence.

Prefer original assets embedded in the mod DLL. The current candidate checker
rejects undeclared loose files. If separate assets are necessary, Claude must
extend package declarations, installed hashes and identity verification
explicitly. Do not weaken the checker globally or abandon animation because of
its current format. Preserve all package and saved prefab identities.

## 6. Finish through actual-character verification

Use meaningful tests where appropriate for phase continuity, duplicate samples,
transitions and cleanup. Such tests do not prove contacts or visual quality.
Do not invent engine stubs or tests that merely repeat implementation constants.

Claude alone performs licensed engine builds under the existing lock, manager
imports and game operation. Supply concrete code/assets and a capture list for
lead integration; do not take over the running profile/game. Basic regression
work belongs to the agents, not the owner.

Complete R25/P02–P04 on the real Valheim character, front/side views, both faces:
mount, several loops, arbitrary stop/hold, reversal, fast climb, seams, supported
top step and blocked-top hold. Also show accepted/rejected air grabs, distinct
Jump versus Drop, slide, hammer placement/repair/removal and normal animation
and equipment after release. Inspect footage for contact sliding, joint flips,
pose snapping and body/capsule separation. Retain lifecycle, scaffolding and
real host/client observer gates; unavailable endpoints are BLOCKED.

Have an uninvolved reviewer assess your source and actual-character evidence.
The rejected limb implementation cannot pass under a new name. Meshy purchase
or export is no longer a dependency or blocker. Any genuine technical blocker
needs inspected evidence, the attempted solution and a concrete next step.
The prior conditional tested walking fallback remains a transparent contingency,
never proof that the selected original animation was delivered.

Return committed original sources and export instructions, source SHA, changed
file allowlist, exact integration hooks/resource needs, verification results and
remaining limitations. Claude integrates, builds a new uniquely versioned ZIP,
imports that exact package into TCC-Ladders-Dev and ties source, installed
DLL/assets, startup and captures to it. Record later documentation commits
separately from the tested build SHA. No public release/tag/main merge is
authorized. Continue until accessible work is complete or a concrete external
blocker remains; further reference collection is not a default reason to stop.

## Primary technical constraints

- [Unity Avatar retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html): configured humanoid Avatars are required for that route; inspect the actual target.
- [Unity AnimationClip.SetCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationClip.SetCurve.html): runtime support is legacy-only; non-legacy authoring requires the appropriate editor pipeline.
- [Unity AnimatorOverrideController](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorOverrideController.html): clip replacement exists, but target compatibility and scoped ownership require inspection.
