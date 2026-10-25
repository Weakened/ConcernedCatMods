# Concerned Ladders — observed status

Updated 2026-10-25 by the local integration lead (Claude Code on the owner's
machine). **Built against the installed game, deployed to its own profile and
played in a disposable world. Single-player only; multiplayer is BLOCKED.**
This file records evidence, not intended behavior; `PROJECT.md` defines that.
The full run log, screenshots and receipts are kept outside the repository in
the worktree's ignored `artifacts/ladders-run/`.

## Source, issues and environment

| Item | Observed |
|---|---|
| Baseline | `89ce8b68e05c4494e5d14165482eca8d2486c786` (= `origin/main` on 2026-10-25) |
| Supplied candidate | `25d392f`, imported from the handoff bundle (hash matched its manifest) |
| Branch | `feat/cl-001-vertical-ladders`, local; not pushed, no PR |
| Issues | CL-001 → #430 (pieces), CL-002 → #431 (traversal), CL-003 → #432 (profile); label `mod:ladders` |
| Game | Valheim **1.0.20** (network version 41), Steam build 25909357, Unity 6000.0.75 |
| Loader | BepInExPack Valheim **5.4.2351** (BepInEx 5.4.23.5), Jötunn **2.30.2** |
| Manager | Thunderstore Mod Manager 1.125.2 (Overwolf) |
| Profile | `%APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\profiles\TCC-Ladders-Dev`, imported from the generated `.r2z` through the manager's own Import / Update → From file flow; nine existing profiles untouched |
| Test save | new character `Laddertest` and world `TCCLadderTest` (both cloud saves; the desktop client has no `-savedir`) |

The final candidate commit is the one that contains this file; its DLL and
package hashes are recorded in the deployment receipt and the handoff report,
not here, because committing them would change the commit.

## Corrections made after the supplied candidate

Each was found by the real build or in game, fixed, and re-observed.

