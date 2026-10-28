# Climbing animation rejection and replacement brief

**Owner review: 2026-10-26. Specification and research only.** This document
supplements the active owner revision and Drop/scaffolding addendum. All existing
mechanics, input, accounting, packaging and evidence requirements remain active.
It expands R25 and P02–P04 without adding or renumbering test rows.

## Review outcome and available evidence

The owner rejected the submitted Valheim animation because its arm and leg
movement is broken. Treat that presentation as rejected until a replacement
passes the criteria below. The submitted candidate's source, version and binary
identity are unknown; do not attribute this rejection to an older audited commit
or claim the current local implementation was inspected here.

The uploaded files were inspected without modifying their originals:

| Attachment | Actual received content | Evidence limit |
|---|---|---|
| `valheim_0YqMtj2yNo.gif` | Single-frame PNG, 1600 × 900 | Rejected pose/context plus owner's report; no motion diagnosis |
| `chrome_CCFS3rFAiU.gif` | Single-frame PNG, 697 × 1169 | Meshy Ladder Mount Start preview pose |
| `chrome_PwrevvFic4.gif` | Single-frame PNG, 855 × 1222 | Meshy Ladder Climb Loop preview pose |
| `chrome_IhKvXoS72v.gif` | GIF, 70 frames, 4,670 ms | Animated fast-climb reference |
| `chrome_1IXDLInCG2.gif` | GIF, 76 frames, 5,070 ms | Animated finish/loop/mount references |

The first three filenames do not establish animated recordings. Do not invent
frame-by-frame causes such as reversed joints, conflicting IK or bad bone axes
from the rejected still. The references communicate grounded mounting,
coordinated climbing, and stepping onto the upper floor; their mannequin
previews contain no ladder, so they do not prove rung contact in this mod.

## Current owner decision — original motion, Codex implementation

The owner subsequently chose original animations using the GIFs as visual
references, without paying Meshy or using its skeletal animation exports.
This supersedes the prior licensed-stock-clip option. The references are enough
to start; the next missing facts are the actual player rig, ladder geometry and
current runtime interface, not more owner reference material.

