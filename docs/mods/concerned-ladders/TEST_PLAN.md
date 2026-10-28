# Concerned Ladders — active revision acceptance and evidence matrix

This is the implementing agents' test course. Run it before asking the owner to
find routine failures. A row is PASS only when its stated observation exists.
Source inspection, math tests and a clean log do not replace visible play.
The active contract is `PROJECT.md` plus `OWNER_REVISION_2026_10_25.md`.
The 0.1.1 PASS rows are preserved in `STATUS_0_1_1_HISTORICAL.md` and do not
satisfy these revised expectations. New results start NOT RUN; no blanket
carry-forward from an old test count or NO P0/P1 verdict.

## 1. Candidate receipt

Start a new evidence directory per candidate, never overwrite prior results.
Record these fields in `EVIDENCE.md`:

| Field | Required evidence |
|---|---|
| Source | full base/build SHA, clean/dirty state, branch, reviewed diff and active specification revision; distinguish any later docs-only HEAD |
| Build | complete `scripts/verify.ps1` log/summary, configuration, .NET version |
| Game | installed Valheim version/build ID, Unity version if logged |
| Dependencies | Thunderstore package versions and actually loaded BepInEx/Jotunn assembly versions |
| Candidate | new numeric version >=0.2.0, full embedded source SHA, verified/ZIP-entry/installed DLL hashes, package hash and identical actual Downloads copy |
| Test isolation | exact manager/profile path, disposable character/world names, observed save mode |
| Machine | test device, OS, graphics/controller setup; no account credentials |
| Observer | host/client/dedicated role and corresponding build/hash |
| Evidence | log excerpts plus screenshots/video named by test ID |

Use the states PASS, FAIL, NOT RUN, BLOCKED and DEFERRED. Include the reason and
next action for every non-PASS. Do not label a run green if required gates were
silently skipped or if the DLL on disk changed during testing.

## 2. Automated gates

| ID | Command/check | Evidence and success |
|---|---|---|
| A01 | `python tools/validate_repo.py` | exit 0; new product identity, pins, solution, independence, icon |
| A02 | `python -m unittest discover -s tools/tests -p "test_*.py"` | run separately in an isolated checkout; mutation tests must not alter a concurrent build/review tree |
| A03 | `dotnet test src/ConcernedLadders.Tests/ConcernedLadders.Tests.csproj --configuration Release --nologo` | production geometry, admission, face, mode, stamina/XP and threshold boundaries; no game-proof claim |
| A04 | `dotnet restore ConcernedCatMods.sln` | MSBuild loads complete solution, no missing/duplicate projects |
| A05 | locked `pwsh scripts/verify.ps1` | full Release solution build, all tests, validator; quote summary |
| A06 | locked `pwsh scripts/package.ps1 -Product ConcernedLadders`, before final A05 | new numeric version, root manifest/readme/changelog/license/icon and own DLL plus declared owned assets; ZIP-entry DLL equals final verified DLL |
| A07 | profile generator twice | identical bytes; no DLLs/scripts/secrets/saves in `.r2z` |
| A08 | development deployment `-WhatIf`, if used | verifies exact profile/source/DLL; writes nothing; not final package installation proof |
| A09 | manager import of exact final mod ZIP | one active own DLL/GUID; verified DLL == ZIP-entry DLL == installed DLL; profile/dependencies observed, other profiles untouched |
| A10 | independent current diff and scope review | every requested feature traced to implementation and R evidence; no current P0/P1; missing scope is not an accepted P2 |
| A11 | `tools/verify_ladders_candidate.py`, command below | identity consistency and fresh startup source claim; success only `IDENTITY_MATCH_ONLY`, `gameplayVerified=false`, `binaryProvenanceVerified=false` |
| A12 | regression effectiveness | legal airborne/same-face cases and new mode/skill boundaries reject the old policy; preserve negative ownership/obstruction cases, no fake game stubs |

A05 is blocked without licensed local game dependencies. Never substitute A03
for A05 or make fake game stubs so an unusable plugin appears compiled.

