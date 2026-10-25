# Claude continuation prompt — implement the owner revision and prove its ZIP

Paste this complete prompt into the existing local integration session.

---

Continue our Concerned Ladders work in `Weakened/ConcernedCatMods`. Implement
and verify the active owner revision; do not return another unchanged 0.1.1
candidate or only a plan. You own integration, licensed builds, manager imports
and game operation. Codex independently checks the current source and evidence.
Use subagents where useful, with isolated worktrees and explicit file ownership.
Never let two writers share a tree or run competing publicizer/build jobs.

## 1. Reconcile real state and scope before changing it

Read AGENTS.md, CLAUDE.md, docs/NAMING_CONVENTIONS.md and this product's PROJECT,
OWNER_REVISION_2026_10_25, ARCHITECTURE, TEST_PLAN and active STATUS. The owner
revision replaces ground-only, single-face and no-stamina/no-skill rules.
STATUS_0_1_1_HISTORICAL is evidence about the old scope, not current acceptance.

The reported working folder is `C:\code\cc-integration\cl-ladders`. Verify actual
root, branch, full SHA, dirty work and worktree list; preserve work newer than
the remote audit. Do not reset to an old source bundle or move the checkout to
satisfy an obsolete path example. Inspect PR #433 and issues #430–432. The
audited head ce6bb7f7ac7b9862b35090baeed61b8ece39a825 differs from tested build
7e4e7357359ce9329e4274aec186740cddfea87e only in STATUS. Two different 0.1.1 ZIPs
were reported. A matching filename/version or old NO P0/P1 is not new behavior.

Record the actual active TCC-Ladders-Dev profile, installed own DLL/hash, imported
ZIP/hash, loaded plugin/version/source, Valheim/Unity and dependency versions.
Unknown package/installation identity stays unknown. Do not ask the owner for
paths discoverable locally. Do not retry a device service's explicit quota
block; use your existing authorized local tools and finish accessible work.

Keep this independent product's GUID, DLL/package/prefab IDs and existing world
geometry. Preserve other products, profiles, saves and the protected screenshot
profile script. BepInExPack 5.4.2351/Jotunn 2.30.2 were the historical verified
pins; recheck the current installed game, preserve other products' versions.

## 2. Implement the required mechanics

**Reach and air catch.** Fresh Use can mount on support or catch while rising/
falling. Initial reach is 1.5 m from capsule-relative chest to the nearest usable
rung span, not camera or prefab origin. The first two latest photos are range
reproductions, not metre measurements; recreate and measure them. Hints and
final Use share eligibility. Native targeting wins; optional ladder-only
assistance stays within reach/45-degree cone and cannot steal doors/chests/
actors/building input. No global interaction-range change or per-frame world
scan. Air alignment stays within 1.0 m horizontal/0.25 m vertical; implement real
low-rung hanging without feet-clamp hoisting. Prove LOS, full capsule route and
room before changing physics/fall/pose/stamina/XP. Failed catches change none
of those; a successful catch arrests that fall once. Later departure falls
normally. Keep ownership/busy/overlay/nonfinite gates.

**Both physical faces and truthful reasons.** Select and lock the approach
face and route onto that same face. At the audited source the pure policy
accepts negative-plane feet but runtime aims at the front, sweeping through its
own ladder box. Fix the lane, not only its message. Derive rear/front stand-off
from each actual collider surface; old local z=.08.. .24 is asymmetric. Do not
generally ignore ladder colliders, cross walls or flip faces with camera yaw.
Keep mixed-stack lanes compatible; reject real gaps/overlaps/parallel shortcuts.
A building-specific blocked error is legal only when the relevant query finds
an actual other blocking native Piece. Own connected ladder, a piece behind a
clear face, excessive range and absent support are different reasons. Terrain
and actors still physically obstruct; classify them truthfully. Unknown/query-
overflow cases refuse safely with a neutral reason. Localize bounded feedback.

