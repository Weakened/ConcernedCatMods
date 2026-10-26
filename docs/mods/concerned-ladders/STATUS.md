# Concerned Ladders — current readiness

**NOT_READY — 0.2.6 internal owner candidate awaiting owner review.** Updated 2026-10-26.

| Item | 0.2.6 |
|---|---|
| Source | `4fe029cefe705de8fef23e1abc64fbb177fa1519` (`feat/cl-001-vertical-ladders`) |
| Verify | `scripts/verify.ps1` PASSED: 16 assemblies, 5353 tests, validator exit 0 (`artifacts/ladders-0.2.6/verify-4fe029c.log`) |
| ZIP | `TheConcernedCat-ConcernedLadders-0.2.6.zip`, SHA256 `4a1de02524abcb89bcd3bc7267ed5b89bae2217a3229a09e5d4afc6fb3f0b4fc` (artifacts and Downloads, identical) |
| DLL | `d04f38cc3cf658fb4f55a02f3f1954bd1b6a02149f5181730b763e745bcdd0eb` (verified = packaged = installed) |
| Access check | 74 game member references, 0 inaccessible |
| Install | Manager "Import local mod", TCC-Ladders-Dev; cold start logs `0.2.6+4fe029c…` and gait source `LadderGait.blend 4393031837c3` |
| A11 | `IDENTITY_MATCH_ONLY` (`artifacts/ladders-0.2.6/A11-identity-receipt-0.2.6.json`) |

**What changed for the owner's 2026-10-26 corrections**

- **Snapping redone** (owner decision): Cycle Snap offers Auto, Bottom and Top. Aimed at a floor, the ladder centres on
  the nearest floor edge (hangs below it for Auto/Top, stands on it for Bottom); aimed at a wall, it centres on the face
  toward the player (stands on the bottom edge for Auto/Bottom, hangs from the top edge for Top); aimed at a ladder, it
  extends the column. Wall-corner modes removed. Game reach and placement checks still apply.
- **Climb speed** (owner decision): 0.9 m/s, 1.2 m/s with Run (were 1.8/2.43); slide, stamina and skill unchanged.
- **Animation**: independent per-limb timing from the mannequin loop, knees into the ladder, torso dwell during foot
  recovery, staggered mount and hands-first catch, rail grip and game walk for the top exit, right-arm-only hammer.
  Authored in `AnimationSources/LadderGait.blend` with an export/compile pipeline; Blender-to-game round trip proven
  (`artifacts/ladders-0.2.6-dev/BLENDER_ROUND_TRIP_RECEIPT.md`). Guide: `src/ConcernedLadders/AnimationSources/README.md`.

**Recordings (normal speed, this exact install)** in `artifacts/ladders-0.2.6/recordings/`: R1a floor snap, R1b wall
snap, R1c column extension, R2 front/side climb, R3a hold/stop/reverse, R3b rear face, R4a fast + Jump, R4b midair
catch + Drop, R4c top exit, R5 building a floor while attached (right arm only). Each has a per-frame trace CSV.

**Known remaining issues (measured, not hidden)**

- Mount: the right hand is out of reach for the first ~0.1 s while the body walks in (3–7 skipped passes), then fits.
- Top finish: hands are out of reach for the first part of the finish (skipped, never stretched) before handing to the
  game's walk.
- Fingers keep the native pose; other players see the native pose; controller and multiplayer rows remain BLOCKED.
- Owner visual acceptance of the motion is outstanding.

## History before 0.2.6

**NOT_READY.** The installed candidate **0.2.1** implements owner notes N01–N06, Run/stamina, the native Climbing
skill and the level-20 slide. Most accessible solo rows below were observed in game. Three things keep it
NOT_READY:

1. **Animation.** The owner rejected the procedural climbing presentation shipped in 0.2.0 and 0.2.1. Original
   replacement animation is assigned to Codex and is not delivered yet. The native animation baseline is **accepted
   for authoring**. Codex's uninvolved reviewer accepted it after the owner authorised the frame-and-timing review
   (`CODEX_ANIMATION_INTERFACE_REPLY_2026_10_26.md`, animation-lane commits `cfd7fa9`, `d3f423f`). This supersedes the
   earlier BLOCKED disposition. Still outstanding: actual repair gameplay, rig/contact fitting, and normal-speed fluidity approval on the integrated candidate.