These IDs group checks; they are not the execution order. Install the profile's
dependencies before binding build references. `package.ps1` rebuilds, so run A06
before the final A05 and capture a new verification log afterward. Compare the
ZIP-entry DLL SHA256 with the verified output. If they differ, repackage those
verified bytes with `tcli build` and `tools/reproducible_zip.py` without rebuilding.
The development deployment script refuses a DLL newer than its verification
log. That timestamp check alone does not prove embedded source identity or
requested behavior. A11 and the new R cases are additional required gates.

## 3. Set up one repeatable disposable test course

Use `TCC-Ladders-Dev` and a deliberately created test character and world. Do not
change the owner's world, character, inventory, progression, Steam Cloud or
global game installation. Manager profile isolation does not itself isolate
saves. If setting `-savedir`, verify current game/cloud behavior visibly.

In the disposable world, prepare:

- Flat workbench area with materials and a forge.
- A 2 m wall and platform; then a 4 m and 20 m tower.
- Parallel shafts and two opposite wall faces.
- A platform with an overhanging lip, low roof, closed door/hatch and railing.
- An intermediate landing, an unsupported end over a pit, and a water-side edge.
- A safe demolition area and an unload/reload route.
- The owner's two photographed reach approaches, measured in game; do not infer
  metres from pixels or treat the photos as proof of the installed DLL.
- A freestanding ladder with both faces clear and a side platform reachable by
  an upward, camera/movement-directed jump; then add a real wall on one face.

Test piece cost/progression first with ordinary resources. If temporary debug
tools are used to construct the rest of the test course, turn off no-cost,
no-damage, flight and other cheats before testing the behavior they would mask.
Record which convenience was used. No third-party gameplay mod is required.

## 4. Startup, identity and placement

| ID | Procedure | Required result |
|---|---|---|
| B01 | Cold Start modded into menu | expected new version/full source fingerprint loads once, no missing dependency or Harmony exception |
| B02 | Inspect all three hammer entries, hover and native repair text | exactly Vertical Wood Ladder / Vertical Copper Ladder / Vertical Iron Ladder in English (owner 2026-10-27), correct icon/recipe, no duplicate/donor icon |
| B03 | New character has not discovered Copper/Iron | native discovery/progression policy preserved; no spoiler unlock grant |
| B04 | Build wood with exactly four wood | cost once; station requirement applies |
| B05 | Build copper/iron with exact cost; repeat insufficient cost and missing forge | valid build succeeds; invalid builds do not consume or place |
| B06 | Place at cardinal and intermediate rotations; cycle every snap mode/point | normal ghost/rotation, readable localized labels/current bindings; no raw snap_ or $ token, model/collider aligned |
| B07 | Stack 2, then 10 sections | exact 2 m spacing; no visible gap or duplicate piece; native support remains meaningful |
| B08 | Stack mixed material sequence | same endpoints, no runtime discontinuity |
| B09 | Unsupported, obstructed and foreign-ward placement | native refusal/feedback; no bypass |
| B10 | Repair, dismantle, damage, destroy | health changes normally; resources/refunds exactly once |
| B11 | Force worn/damaged states by ordinary game mechanics | no returning donor wall/pole, no pink material, collision remains stable |
| B12 | Log out → world B → back to world A | no duplicate prefabs/menu entries or residual runtime state |

## 5. Single-player traversal: required before owner test

