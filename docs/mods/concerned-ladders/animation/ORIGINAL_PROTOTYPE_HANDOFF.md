# Original animation internal prototype handoff — 2026-10-26

**SOURCE ONLY — NO_GO as a finished game build.** Baseline accepted at `d3f423f` before these sources were authored. This checkpoint supplies reviewable original sources and exact integration instructions to Claude. It is not a correctly fitted loop, a final animation or a tested ZIP.

## Integration ownership and hooks

Claude owns Plugin/LadderRuntime/shared declarations/builds/imports/game. Codex owns the new Animation subtrees, AnimationSources, tests/docs/tools. Base includes Claude's committed interface/runtime through `825f16c`, merged locally as `8cb2e9b` without touching his worktree.

- Retire the rejected `LadderClimbAnimation`/`LimbCycle` installation before `OriginalClimbPresenter.Install(PluginGuid + ".presentation")`. Install refuses the rejected `.pose` owner. Never run both pose implementations.
- Construct `Runtime.Animation.OriginalClimbPresenter(log)` and pass it through the existing `SetPresenter` seam after successful Install. No second movement controller is supplied.
- Existing producer calls Publish after accepted steps; End is synchronous before release impulses; Update supplies lifecycle checks. Keep those calls and all end reasons, including logout/world switch/settings/ownership/death/teleport/removal.
- Remove shared runtime references to the rejected pose, including `_animation.SetLocal`, `_animation.Ready`, Update and construction. Suppress the walking contingency while the original presenter is active; use the existing native walking contingency only if Install/binding/presentation fails. A callback fault is contained and reported to the producer on the next Publish.
- End stops new pose writes immediately. Restore saved IK/body inputs only in a legal IK pass. Dispose transfers pending numeric leases to a rig-local cleanup continuation, releases its global instance/own hook immediately and allows a new Install even if the old rig is disabled. No Rebind/controller reset/equipment mutation is used.

## First actual-rig checkpoint, under the lead's lock

When the owner game session is free, controlled build/import of the exact reviewed source; record source/version, build record, DLL/package/installed hashes, single active own installation and fresh startup identity. No untracked DLL copy as provenance proof.

1. Clear-floor front/side footage, native rig with ordinary equipment: mount, climb upward for several interior cycles, release movement into a stable hold, then Jump or Drop and demonstrate normal walking/running/jumping afterward. Record accepted travel vs displayed pose, actual IK layer callback, palms/soles, knees/elbows and pelvis/torso.
2. Include the initial binding measurements and any per-limb reach warning. Capture actual solved bones after the solver with a labelled hook; goal coordinates are not solved contacts. Current source handles only the local owner; do not infer observer success.
3. Then rear face, descent/reversal, fast, slide, catch, grounded mount, top/side exit, action onset after last Publish, hammer place/repair/remove, equipment and teardown. Build/import changes stay with Claude; no race with owner operation.

## Concrete fitting and integration risks

- Palm normal and sole/palm mesh offsets are provisional anatomical estimates. Finger closure is not yet originally authored. No current source-only claim passes contact fit or visual acceptance.
- Actual paired-vs-deliberate normal/fast comparison remains outstanding; the existing paired prototype must not silently become the final gait.
- Potential top-end route conflict: native shoulder is about1.474m above feet with arm chain about.578m. Highest rung is Top−.167m. The current top-plan band.6m places shoulder aroundTop+.874m before our modest pose changes, over1m above the last grip. This cannot be solved by inventing rungs or stretching arms. Reach guards yield a non-fitting limb and log it. Claude must test an earlier supported finish/bracing route or a separately validated contact surface/transition; presentation cannot choose a landing or change motor speed. These are source/dimension calculations, not a reproduced in-game defect verdict.
- Exact callback/layer order, IK restoration and cleanup semantics require the licensed game. Test Dispose after posing, then disable/destroy the rig before another IK pass and install again. Also test failed first cleanup scheduling then successful retry.
- Native action ownership is checked from current AND next Animator tags directly at IK, avoiding cached Player.InAttack results; both arms/torso yield conservatively.
- Observer transport is absent. The bounded direct-peer design remains a proposal pending ownership/review and real host/client evidence. Proximity inference stays rejected.

## Verification and delivery boundary

150 game-free production-policy tests passed, including23 new animation tests;15 analyzer tests passed before implementation. Repo validation and whitespace checks are required before handing this source revision over. No licensed presenter compilation, integrated runtime capture, package or normal-speed animation approval has occurred in this lane. Final exact-candidate checks and the owner's complete demonstrations remain required.