| Commit | Defect observed | Correction |
|---|---|---|
| e78113a | Did not compile on 1.0.20: `Hoverable.GetHoverOffset()` added to the interface | Implemented, returning the vanilla Ladder default 0 |
| 12bbebc | Review against the 1.0.20 sources: a successful `Interact` played the interact animation, whose minor-action state cancelled the new climb; `IsOnGround` stayed true through a climb so a jump push-off was overwritten | `Interact` returns false like the vanilla Ladder; a release that does not end standing starts airborne (vanilla `ForceJump` value) |
| fa272ca | First in-game mount refused on flat ground: the query capsule overlapped the floor it stood on | Query capsule floats 5 cm above the feet; refused mounts log one diagnostic line |
| 67ffcdd | The deploy script's backup of a replaced DLL inside `artifacts/` made the next `verify.ps1` fail | Backups go to `%LOCALAPPDATA%\TheConcernedCat\ConcernedLadders\profile-backups\` |
| 2dfeb4c | A ladder snapped to a wall on a slope could not be mounted from its open (downhill) face | Mount allows a 0.75 m step up to the foot (`MaximumStepUpToFoot`), boundary-tested |
| e9531d1 | A mount from terrain never finished (route arrival tolerance 2 cm vs ground contact 2.2 cm), so W did nothing; the bottom exit was unreachable for a standing player; corner snaps put the rails 9 cm inside a wood wall | Final route leg ends within the route's own lift; descending onto support within 0.5 m of the foot exits; stepping down onto ground below a raised foot; corner snaps set back 8 cm |
| 693eb47 | At a blocked top the crosshair rested on the blocking wall, so Use could not request a dismount; transfers slid in a standing pose | Use (the player's own `Use`/`JoyUse` binding) while climbing with nothing interactable hovered requests the safe dismount; transfer legs drive the walk cycle |
| 711e6c8 | With `ClimbingEnabled=false` a Use press reported a startup failure | Separate "turned off in settings" message |
| 7c7017c, cc3240e | (diagnostics) | One log line per mount, release and refused mount; event-driven, numbers only |
| freeze commit | Second review (P2): diagnostic numbers used the machine locale (unreadable in comma-decimal locales); lowering on a transfer leg animated forward | Invariant-culture formatting; transfer pose takes the sign of the vertical motion |

## Automated gates

| ID | Result | Evidence |
|---|---|---|
| A01 | PASS | `tools/validate_repo.py` exit 0 on every candidate |
| A02 | PASS at 12bbebc | `python -m unittest discover -s tools/tests`: 120 tests, OK (1 skipped), 74 min, separate worktree. Not rerun on later commits (they change no tool or validator code) |
| A03 | PASS | 35/35 (31 supplied + 4 new step-up boundary tests) |
| A05 | PASS on each deployed candidate | `scripts/verify.ps1` under the machine build lock, bound to TCC-Ladders-Dev: Release, 16 assemblies, 5222 passed / 0 failed, validator exit 0 |
| A07 | PASS | Profile generator twice: `created` then `unchanged`, SHA256 `e7acb27a…` (matches the supplied artifact), one entry `export.r2x` |
| A08 | PASS | `deploy-ladders-profile.ps1 -WhatIf`: `PLAN_ONLY`, no findings |
| A09 | PASS | Real deployments: `DEPLOYED_NOT_LAUNCHED`, own DLL hash matched, plugins folder holds only this DLL and Jötunn |
| A10 | Two independent read-only reviews | Findings verified against the decompiled 1.0.20 sources and fixed (see above) |

## In-game acceptance (single player, disposable world)

A dev-only test harness plugin (never committed or packaged; removed before
every gated deploy) teleported the player precisely, placed course pieces at
exact transforms and printed a read-only dump of the climb state. Course pieces
placed that way skip cost and station checks; cost, station and discovery were
tested separately with the hammer. "Released" lines are the mod's own log.

| ID | Result | Observation |
|---|---|---|
| B01 | PASS | Cold start: Jötunn and Concerned Ladders load once, `Chainloader … 0 failed`, all three prefabs registered, 0 error lines |
| B02 | PASS | Three hammer entries with rendered icons; names, costs and stations as specified |
| B03 | PASS | Before a forge only the wood ladder is listed; building a forge shows native "New build piece: Copper ladder" and lists copper and iron |
| B04 | PASS | Wood ladder costs exactly 4 wood (33 → 29) at a workbench |
| B05 | PASS | Copper 2+1 and iron 2+1 charged exactly; insufficient copper and no forge → native "Missing requirement", nothing consumed |
| B06 | PASS (with note) | Ghost, rotation and the six snaps work; corner snaps were corrected after the first wall placement |
| B07, B08 | PASS | 10 sections (wood, wood, then alternating copper/iron) at exact 2 m pitch stand under native support |
| B09 | NOT RUN | Unsupported/obstructed/ward placement |
| B10 | PARTIAL | Hammer removal refunds exactly once (iron 2→4, wood 16→17); damage/repair not run. Initial health wood 200, copper 400, iron 800 |
| B11 | NOT RUN | Forced worn/damaged visuals |
| B12 | PARTIAL | Repeated cold starts and menu → world re-entries never duplicated registration; a second world was not visited |
| T01 | PASS | Mount from the front: "Ladder mounted: 1 section(s), 2.00 m run" |
| T03 | PASS | From behind: "The route onto the ladder is blocked." |
| T04 | PASS | Jump then Use beside the ladder: no midair catch |
| T05 | PASS | 1.88 m/s; 10 s neutral hold drifts 0.000 m; reversal symmetric |
| T06 | PASS | 10-section mixed run, 20 m, every seam both ways, no detach |
| T07 | PASS | Top entry from a landing: "entering at 2.00 m" |
| T08 | PASS | Entry from a side landing at +2.1 m on a 4 m run (the lateral reach band is narrow) |
| T09 | PASS | Top exit rises over the floor lip and crosses onto the floor |
| T10 | PASS | Solid ceiling: holds with "The ladder route is blocked…"; descend and Jump still work |
| T11 | PASS | Wall on the landing: holds with "There is no clear landing at the top…" |
| T12 | PARTIAL | A boar on the landing attacked during the exit → safe external release; a stationary blocking actor was not observed |
| T13 | PASS | Descent to the ground releases standing, not embedded |
| T14 | PASS | Foot 6 m above ground: holds with "There is no footing below the ladder…" |
| T15 | PASS | Use midway with no landing: "There is no safe landing within reach…", stays attached |
| T16 | PASS | Use at a side landing (looking at the sky) steps onto it; a landing whose edge overhangs the climb line correctly refuses |
| T17 | PASS | Jump from 6 m: ordinary fall, health 25 → 16 (no immunity) |
| T18 | PASS | Camera turned 180° or pitched up: W still climbs, S still descends |
| T19 | NOT RUN | Remapped keys (code reads the player's processed movement and the `Use`/`JoyUse` bindings) |
| T20 | NOT RUN | No controller connected |
| T21 | PASS | Inventory or Esc menu while holding W: no motion |
| T22 | PASS | Attacking while climbing: safe release, normal control after |
| T23 | PASS | Parallel neighbour 0.85 m away and a reversed section on top are not joined |
| L01 | PASS | Destroying the climbed section releases into an ordinary fall |
| L02 | PASS | Destroying the landing mid-exit releases; no floating |
| L03 | PASS | A wall placed across a paused exit path stops the body at its face |
| L04 | PASS | Moving the climbed section releases |
| L05 | PASS | Death mid-climb: external release; respawn and controls normal |
| L06 | PASS | Teleport mid-climb: external release, nothing written back |
| L07 | PASS | `ClimbingEnabled=false` mid-climb releases; pieces stay registered and hoverable; re-enable works |
| L08 | PASS | Pieces persist across save, quit and cold relaunch (many times) |
| L09 | PASS | Log out mid-climb: external release; re-entry has a clean motor |
| L10, L11 | NOT RUN | Real Foreman in a separate compatibility profile. The detector's owner id and method were checked against Foreman's source and exercised in game with a simulated owner (see the run log) |
| L12 | PARTIAL | Walking, sprinting, jumping and combat after leaving; stairs, doors and ships not exercised |
| M01–M10 | BLOCKED | No second client or dedicated server is available on this machine |
| P02 | PASS (fallback) | Walk cycle plays while moving and stops while holding; no stuck pose after release |
| P01, P03–P07 | NOT RUN | Lighting/weather review, observer view, performance captures |

## Known limitations

- Multiplayer and dedicated-server behavior are unobserved.
- Real Foreman coexistence is unobserved.
- The lateral entry band from a side landing is narrow (side offset ≤ 0.7 m).
- A ladder placed against a wall before e9531d1 sits 8 cm closer to the wall than one corner-snapped
  now, so the two do not join into one run. Only development worlds can contain such pieces.
- Visual art and the walking fallback are a functional first pass, not owner-approved.
- Do not publish, tag or merge on this evidence alone.