| ID | Procedure | Required result |
|---|---|---|
| T01 | Interact from centred bottom approach | finite smooth mount, correct facing, no instant jump to top |
| T02 | Approach obliquely/from offset and repeat photographed approaches | measured chest-to-rung band; hint/action agree; range refusal is truthful, no wall pull |
| T03 | Interact from each clear face; repeat with a wall, actor, terrain or excessive range | both clear faces succeed on their same-face lanes; true obstruction/range refuses with the right reason |
| T04 | Fresh Interact while rising/falling beside it, plus blocked/out-of-range attempts | legal catch succeeds without hoist; failed catch preserves velocity/fall state and spends no stamina/XP |
| T05 | Climb up, stop for 10 s, reverse, stop, resume | correct direction; no gravity drift, jitter or stale input |
| T06 | Cross every seam of 10-section mixed stack both ways | no detach, teleport, animation spike or truncated 4 m run |
| T07 | Top entry from a supported landing | smooth correct mount and descent |
| T08 | Intermediate landing entry and reachable low-rung airborne hang | legitimate entry allowed, no endpoint hoist or unrelated neighbor |
| T09 | Clear top with thin floor, thick lip and sloped/uneven approach | swept rise/cross transfer; no head/feet clipping |
| T10 | Solid ceiling directly over ladder | safe hold; descend and Jump still work |
| T11 | Destination empty but wall/closed hatch blocks path | no teleport through barrier; useful blocked message |
| T12 | Top landing has another character or obstacle | hold/recheck; no actor ghosting |
| T13 | Bottom at supported floor/slope | safe grounded exit without embedding or sudden drop |
| T14 | Bottom ends over open pit or water | hold without standing support; affordable Jump hops, then ordinary falling/swimming; exhausted Jump still releases |
| T15 | Interact halfway with no landing | stay attached; message; no involuntary freefall |
| T16 | Interact halfway at a supported landing | swept safe dismount; normal movement immediately resumes |
| T17 | Space/Jump low/high, neutral and toward a side platform, in every attached mode | visible upward/directed hop when affordable, one native payment; zero stamina still releases; 0.4 s plus fresh Use before remount |
| T18 | Turn camera upward/downward/180° during climb | forward still up; backward still down; no camera seizure |
| T19 | Remap movement, Run, Interact, Jump and Crouch; toggle sprint | all use current processed actions; no hardcoded E/W/Shift/Space-only path |
| T20 | Controller catch, analog normal/fast/slide, hold, exit, Jump and Drop | complete loop without keyboard, current hints/toggle/deadzone; record BLOCKED if no real controller |
| T21 | Open chat/console/map/inventory/menu while moving | intent pauses; no typing-driven motion, no stuck movement after close |
| T22 | Attack/dodge/stagger/knockback during approach or climb | defined safe refusal/release; no locked controls or duplicate combat action |
| T23 | Nearby parallel, reversed, gap and rotated ladder segments | no false joins, shortcuts or transfer to wrong shaft |

Record the revised traversal video specified in section 10, including T01,
T05, T06, T09, T11 and T15–T17. Still images cannot prove motion or safe release.

## 6. Interruption, persistence and compatibility

| ID | Procedure | Required result |
|---|---|---|
| L01 | Destroy/demolish the current or connecting ladder during climb | release or bounded safe state; normal gravity/control; no immortal session |
| L02 | Remove platform during multi-frame exit | transfer revalidates; no floating or invisible foothold |
| L03 | Add obstacle during exit | no movement through newly blocked path |
| L04 | Rotate/move supporting piece through a legitimate test facility | stale run is rejected; moving-platform support is not assumed |
| L05 | Die mid-climb, respawn | normal death/respawn/equipment/native skill loss or protection, no lingering gravity/pose/accounting |
| L06 | Legitimate teleport/attach takeover | ladder relinquishes control; no write back to old transform |
| L07 | Disable climb setting mid-climb | restore movement; custom prefabs and Climbing skill remain registered |
| L08 | Save/reload, zone unload/reload | ladder identities remain, no persisted session or double registration |
| L09 | Logout/world switch during climb | next world/character has clean motor and input state |
| L10 | Install Foreman with its ladder motor on in separate compatibility profile | intentional refusal/message, no competing motor; vanilla unrelated play works |
| L11 | Disable Foreman ladders, restart compatibility profile | verify current arbitration policy; don't claim compatible until observed |
| L12 | Normal stairs, doors, ships, jump, sprint, combat with no climb | vanilla behavior unchanged by idle hooks |

