# Concerned Ladders — acceptance and evidence matrix

This is the implementing agents' test course. Run it before asking the owner to
find routine failures. A row is PASS only when its stated observation exists.
Source inspection, math tests and a clean log do not replace visible play.

## 1. Candidate receipt

Start a new evidence directory per candidate, never overwrite prior results.
Record these fields in `EVIDENCE.md`:

| Field | Required evidence |
|---|---|
| Source | full base/head SHA, clean/dirty state, branch and reviewed diff |
| Build | complete `scripts/verify.ps1` log/summary, configuration, .NET version |
| Game | installed Valheim version/build ID, Unity version if logged |
| Dependencies | Thunderstore package versions and actually loaded BepInEx/Jotunn assembly versions |
| Candidate | own DLL version, informational version/source, SHA256 and package SHA256 |
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
| A02 | `python -m unittest discover -s tools/tests -p "test_*.py"` | complete pass; new metadata handling preserves old audits |
| A03 | `dotnet test src/ConcernedLadders.Tests/ConcernedLadders.Tests.csproj --configuration Release --nologo` | production geometry/policy tests pass; no game-proof claim |
| A04 | `dotnet restore ConcernedCatMods.sln` | MSBuild loads complete solution, no missing/duplicate projects |
| A05 | locked `pwsh scripts/verify.ps1` | full Release solution build, all tests, validator; quote summary |
| A06 | locked `pwsh scripts/package.ps1 -Product ConcernedLadders`, before final A05 | own DLL only, numeric version, license/readme/icon; ZIP-entry DLL hash equals the final verified DLL |
| A07 | profile generator twice | identical bytes; no DLLs/scripts/secrets/saves in `.r2z` |
| A08 | deployment `-WhatIf` | verifies exact profile/source/DLL; writes nothing |
| A09 | actual deployment | own DLL hash matches; no duplicate/unrelated DLL; receipt says deployed, not launched |
| A10 | independent diff review | no current P0/P1, verified API uncertainty explicitly named |

A05 is blocked without licensed local game dependencies. Never substitute A03
for A05 or make fake game stubs so an unusable plugin appears compiled.

These IDs group checks; they are not the execution order. Install the profile's
dependencies before binding build references. `package.ps1` rebuilds, so run A06
before the final A05 and capture a new verification log afterward. Compare the
ZIP-entry DLL SHA256 with the verified output. If they differ, repackage those
verified bytes with `tcli build` and `tools/reproducible_zip.py` without rebuilding.
The deployment script refuses a DLL newer than its verification log.

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

Test piece cost/progression first with ordinary resources. If temporary debug
tools are used to construct the rest of the test course, turn off no-cost,
no-damage, flight and other cheats before testing the behavior they would mask.
Record which convenience was used. No third-party gameplay mod is required.

## 4. Startup, identity and placement

| ID | Procedure | Required result |
|---|---|---|
| B01 | Cold Start modded into menu | expected versions load once, no missing dependency or Harmony exception |
| B02 | Open hammer; inspect all three entries | correct material/name/icon/recipe; no duplicates or donor icon on graphical client |
| B03 | New character has not discovered Copper/Iron | native discovery/progression policy preserved; no spoiler unlock grant |
| B04 | Build wood with exactly four wood | cost once; station requirement applies |
| B05 | Build copper/iron with exact cost; repeat insufficient cost and missing forge | valid build succeeds; invalid builds do not consume or place |
| B06 | Place at cardinal and intermediate rotations against wall | normal ghost/rotation/snaps; model and collider aligned |
| B07 | Stack 2, then 10 sections | exact 2m spacing; no visible gap or duplicate piece; native support remains meaningful |
| B08 | Stack mixed material sequence | same endpoints, no runtime discontinuity |
| B09 | Unsupported, obstructed and foreign-ward placement | native refusal/feedback; no bypass |
| B10 | Repair, dismantle, damage, destroy | health changes normally; resources/refunds exactly once |
| B11 | Force worn/damaged states by ordinary game mechanics | no returning donor wall/pole, no pink material, collision remains stable |
| B12 | Log out → world B → back to world A | no duplicate prefabs/menu entries or residual runtime state |

## 5. Single-player traversal: required before owner test

