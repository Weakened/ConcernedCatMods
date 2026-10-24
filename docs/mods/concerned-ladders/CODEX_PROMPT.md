# Codex prompt — independent review, pieces and visual quality

Paste this complete prompt into Codex on the development machine. Run alongside
the Claude integration lead with disjoint worktrees and explicit file ownership.

---

You are the independent reviewer and presentation/build-piece engineer for
**Concerned Ladders** in `Weakened/ConcernedCatMods`. The owner wants wall-placed
vertical wood, copper and iron ladders, smooth traversal, optional genuine climb
animation with walking accepted as fallback, and a separate ready-to-run
Thunderstore profile. They want the implementing agents to find ordinary defects
before asking them for another test cycle.

## Read and coordinate first

Read `AGENTS.md`, `CLAUDE.md`, `docs/NAMING_CONVENTIONS.md`, and the complete
`docs/mods/concerned-ladders/` spec/test/status/profile documents. Inspect the
actual candidate source; do not start another ladder PoC. Foreman's old ladder
code was audited and its unobserved gates are not proof of this product. The
current request permits an independent product and new material pieces.

Confirm the lead's candidate SHA and file ownership. Use a dedicated worktree
outside the canonical integration tree. You may use subagents for bounded
read-only audits or disjoint code tasks, one writer per worktree. The lead alone
integrates, serializes game-dependent builds, deploys and operates Valheim. Do
not run competing publicizers or change a profile while the lead is testing.
Preserve all existing profiles, saves, owner changes and the protected
`Prepare-TCC-Screenshot-Profile.ps1`.

Your default edit allowlist is `src/ConcernedLadders/Pieces/**` plus explicitly
agreed piece/presentation tests and documentation corrections. Runtime motor
changes belong to the lead or their assigned worker. Report a necessary
out-of-allowlist change as a precise proposed diff; do not race another writer.
Do not edit solution/CI/package pins or another product without coordination.

## Workstream A — independent runtime review

Review the exact candidate before the live test. Identify defects with a file,
path/condition, likely consequence and minimal corrective action. Distinguish
confirmed source defects from hypotheses that need the installed game.

Review these boundaries:

1. Only the current locally owned player can enter and remain controlled.
   Check ownership before pose writes as well as physics writes. No shared
   per-ladder anchor, remote-player mutation or ownership claim.
2. Endpoint chains cover the complete loaded run, including ten mixed sections;
   no initial small-radius truncation. Opposite sides, parallel shafts, gaps,
   overlaps and differently rotated modules cannot join accidentally. Bounds and
   nonfinite data fail safely without random neighbor selection.
3. Mount and every movement/transfer stage sweep the actual capsule. Account for
   radius, height, centre, scale and the authored ladder front face. Do not use
   destination-only clearance or ignore a whole building's colliders. Own body
   colliders are excluded; other actors and walls are not.
4. Interact while attached seeks safe supported ground or stays attached;
   Jump deliberately releases. Dangling bottom holds. Returning from a midspan
   exit returns to that height, not the run's top. Removing a platform during
   a transfer is revalidated.
5. Menus/chat/map/controller/remapping feed no stale movement. Climbing direction
   is consistent even as the camera turns. Input is consumed only while this
   controller owns a valid session. Native binding failures refuse mounting.
6. Death, teleport, world unload, invalid piece, disabled setting, exception and
   ownership loss release control. State restoration does not overwrite an
   external controller's new velocity/animation or leave gravity disabled.
   Fall handling cannot create immunity or allow falling grabs to erase damage.
7. Foreman motor detection is explicit and safe; no other mod is unpatched,
   disabled or globally reconfigured. Idle hooks preserve ordinary gameplay.
8. Error text uses the repository scrubber. Diagnostics are bounded and explain
   action; no per-frame logs or machine paths. Registry entries clear on unload.

Known initial review defects were corrected in the supplied candidate: capsule
offset overlapped the ladder, safe Interact was a freefall release, bottom
departure ignored footing, failed clone registration retried an already cached
ID, and midspan reversal selected the wrong height. Verify the corrections remain
present; do not report historical code as the current implementation.

Deliver a compact P0/P1/P2 report. A clean source review is not a gameplay PASS.

## Workstream B — exact building-piece quality

Read the current Jotunn 2.30.2 source/API where needed. The normal wooden-wall
prefab is `woodwall`, not `wood_wall`; metal donor is `woodiron_pole`. Current
build-menu usage tags are explicit. Keep the three permanent custom IDs:
`tcc_ladder_wood`, `tcc_ladder_copper`, `tcc_ladder_iron`.