## 7. Multiplayer and dedicated server

Use legitimate available client/server installations. Do not synthesize a second
player and call that network evidence. If unavailable, mark BLOCKED and report
the limitation before release; solo tests cannot substitute.

| ID | Procedure | Required result |
|---|---|---|
| M01 | Host builds, catches, jumps, fast-climbs/slides while client observes | same pieces, face/mode and coherent movement/presentation; no observer stamina/XP |
| M02 | Client performs the same revised loop while host observes | client owns own body; no host-only behavior or remote accounting |
| M03 | Two players climb same run | independent state; no shared-anchor dragging |
| M04 | Opposite directions/occupied landing | safe collision/hold; neither player warped |
| M05 | Other player destroys active/next segment | correct replicated removal and cleanup |
| M06 | Late join and reconnect | persisted custom IDs/visuals; no stale sessions |
| M07 | Restart host/server and reload world | pieces survive, normal state on join |
| M08 | Missing mod or candidate version mismatch | clear compatibility rejection before incompatible world use |
| M09 | Dedicated server without graphics | registers pieces without icon-render failure; both clients behave |
| M10 | Network delay/brief loss/ownership change | no forced ownership, resource duplication or permanent suspension |

## 8. Presentation and performance

| ID | Procedure | Required result |
|---|---|---|
| P01 | Inspect all variants day/night/rain, near/far | readable silhouette and material, no absent state/pink shader |
| P02 | Record catch/normal/fast/slide/hold/reverse/exits, Jump, Drop and hammer work | custom animation meets active contract, or documented CUSTOM_ANIMATION_DEFERRED plus motion-linked tested walk fallback |
| P03 | Observe another player's animation | no local-only success disguised as multiplayer proof |
| P04 | Test equipment/stowing, normal animation after release/death | no persistent override; equipment inventory unchanged |
| P05 | 100+ pieces, nobody climbing | no per-frame world scans/log spam; compare same-base frame-time capture |
| P06 | Climb dense 20 m tower, repeat 20 cycles | bounded allocations and queries; no rising registry count/leak |
| P07 | Profile reload/change world ten times | no cumulative prefabs, hooks, event subscribers or stale segments |

No hard FPS figure is promised across different machines. Record a comparable
baseline, hardware and observation window. Fix actual sustained regressions.

## 9. Owner revision matrix

Each row needs candidate SHA/DLL hash, procedure, observed result and capture.
All start NOT RUN for this revision. A prior 0.1.1 PASS cannot populate them.