2. **Drop and scaffolding (N07/N08).** Implemented in `075f14b`. The Codex reviews so far:
   - `075f14b`: NO_GO, 6 P1s.
   - `85713d6`: NO_GO, with P1-06 (Drop velocity) partial, a pending-Drop focus race, hammer IK overlap, and the hammer-mount gap.
   - `d913e26`: NO_GO, hammer IK at action onset.
   - `2c810ab`: NO_GO, a cached `InAttack` could miss onset.
   - `c32e6b5`: reads the animator's state tags at the IK pass. **GO** (`CODEX_REVIEW_93119e7.md`).
   - `125c9b0`: owner decision 2026-10-26. Mounting with the hammer equipped is allowed, and only the hammer (right) arm animates natively while the body keeps its climbing pose. `d4c0017` review: NO_GO on a 16-hit buffer, fixed in `38964c8` (64 hits, saturated ray refused). **GO** (`CODEX_REVIEW_2bef0d9.md`), with STATUS ACCEPTED.

   **Mounting with the hammer equipped is implemented in `125c9b0` (owner decision 2026-10-26), not yet run in game.** The build and policy-test logs for `d913e26`, `2c810ab` and `c32e6b5` (127 passed each, 0 build errors) are in `artifacts/ladders-0.3-dev/`; `125c9b0` passes 128 tests (logs in the same folder). None of it has been packaged or run in game.
3. **Controller and network.** Every controller row and every multiplayer/observer row is BLOCKED: no
   controller and no second endpoint were available.

Updated 2026-10-26. Active contract: [PROJECT.md](PROJECT.md), [OWNER_REVISION_2026_10_25.md](OWNER_REVISION_2026_10_25.md)
and [DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md](DROP_AND_SCAFFOLDING_ADDENDUM_2026_10_25.md). The 0.1.1
record stays in [STATUS_0_1_1_HISTORICAL.md](STATUS_0_1_1_HISTORICAL.md); none of its results are carried over.

## Candidate identity

| Item | 0.2.1 (installed now) | 0.2.0 (where most rows ran) |
|---|---|---|
| Build source | `441ea830785a27920f4bad11c01f877ed94b7d28`, clean tree | `678f65a4788c8f9eb6a62b4da0e3463b01dd3730` |
| `scripts/verify.ps1` | PASSED: 5281 passed / 0 failed, validator 0 (`verify-final-441ea83*.log`) | PASSED: 5281 / 0, 16 assemblies, validator 0 |
| ZIP | 130,806 B, SHA256 `01e3a766a104e4a680182891a7cc20c52375e29f3f8fada689b208ff015e7837` | 129,947 B, `343a2f6557018447202dcd1ad5bcc5c9897dc9c066c21fc9c41c489c2a372625` |
| DLL | `287b825ad7cc97f94bfe95c47feccc3096627d616a657c148c29591013110f19` | `f17bc541d2046b9695e416c6059a80070679a627a41f5dc05631f44caad69a68` |
| Manager import | TCC-Ladders-Dev, Import local mod after uninstalling 0.2.0 (`INSTALL-1/2-*.png`) | Import local mod (`INSTALL-1-import-dialog-0.2.0.png`) |
| Cold start | Banner `0.2.1+441ea83…`, 0 failed, three prefabs. The first start showed a stale "Loading [… 0.2.0]" from BepInEx's type-loader cache; it was correct once the cache was cleared (`LogOutput-coldstart-0.2.1*.log`). | Banner `0.2.0+678f65a…`, 2 loaded / 0 failed |
| A11 | `IDENTITY_MATCH_ONLY` (`gameplayVerified=false`, `binaryProvenanceVerified=false`) | same |
| Downloads | `C:\Users\erenc\Downloads\TheConcernedCat-ConcernedLadders-0.2.1.zip`, same SHA256 | 0.2.0 ZIP, same SHA256 |

**What 0.2.1 changes from 0.2.0.** Only two things:
- the mid-ladder Use side landing (T16), which now probes far enough sideways to clear the rails;
- the README reach wording.

Rows marked 0.2.0 ran on DLL `f17bc541`. T16 was found on 0.2.0, and its fix is **not yet re-observed** on 0.2.1.

Evidence is ignored and local, in `artifacts/ladders-0.2.0/` and `artifacts/ladders-0.2.1/`. The measurement log
`CANDIDATE-measurements-f17bc541.log` holds the timed rows. A dev-only harness was loaded for course setup:
teleport and aim, read-only state, and setting skill level and stamina. It patches nothing in the mod.

## Observed on the installed candidate