Use the [Climbing library](https://www.meshy.ai/animation-library/body-movements/climbing)
only to understand the grounded mount, coordinated climb and top step-off.
Create original keyframes/curves or original procedural motion fitted to the
observed Valheim rig. Do not require a Meshy account, purchase or export.

Original authoring removes the external asset dependency; it does not establish
that a guessed bone mapping will work. Inspect local axes, rest pose, limb
proportions and current pose writers before authoring. Keep editable original
source and reproducible build/export instructions. No extracted player/game
assets or dependency binaries are committed or distributed.

Codex owns presentation in an isolated worktree. Claude retains motor/input/
collision/stamina/skill/building authority, shared integration files, licensed
builds, packaging, manager imports and game operation. Agree a small read-only
interface from accepted gameplay results. Traversal phase follows actual travel
once per distinct sample; mount/catch/finish also need validated transition
progress. No second input or movement controller.

See CODEX_PROMPT.md for the complete implementation assignment. Preserve the
full independent review matrix in CODEX_REVIEW_CHECKLIST_2026_10_26.md; an
uninvolved reviewer must assess Codex's animation. These are role/specification
changes, not evidence that local sessions were contacted or motion implemented.

## Later owner requirements: frame reference and native analysis first

Complete VANILLA_ANIMATION_BASELINE_2026_10_26.md before replacement motion
implementation. Inspect genuine native rig/clip metadata and actual playback,
separating asset sampling from effective playback, screen and capture FPS.
Choose final fluidity, easing and cadence from that evidence. Optional curve
inspection is useful when available; missing native access/playback is an
explicit blocker rather than permission to invent a vanilla baseline.

LIMB_FRAME_REFERENCE_2026_10_26.md and its CSVs supply every numbered sample of
provisional ascent/descent contact plans, plus body and transition guidance.
These frame indices are normalized phases, not a required 60-FPS clip. Keep
original motion and fit the plan to the actual rig after the native analysis.
The reviewer compares limb contacts and torso movement to actual captures.

## Implementation contract

1. **Inspect before mapping.** Record the installed player rig/avatar, bone
   hierarchy, rest pose, axes, scale, controller and relevant animation mods.
   A Unity Humanoid export does not prove target compatibility. Unity's
   [retargeting documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html)
   requires configured humanoid Avatars for that route. Select a compatible
   pipeline from observed facts; do not invent Valheim rig details.

2. **Keep movement with the motor.** Normalize authored travel into appropriate
   in-place presentation while retaining natural local body movement. The
   collision-checked ladder motor owns the capsule, gravity and trajectory.
   Inspect both root and skeletal displacement: disabling root motion alone
   does not establish a stationary visible body. Unity's
   [root-motion documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html)
   distinguishes movement in the root from movement retained in the pose.

3. **Make contacts believable.** Fit hands and feet to actual rung spacing and
   player proportions. Keep elbows/knees bending consistently without joint
   flips, stretched limbs, repeated penetration or sliding through planted
   contacts. Use a coherent pose/blending owner with bounded contact correction
   where needed. Both faces rotate the whole presentation correctly; negative
   scale or mirrored bone assumptions must not invert limbs.

4. **Follow actual travel.** Drive loop phase from signed movement that occurred,
   including variable speed and seam crossings. Blocking or releasing movement
   stops traversal and blends to a stable hold without cycling in place or
   moving to another rung merely to finish an animation. Reverse smoothly.
   Evaluate a descending clip or adapted reversal on the actual character;
   blindly reversing the upward recording is not acceptance evidence.

5. **Keep transitions distinct.** Use the start clip for admitted grounded
   mounts. An accepted midair grab blends from the airborne pose into a nearby
   grip without replaying a ground launch, hoisting or resetting a failed fall.
   Align finish presentation with a validated top-exit path and support. A clip
   cannot create a landing, bypass a hatch/railing or teleport through a lip.
   Invalid exits retain responsive hold, reverse, safe Use, Jump and Drop.

6. **Cover every attached mode.** Provide intentional hold, ascent, descent,
   faster movement, learned slide and transfer presentation. Preserve existing
   Run/stamina and Climbing-skill rules. Gameplay-focused Jump and fresh Crouch
   interrupt immediately into powered departure and unpowered release,
   respectively; neither waits for a clip to end. Existing release accounting
   and regrab guards remain unchanged.

7. **Support building.** Hold the physical anchor while native hammer actions
   use their required arm/upper-body presentation. Grip IK/full-body climbing
   must not freeze the hammer, cancel native operations or move the capsule.
   Menus retain input ownership; equipment remains correct; deliberate travel
   resumes without requiring the hammer to be put away.

8. **Restore and observe.** Scope animation changes per character and release
   only owned state on Jump, Drop, destruction, death, teleport, disable,
   logout or another controller's takeover. Verify observer presentation and
   stale/late-join cleanup under the existing networking constraints. Remote
   presentation must not acquire motor, stamina or XP authority. Package and
   verify any animation assets under the existing exact-candidate rules.

## R25 / P02–P04 acceptance evidence

Record the actual Valheim character from front and side on both clear ladder
faces, with the source/version/installed candidate identified. Show grounded
mount, several complete loops, stop/hold, reversal, fast climb, segment seams,
and a supported top step-off. Include a genuinely blocked top exit to prove
that the animation cannot override collision or support checks.

Also show accepted/rejected midair grabs, distinct Jump versus Drop, learned
slide, hammer placement/repair/removal while anchored, and normal equipment and
animation afterward. Inspect the transitions in slow playback for joint flips,
contact sliding, visible body/capsule separation and pose snapping. Preserve
the existing lifecycle and real host/client observer evidence requirements;
an editor/mannequin preview is preliminary evidence only.

The conditional `CUSTOM_ANIMATION_DEFERRED` route still requires a concrete
feasibility attempt and tested walking fallback. It cannot reclassify the
owner-rejected limb implementation as accepted custom animation or a passing
fallback. Report unavailable assets/endpoints explicitly; no fabricated pass.

## Local agent assignment and completion

**Codex:** reconcile this brief with current local work and implement original
motion, rig binding, contacts and scoped presentation in an isolated worktree.
Supply original authoring sources, integration hooks, asset declarations and
animation-specific checks. The first correct loop is an internal checkpoint;
continue through all transitions and modes.

**Claude:** continue runtime work, supply accepted-state hooks, integrate Codex's
animation, run the licensed build/game and produce the exact tested replacement
ZIP. Preserve all prior functional and candidate-identity requirements.

**Uninvolved reviewer:** assess animation source, actual-character evidence,
cleanup, observer behavior and package identity. Codex cannot supply its own sole
independent animation acceptance; it may review runtime code it did not author.

Completion requires a replacement visibly satisfying R25/P02–P04 plus retained
functional gates, or an honestly recorded conditional deferral. This document
itself establishes no runtime acceptance and authorizes no public release.