| ID | Required scenario and result |
|---|---|
| R01 | Reconcile actual local branch/dirty work, full build SHA, installed/package identity and active profile; preserve old candidate without treating it as the revision. |
| R02 | Recreate the reported upper-floor layout. Record geometry; a valid lip exits without Jump, a closed shaft still blocks. |
| R03 | Twenty up/onto-floor/walk-away/return/down loops across three materials, both clear faces and rotations; no snag, edge fall or precision timing. |
| R04 | Thin/thick lip, railing, intermediate landing, low roof and closed hatch: valid routes pass, true barriers hold; down/Use/Jump/Drop remain responsive. |
| R05 | Jump neutral and with camera-relative movement from mounting/hold/climb/exit; upward and intended horizontal departure, including onto a side platform; one native payment. |
| R06 | Jump during fast climb/slide and at zero stamina; real affordable hop or deliberate exhausted release, no retained motor/automatic regrab, ordinary falling. |
| R07 | All ladders enforce 0.4 s plus fresh Use after Jump or Drop; held Use, adjacent ladder, same-frame Jump/Drop/Use and remaps cannot double-dispatch. |
| R08 | Both freestanding faces mount/climb/exit on their own lanes. Wall blocks only its actual route; camera never flips sides. Do not solve rear routing by ignoring ladder colliders. |
| R09 | Reproduce the two range photos without inferring their metres. Measure chest-to-rung probes at 0.5/1.0/1.5 m and just outside the configured boundary, including offsets; hints agree with Use. |
| R10 | Deliberate rising/falling catches high/low preserve bounded alignment; legal low-rung hang works without feet-clamp hoisting or invisible extension. |
| R11 | Failed air grabs through wall, out of range or with a blocked capsule path leave velocity/fall history/stamina/XP unchanged. |
| R12 | Nearby door/chest/actor, another ladder and hammer mode preserve native priority, deterministic selection and one Use dispatch. |
| R13 | Ten-section mixed run on each face, reverse at every seam; reject gaps, overlaps and physically incompatible lanes. |
| R14 | Timed full-input normal/fast travel proves 1.8/2.43 m/s; stamina matches eligible distance/time, not held intent or frame count. |
| R15 | Exhaustion during fast/slide returns to normal/hold. Blocked/neutral/overlay states have no mod drain or XP. |
| R16 | Remapped Run/Use/Jump/Crouch/movement, real controller, native sprint toggle, device switch and analog deadzone; no keyboard-only inference. |
| R17 | Natural practice earns Climbing; native UI, readable original icon/name/description and notifications appear without synthetic initial XP. |
| R18 | Save, exit and reload preserve native learned level/within-level progress; other character is independent; one definition. Unawarded sub-metre carry need not persist across exit. |
| R19 | Native death loss and applicable protection/world settings observed; exact outcomes recorded, no fixed-percent assumption. |
| R20 | Learned 19.99/20.00/>20 changes down+Run from fast descent to slide exactly at 20; loss below 20 relocks subsequent slides. |
| R21 | Slide at 4.5 m/s brakes on neutral, returns to normal on Run release and reverses immediately; supported bottom exits, unsupported bottom holds. |
| R22 | No XP from hold, blockage, failed/correct catch, slide, mount/exit, fall, teleport or repeated input without travel; fractional normal/fast travel accumulates once on owner. |
| R23 | Different frame rates, long frame, short obstruction and pending-step release settle stamina/XP once; analog cost per metre stable, no old 1.8 speed clamp or double modifiers. |
| R24 | Cycle every snap point/mode: native-style localized Top/Bottom/corners and current key/controller hints; no snap_ or raw $ token. |
| R25 | Complete the native-animation baseline before replacement implementation; separate sampling/playback/render/capture rates and justify vanilla-like fluidity. Cross-check the written per-frame contact/body reference against actual footage. Rework the owner-rejected limb presentation under ANIMATION_REVIEW_2026_10_26.md. Actual-character evidence must prove fitted rung contacts, grounded mount, continuous travel/hold/reverse/fast/slide, accepted midair catch, supported top step and blocked-top hold, immediate Jump/Drop and hammer work on both faces, with equipment/cleanup intact. Original authored motion from the supplied GIF references is required for the selected implementation route; no Meshy acquisition or mannequin/GIF-only acceptance. An uninvolved reviewer assesses Codex-authored animation. A concrete inspected/attempted blocker plus CUSTOM_ANIMATION_DEFERRED and tested walking remains conditional; the rejected limb implementation cannot pass. |
| R26 | Jump/Drop/death/teleport/disabled climb/destruction/lost owner/logout and ten world/profile cycles restore presentation/control and clear pending accounting. |
| R27 | Real host/client each catch/jump/drop/build/fast-climb/slide while observed; coherent motion/presentation, no remote XP/stamina writes. |
| R28 | Real simultaneous/opposite-face users, occupied exit, remote ladder removal, late join/reconnect and stale-presentation cleanup. |
| R29 | Dedicated server and missing/mismatched candidate; complete original M matrix for release review. |
| R30 | Exact final ZIP imported in observed TCC-Ladders-Dev; one active own DLL/GUID, new version/full source startup and dependencies/assets; A11 identity chain passes. |
| R31 | Normal walking/jump/sprint/swimming/combat, stairs/doors/building, saved pieces, recipes/support/repair/refunds remain correct. |
| R32 | Independent reviewer checks final source against every requested behavior and exact package/evidence; no in-scope P0/P1, precise readiness verdict. |
| R33 | All three English names read Vertical Wood Ladder / Vertical Copper Ladder / Vertical Iron Ladder (owner 2026-10-27) in hammer, hover and repair text; saved IDs unchanged. |
| R34 | Building-block reason appears only for an actual other blocking Piece on the queried route. Own ladder, Piece behind clear face, distance, terrain, actor and missing support never falsely report it; real collision still blocks unsafe routes. |
| R35 | One final-version evidence set contains all six original-note demonstrations below, later Drop/scaffolding demonstrations and earlier sprint/skill/slide/snap/animation results; source and installed identity are tied to captures. |
| R36 | Fresh Crouch/Drop during mounting, hold, up/down/fast climb, slide, transfer and hammer use on both faces releases without hop, outward kick, landing requirement or release charge; repeat at zero stamina. |
| R37 | Remapped keyboard/controller Crouch, pre-held/toggled state, overlay closure and simultaneous Jump/Drop/Use: one intentional departure, no buffered drop or native sneak/world-use leakage; native crouch works off ladder. |
| R38 | Drop removes only ladder-commanded motion, settles preceding climb cost/progress once, restores animation/control and ordinary fall from release height; independent motion/ownership preserved, no fall XP, 0.4 s plus fresh Use guard across same/adjacent ladders. |
| R39 | Mount with/equip hammer, aim, choose/rotate/snap/place, repair/remove, hotbar and build/inventory menus while held; both faces/materials. Anchor remains still with no passive climb cost/XP; deliberate traversal resumes without putting the hammer away. |
| R40 | Hammer operations retain native range/material/station/ward/support/collision/placement/refund rules and execute once. Shared native snap/Use binding does not dismount; gameplay-focused Jump/Drop still works; no combat-wide busy exemption. |
| R41 | Build aligned same/mixed-material extensions while attached, then climb into the new run without remounting. Bounded refresh preserves face/anchor; gaps, ambiguous joins and parallel shafts do not create a shortcut. |
| R42 | Remove the active ladder and observe clean ordinary falling/control; unrelated removal leaves attachment intact. Changed geometry never leaves invisible cached support. Hammer/climb animation and equipment restore after actions, Jump/Drop, death and logout. |
| R43 | Real host/client scaffold building, extension, removal and Drop show native world changes and coherent held/hammer presentation; no remote motor/XP/stamina writes or duplicate actions. Required for release review; explicitly BLOCKED if no second endpoint. |