| Row(s) | Build | Observed | Evidence |
|---|---|---|---|
| N01 reach (R09–R11) | 0.2.0 | Chest-to-rung reach of 1.5 m, with the hover prompt agreeing with Use. The owner's photographed approaches mount. Boundary: at 1.44 m the **prompt** accepts (`promptInReach=True`); a matching mount at that distance is not separately recorded. At 1.59 m it is refused. | gameplay session log |
| R14 speeds and stamina | 0.2.0 | Normal 1.80 m/s at 0 stamina/s; fast 2.43 m/s at 5.6–5.8/s | measurements log |
| R15 exhaustion | 0.2.0 | Natural drain to 0 falls back to normal; Jump at 0 stamina is a push (2 × `Push from Climbing (Normal)`) | measurements and session logs |
| R20/R21 slide | 0.2.0 | Learned 19.99: Run+S is a fast descent at 2.43. Learned 20.00: slide at 4.50 m/s, 1.8–2.0/s. Releasing Run during a slide returns to normal descent. | measurements log |
| R23 frame rate | 0.2.0 | Fast climb still 2.43 m/s at measured 30.0 and 141.5 fps | measurements log |
| R05–R07 Jump | 0.2.0 | Jump from Mounting, Hold, Fast and Slide: native 8.1 m/s up, 5.3 m/s away. Held Use after a jump does not regrab. The stamina payment per mode was **not** measured before and after. | measurements log |
| R17/R18 skill | 0.2.0 | A fresh character went 0 → level 1 from natural climbing (accumulator 0.75) and kept it across relog. A new character starts at 0. Native death loss: 19.38, set by a setup command, became 18.411 after death and respawn. Only that one step is evidenced. | measurements and session logs |
| R03 floor loops | 0.2.0 | Up, step off onto the floor, back on, down. The procedure **teleported the character between the walk-off and the remount**, so these are traversal loops, not walk-away/return loops. Counts: 20/20 wall tower front face; 5/5 front and 5/5 rear on the slot ladder; 5/5 on a rotated copper/iron tower. | `R03-*loops*.log` |
| R13 seams | 0.2.0 | PARTIAL. On both faces the 10-section, 20 m mixed run was traversed from bottom to top (feet at 49.99/50.12) and back to the floor. The scripted reversal past each of the 9 seams is not logged seam by seam. Invalid joins: a section 0.15 m above the run stays out of it (the run stops at 3 sections, 6 m); a parallel ladder 0.9 m aside mounts as its own 1-section run. | measurements log ("R13 …", "T23/R13 invalid joins") |
| R34 terrain | 0.2.0 | A real terrain mound refuses with "The ground is in the way." | `R34-terrain-refusal.png` |
| N04 names | 0.2.0 | PARTIAL. Sampled video frames show the names in the hammer menu: Wood Ladder at about 1:36 and Copper Ladder at about 1:42, and the Iron Ladder hover at about 2:06–2:12. Times are from a 6-second contact sheet. The repair calls in the retained log target only the Wood Ladder and do not record the text shown. Copper/Iron repair text is not evidenced. | `artifacts/ladders-0.2.0/VIDEO-0.2.0-six-owner-notes-678f65a.mp4` |
| N06 snap labels | 0.2.0 | PARTIAL. The video shows the native "Snapping: Bottom left corner" message at about 2:06–2:12 (6-second contact sheet). A full both-direction cycle is not retained frame by frame. | `artifacts/ladders-0.2.0/VIDEO-0.2.0-six-owner-notes-678f65a.mp4` |
| B03–B05, B09–B11 pieces | 0.2.0 | B03 discovery: wood only for a fresh character, copper after copper plus a forge, iron after iron. B04/B05: exact costs, and refusals without material or station. B09: blocked by a player, and by a foreign ward. B10: exact refunds. B11: worn and broken visuals. | `B*.png`, B03-B10 session log |
| T14, T18, T21, L02, L04 | 0.2.0 | Water releases to swimming. A camera turn and an inventory hold keep the climb. Removing the floor mid-exit releases safely. A nudged ladder releases. | T14…B11 session log |
| L10/L11 Foreman | 0.2.0 | With real Foreman 0.1.0 installed, its refusal message shows (L10). With Foreman's ladders off, our climb works (L11). | `L10/L11-*.png`, compat logs |
| P05 load | 0.2.0 | Frame time 8.33 → 8.44 ms mean with 100 extra ladders | measurements log |
| P06 stability | 0.2.0 | PARTIAL. 20 fast up/down cycles on a **7-section, 14 m** mixed tower: piece count stable (181 before and after), 0 errors. After a reload, native structural support broke the top three freestanding sections, so the dense 20 m run remains open. | measurements log |
| P07 / R26 world switches | 0.2.0 | 10 world switches: no re-registration, 0 errors, registry stable. Prefab and hammer counts stay stable, and 0 climbers are sampled. Heap growth reproduces **without** the mod and is similar in size: forced GC 304.7 → 764.4 MB (+459.7) without it, against 325.7 → 784.0 MB (+458.3) with it. That supports native origin; it does not prove the mod contributes nothing. The harness's `segments=-1` is not a measured segment count. | `P07-*.log` |
| A02 / A07 | tools at `e400292` | Python suite: 138 tests, 2 skipped, clean checkout. The profile generator is byte-identical across two runs. | `a02-python-suite-e400292.log`, `*.r2z` |

