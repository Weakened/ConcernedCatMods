# Independent scope, runtime and package review checklist

Use this checklist with an uninvolved reviewer. The current Codex assignment is
original animation implementation in CODEX_PROMPT.md. Codex may independently
review code it did not author, but cannot be the sole independent reviewer of its
own animation. Claude remains integration lead.

---

Continue Concerned Ladders in `Weakened/ConcernedCatMods`. Claude integrates the
owner revision and prepares the new tested ZIP. Your default is independent
read-only review. Do not race the lead in source, builds, manager or game.
Fixes require an agreed allowlist in a separate worktree and lead integration.
Use isolated subagents for bounded reviews if helpful. Only the lead operates
Valheim and imports packages; actual product builds use the existing lock.

## 1. Establish the current candidate and actual acceptance contract

Read AGENTS.md, CLAUDE.md, naming conventions and this product's PROJECT,
OWNER_REVISION_2026_10_25, ARCHITECTURE, TEST_PLAN and current STATUS. These
explicitly replace the old supported-only/no-stamina/no-skill scope. Do not
reject new behavior because an obsolete test required every falling grab to
fail. Historical results live in STATUS_0_1_1_HISTORICAL and are not new passes.

Reconcile current local root, branch/full SHA/dirty work and lead ownership.
The reported path was `C:\code\cc-integration\cl-ladders`; inspect it rather than
resetting to an old bundle. Audited PR #433 head
ce6bb7f7ac7b9862b35090baeed61b8ece39a825 only changes STATUS after tested product
7e4e7357359ce9329e4274aec186740cddfea87e. Two different 0.1.1 ZIPs were reported.
Neither an old NO P0/P1 nor a 0.1.1 filename identifies today's installed bytes.
Keep unknown local source/ZIP/profile identity unverified until observed.

Before a defect verdict, build a coverage table mapping every current request
to implementation, production-policy tests and actual new game evidence.
**Missing requested mechanics are NO_GO, not accepted P2s or optional polish.**
A name-only patch, new log line or updated specification cannot fulfill them.

## 2. Verify every current feature

- **Reach and air admission:** initial 1.5 m capsule-relative chest-to-usable-rung
  maximum, not camera/prefab distance. First two latest photos are range
  reproductions, not measured metres. Same hint/admission predicate, native
  interaction priority, optional bounded ladder-only 45-degree assistance.
  Fresh Use supports rising/falling; 0.25 m vertical/1 m horizontal alignment,
  legitimate low-rung hang without endpoint hoist. LOS AND full capsule route
  before all physics/fall/pose/stamina/XP writes. Failed catch is inert;
  successful catch arrests its fall once. No global interaction/fall change.
- **Both faces:** chosen approach face is locked; stand-off derives independently
  from each real collider face. The old box at z=.08.. .24 is asymmetric. Pure
  negative-plane admission is not proof: audited runtime routes it to front
  through its own box. Require a same-face lane fix, not message suppression or
  blanket ladder-collider exclusions. Camera cannot flip sides; mixed seams
  need compatible physical lanes, no gaps/overlaps/parallel shortcuts.
- **Space jump and usable floors:** upward plus camera-relative movement-directed
  departure, including onto a side platform; neutral outward. Native force,
  cost and modifiers once in every attached mode, no fake grounding or lost
  next-tick impulse. Zero stamina deliberately releases. All ladders require
  0.4 s plus fresh Use, Jump beats stale Use. Independently require a reliable
  next-floor walk-off without Jump: early lip planning, bounded destinations,
  full room/inboard footprint support and continuous swept-route revalidation.
  A real closed roof/hatch must still block with responsive controls.
- **Truthful reason:** building-blocked wording requires an actual other blocking
  native Piece identified by the relevant query, not own connected ladder,
  a Piece behind the clear face, range failure or no landing. Terrain/actors
  still physically block and receive appropriate reasons; unknown/overflow
  fails safely with neutral feedback. Reject a text-only repair masking the
  wrong mount lane or removal of collision to make the error disappear.
- **Crouch/Drop:** inspect DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md.
  A fresh native action edge releases every attached mode, no jump/outward
  impulse, landing or stamina gate. Settle earned climb cost once; remove only
  ladder-commanded motion, including transfer motion. Preserve independent
  impulses, ordinary fall/collision, ownership and cleanup. Remapped/controller
  Crouch works; pre-held/toggled state and queued menu presses do not trigger.
  Jump > Drop > eligible Use; consume competing edges so no sneak/world-use
  action leaks after departure. Same 0.4 s plus fresh Use remount guard.
- **Scaffolding:** mounting with/equipping a hammer is allowed; build/menu holds
  retain the anchor without passive climb cost/XP. Native menu/selection/aim/
  rotation/snap/place/repair/remove and hotbar work once, with normal costs,
  range, stations, wards, support, placement/collision and refunds. Native hammer
  Use takes priority over ladder dismount; Jump/Drop remain available with
  gameplay focus. No blanket InPlaceMode or verified hammer-action refusal,
  but no combat-wide bypass either. Fresh connected extensions update the
  bounded session run without remounting; no per-frame world scan or imaginary
  support after removal. Current support removal releases; unrelated removal
  does not. Verify hammer animation/equipment and ladder hold coexist.