**Real Space hop and floor exits.** Jump from mounting, hold, normal/fast climb,
slide and exit transfer must have an upward component plus camera-relative
movement-directed departure, including toward a side platform. Neutral defaults
outward. Use actual installed native jump force/cost/modifiers once; inspect
forced/airborne semantics so the call is not silently refused. Never fake global
grounding or let the next tick erase the impulse. Settle previous climbing cost
before affordability. Zero stamina still deliberately releases. All ladders
require 0.4 s AND a new Use edge before remount; Jump wins simultaneous inputs.
Separately make the next-floor walk-off reliable without Jump. Plan before the
head reaches a lip, search bounded plausible landings, require full standing
room/inboard footprint support and sweep clearance/rise/cross/settle. Revalidate
every stage. Real roofs/hatches/railings block with responsive reverse/Use/Jump.

**Run, stamina and slide.** Read current processed Run intent with remaps,
controller and native hold/toggle, not raw Shift or a stale running flag after
motion suppression. Normal speed 1.8 m/s, fast 1.35x=2.43 at base 6 stamina/s.
Down+Run below learned level 20 fast-descends; at >=20 it slides at 2.5x=4.5 at base 2/s.
Release movement to hold, release Run for normal descent, reverse immediately.
Exhaustion returns to normal/hold, never forces a fall. Remove the old 1.8 clamp
from both planning and applied mode speed; mount/exit alignment stays ordinary.
Pay native stamina once for observed eligible distance/full-input mode speed.
Pre-budget maximum effective cost, allow one pending step, settle once before
the next paid step or voluntary Use/Jump and before jump affordability. Apply
native multipliers once; discard pending state on ownership loss without remote
writes. No hold/blocked/overlay/failed-grab/mount/exit charge. Teleport/knockback
is not climbing distance. Analog cost per metre must remain consistent.

**Native skill.** Register in Awake regardless of climb toggle, permanent ID
`com.theconcernedcat.valheim.concernedladders.climbing`, localized name/description,
original readable icon, `IncreaseStep=1`. Keep AddSkill's returned Skills.SkillType;
SkillManager.GetSkill is a definition, not learned progress. Native RaiseSkill
factor 0.25 per completed normal/fast metre, owner only. Carry unawarded fractions
across mounts in one character/world session; clear at logout/character/world
change. No XP for hold/block/catch/slide/mount/exit/fall/correction/input spam.
Native UI/save/load/death/protection/world settings; no second save, production
level assignments or hardcoded death-loss percent. Current learned level 20 unlocks,
loss below level 20 relocks. Linear 0..25% stamina saving over learned0..100 for fast/
slide only. Verify natural first progress and native within-level persistence;
transient unawarded distance need not survive a full exit.

**Names and snap UI.** All current English hammer/hover/repair names must be
Wood Ladder, Copper Ladder and Iron Ladder. Retain any already-integrated source
fix and verify all three surfaces in game. Preserve IDs. Inspect actual native
snap formatter before changing labels; use native Top/Bottom plus localized
corners, current bindings and existing panel/mode/cycling. No snap_bottom_left
or raw $ token; do not replace the entire hint UI. Retire midair-unavailable
feedback; separate range, genuine blockage, control and no-landing reasons.

**Conditional animation.** Inspect actual rig/controller and compatible Unity
pipeline. Attempt original in-place clips or a scoped procedural limb solution:
climb/hold/reverse/fast/slide/transfer, actual-travel cadence, equipment intact,
per-player state and complete restoration. Walking-speed parameters alone are
fallback. Runtime SetCurve does not author arbitrary non-legacy Animator clips.
Prefer embedded owned assets; loose assets need packaging/hash coverage. Prove
observer presentation; local playback does not automatically replicate. Any
necessary minimal presentation-only sync needs documented independent review,
owner validation/bounds/stale cleanup, no remote motor or persistent player-ZDO
keys. If a sound solution cannot be completed, document concrete inspected
blockers/attempt or failed prototype, keep tested walking and mark
CUSTOM_ANIMATION_DEFERRED. Other requested mechanics remain required.

## 3. Implement, review and test in that order

Repair reach/face/jump/floor/blocker behavior first; then stamina/skill/slide,
labels and animation attempt. Use narrow verified native adapters and existing
production policy seams. Replace obsolete unsupported/falling rejection tests
with legal airborne positives and blocked negatives; preserve ownership,
overlay, cooldown, nonfinite and lifecycle protections. Add meaningful tests
that reject the old behavior, not fake game stubs or self-fulfilling assertions.

Request Codex review of the exact repaired source with requirement coverage,
not only a generic bug verdict. Fix actual findings and obtain review of the
new head. Source review does not substitute for running the plugin.