Boundary skill levels may be set by recorded disposable-course setup commands;
natural progression is tested separately. Turn off no-cost/infinite-stamina,
flight and damage cheats before observations they could mask. A single PNG
frame with a .gif filename is pose/layout evidence, not a motion recording.

## 10. Final identity check and demonstration

Freeze source and new numeric version, package under the existing build lock,
then run the final full verify. Compare the package DLL to the verified output;
if needed repackage those verified bytes without rebuilding. Import the exact
ZIP with the manager's **Import local mod from file**. An `.r2z` is the dependency
profile archive and must not be imported/renamed as the ladder mod.

Inspect the actual active profile. Remove only known duplicate own installs
through supported manager operations, or back up a known loose own DLL outside
all active plugin trees. Never hand-edit manager metadata or delete unrelated
profiles. Cold-start with Start modded and capture a fresh startup log showing
the intended version/full SHA. Do not replace DLLs after capturing evidence.

Resolve the actual Windows Downloads Known Folder through the operating system
(including redirection); do not assume a literal user-profile Downloads path.
Copy the tested ZIP there and compare its hash. Set these variables from real
observed files and the frozen build receipt, then run:

```powershell
python ./tools/verify_ladders_candidate.py `
    --package $ladderPackagePath `
    --verified-dll $ladderVerifiedDllPath `
    --plugins $ladderActivePluginsPath `
    --game-log $ladderFreshGameLogPath `
    --source-commit $ladderFullBuildSha `
    --version $ladderCandidateVersion `
    --downloads-copy $ladderDownloadsZipPath