- **Run and accounting:** native processed Run/remap/controller/hold-toggle;
  normal 1.8, fast 2.43 at base 6 stamina/s, learned level 20 down+Run slide 4.5 at base 2/s.
  Below level 20 down+Run fast-descends. Neutral/Run-release/reverse brake predictably;
  exhaustion returns to normal/hold. Mode speed reaches actual motion, no old
  clamp charging for an absent boost. Native cost once from actual eligible
  distance/full-input mode speed; one pre-budgeted bounded pending step settles
  before the next paid step or voluntary Use/Jump/Drop, before jump affordability.
  No blocked/hold/overlay/failed-grab/mount/exit cost, teleport credit, double
  multiplier or writes after ownership loss; analog cost per metre consistent.
- **Native skill:** stable ID
  `com.theconcernedcat.valheim.concernedladders.climbing`, registered in Awake
  even with climb disabled. Store AddSkill return, read actual learned native
  level; SkillManager.GetSkill is a definition. `IncreaseStep=1`, native RaiseSkill
  factor 0.25 per completed normal/fast metre, owner only. Fractions carry across
  mounts in one character/world session and clear at logout/change. No XP from
  hold/block/catch/slide/mount/exit/fall/correction/spam. Native UI/save/within-
  level progress/death/protection/settings; no second save or production level
  assignments. Learned 19.99/20.00/>20 and loss-below level 20 relock, not effective buff
  or remembered unlock. Linear 0..25% fast/slide stamina saving over 0..100.
- **Names and snapping:** Wood Ladder, Copper Ladder, Iron Ladder in all three
  hammer/hover/repair surfaces, permanent IDs unchanged. Native snap panel,
  modes/cycling/coordinates/bindings stay; localized Top/Bottom/corners, no raw
  snap_bottom_left/$ tokens. Inspect actual formatter, no double-prefix guess
  or whole-panel replacement. New hints/reasons localized, old no-midair text
  removed only alongside actual new behavior.
- **Animation prerequisite:** verify VANILLA_ANIMATION_BASELINE_2026_10_26.md
  was completed before motion implementation. Inspect actual native metadata and
  playback, separated sampling/state/render/capture rates, and justified final
  cadence/easing/blends. Missing optional curves may be recorded; missing native
  access/playback cannot be fabricated. Require LIMB_FRAME_REFERENCE_2026_10_26.md
  frame-to-capture comparison, all four contacts and body movement. Reference
  frame numbers do not mandate 60 FPS or supply physical root motion.
- **Animation:** inspect actual rig/controller/pipeline and
  original authored clips adapted for in-place presentation,
  or a scoped procedural attempt with real limb motion, actual-travel cadence,
  equipment integrity and restoration. Walking parameters are fallback, not
  custom animation. A concrete inspected/attempted blocker or failed prototype
  plus tested walking may be CUSTOM_ANIMATION_DEFERRED. Only this feature is
  conditional. Existing packaging limits must be extended for needed original
  assets; they are not an excuse to skip the feasibility attempt.
  Apply ANIMATION_REVIEW_2026_10_26.md: the owner rejected the submitted limbs.
  Verify the real rig mapping, rung contacts, scoped pose ownership and root/body
  motion after import. Meshy start/loop/finish are visual references only; no stock asset acquisition.
  Reject mannequin-only, static-image or source-only animation acceptance; R25
  needs actual Valheim mount/loop/hold/reverse/top exit, catch, Jump/Drop and
  scaffold footage. Preserve prior functional and observer gates. Do not relabel
  the rejected limb implementation as an accepted custom or walking fallback.

## 3. Audit the difficult runtime boundaries

Confirm each bug in current source before calling it fixed. Owner screenshots
show layouts/messages, not a proven collision-free route or measured distance.
Require positive valid-lip/rear-face/air-catch cases beside real roof/wall/
range/occupied-route negatives. General collision bypass is never a fix.

Check local ownership BEFORE motion, pose, stamina and XP. Failed binding leaves
pieces registered and native movement usable. Death/teleport/changed owner/
destroyed or rotated piece/disabled setting/unload/logout/exception must clear
sessions and pending accounting without rewriting an external controller.
No remote actor, NPC or another product acquires this motor.

Trace input order end to end. Native Interact plus assisted targeting must not
dispatch Use twice. Native hammer actions retain shared bindings before ladder
Use-to-dismount. Jump wins simultaneous eligible release inputs, losing edges
are consumed, Crouch/Drop uses a fresh native press, and current Run survives native
motion suppression, overlays stop intent. Controller/toggle works through real
current actions, not a keyboard GetButton assumption. Idle hooks preserve
native doors/chests/combat/build controls and ordinary ladders/stairs.

Inspect actual capsule centre/radius/scale and asymmetric ladder collision,
allowed rotations, below-bottom hanging, headroom, footprint support, changing
routes, actor blocking and full query buffers. Bounds cannot silently truncate
a run or choose a random neighbor. No per-frame world scans/unbounded allocation.