Use disposable character/world/course. Profile isolation does not isolate
native saves. Turn off cheats before stamina, fall, collision and natural skill
observations. Execute applicable original A/B/T/L/P/M gates and R01–R35 in
TEST_PLAN. Include 20 floor loops, all Jump modes/zero stamina/side platform,
both clear faces, measured reach/rising/falling/failed catches, input priority,
mixed seams, fast/slide speed and cost, native skill persistence/death/isolation,
19.99/20 threshold/relock, snap text and every cleanup. Never carry an old PASS
forward without applicable evidence. Real controller/network unavailable means
BLOCKED with a reason; complete accessible work, do not invent a substitute.

Record the six latest-note video segments in the owner's order: measured photo
reach approaches; actual upward/directed Space hop plus the separate top-floor
walk-off; rising/falling midair catches and inert failed catch; Title Case names
in hammer/hover/repair; both faces with truthful building vs own-ladder/behind-
face/range/terrain/actor reasons; every native Cycle Snap label/mode/binding.
Add earlier sprint/skill/level-20 slide and animation evidence. Stills,
old clips or a PNG named .gif do not prove new movement.

## 4. Freeze, package and prove the installed bytes

Choose >=0.2.0, strictly newer than the reconciled local candidate. Synchronize
all version fields and release notes without changing permanent identities.
Freeze clean source before final evidence. Run validator and focused production
tests, the full Python suite separately in an isolated checkout, and mandatory
full scripts/verify.ps1 with real licensed references. The existing lock was
`C:\code\concernedcat-handoffs\2026-09-17-gunnar-thorstein-work\with-build-lock.ps1`;
inspect its actual parameters and current path. Do not invent a competing lock.

package.ps1 rebuilds: package ConcernedLadders under the lock, then FINAL full
verify. Compare ZIP-entry DLL to verified DLL; if different, package those
verified bytes through existing tcli/reproducible ZIP tools without rebuilding.
Metadata belongs at ZIP root, own DLL under plugins; only declared owned assets
beyond it. No wrapper/source bundle/game/dependency/other-product DLLs or saves.

Import that exact ZIP through the manager's Import local mod from file in the
observed TCC-Ladders-Dev. `.r2z` is a dependency profile archive, never this mod.
Resolve only known own duplicates with supported manager operations or backups
outside every active plugins tree. Do not hand-edit mods.yml/delete profiles.
Start modded, observe one new version/full source fingerprint, and run final
regression on those bytes. No post-test DLL replacement or rebuild silently.

Resolve the actual Windows Downloads Known Folder, including redirection, and
copy the tested ZIP there. With variables set from observed existing files and
the frozen full 40-character build SHA, run the read-only additional identity gate:

```powershell
python ./tools/verify_ladders_candidate.py `
    --package $ladderPackagePath --verified-dll $ladderVerifiedDllPath `
    --plugins $ladderActivePluginsPath --game-log $ladderFreshGameLogPath `
    --source-commit $ladderFullBuildSha --version $ladderCandidateVersion `
    --downloads-copy $ladderDownloadsZipPath
```

`IDENTITY_MATCH_ONLY` has `gameplayVerified=false` and
`binaryProvenanceVerified=false`; keep actual R-row observations and controlled
build evidence separate. Python 3.11+ is required. The helper currently accepts
exactly six standard package files; needed loose assets require extending its
package/install/hash contract first, not merely recording separate hashes or
declaring animation deferred because of the checker. Record tested build SHA separately
from a later docs-only evidence commit; recording hashes need not rebuild it.

## 5. Finish the actual handoff

Update active STATUS with only new observed facts, preserve historical evidence,
and return exact source/PR, version, real package/Downloads paths and hashes,
installed identity, profile/start route, complete tests/captures and independent
review. Use READY_FOR_OWNER_SOLO_TEST, READY_FOR_RELEASE_REVIEW or NOT_READY with
specific remaining rows. A missing core mechanic stays NOT_READY. Conditional
animation deferral must be explicit; real network gates are needed for release
review. No public upload, release tag or main merge in this assignment.

Keep fixing until accessible requirements are complete or a concrete external
blocker remains. Leave the owner at most four preference checks, not your
build/import/regression duties. Do not describe current docs or a name-only
patch as the finished requested runtime.
