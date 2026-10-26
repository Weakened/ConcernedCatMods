# Owner revision — 25 October 2026

## Active authority and purpose

The owner has already requested the mechanics below. Implement them in the
existing Concerned Ladders branch and prove the new package; do not seek a new
scope approval or ask the owner to rediscover routine defects. This document,
[PROJECT.md](PROJECT.md), [ARCHITECTURE.md](ARCHITECTURE.md) and
[TEST_PLAN.md](TEST_PLAN.md) replace the old acceptance contract. The two role
prompts are continuation instructions, not a request for another proof of concept.

The earlier detailed playtest handoff remains incorporated here: deliberate
midair grabs; custom animation if feasible; a real jump and usable next-floor
exit; both clear faces; native Run/stamina; native Climbing skill and level-20
slide; native snap text; reasonable measured reach. Later clarifications below
are additive. The photos do not measure metres or identify the installed DLL.

## Six latest clarifications and decisive observations

| Note | Required correction | Evidence |
|---|---|---|
| N01 — reach photos | The first two latest photos concern reach. Reproduce their approaches; measure capsule-relative chest-to-rung distance in game. The initial 1.5 m design limit is not a number read from a photo. Hints and Use agree. | R09, video segment 1 |
| N02 — actual Space jump | Space/Jump produces upward and camera-relative movement-directed departure, including a hop onto a side platform; neutral defaults outward. It works in every attached mode. Zero native stamina still allows deliberate release. Earlier reliable upper-floor walk-off remains required separately. | T17, R02–R07, video segment 2 |
| N03 — midair grabs | Fresh Use deliberately catches while rising or falling within reach and a clear capsule route; a failed catch leaves the original fall unchanged. | T04, R10–R11, video segment 3 |
| N04 — Title Case | Current English display names are exactly **Wood Ladder**, **Copper Ladder**, **Iron Ladder**, including hammer, hover and native repair text. Permanent prefab IDs stay unchanged. | B02, R33, video segment 4 |
| N05 — both faces and real blockers | Mount/climb/exit on either clear face, staying on that face's lane. Building-blocked wording requires an actual other blocking native Piece on the queried route. Own ladder, a Piece behind the clear face, range and missing support do not qualify. Terrain/actors still collide and get truthful reasons. | T03, R08/R13/R34, video segment 5 |
| N06 — Cycle Snap | Cycle every native snap point/mode with readable localized Top/Bottom/corner labels and current bindings; no internal snap_ names or raw tokens. | B06, R24, video segment 6 |

## Additional owner request: Crouch drops from the ladder

N07 adds a distinct deliberate Drop on a fresh native Crouch press, while Jump
remains the powered hop and Use retains its safe-exit behavior. Drop works in
every attached mode, including at zero stamina or without a landing below.
It supplies no upward/outward impulse, restores ordinary gravity/collision/fall
behavior and the player's normal animation, and uses the shared 0.4 s plus
fresh Use remount guard. Respect remaps/controllers; a pre-held or already-toggled
Crouch is not a new release request. Jump > Drop > Use resolves simultaneous
input once. The release press cannot also toggle sneak or leak through an overlay.

Full contract: [DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md](DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md).
Evidence: R36–R38 and an added Drop segment after the original six demonstrations.
This request is required and additive; no runtime implementation is claimed here.

## Additional owner request: build while holding the ladder

N08 makes the ladder usable as a scaffold. While attached, equip the hammer,
aim, select/rotate/snap/place, repair and remove through native controls; opening
menus keeps the player anchored. Equipping the hammer alone does not forbid
mounting or climbing. Building actions and focused menus hold the anchor with
no passive climb cost/XP. Normal range, materials, station/ward/support and
placement/collision rules still apply. No combat-wide busy-state exemption.

Native hammer bindings win over a conflicting ladder Use-to-dismount action;
in particular snap cycling must not detach the player. Jump and Crouch/Drop
remain separate deliberate departures when gameplay input has focus. A newly
built connected ladder section becomes usable without remounting, through
bounded run refresh. Removing the current support causes ordinary release;
removing an unrelated piece does not. Preserve native hammer animation and
equipment alongside ladder pose, and restore them on all exits.

Evidence: R39–R43 and an added scaffolding demonstration. Both N07 and N08
are required additions to the original six notes and earlier features.

## Later animation review — 26 October 2026

N09 records the owner's rejection of the submitted arm/leg motion and the Meshy
mount/climb/top-finish references. Its source and installed package identity are
unknown here; do not attribute that preview to the historical audited commit.
Follow [ANIMATION_REVIEW_2026_10_26.md](ANIMATION_REVIEW_2026_10_26.md).
The subsequent N10 decision below selects original authoring. GIFs define the
visual target but are not importable skeletal clips. Original motion still
requires actual rig fitting and gameplay integration. Expand R25/P02–P04 evidence as
specified there. Do not mark the rejected presentation accepted or silently
reuse it under the conditional walking-fallback allowance.

## Original animation ownership — later decision on 26 October 2026

N10 supersedes the prior Meshy acquisition option. Use the GIFs as references and
author original motion. No Meshy purchase or stock animation export is required
or selected. Existing references are sufficient to begin; ask for another only
if a specific unresolved visual choice would materially change the result.

Codex owns animation implementation in an isolated worktree while Claude keeps
runtime mechanics, shared integration files, builds, packaging and game operation.
Follow the new CODEX_PROMPT.md. Preserve the former full review matrix in
CODEX_REVIEW_CHECKLIST_2026_10_26.md, using an uninvolved reviewer for animation.
A small read-only presentation interface carries accepted movement and state;
it does not grant Codex a second movement/input controller. The supplied GIFs
remove no requirement to inspect the actual rig, contacts and gameplay result.

