# Vanilla animation baseline — 2026-10-26

**Status: BLOCKED_NATIVE_ANIMATION_BASELINE — capture in progress, replacement authoring has not started.**

This is the local execution record for the mandatory first step in the owner reference pack. It is not the cloud template and does not claim completion from API inspection. The owner assigned original animation to Codex; Claude retains runtime, licensed builds, profile imports and game operation.

## Identity and ownership

- Isolated branch: `feat/cl-431-original-animation`.
- Worktree: `C:\Users\erenc\.codex\worktrees\ladders-original-animation\ConcernedCatMods`.
- Runtime source inspected: `441ea830785a27920f4bad11c01f877ed94b7d28`, version 0.2.1.
- Claude's documentation reconciliation `ec72119` was fast-forwarded into this lane without runtime changes.
- Reference ZIP: `ConcernedLadders_Original_Animation_REFERENCE_AND_PROMPTS.zip`, SHA256 `8c7fae5a903ef0096c9a475c4ad4db5f56e330ee4ec476a319be038103d537b1`.
- Scope and ownership acknowledged in shared `ANIMATION_LEAD_RESPONSE_2026_10_26.md`; interface clarifications sent in `CODEX_ANIMATION_INTERFACE_REPLY_2026_10_26.md`.
- No replacement motion, rig-binding writes, controller overrides or motor implementation has been added by this lane.

## Observed facts

| Fact | Source and limit |
|---|---|
| Current source has rejected custom IK | Plugin installs `LadderClimbAnimation` unconditionally; it patches `CharacterAnimEvent.OnAnimatorIK`. This identifies current code, not the unknown source behind the owner's earlier uploaded still. |
| Climb toggle does not remove that hook | Observer inference is independent of the local climb toggle. Lead must positively remove/disable the pose hook for native capture. |
| Native hooks exist | Read-only installed-assembly inspection confirms OnAnimatorIK calls UpdateLookat then UpdateFootIK; CustomLateUpdate adjusts head/shoulders afterward. API availability is not live rig/controller metadata. |
| Clip/state inspection route exists | Installed Unity metadata exposes controller clips, frameRate/length/loop, current/next state and weighted clips, bone mapping, IK inputs and update modes. |
| Native sync is parameter-based | Installed ZSyncAnimation does not transmit a custom controller, graph or bone transform automatically. Observers require a verified signal or reviewed protocol. |
| Engine log | Existing Player.log reports Unity 6000.0.75f1; native capture must re-record Application.unityVersion. CoreModule Windows version metadata is 0.0.0.0 and cannot substitute. |
| Ladder geometry in source | 2m modules, pitch 1/3m, six rung centres +1/6 through +11/6m; visible rung faces Z=.10/.22, collider faces .08/.24. These are source-derived, pending live geometry/rig fitting. |
| Reference contents | ZIP contains docs/CSVs, no game clips/rigs/GIFs. Five named GIFs were not found in bounded Downloads/attachments searches. Existing written/CSV references suffice; no owner reference request is needed. |

## Lead capture underway

Claude acknowledged the request and is producing a dev-harness native dump, frame traces and synchronized gameplay videos in:
`C:\code\cc-integration\cl-ladders-review-evidence\native-baseline`.

Planned native actions: idle, walk, run, crouch idle/walk, standing/running Jump with landing, swim, hammer idle/place/repair/remove. The rejected ladder view is a separate reference capture, never native baseline. A transient test capture or a file still being written is not delivered evidence. Capture paths, hashes, source/DLL identity and actual isolation log will be recorded after delivery.

The harness must identify its actual sample phase/order, avoid calling an evaluated idle pose a rest pose, separate state speed/multiplier/Animator speed, and report recorder drops/duplicates independently of CPU render-update cadence. Claude has received those corrections. No game operation is performed by this lane.

## Inferences and provisional analysis

With source pitch 1/3m, two-rung travel at normal 1.8m/s lasts .3704s; fast 2.43m/s lasts .2743s. Paired swings last .1852/.1372s; sequential single-limb swings .0926/.0686s. Under the provisional quintic easing, paired peak vertical world contact-target rates are 6.75/9.1125m/s; sequential peaks 13.5/18.225m/s.

These are consequences of the reference formulas and current source defaults. They are not native limb-speed measurements and do not select a final gait. A deliberate four-beat pattern has a credible flailing risk at full speed; actual rig reach/playback must resolve it. The CSV body travel is already motor travel and will never be added to the root.

## Unavailable / pending evidence

- Actual loaded native Animator/controller/Avatar identity, required bone mappings and valid axes/scale/limb lengths.
- Native clip sampling/duration/loop inventory tied to the actual selected player.
- Isolated native playback and contact/body/blend observations for every requested action.
- Measured effective playback and render-update cadence, separate capture metadata and drops/duplicates.
- Rest/reference-pose data and raw-curve availability, labelled separately from evaluated pose.
- Final authoring sampling, gait, easing, contact fit and blend durations justified by observations.
- Uninvolved baseline review.

Raw curves are optional. Missing actual native metadata/playback is the current blocking prerequisite; it does not establish animation impossibility or justify CUSTOM_ANIMATION_DEFERRED.

## Completion rule

Fill `NATIVE_ANIMATION_BASELINE.md` and `native_animation_baseline.csv` with observed facts, measured playback and explicit unknowns; include visual analysis of all four limbs and pelvis/torso. Compare paired and deliberate motion rates against the native evidence. Obtain uninvolved baseline review before any replacement pose/clip implementation.

Only then change this gate to COMPLETE and begin original motion. First fitted loop remains an internal checkpoint; the full mount/up/down/hold/reverse/fast/slide/catch/top-exit/Jump/Drop/hammer, lifecycle, scaffold and observer set remains required. Original animation is not delivered by this preparatory commit.
