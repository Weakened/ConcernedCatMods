# Historical evidence: Concerned Ladders 0.1.1

**SUPERSEDED ACCEPTANCE CONTRACT — NOT CURRENT READINESS.**

The record below is preserved verbatim from PR #433 head
`ce6bb7f7ac7b9862b35090baeed61b8ece39a825`. Its tested product source was
`7e4e7357359ce9329e4274aec186740cddfea87e`; the later commit changed only
`STATUS.md`. It reports the previous supported-mount, single-face candidate.
Its PASS rows and review verdicts do not accept the owner's revised gameplay.
In particular, refusing the rear face and airborne grabs is obsolete behavior.
Literal old UI strings and historical version numbers below remain evidence,
not the display-name or release contract for the next candidate.

Use [STATUS.md](STATUS.md) for current readiness,
[PROJECT.md](PROJECT.md) for the active contract, and
[TEST_PLAN.md](TEST_PLAN.md) for required observations. No profile, current
installed DLL or package identity was reverified by creating this archive.

---

# Concerned Ladders — observed status

Updated 2026-10-25 by the local integration lead (Claude Code on the owner's
machine). **0.1.1 owner-test candidate: built against the installed game,
packaged, imported through Thunderstore's "Import local mod from file" and
played in disposable worlds. Single-player only; multiplayer is BLOCKED and
controller play is BLOCKED (no controller).** This file records evidence, not
intended behavior; `PROJECT.md` defines that. Run logs, screenshots, receipts
and clips are kept outside the repository in the worktree's ignored
`artifacts/ladders-run/`.

## Source, issues and environment

| Item | Observed |
|---|---|
| Baseline | `89ce8b68e05c4494e5d14165482eca8d2486c786` (= `origin/main` on 2026-10-25) |
| Supplied candidate | `25d392f`, imported from the handoff bundle (hash matched its manifest) |
| Tested build source | `7e4e7357359ce9329e4274aec186740cddfea87e` (the commit the packaged DLL was built from; its informational version carries it) |
| Documentation | this file was committed after testing, on top of the tested source; it changes no product file |
| Branch | `feat/cl-001-vertical-ladders` |
| Issues | CL-001 → #430 (pieces), CL-002 → #431 (traversal), CL-003 → #432 (profile); label `mod:ladders` |
| Game | Valheim **1.0.20** (network version 41), Steam build 25909357, Unity 6000.0.75 |
| Loader | BepInExPack Valheim **5.4.2351** (BepInEx 5.4.23.5), Jötunn **2.30.2** |
| Manager | Thunderstore Mod Manager 1.125.2 (Overwolf) |
| Profile | `%APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\profiles\TCC-Ladders-Dev`; nine other profiles untouched |
| Test saves | character `Laddertest`, worlds `TCCLadderTest` and `TCCLadderTestB` (cloud saves; the desktop client has no `-savedir`) |
| Reviews | Codex (independent, read-only): 25d392f→3383a3e, eca4a83, eca4a83→601392f, 601392f→4c4892c, 4c4892c→7e4e735, each `NO P0/P1`; package inspections of both 0.1.1 ZIPs `ACCEPT` |

Package and DLL hashes are recorded in the deployment/import receipts and the
handoff report, not here, because committing them would change the commit.

## Corrections made after the supplied candidate

Each was found by the real build, an independent review or in game, fixed, and re-observed.