## Frame reference and native fluidity — additional decisions on 26 October

N11 requires a written frame-by-frame reference for the hands, feet, elbows,
knees, pelvis and torso during ascent/descent. Use
LIMB_FRAME_REFERENCE_2026_10_26.md and the per-frame CSVs. These are provisional
original contact plans, with exact offsets and swing/plant labels rather than
guessed bone angles. Compare implementation captures against the chosen plan.

N12 requires native animation analysis BEFORE replacement implementation.
Follow VANILLA_ANIMATION_BASELINE_2026_10_26.md: inspect actual native rig/clip
metadata and playback, including transitions and hammer actions, then choose
sampling, cadence, easing and blends consistent with vanilla. Separate asset
sampling, playback speed, screen FPS and capture FPS. Do not infer 30/60 FPS from
references, hide missing access or require a full curve dump unnecessarily.
Claude runs the game; Codex analyzes; an uninvolved reviewer checks the baseline.
Both requests extend R25/P02–P04 and retain all earlier features.

## Earlier requirements retained

The earlier Run, stamina, skill, slide and snap-text requests remain required.
Normal/fast/slide defaults are 1.8/2.43/4.5 m/s; fast/slide cost 6/2 stamina per
second of eligible travel, with a learned-level stamina saving up to 25%.
The skill identifier is permanently
`com.theconcernedcat.valheim.concernedladders.climbing`; learned level >=20
unlocks slide, and native loss below 20 relocks it. The full input, cost,
progression and persistence rules are in PROJECT sections 3 and 7.

Custom animation remains conditional on a real feasibility attempt. A tested
walk fallback plus concrete inspected/attempted limitation may be recorded as
`CUSTOM_ANIMATION_DEFERRED`. That exception does not waive any other mechanic.
Do not turn walking-parameter changes into a claim of custom limb animation.

## Why the old candidate does not satisfy this request

The audited source is PR [#433](https://github.com/Weakened/ConcernedCatMods/pull/433)
head `ce6bb7f7ac7b9862b35090baeed61b8ece39a825`. Its reported tested build source
is `7e4e7357359ce9329e4274aec186740cddfea87e`; the later commit changes only
STATUS.md. The old STATUS reports two different 0.1.1 imports. Their common
version label cannot establish which bytes are installed now.

At that source, CanMount rejects unsupported/falling players. The pure policy
allows negative-plane approach coordinates, but the runtime routes both signs
to a front-face line; the rear sweep crosses the ladder box. Fix the same-face
lane, not only its refusal text, and do not ignore ladder collision generally.
The old Jump path is not proof of the requested upward, directed hop. Sprint,
native Climbing skill and slide are absent. Raw internal snap labels and old
name casing were reported. A documentation or name-only correction is not
completion of the runtime revision.

The prior tests explicitly required falling-grab refusal; rear-face refusal
was recorded PASS. Those results are preserved in
[STATUS_0_1_1_HISTORICAL.md](STATUS_0_1_1_HISTORICAL.md) and cannot satisfy the
new T03/T04/T17 or R01–R43 cases. No current profile, installed DLL, ZIP hash
or moving-game evidence was independently reverified by this repository audit.

## Implementation and review obligations

Continue the actual local branch after reconciling source HEAD/dirty work and
active profile. The reported local folder was `C:\code\cc-integration\cl-ladders`;
verify it, do not reset it to a cloud bundle or overwrite ongoing changes.
Use isolated worker worktrees if helpful. Only the lead integrates, builds
under the existing machine-wide lock, imports and operates the game.

First add meaningful production-policy regressions that distinguish the new
contract from the old candidate: legal air admission, actual same-face lane,
real jump departure, truthful blocker classification, speed/cost accounting,
learned level 20 threshold and readable names/snaps. Keep obstruction/ownership/
nonfinite/overlay/lifecycle negatives. A test asserting every air grab fails
must be replaced, not preserved as a veto on the requested behavior.

The independent reviewer checks requirement coverage before issuing a defect
verdict. Missing requested runtime functionality is not an accepted P2 or a
visual preference. Require current SHA, exact package identity and observed
results, not repeated historical NO P0/P1 comments or a large test total.
Use the active role prompts and keep issue/PR acceptance aligned with this scope.

## New candidate and handoff

Use a new version >=0.2.0, newer than any reconciled local candidate, with
unchanged product/prefab/skill identities. Freeze source, package, full verify,
compare bytes, manager-import the exact ZIP, then cold-start and test it. The
`.r2z` contains dependency profile references; it is not this local-mod ZIP.

Run `tools/verify_ladders_candidate.py` with actual package, verified DLL, active
plugins directory, fresh game log, full 40-character build SHA, new version and
the tested ZIP copied into the actual Windows Downloads Known Folder. Follow the full
command in TEST_PLAN section 10. `IDENTITY_MATCH_ONLY` explicitly means
`gameplayVerified=false` and `binaryProvenanceVerified=false`; it cannot approve
any R-row behavior or establish which source compiled the DLL. The helper
requires Python 3.11+ and the current six-file package; loose assets require
extending its package/install/hash contract first. Record the build SHA
separately from a later docs-only evidence commit.

Return the tested ZIP's actual existing path/version/SHA256, source and DLL
identity, profile/start route, six-part demonstration and complete R evidence.
No public upload, release tag or main merge. Use READY_FOR_OWNER_SOLO_TEST,
READY_FOR_RELEASE_REVIEW or NOT_READY with specific remaining rows. Complete
all accessible implementation/regression work before leaving at most four
preference checks for the owner. Current readiness remains in STATUS.md.
