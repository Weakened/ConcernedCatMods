# Concerned Ladders — current readiness

**NOT_READY.** The installed candidate **0.2.1** implements owner notes N01–N06, Run/stamina, the native Climbing
skill and the level-20 slide. Most accessible solo rows below were observed in game. Three things keep it
NOT_READY:

1. **Animation.** The owner rejected the procedural climbing presentation shipped in 0.2.0 and 0.2.1. Original
   replacement animation is assigned to Codex and is not delivered yet. The native animation baseline it must
   follow is complete and independently reviewed (2026-10-26).
2. **Drop and scaffolding (N07/N08).** Implemented in `075f14b`, which builds and passes its tests. It has
   **not** been packaged or run in game.
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
| N01 reach (R09–R11) | 0.2.0 | Chest-to-rung reach of 1.5 m, with the hover prompt agreeing with Use. The owner's photographed approaches mount. The boundary is 1.44 m mounts, 1.59 m refused. | gameplay session log |
| R14 speeds and stamina | 0.2.0 | Normal 1.80 m/s at 0 stamina/s; fast 2.43 m/s at 5.6–5.8/s | measurements log |
| R15 exhaustion | 0.2.0 | Natural drain to 0 falls back to normal; Jump at 0 stamina is a push (2 × `Push from Climbing (Normal)`) | measurements and session logs |
| R20/R21 slide | 0.2.0 | Learned 19.99: Run+S is a fast descent at 2.43. Learned 20.00: slide at 4.50 m/s, 1.8–2.0/s. Releasing Run during a slide returns to normal descent. | measurements log |
| R23 frame rate | 0.2.0 | Fast climb still 2.43 m/s at measured 30.0 and 141.5 fps | measurements log |
| R05–R07 Jump | 0.2.0 | Jump from Mounting, Hold, Fast and Slide: native 8.1 m/s up, 5.3 m/s away, with one stamina payment. Held Use after a jump does not regrab. | measurements log |
| R17/R18 skill | 0.2.0 | A fresh character went 0 → level 1 from natural climbing (accumulator 0.75) and kept it across relog. A new character starts at 0. Native death loss 20.40 → 19.38 → 18.41. | measurements and session logs |
| R03 floor loops | 0.2.0 | Up, step off onto the floor, back on, down. The procedure **teleported the character between the walk-off and the remount**, so these are traversal loops, not walk-away/return loops. Counts: 20/20 wall tower front face; 5/5 front and 5/5 rear on the slot ladder; 5/5 on a rotated copper/iron tower. | `R03-*loops*.log` |
| R13 seams | 0.2.0 | Reversing at every seam works on both faces of a 14 m mixed run. A gap or a parallel ladder does not join. | R03-R13 session log |
| R34 terrain | 0.2.0 | A real terrain mound refuses with "The ground is in the way." | `R34-terrain-refusal.png` |
| N04 names | 0.2.0 | Wood, Copper and Iron Ladder in the hammer menu, hover and native repair text | session logs |
| N06 snap labels | 0.2.0 | Native Cycle Snap shows Top, Bottom and four corner labels in both directions | session log |
| B03–B05, B09–B11 pieces | 0.2.0 | B03 discovery: wood only for a fresh character, copper after copper plus a forge, iron after iron. B04/B05: exact costs, and refusals without material or station. B09: blocked by a player, and by a foreign ward. B10: exact refunds. B11: worn and broken visuals. | `B*.png`, B03-B10 session log |
| T14, T18, T21, L02, L04 | 0.2.0 | Water releases to swimming. A camera turn and an inventory hold keep the climb. Removing the floor mid-exit releases safely. A nudged ladder releases. | T14…B11 session log |
| L10/L11 Foreman | 0.2.0 | With real Foreman 0.1.0 installed, its refusal message shows (L10). With Foreman's ladders off, our climb works (L11). | `L10/L11-*.png`, compat logs |
| P05 load | 0.2.0 | Frame time 8.33 → 8.44 ms mean with 100 extra ladders | measurements log |
| P06 stability | 0.2.0 | 20 fast up/down cycles on a 20 m mixed tower: pieces stable, 0 errors | measurements log |
| P07 / R26 world switches | 0.2.0 | 10 world switches: no re-registration, 0 errors, registry stable. Managed heap grows by the same amount **without** the mod (forced GC: 305 → 764 MB over six switches, against 326 → 784 MB with it), so the growth is not caused by the mod. | `P07-*.log` |
| A02 / A07 | tools at `e400292` | Python suite: 138 tests, 2 skipped, clean checkout. The profile generator is byte-identical across two runs. | `a02-python-suite-e400292.log`, `*.r2z` |

## Open rows

| Row(s) | State | Reason and next action |
|---|---|---|
| R25, P02–P04 original animation | NOT DELIVERED | The owner rejected the procedural look. Codex is authoring original motion against the reviewed native baseline (`C:\code\cc-integration\cl-ladders-review-evidence\native-baseline\`). The lead integrates it through `IClimbPresenter`. |
| R36–R42 Drop and scaffolding | IMPLEMENTED, NOT RUN | `075f14b`. Needs a packaged candidate and game runs. |
| T16, T13, T22 on 0.2.1 | NOT RUN | The fix was not re-observed in game after packaging. |
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
`075f14b` is under review. Reports are in `C:\code\cc-integration\cl-ladders-review-evidence\`.

No publication, release tag or main merge.