## Open rows

| Row(s) | State | Reason and next action |
|---|---|---|
| R25, P02–P04 original animation | NOT DELIVERED | The owner rejected the procedural look. Codex is authoring original motion against the reviewed native baseline (`C:\code\cc-integration\cl-ladders-review-evidence\native-baseline\`). The lead integrates it through `IClimbPresenter`. |
| R36–R42 Drop and scaffolding | IMPLEMENTED (code review pending), NOT RUN | `075f14b` → `85713d6` → `d913e26` → `2c810ab` → `c32e6b5` (GO) → `125c9b0`. Needs a packaged candidate and game runs. |
| R39 mount with the hammer equipped | IMPLEMENTED (owner decision 2026-10-26), NOT RUN | `125c9b0` + `38964c8`: in build mode a Use press repeats the native hover test (the game's 64-hit buffer; a saturated ray is refused) and mounts. The snap-cycle on the same key still happens. Only the hammer arm animates natively. |
| R05 Jump during exit/transfer; R20 slide relocking after a death drops the level below 20 | NOT OBSERVED | Retained jumps cover Mounting, Hold, Fast and Slide only. The death record ends at 18.411, with no slide attempt after it. |
| R13 reversal at each seam | PARTIAL | The scripted reversals were not logged seam by seam. |
| T16, T13, T22 on 0.2.1 | NOT RUN | The fix was not re-observed in game after packaging. |
| R22 negative XP matrix; R23 long-frame, obstruction and pending-step settlement; R19 protection and world-setting cases | NOT EVIDENCED | Not run, or not retained, on this revision |
| P06 dense 20 m run | OPEN | The 20 m tower lost sections to native support after a reload |
| N04 copper/iron repair text, N06 full cycle, Jump stamina per mode | OPEN | Needs a visual or measured record |
| R03 walk-away/return | PARTIAL | Run with a teleport between walk-off and remount; a pure walking loop is still to do. |
| R16/T19 keyboard remap | NOT EVIDENCED | A Run remap was tried during the 0.2.0 session, but no log or capture of it was kept; re-run it on the next candidate |
| T20 controller | BLOCKED | No physical controller |
| P03, M01–M10, R27–R29, R43 | BLOCKED | No second client or dedicated server. Observer presentation is unverified. |
| Six-segment video | Recorded on 0.2.0 (2:23) | Must be re-recorded on the final candidate, with Drop and scaffolding demonstrations added |

## Known packaging issue (reported, not fixed here)

The reproducible ZIP stamps 1980 timestamps on its files. BepInEx caches plugin metadata by path and timestamp, so
upgrading in place from 0.2.0 showed the old version string until `chainloader_typeloader.dat` was cleared. The
loaded DLL was correct throughout.

## Reviews

Codex read-only reviews: `da8c5a2`, `27528f0`, `4926903` and `7726fe3` were NO_GO, each fixed. `b3c20a6` and
`441ea83` were GO. The 0.2.0 STATUS draft was REJECTED for overclaiming; this rewrite answers it row by row.
Reviews of the N07/N08 code: `075f14b` NO_GO (6 P1s); `85713d6` NO_GO (4 findings); `d913e26` NO_GO (hammer IK onset); `2c810ab` NO_GO (cached `InAttack`); `c32e6b5` GO; `125c9b0` NO_GO (hover buffer); `38964c8` **GO**. This STATUS was ACCEPTED at `2bef0d9`. The first version of this STATUS (`866a800`) was REJECTED with 7 must-fixes, each corrected here. Reports are in `C:\code\cc-integration\cl-ladders-review-evidence\`.

No publication, release tag or main merge.