```

The source argument is the full 40-character build SHA; version is >=0.2.0 and
newer than the old candidate. The checker is read-only and success reports
`IDENTITY_MATCH_ONLY` with `gameplayVerified=false` and
`binaryProvenanceVerified=false`. It does not compile, install, launch, parse
assembly metadata, attest compiler provenance or approve requested behavior.
Reject an old 0.1.1, mismatched startup source claim, stale/different DLL,
duplicate plugin or different Downloads ZIP. Require the controlled licensed
build record separately; a supplied log claim is not binary attestation.
The helper needs Python 3.11+ and currently accepts exactly the six standard
package files. Loose assets require extending the strict package/install/hash
contract and verifier first; separate hashes alone cannot make that check pass.
Record hashes in the receipt; a later docs-only commit can record the build SHA
and hashes without rebuilding or inventing a recursive hash.

Capture moving video tied to that startup/receipt, with six named segments:

1. **Reach:** reproduce both latest photo approaches; measure and show the
   inside/outside boundary, offsets and consistent hints/Use.
2. **Actual Space jump and floor access:** show an upward, camera/movement-directed
   hop onto a side platform, neutral and exhausted departure. Separately walk
   onto a valid upper floor without Jump and mount down; a closed roof still holds.
3. **Midair grabs:** show deliberate rising and falling catches and a failed
   blocked/out-of-reach grab continuing the original fall unchanged.
4. **Title Case:** show Wood Ladder, Copper Ladder and Iron Ladder in all three
   hammer, hover and repair contexts.
5. **Both faces and truthful blocking:** climb each clear face, then block one
   with a real other Piece. Test own ladder, behind-face Piece, range, terrain
   and actor cases; maintain collision without false building-specific errors.
6. **Cycle Snap:** cycle all native snap modes/points with readable localized
   Top/Bottom/corner labels and the actual current key/controller bindings.

Also capture timed normal/fast/slide movement, exhaustion, skill level 20 threshold/
relock, native snap text and actual animation or the documented fallback. Keep
full R-row observations; this short demonstration does not replace them.

Add two further final-version segments for the later owner requests:

7. **Drop:** contrast powered Jump with Crouch release; show zero stamina,
   fresh/remapped input, ordinary fall, cleanup and deliberate later regrab.
8. **Scaffolding:** hammer menu/snap/place/repair/remove while held, build an
   extension and climb it without remounting, then remove current support.
   Show normal costs/collision, no passive climb drain/XP, and intact equipment.

R43 is a real network row alongside R27–R29; unavailable endpoints are BLOCKED
for solo testing and required for release review. See the full addition in
[DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md](DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md).

## 11. Finish and retest rules

On a failure, record the test ID, exact source/DLL hash, repro and log/capture,
fix it, and rerun the failed row plus direct neighbors and lifecycle cleanup.
Do not repeatedly rerun every unrelated product unless an affected shared file
or required gate warrants it. Run the full mandatory gate once on the final
candidate and deploy exactly that build.

Ready for owner solo testing requires all applicable A01–A12, B01–B12,
T01–T23, L/P and non-network R01–R43 rows, current scope review, no in-scope
P0/P1 and exact importable package identity. Required controller evidence may
be BLOCKED only when a real controller is unavailable and that limitation is
explicit; complete every accessible solo case. Real network rows may likewise
be BLOCKED for solo testing, but release review requires the actual M/R network
matrix. Only custom animation can be DEFERRED after its feasibility/fallback
gate; the other requested mechanics cannot be deferred as optional polish.

Return READY_FOR_OWNER_SOLO_TEST, READY_FOR_RELEASE_REVIEW or NOT_READY with
failed/missing rows. A source review or IDENTITY_MATCH_ONLY cannot promote this
status. Never publish, tag or merge in this job. The final handoff includes
source/version/hashes, actual profile/start route and Downloads ZIP path,
recorded evidence, limitations and at most four owner preference checks.