| Commit | Defect observed | Correction |
|---|---|---|
| e78113a | Did not compile on 1.0.20: `Hoverable.GetHoverOffset()` added to the interface | Implemented, returning the vanilla Ladder default 0 |
| 12bbebc | A successful `Interact` played the interact animation, whose minor-action state cancelled the new climb; a jump push-off was overwritten | `Interact` returns false like the vanilla Ladder; a release that does not end standing starts airborne |
| fa272ca | First in-game mount refused on flat ground: the query capsule overlapped the floor | Query capsule floats 5 cm above the feet; refused mounts log one diagnostic line |
| 67ffcdd | The deploy script's backup inside `artifacts/` made the next `verify.ps1` fail | Backups go to `%LOCALAPPDATA%\TheConcernedCat\ConcernedLadders\profile-backups\` |
| 2dfeb4c | A ladder on a slope could not be mounted from its downhill face | Mount allows a 0.75 m step up to the foot, boundary-tested |
| e9531d1 | A mount from terrain never finished, so W did nothing; the bottom exit was unreachable; corner snaps put the rails 9 cm inside a wood wall | Final route leg ends within the route's lift; descending onto support near the foot exits; step-down onto ground below a raised foot; corner snaps set back 8 cm |
| 693eb47 | At a blocked top Use could not request a dismount (crosshair on the wall); transfers slid in a standing pose | Use while climbing with nothing interactable hovered requests the safe dismount; transfers drive the walk cycle |
| 711e6c8 | With `ClimbingEnabled=false` a Use press reported a startup failure | Separate "turned off in settings" message |
| 114ea75 | Diagnostics used the machine locale; lowering on a transfer leg animated forward | Invariant culture; pose follows the vertical sign |
| 28aff52 | Off-centre approach over rising ground refused "route blocked" | 0.3 m step lift retried, still swept; route ends on the floor under the climb line |
| bd95277 (Codex ba48dae) | A null/empty icon render silently kept the donor icon; donor snow/non-solid references survived on the clone | Icon validated, failure reported; donor references cleared |
| eca4a83 | (Codex review) a displacement the motor did not make was released through the controlled path, zeroing the mover's velocity and rewriting the fall | `LadderRules.IsExternalRelease`, unit-tested; version 0.1.1 |
| fd70727 | (Codex review) after stepping below the foot, neutral input pulled the body back up to the foot | `LadderRules.LowestHold`, unit-tested |
| 601392f | (Owner's own session) the native prompt showed at 1.50–1.57 m but the mount band ended at 1.25 m; a side landing at 0.78 m was refused | Band 1.6 m forward, 0.45 m beyond the rails' half width; route still swept |
| 0134208 | A ladder could be placed straight into the builder: the placement ghost's blocked-by-player test skips root colliders | Collider moved to a child object, as on native walls |
| 4c4892c | Opening a door from the ladder (interact animation = minor action) released the climb and the climber fell | An active climb holds for a minor action; mounting still needs a free player |
| 7e4e735 | Standing on the ground 1.7–2.1 m from a ladder (where the native Climb prompt already shows) and pressing Use said "Stand on a nearby landing… Midair grabs are unavailable." | That case now says "Move closer to the ladder to climb." (`LadderRules.TooFarToMount`); no mount decision changed |

## Automated gates

| ID | Result | Evidence |
|---|---|---|
| A01 | PASS | `tools/validate_repo.py` exit 0 in every verify run |
| A02 | PASS | `python -m unittest discover -s tools/tests -p "test_*.py"` in the dedicated checkout `cl-ladders-a02`: at 7e4e735, 120 tests, OK (1 skipped: symlinks unavailable on this machine), 67 min; checkout clean afterwards |
| A03 | PASS | 53/53 ladder domain tests (31 supplied + 22 added) |
| A04 | PASS | implicit in every verify solution build (16 projects) |
| A05 | PASS | final locked `scripts/verify.ps1` at 7e4e735, run after packaging: Release solution build succeeded, 16 assemblies, 5240 passed / 0 failed, validator exit 0 |
| A06 | PASS | `scripts/package.ps1 -Product ConcernedLadders` before the final verify; ZIP holds exactly manifest, README, CHANGELOG, LICENSE, icon and the one own DLL; the ZIP's DLL equals the verified Release DLL |
| A07 | PASS | Profile generator: identical bytes, one entry `export.r2x` (earlier pass) |
| A08 | PASS | `deploy-ladders-profile.ps1 -WhatIf`: `PLAN_ONLY` (earlier pass) |
| A09 | PASS | Gated dev deployments of every tested build: `DEPLOYED_NOT_LAUNCHED`, own DLL hash matched, duplicate-plugin refusal observed (the dev harness was refused until removed). The final bytes were installed by the manager's import instead (see Installation) |
| A10 | PASS | Five independent Codex reviews, final verdict `NO P0/P1`. Accepted P2s: a neutral hold below the foot after the support vanishes is silent (as every neutral hold is); "Move closer" can also show for a distant player behind an obstacle in front of the ladder, or one whose support ray hits while falling — both still safely refused |

## Installation through the manager

Observed on 2026-10-25, game closed throughout:

- The earlier `.r2z` import through "Import local mod from file" had created an inert local mod
  `TCC-Ladders 1.0.0 by TCC` holding only the `.r2z` (a profile file, not a mod). Removed with the
  manager's own Uninstall. The loose development DLL was moved out of `BepInEx\plugins` to
  `%LOCALAPPDATA%\TheConcernedCat\ConcernedLadders\profile-backups\`.
- Settings → Profile → Import local mod → Select file → the package ZIP. The manager read
  Mod name `ConcernedLadders`, Author `TheConcernedCat`, Version `0.1.1` from the package itself.
- My mods then lists `ConcernedLadders v0.1.1 by TheConcernedCat`, enabled, beside BepInExPack 5.4.2351
  and Jötunn 2.30.2. The profile holds exactly one `TheConcernedCat.ConcernedLadders.dll`, at
  `BepInEx\plugins\TheConcernedCat-ConcernedLadders\`, byte-identical to the ZIP entry and the verified
  Release DLL. No file was copied over it afterwards.
- Start modded: one `Concerned Ladders 0.1.1+7e4e735…` loaded, `2 loaded, 0 skipped, 0 failed`, three
  prefabs registered, 0 error lines.
- Decisive smoke on those bytes (no harness, ordinary input, dev `nocost` off and proven off by a
  4-wood charge): hammer-placed a workbench and a wood ladder (wood 12 → 8); mount → climb a 10-section
  mixed tower → hold → reverse → blocked top → Use with no landing (stays on) → Jump (ordinary fall);
  top exit over a lip; Use at a side landing steps onto it; "Move closer" shown at 1.71 m; quit, cold
  relaunch, the placed ladder persists and the tower climbs again. 0 error lines in all three sessions.

An earlier import of the same version built from 4c4892c was uninstalled through the manager before
this one; the superseded ZIP is kept under `artifacts/ladders-run/superseded/`.

## In-game acceptance (single player, disposable worlds)

A dev-only test harness plugin (scratchpad, never committed or packaged;
removed before every gated deploy and absent from the final import) teleported
the player, placed course pieces at exact transforms, applied vanilla
`WearNTear.Damage` hits and printed read-only state. Pieces placed that way
have no recorded builder, so vanilla returns a third of their resources when
they are destroyed; cost, stations and refunds were tested with the hammer.
"Released" lines are the mod's own log.

| ID | Result | Observation |
|---|---|---|
| B01 | PASS | Every cold start: one Concerned Ladders 0.1.1+<sha>, `Chainloader … 0 failed`, three prefabs registered, 0 error lines |
| B02 | PASS | Three hammer entries with rendered icons; names, costs and stations as specified |
| B03 | PASS | Before a forge only wood is listed; a forge shows native "New build piece" and lists copper and iron |
| B04 | PASS | Wood costs exactly 4 wood at a workbench (also 75 → 71 in this pass) |
| B05 | PASS | Copper 2+1 / iron 2+1 exact; insufficient copper and no forge → native "Missing requirement", nothing consumed |
| B06 | PASS | Ghost, rotation and the six snaps; corner snaps corrected (e9531d1) |
| B07 | PASS | Ten 10-section towers at exact 2 m pitch stand; removing the bottom of a 2-stack with the hammer takes the upper section down (native support) |
| B08 | PASS | Mixed wood/copper/iron towers join as one 20 m run |
| B09 | PASS | Ghost over the builder: red, native "Blocked", nothing charged (after 0134208; before it the ladder was placed into the player). Foreign ward (guard stone with a fictitious other owner, a simulation): red, "The ward stops you", nothing charged |
| B10 | PASS | Vanilla damage exact (wood 200→140→40, copper 400→280→80, iron 800→560→160→destroyed with drops); hammer repair without a forge → native "Missing the required crafting station"; with one → "Repaired Copper ladder" 80→400, wood 40→200; full health → "does not need repairs"; hammer removal refunds once |
| B11 | PASS | Worn (70 %) and broken (20 %) states switch visuals with no donor wall/pole and no pink material; a broken copper ladder still mounts, climbs and exits |
| B12 | PASS | Log out → new world `TCCLadderTestB` → place and climb → back to A; prefabs 3, hammer entries 3 throughout; see P07 |
| T01 | PASS | Mount from the front: "Ladder mounted: 1 section(s), 2.00 m run" |
| T02 | PASS | Off-centre approach on uneven ground (28aff52); mount band widened to where the native prompt shows (601392f) |
| T03 | PASS | From behind: refused (owner's own attempt `reach=-0.41`; earlier "route … blocked") |
| T04 | PASS | Use while falling beside the ladder: refused (owner session `vy=-25.20 supported=False`) |
| T05 | PASS | 1.88 m/s; 10 s neutral hold drifts 0.000 m; reversal symmetric |
| T06 | PASS | 10-section mixed 20 m run, every seam both ways, 20 consecutive full cycles (P06) |
| T07 | PASS | Top entry from a landing |
| T08 | PASS | Entry from a side landing |
| T09 | PASS | Top exit rises over the lip onto the floor |
| T10 | PASS | Solid ceiling: holds with "The ladder route is blocked…"; descend and Jump work |
| T11 | PASS | Wall on the landing, and a closed wood door across the exit: holds with "There is no clear landing at the top…"; opening the door from the ladder keeps the climb (4c4892c) and the exit then walks through the doorway |
| T12 | PASS | A boar standing on the landing: holds with the same message; once it moved, forward completed the exit ("controlled from Exiting") |
| T13 | PASS | Descent to the ground releases standing |
| T14 | PASS (pit) | Foot 6 m above ground: holds with "There is no footing below…". Water variant not set up (see limitations); swimming at a ladder's foot refuses the mount with "You cannot climb right now." |
| T15 | PASS | Use midway with no landing: "There is no safe landing within reach…", stays attached |
| T16 | PASS | Use at a side landing steps onto it |
| T17 | PASS | Jump from 6 m: ordinary fall, health 25 → 16 |
| T18 | PASS | Camera turned 180° or pitched: W still climbs, S still descends |
| T19 | PASS | Settings → Keyboard & Mouse: Use → L, Forward → I, Backward → K, Jump → J. E, W and Space did nothing; L mounted ("[L] Climb" prompt), I climbed 34.14 → 35.29 m, K descended, J released. Bindings restored to the owner's exact values afterwards |
| T20 | BLOCKED | No controller is connected to this machine; the code reads `JoyUse` and the processed analog movement, unobserved |
| T21 | PASS | Inventory or Esc menu while holding W: no motion |
| T22 | PASS | Attack while climbing: safe release |
| T23 | PASS | Parallel neighbour and reversed section are not joined |
| L01–L09 | PASS | Destroyed section, removed landing, obstacle during exit, moved section, death, teleport, setting off mid-climb, save/reload, logout mid-climb — all release cleanly (earlier pass) |
| L10, L11 | NOT RUN | Real Foreman in a compatibility profile; detection exercised with a simulated owner id only |
| L12 | PASS | With no climb: walking, sprinting, jumping, combat, a wood stair, opening a door, standing on a floating raft |
| M01–M10 | BLOCKED | No second client or dedicated server on this machine |
| VIDEO | Recorded | One H.264 MP4 (1 min 15 s), imported 0.1.1 bytes, no harness: T01, T05, T06, T11 (blocked top), T15, T17 on the 20 m mixed tower, then T09 (top exit) and T16 (side landing). `VIDEO-0.1.1-required-traversal-T01-T05-T06-T09-T11-T15-T16-T17.mp4` |
| P01 | PASS | All three variants by day, at night and in rain: readable silhouettes, no pink or absent state (appearance is the owner's call) |
| P02 | PASS (fallback) | Walk cycle while moving, still while holding, normal after release |
| P03 | BLOCKED | Needs an observer client |
| P04 | PASS | Equipment and normal animation after release and after death/respawn |
| P05 | PASS | 142 ladders loaded, nobody climbing: 8.33 ms mean / 8.34 ms max over 20 s, same as the 8.33 ms baseline at the 120 fps cap; 0 ladder log lines |
| P06 | PASS | 20 full cycles on a 10-section mixed tower: 40 event lines only, 0 errors, 8.34 ms mean mid-run, piece count stable |
| P07 | PASS | Ten logout → world switches alternating A and B: prefabs 3 and hammer entries 3 every time, ladder piece counts per world unchanged (142 / 1), 0 new registrations, 0 errors |

## Known limitations

- Multiplayer, dedicated-server and observer behavior are unobserved (BLOCKED).
- Controller play is unobserved (BLOCKED: no controller).
- Real Foreman coexistence is unobserved.
- A swimming player cannot grab a ladder (mounts need standing support); a dock ladder must be reached from the dock or the seabed. Owner decision whether to allow it.
- A ladder whose top is tucked under a floor can be hard to target from that floor; the water variant of T14 was not run for that reason.
- A ladder placed against a wall before e9531d1 sits 8 cm closer to the wall than one corner-snapped now and does not join new ones above it (development worlds only).
- Visual art and the walking fallback are a functional first pass, not owner-approved.
- Do not publish, tag or merge on this evidence alone.