Review jump's airborne guard, impulse handoff and exactly-once native payment.
Successful catch changes fall accounting only after acceptance; failed catch is
inert and later departure has normal fall semantics. Verify jump toward the
camera-relative requested side platform, not only neutral outward release.

Audit observed displacement settlement, maximum effective budget and stale-step
cleanup under long frames, collision, zero stamina, voluntary release and lost
ownership. An intentional completed step cannot become free via an immediate
Jump, but lost ownership cannot cause a remote write. Native learned level,
not a definition/buff, gates slide. Do not claim native save/death behavior from
Jotunn source alone; inspect/test the actual installed game.

For custom animation inspect asset source/license, runtime asset loading,
per-character override/graph, no root-motion travel, restoration and observer
presentation. Local playback is not network proof. Independently review any
minimal presentation-only protocol before integration: owner validation,
finite/bounded values, stale expiry, late join, disconnect; no remote movement,
per-frame motor RPCs or persistent player-ZDO keys. Observers award no XP/cost.

## 4. Demand evidence that distinguishes the old candidate

Inspect the active T03/T04/T17 expectations and R01–R43. Preserve useful original
A/B/L/P/M checks. Tests must prove legal airborne/same-face behavior, not only
assert existing constants; retain negative ownership/overlay/cooldown/collision/
nonfinite cases. Old unsupported/falling-negative tests must be replaced where
they conflict. Do not accept engine stubs or large unrelated test totals as a
compiled/gameplay proof. Full Python mutation tests run separately in an
isolated checkout; mandatory verify.ps1 uses actual licensed references.

Require measured reach, rising/falling catches and failed grab, twenty floor
loops, all jump modes/zero stamina/remount, both faces, ten mixed seams, native
interaction/remaps/controller/toggle, timed speed/stamina/XP, natural skill
progress/save/death/isolation,19.99/20/relock, slide braking, readable snaps,
all cleanup and current package import. Mark missing observations NOT RUN or
BLOCKED, never PASS. Only custom animation can be conditionally DEFERRED.

Inspect six final-version video segments in the owner's order: measured photo
reach approaches; actual Space hop onto a side platform plus separate top-floor
walk-off; rising/falling air catches and inert failed catch; Title Case names in
hammer/hover/repair; both faces and correct building vs own-ladder/behind-face/
range/terrain/actor reasons; every native Cycle Snap label/mode/binding.
Also require the later Drop and scaffolding demonstrations (R36–R43), sprint,
skill/level-20 slide and actual animation or fallback evidence. A still or PNG named .gif proves no motion.

Report defects with severity, source location, condition, consequence and minimal
repair. Separate confirmed bugs from hypotheses/unrun tests. Review the repaired
head, not an older diff. Do not let an old PASS survive a changed requirement.

## 5. Verify exact package identity, then issue the verdict

The new feature version is >=0.2.0 and strictly newer than reconciled local work.
GUID/assembly/package/prefab/skill identities remain permanent. Version metadata,
changelog and manifest agree. Root metadata, own DLL under plugins, no outer
wrapper/game/dependency/other-product binaries/source bundle/saves/credentials.
Prefer owned assets embedded in DLL; any loose assets need explicit packaging,
identity-gate extension and hash/install coverage. Do not quietly omit them.

Verify sequence: freeze -> package (rebuild) -> full verify -> compare DLL ->
repackage verified bytes without rebuild if needed -> manager-import exact ZIP
-> cold-start -> final gameplay evidence. Exactly one active own DLL/GUID and
expected dependencies; no loose development copy beside managed import. `.r2z`
is a dependency profile file, not the local mod ZIP. Preserve unrelated profiles.

Inspect the actual Windows Downloads Known Folder and identical tested ZIP copy.
Run/review `tools/verify_ladders_candidate.py` with all seven observed arguments:
--package, --verified-dll, --plugins, --game-log, --source-commit (full 40-character build
SHA), --version and --downloads-copy. Python 3.11+ and the strict six-file package
contract apply; extend the verifier first for necessary loose assets. See
TEST_PLAN for the PowerShell command.
Success is IDENTITY_MATCH_ONLY, gameplayVerified=false and
binaryProvenanceVerified=false. It checks identity consistency, not requested
behavior or compiler provenance. Require the real controlled build record and
fresh one-startup full-SHA log; a fabricated banner is not evidence. Record the
tested build SHA separately from a later docs-only commit. Reject old/same-version
confusion, stale logs, duplicate plugins, mismatched bytes or unverified assets.

Your verdict names exact reviewed source SHA, version and package SHA256:
GO_FOR_OWNER_SOLO_TEST, GO_FOR_RELEASE_REVIEW or NO_GO. Required missing mechanics
mean NO_GO. A genuinely unavailable controller or real network endpoint may be
explicitly BLOCKED for solo testing; release review requires actual network/
dedicated rows. Conditional animation fallback must be documented. Identity
success and source-only review never become a gameplay PASS.

Keep reviewing concrete repairs until accessible gates are complete. No public
publication, tag or main merge. The lead must return the exact reinstallable
ZIP in actual Downloads, evidence and at most four preference checks for the
owner; build/import/basic regression remain the agents' work.
