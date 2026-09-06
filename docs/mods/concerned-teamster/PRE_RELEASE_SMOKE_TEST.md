# Pre-release smoke test — Concerned Teamster v0.9 public beta

CT-044. The single-session-family human release checklist. This document
compiles every manual-only verification the autonomous conveyor deferred
from CT-001 through CT-043 into one ordered, step-by-step checklist with
expected results per step, following the proven `PRE_RELEASE_SMOKE_TEST.md`
pattern from Concerned Cartographer's own release line. Rows marked
**BLOCKS** must pass before the v0.9 beta publishes; others are
record-and-ship. No row here has ever been run — every one is compiled
from a still-open `HUMAN_ATTENTION.md` entry, cross-referenced below, and
none is claimed PASS by this document itself.

> Status: DRAFT — first compilation, v0.9 line. Not yet run.

## Cross-reference: every pending claim this checklist covers

| HUMAN_ATTENTION entry | Issue | Checklist section |
|---|---|---|
| Generated placeholder package icon | CT-001 (#109) | §0 (record-and-ship — owner taste decision) |
| CT-002 startup probe log excerpt | CT-002 (#110) | §1 |
| CT-003 in-game telemetry spot check | CT-003 (#111) | §2 |
| CT-004 in-game grade/surface spot check | CT-004 (#112) | §2 |
| CT-005 panel visual check + v0.1 campaign | CT-005 (#113) | §2, §11 |
| CT-006 manifest-vs-container check | CT-006 (#115) | §3 |
| CT-007 manifest panel screenshot | CT-007 (#116) | §3 |
| CT-008 calibration protocol runs | CT-008 (#117) | §3 |
| CT-009 in-game warning transcript | CT-009 (#118) | §3 |
| CT-011 descent calibration runs | CT-011 (#121) | §4 |
| CT-012 in-game brake demonstration | CT-012 (#123) | §4 |
| CT-013 staged stuck scenarios | CT-013 (#124) | §4 |
| CT-014 guidance walkthrough | CT-014 (#125) | §4 |
| CT-016 in-game trip recording check | CT-016 (#128) | §5 |
| CT-017 real-trip score sanity check | CT-017 (#129) | §5 |
| CT-018 history/comparison screenshot | CT-018 (#130) | §5 |
| CT-019 in-game bottleneck view | CT-019 (#131) | §5 |
| CT-022 route picker checks | CT-022 (#135) | §6 |
| CT-023 route profile check | CT-023 (#136) | §6 |
| CT-024 route report demonstration | CT-024 (#137) | §6 |
| CT-025 coexistence matrix | CT-025 (#138) | §6 |
| CT-027 live multiplayer scenario runs | CT-027 (#141) | §7 |
| CT-028 coop live participant feed + staged scenario | CT-028 (#142) | §7 |
| CT-029 live lifecycle runs | CT-029 (#143) | §7 |
| CT-030 live two-topology campaign | CT-030 (#144) | §7 |
| CT-031 live controller wiring | CT-031 (#146) | §8 |
| CT-033 scale/contrast visual confirmation | CT-033 (#148) | §8 |
| CT-034 onboarding hint + profile switch | CT-034 (#149) | §8 |
| CT-037 in-game coexistence matrix (BetterCarts) | CT-037 (#153) | §9 |
| CT-038 in-game coexistence matrix (ItemStacks/ValheimPlus) | CT-038 (#154) | §9 |
| CT-039 two visual polish items (non-blocking) | CT-039 (#155) | §8 (record-and-ship) |
| CT-040 corruption/compat/scale live campaign | CT-040 (#156) | §10, §9, §11 |
| CT-041 Report a Bug real browser-open | CT-041 (#158) | §12 |
| CT-042 real-gameplay media capture | CT-042 (#159) | Throughout — capture during §2–§9 |
| CT-043 profile creation + in-game rows | CT-043 (#160) | §0, throughout |

## 0. Preamble: profile setup and package install — DRY-RUN VERIFIED RUNNABLE

Per `PROFILE_REHEARSAL.md`. **BLOCKS.**

1. In your mod manager: New Profile → `TCT-Clean` → Install BepInEx. No
   Teamster DLL goes here, ever — it is the vanilla-truth baseline.
2. New Profile → `TCT-Dev` → Install BepInEx. Configure
   `Environment.props`'s `TEAMSTER_DEPLOYPATH` at its plugins folder, then
   `pwsh ./scripts/deploy.ps1 -Product ConcernedTeamster -Profile Dev`.
3. New Profile → `TCT-Compat` → Install BepInEx, then install BetterCarts,
   ItemStacks, and ValheimPlus from Thunderstore's own browser. Configure
   `TEAMSTER_COMPAT_DEPLOYPATH`, deploy with `-Profile Compat`.
4. New Profile → `TCT-Dedicated` (or your manager's dedicated-server
   support) → Install BepInEx. Configure `TEAMSTER_DEDICATED_DEPLOYPATH`,
   deploy with `-Profile Dedicated`.
5. Package audit: import the exact v0.9 RC ZIP named in
   `RELEASE_DOSSIER.md` (once CT-045 seals it) — ZIP root is
   `manifest.json`, `README.md`, `CHANGELOG.md`, `LICENSE`, `icon.png`
   (256×256), `plugins/TheConcernedCat.ConcernedTeamster.dll`, nothing
   else; dependencies pinned to BepInExPack 5.4.2333 and Jötunn 2.29.2.

**Dry-run evidence (CT-044, already performed against the current
pre-v0.9-seal source — the automatable half of this preamble):**
`rehearse-teamster-lifecycle.ps1` proves the fresh-install file layout,
`deploy.ps1 -Profile {Dev,Compat,Dedicated}` all resolve and validate
correctly, and `validate_repo.py --require-binary` confirms the package
audit's exact-contents claim — all green on this machine as of this
compilation (see the CT-044 PR evidence). The profile-creation clicks
themselves (steps 1–4) remain owner-interactive and unverified until run
for real — no TCT-* profile exists on this machine yet.

## 1. Fresh install and startup (CT-002)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 1.1 | TCT-Dev, fresh | Start modded | Log shows the version banner (real Valheim/BepInEx/Jötunn versions) and "Cart telemetry capability ENABLED: 11 game members verified." — no WARN, no CT exception | LogOutput.log | Yes |
| 1.2 | 1.1 | Enter a disposable world; open/close the Cart Status panel repeatedly; logout to menu; re-enter | No exceptions; panel state matches reality every time; no stale telemetry from the previous session | LogOutput.log | Yes |

## 2. Vanilla truth baseline and Cart Status panel (CT-003/004/005)

Prep: the same cart and cargo set loaded in both TCT-Clean and TCT-Dev; a
built dirt slope and flat ground.

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 2.1 | TCT-Clean vs TCT-Dev, same cart/cargo | Compare vanilla's own displayed cargo weight (container UI) against Teamster's Cart Status panel in TCT-Dev | Numbers match exactly (base + cargo breakdown); quality-scaled gear (worn items) matches vanilla's own charge | Screenshot both | Yes |
| 2.2 | TCT-Dev, built dirt slope + flat ground | Attach and pull the cart across both | Grade reads ~0% on flat, positive climbing / negative descending on the slope, sign matches the pull-handle heading; ground surface label matches the terrain | Screenshot + map/heading note | Yes |
| 2.3 | TCT-Dev | Click the Cart button (right screen edge, in-world only) | Panel opens showing total mass, grade, surface, attachment/pull state, freshness; button is invisible outside a world | Screenshot | Yes |
| 2.4 | 2.3 | Walk away from the cart; return | Data marked stale while away, refreshes on return — never a wrong number shown as current | Screenshot (stale state) | Yes |
| 2.5 | Any world | Build/attach/detach/destroy/rebuild the cart; logout/login; switch worlds; uninstall the DLL and relaunch | Panel always follows reality (no stale identity); world/character switches leak no telemetry; uninstalled leaves pure vanilla behavior with no missing-object errors | LogOutput.log | Yes |
| 2.6 | TCT-Dev, panel open | 30 minutes of normal hauling with the panel open | No visible frame-time spikes attributable to Teamster; log volume stays bounded | Observation + LogOutput.log size | Yes |

**Capture for CT-042 media**: at least one clean screenshot of the open
Cart Status panel with a loaded cart, for the package README.

## 3. Cargo manifest and load planning (CT-006/007/008/009)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 3.1 | Cart loaded with mixed/quality-scaled gear | Open the manifest, compare every line/count/weight against the vanilla container UI | Exact match, including quality-scaled items; unreadable slots show an explicit marker, never a wrong number | Screenshot both | Yes |
| 3.2 | Full-ish cart | Sort every column both directions; type a filter (incl. a localized item name); watch responsiveness | Sort/filter behave per the documented matrix; ▲▼ glyphs render (or record as boxes if the font lacks them — cosmetic) | Screenshot | Yes |
| 3.3 | Calibration protocol (`CALIBRATION_PROTOCOL.md`) | Run 5 cargo sets × 3 graded ramps × 2 reps in TCT-Clean/TCT-Dev, recording pull results | Produces real Measured calibration rows; note the exact gravity used (bounds hold ≥4.4 m/s²) | Recorded protocol data sheet | No (record-and-ship; sharpens advice, does not block) |
| 3.4 | Built test slope | Load progressively heavier cargo and climb; watch the panel warning row and (if enabled) the HUD hint | Steep-climb caution rises smoothly, holds through brief grade dips (no flicker), releases correctly; Unknown calibration never warns | Screenshot / clip of the transcript | Yes |

**Capture for CT-042 media**: a manifest-panel screenshot showing sorting
and a warning row, for the package README.

## 4. Descent safety and the parking brake (CT-011/012/013/014)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 4.1 | Descent calibration protocol | Run 5 sets × 3 ramps × 3 entry speeds × 2 reps | Produces real Measured descent rows | Recorded protocol data sheet | No (record-and-ship) |
| 4.2 | Cart on a graded slope, attached | Engage the brake button | Cart holds on the grade; wheels/joints behave (no visible dangling); button reflects engaged state | Clip | Yes |
| 4.3 | 4.2 | Release the brake | Vanilla rolling resumes immediately; releases correctly also on: grabbing the cart, walking away past the distance threshold, world exit, plugin shutdown, cart destruction | Clip per release path | Yes |
| 4.4 | Multiplayer, two clients | Authority hand-off mid-haul with the brake engaged | Brake state and button visibility follow authority correctly on both clients | Clip | Yes |
| 4.5 | Staged: wheel against a rock (flat ground) | Pull the cart into the obstruction | Stuck diagnosis correctly identifies the obstruction cause | Screenshot | Yes |
| 4.6 | Staged: chassis grounded on a terrain lip | Pull the cart onto the lip | Diagnosis identifies grounding, not overload | Screenshot | Yes |
| 4.7 | Staged: genuine overload on a built ramp | Overload the cart and attempt the climb | Diagnosis identifies overload with a quantitative unload suggestion traced to the load model | Screenshot | Yes |
| 4.8 | 4.5–4.7 each | Open the Guidance panel from the stuck state | Numbered vanilla-legal steps match the diagnosis; following them frees the cart | Screenshot + clip of the cart freed | Yes |

## 5. Trip recording and road quality (CT-016/017/018/019)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 5.1 | TCT-Dev | Attach, pull a real route, detach | Sidecar file appears under `BepInEx/config/ConcernedCatMods/ConcernedTeamster/`; logout flushes the open trip; switching worlds isolates history correctly | Inspect the sidecar file | Yes |
| 5.2 | A smooth built road vs. raw meadow terrain, same cargo | Haul both, compare road-quality scores | Built road scores less rough than meadow; a mud/water crossing shows a lower drag-proxy speed | Screenshot of both scores | Yes |
| 5.3 | Several recorded trips | Open Trip History: sort, select an A/B pair, delete one (two-step confirm) | Sorting/selection/comparison render correctly; deletion removes exactly the one raw trip, segment history intact | Screenshot | Yes |
| 5.4 | A recorded route with a known rough/steep spot | Open the bottleneck view for that route | Located meter/percent point matches where the haul actually struggled | Screenshot with annotation | Yes |

**Capture for CT-042 media**: a Trip History comparison screenshot and a
bottleneck-view screenshot.

## 6. Optional Cartographer integration (CT-022/023/024/025)

Prep: Concerned Cartographer 0.10.0+ installed alongside Teamster (a
combined profile, or TCT-Dev with Cartographer additionally installed),
with at least one route drawn.

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 6.1 | Both mods installed | Open the Routes button | Cartographer's drawn routes are listed; an ineligible route shows its reason | Screenshot | Yes |
| 6.2 | Teamster alone (Cartographer absent) | Look for the Routes button | Absent entirely — no button, one INFO line in the log | LogOutput.log | Yes |
| 6.3 | 6.1, select a route | Profile it | Numbers look sane against the visible terrain; unloaded far stretches show UNSAMPLED, never guessed; the load line matches the cart's current mass | Screenshot vs. terrain | Yes |
| 6.4 | 6.3 | Open the report | Numbered problem sections match the terrain; gap entries appear where terrain was unloaded | Screenshot | Yes |
| 6.5 | Both mods, a version-mismatch simulated (older Cartographer if available) | Observe the integration | Hides with one INFO line below the version floor | LogOutput.log | Yes |
| 6.6 | Extended dual-mod session | Play normally with both mods for 15+ minutes | No new log exceptions from either mod; Cartographer's own atlas is never touched by Teamster | LogOutput.log (both mods) | Yes |

## 7. Multiplayer trust and authority (CT-027/028/029/030)

Prep: a dedicated server (TCT-Dedicated) and a player-hosted session, two
clients, at least one unmodded peer if available.

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 7.1 | Player-hosted, two clients | Cart authority handoff mid-haul (host leaves, other player takes over pulling) | Brake button appears/disappears correctly per authority; panels re-label observer-vs-owner readings correctly | Clip both clients | Yes |
| 7.2 | Dedicated server, two clients | Repeat 7.1 against TCT-Dedicated | Same correct behavior on a real dedicated topology | Clip | Yes |
| 7.3 | Unmodded peer present | Peer plays normally alongside a Teamster client | Peer sees pure vanilla behavior — no visible change, no errors on their end | Peer's own log (if accessible) + observation | Yes |
| 7.4 | Two players, one intentionally pulling the wrong way | Observe cooperative diagnostics | Classification (helping/hindering/idle) matches what both players actually experience | Clip | Yes |
| 7.5 | Mid-haul | One teammate joins, one leaves, one disconnects; a world switch during observation | No exceptions; no stale mutating state carried into the new context | LogOutput.log | Yes |

## 8. Controller, accessibility, onboarding, and profiles (CT-031/033/034/039)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 8.1 | Gamepad connected | Navigate every panel using only the controller | Focus visibly moves in the documented order; every feature is reachable by button; accelerators fire and conflicts warn correctly | Clip | Yes |
| 8.2 | `Ui.Scale` at 0.8, 1.0, 1.3 | Open every panel at each scale, including alongside a neighboring panel | No clipping against a neighbor; the tallest panel (Trip History) fits the actual canvas; text stays readable with the outline applied | Screenshots ×3 scales | Yes |
| 8.3 | Default wood-panel background | Spot-check contrast visually against `ACCESSIBILITY.md`'s documented estimate | Text reads clearly; if the real background is notably lighter than estimated, flag for a contrast re-audit | Screenshot | No (record-and-ship unless a real readability problem is found, then BLOCKS) |
| 8.4 | Fresh profile, first world entry | Observe the onboarding hint | Appears once, points at the Cart button, wraps/reads cleanly without clipping; dismissing it hides it permanently | Screenshot | Yes |
| 8.5 | Config file | Switch `General.Profile` between Minimal/Standard/EverythingObservational and relaunch | Warnings/HUD-hint/trips/risk-lookahead change per the documented preset; the brake stays whatever you left it at, every time | Screenshot + config diff | Yes |
| 8.6 | A manually-edited individual setting | Restart without changing `Profile` | The manual edit survives untouched | Config diff | Yes |
| 8.7 | Support panel open, `Ui.Scale` at 1.3 | Check the third button row (Support, beside Compat) clearance | Fits without crowding the row-text block above it (CT-039 polish item) | Screenshot | No (record-and-ship) |

## 9. Compatibility matrix (CT-037/038/040)

Prep: TCT-Compat with BetterCarts, ItemStacks, and ValheimPlus installed.

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 9.1 | BetterCarts installed alongside Teamster | Open the Compat panel; check warnings, stuck diagnosis, recovery guidance, route bottleneck | Compat panel lists BetterCarts detected, Adapt policy; every load-advice surface shows "load advice unavailable" instead of a vanilla-calibrated number | Screenshot | Yes |
| 9.2 | ItemStacks installed alongside Teamster | Same checks as 9.1 | Same suppression behavior, ItemStacks correctly detected | Screenshot | Yes |
| 9.3 | ValheimPlus installed, Wagon section left at its default (disabled) | Check the Compat panel | Warn-level note shown, but load advice is NOT suppressed (matches vanilla physics while disabled) | Screenshot | Yes |
| 9.4 | ValheimPlus, Wagon section explicitly enabled by you in `valheim_plus.cfg` | Recheck the Compat panel and load advice | Warn note still shown (presence-only detection can't see the live config — documented limit); advice quality is the player's own responsibility once they've opted into an incompatible config | Screenshot | Yes |
| 9.5 | All three installed together | Play normally for 15+ minutes | No conflicts, no new log exceptions from any of the four mods | LogOutput.log | Yes |

**Capture for CT-042 media**: a Compat panel screenshot showing a
detected mod and its policy.

## 10. Recovery, migration, and support bundle (CT-039/040)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 10.1 | Mid-haul, trips being recorded | Kill the game process (Task Manager) during a write | On next launch, the prior sidecar survives via its rotated backup; a recovery event is recorded | Inspect the `.bak-*` files + Support panel | Yes |
| 10.2 | A real pre-v1 (pre-CT-039) sidecar file (or one built by an old ZIP per §0's chain) | Load a world with the RC installed | Migration fires once, logs the expected one-line message, recovery event visible in the Support panel | LogOutput.log | Yes |
| 10.3 | Any world with some history | Click Export on the Support panel; open the file | Contains versions, config, compatibility status, sidecar summaries, recovery events, recent log lines — zero world names, player names, or full file paths | The exported file | Yes |

## 11. Scale spot check (CT-005/CT-040)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 11.1 | A dense cart cluster (many carts near each other) | Long real hauling session, panel open | The documented tracked-cart-ceiling behavior (freshness over raw coverage) matches `RELEASE_DOSSIER.md`'s scale finding; no frame-time spikes | Observation + LogOutput.log | Yes |
| 11.2 | Max-retention trip history (or a fixture approaching it) | Open Trip History | Stays responsive; no visible lag opening/sorting/comparing | Observation | No (record-and-ship) |

## 12. Feedback path (CT-041)

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 12.1 | Support panel open | Click **Report a Bug** | Default browser opens to the GitHub issue tracker; no data sent, no crash | Observation | Yes |
| 12.2 | Same session | Verify no other button/action opens a network connection | Report a Bug is the only outbound action the mod ever takes | Observation | Yes |

## 13. Upgrade, migration, and uninstall

Cross-referenced with `PROFILE_REHEARSAL.md`'s owner-smoke-checklist
addition and `rehearse-teamster-lifecycle.ps1`'s file-level proof.

| # | Setup | Action | Expected | Evidence on failure | Blocks |
|---|---|---|---|---|---|
| 13.1 | Scratch profile, sealed v0.7.0 ZIP (chain-integrity-verified by `rehearse-teamster-lifecycle.ps1`) | Install v0.7.0 for real, play a short session to generate a config + sidecar | Config and sidecar files created normally | Inspect the files | Yes |
| 13.2 | 13.1 | Upgrade in place to the sealed v0.9.0 RC ZIP; relaunch | Startup log shows the expected one-line schema-migration message; existing trip history intact | LogOutput.log | Yes |
| 13.3 | Any TCT profile with history | Remove the plugin DLL only; relaunch | Vanilla behavior, no missing-object errors; the player's own sidecar/support-bundle files remain untouched under `BepInEx/config/` until the player separately deletes them | LogOutput.log | Yes |

## 14. Thunderstore preflight (owner-only)

- [ ] `python ./tools/validate_repo.py --product teamster --expected-version 0.9.0 --require-binary` passes. **BLOCKS**
- [ ] ZIP inspected by a human for secrets/saves/game DLLs/unrelated files. **BLOCKS**
- [ ] README/CHANGELOG on the package page match actual behavior (cross-check against this checklist's captured evidence). **BLOCKS**
- [ ] Categories: mods, client-side, utility, **ai-generated**. **BLOCKS**
- [ ] Icon: keep the generated placeholder, or replace with commissioned/final art (CT-001/CT-042 open item — owner taste decision, does not block). Record the decision here.
- [ ] Upload is Eren-only, via the Thunderstore web UI or a publish script with a token passed via environment variable, never stored. **BLOCKS**

## 15. Post-publication smoke

- [ ] Install the published package from Thunderstore into a clean profile; §1 passes.
- [ ] Package page renders README/icon/changelog correctly.
- [ ] First community-visible version pinned in the GitHub release notes.