| ID | Procedure | Required result |
|---|---|---|
| T01 | Interact from centred bottom approach | finite smooth mount, correct facing, no instant jump to top |
| T02 | Approach obliquely and from slight offset | forgiving legal band; rejected reach explains; no wall pull |
| T03 | Interact from wrong side/through wall/out of reach | no traversal through solid geometry |
| T04 | Interact while falling beside it | no mid-air catch/fall-reset exploit in v0.1 |
| T05 | Climb up, stop for 10s, reverse, stop, resume | correct direction; no gravity drift, jitter or stale input |
| T06 | Cross every seam of 10-section mixed stack both ways | no detach, teleport, animation spike or truncated 4m run |
| T07 | Top entry from a supported landing | smooth correct mount and descent |
| T08 | Grounded intermediate landing entry | legitimate entry allowed, unrelated neighbor not chosen |
| T09 | Clear top with thin floor, thick lip and sloped/uneven approach | swept rise/cross transfer; no head/feet clipping |
| T10 | Solid ceiling directly over ladder | safe hold; descend and Jump still work |
| T11 | Destination empty but wall/closed hatch blocks path | no teleport through barrier; useful blocked message |
| T12 | Top landing has another character or obstacle | hold/recheck; no actor ghosting |
| T13 | Bottom at supported floor/slope | safe grounded exit without embedding or sudden drop |
| T14 | Bottom ends over open pit or water | hold when no valid standing support; Jump deliberate ordinary fall/swim |
| T15 | Interact halfway with no landing | stay attached; message; no involuntary freefall |
| T16 | Interact halfway at a supported landing | swept safe dismount; normal movement immediately resumes |
| T17 | Jump off at low and high altitude | release, ordinary falling/damage, can later mount again from support |
| T18 | Turn camera upward/downward/180° during climb | forward still up; backward still down; no camera seizure |
| T19 | Remap movement, Interact and Jump | all use current bindings; no hardcoded E/W/Space-only path |
| T20 | Controller mount, analog climb, neutral hold, exit, Jump | complete loop without keyboard; appropriate hints |
| T21 | Open chat/console/map/inventory/menu while moving | intent pauses; no typing-driven motion, no stuck movement after close |
| T22 | Attack/dodge/stagger/knockback during approach or climb | defined safe refusal/release; no locked controls or duplicate combat action |
| T23 | Nearby parallel, reversed, gap and rotated ladder segments | no false joins, shortcuts or transfer to wrong shaft |

Record a video showing T01, T05, T06, T09, T11, T15–T17 in one run. Screenshots of
the piece alone cannot prove motion, reversal, holding or safe release.

## 6. Interruption, persistence and compatibility

| ID | Procedure | Required result |
|---|---|---|
| L01 | Destroy/demolish the current or connecting ladder during climb | release or bounded safe state; normal gravity/control; no immortal session |
| L02 | Remove platform during multi-frame exit | transfer revalidates; no floating or invisible foothold |
| L03 | Add obstacle during exit | no movement through newly blocked path |
| L04 | Rotate/move supporting piece through a legitimate test facility | stale run is rejected; moving-platform support is not assumed |
| L05 | Die mid-climb, respawn | normal death/respawn/equipment, no lingering gravity/pose changes |
| L06 | Legitimate teleport/attach takeover | ladder relinquishes control; no write back to old transform |
| L07 | Disable climb setting mid-climb | restore movement; custom prefab still registered and removable |
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
| M01 | Host builds/climbs while client observes | same pieces, facing and coherent movement |
| M02 | Client builds/climbs while host observes | client owns own body; no host-only behavior |
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
| P02 | Record mount/move/hold/reverse/both exits | walking fallback cycles only with motion; no stuck pose |
| P03 | Observe another player's animation | no local-only success disguised as multiplayer proof |
| P04 | Test equipment/stowing, normal animation after release/death | no persistent override; equipment inventory unchanged |
| P05 | 100+ pieces, nobody climbing | no per-frame world scans/log spam; compare same-base frame-time capture |
| P06 | Climb dense 20m tower, repeat 20 cycles | bounded allocations and queries; no rising registry count/leak |
| P07 | Profile reload/change world ten times | no cumulative prefabs, hooks, event subscribers or stale segments |

No hard FPS figure is promised across different machines. Record a comparable
baseline, hardware and observation window. Fix actual sustained regressions.

## 9. Finish and retest rules

On a failure, record the test ID, exact source/DLL hash, repro and log/capture,
fix it, and rerun the failed row plus direct neighbors and lifecycle cleanup.
Do not repeatedly rerun every unrelated product unless an affected shared file
or required gate warrants it. Run the full mandatory gate once on the final
candidate and deploy exactly that build.

Ready for owner visual review: A01–A10, B01–B12, T01–T23 and critical L/P rows
completed, no in-scope P0/P1, explicit multiplayer status. Release-ready is a
separate stronger claim including the network matrix and owner visual decision.
Never publish in this job. The final handoff gives the exact profile and Start
modded route, four short owner checks, remaining limitations and all evidence.