Verify or improve the supplied original 2 m rails/rungs while preserving the
geometry contract: 0.8 m width, bottom `(0,0,0)`, top `(0,2,0)`, open face +Z,
matching rung pitch and `FrontFaceDepth` derived from actual collision. Do not
quietly resize visuals without updating the motor clearance contract and tests.

Keep the normal hammer workflow, costs (Wood4; Copper2+Wood1; Iron2+Wood1), native
workbench/forge requirements, material discovery, ward checks, repair/demolition
and support. Initial health is Wood200/Copper400/Iron800; metal donors retain
their documented iron support class. These are design values, not claims about
vanilla ladders. Do not replace existing stairs or change every native ladder.

Inspect prefab lifecycle carefully: clone under the inactive Jotunn parent;
do not instantiate a live world piece then strip it. Change the clone only.
Remove donor visual/LOD/collider/snap children and repair all real descendant
references; never invent unknown cache fields. Bind new/worn/damaged visual
roots, keep collision independent of their visibility, and preserve appropriate
vanilla structural logic/effects. Direct-child `snappoint` transforms must give
useful wall and end-to-end snaps at normal rotations.

Use loaded material references by verified name, copied before modification.
The donor may include snow/LOD/effects materials; its first renderer is not a
safe selection rule. Don't tint shared native material instances, export game
assets, or import another mod's GPL implementation casually. Materials must look
coherent in daylight/night/rain and damage states. The initial runtime geometry
is a functional art pass, not a claim of final visual approval.

Stable piece registration must survive a cosmetic icon render/cache error.
Keep a fallback icon, emit a useful diagnostic and fail the visual acceptance
row until repaired; don't make the world lose its prefab because a thumbnail
couldn't render. A partial actual prefab registration failure is terminal for
that session and must be reported, not retried with a new save ID.

Ask the lead to run B01–B12 and the relevant P rows with your build. Inspect real
screenshots/video and logs. A Unity asset viewer render is not in-game evidence.

## Workstream C — animation within the agreed scope

Begin by checking the existing walking fallback, on local and remote avatars.
Movement should drive the cycle; neutral should hold still; reverse should read
coherently; entry/exit/death must restore normal locomotion. Equipment must not
vanish, duplicate, or remain stuck. A standing slide or unchanging idle is not
the promised walking fallback and needs correction or an explicit limitation.

Only after traversal works, investigate a more natural original climbing loop
or procedural pose. Inspect the actual humanoid rig and legal asset pipeline.
If a new clip is practical, specify/import it through the appropriate Unity
asset bundle, document authorship/license, isolate its animator override, phase
it from actual climb distance, blend in/out, and retain walking as fallback.
If it is not practical, finish with the owner-accepted walking fallback. Do not
block a usable ladder mod on perfect IK, add a heavy mandatory animation
framework, rip a game clip, or trigger attack/emote gameplay events as a trick.

Bones are optional later visual variety. Do not add later-game spoiler materials,
boss unlocks, custom skills, extra resources or broad progression changes.

## Workstream D — evidence, review and completion

Run the relevant pure tests and validator. Coordinate any required licensed build
with the lead's machine-wide lock. Review the actual final diff after your fixes;
don't cite a review of an older head. Validate native API changes against the
installed assemblies rather than letting fabricated test stubs bless them.

For each visual/traversal defect, provide a minimal disposable-course repro,
expected/observed result and exact candidate SHA/hash. The lead runs the full
matrix, including keyboard/controller, closed top, removed platform, ten-section
stack, death, reload and network roles. Help classify what really ran. Mark a
missing second client BLOCKED; don't infer multiplayer from local code.

Do not change test criteria to match a flawed implementation. If an effect cannot
be observed with current access, record it and keep completing independent code
work. Do not repeatedly ask the owner to check basic placement or movement while
the implementing agents still have the tools to do so.

Your final handoff to the lead contains: base/head SHA, allowlisted changed files,
specific defects fixed, exact checks/results, remaining verified limitations,
screenshots/video references actually captured, and a short owner visual-review
list. It must say whether the actual plugin was built and run. No claim of
profile installation, multiplayer support or release readiness without evidence.

The lead integrates and prepares the final candidate. No public release,
Thunderstore publication, tags, main merge or unrelated cleanup from your
workstream. Continue to completion of your assigned tasks with subagents where
useful, preserving the independent-product and one-writer boundaries.
