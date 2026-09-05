# Concerned Teamster release dossier

Internal release candidates are sealed here with exact hashes and the honest
split between automated evidence and pending manual claims, following the
dossier discipline proven on Concerned Cartographer. Publication of anything
is owner-only, always.

## v0.4 RC1 — "Road Quality and Trip Profiles" (sealed 2026-09-05)

| Item | Value |
|---|---|
| Version | 0.4.0 (internal; no publication) |
| Source commit | `6ad2c8f163bd370696a72b2f64df4ffe4505aeca` (branch `chore/ct-020-v04-rc`, merged to main via PR) |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.4.0.zip` |
| ZIP SHA-256 | `922b41e0a33ebe586d13296b4563a0ee9b4b4594638aa6e93bc2e2e14e2e2693` (97,152 B) |
| DLL SHA-256 | `24d7d617a0bfadc168c62b859c5aada7a430b125077651c15d49d41c4122bbd7` (141,312 B) |
| DLL identity | AssemblyVersion 0.4.0.0, InformationalVersion `0.4.0+6ad2c8f163bd370696a72b2f64df4ffe4505aeca` |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md, CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB |
| Built against | Valheim 0.221.12 (network 36, buildid 21981559 — re-verified in the Steam manifest at seal time), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.4.0 across csproj/Plugin.cs/thunderstore.toml/CHANGELOG — validator-asserted (`--expected-version 0.4.0 --require-binary`) |

### Sprint scope sealed in this RC

CT-016 per-world sidecar trip recording (atomic writes, world-UID
isolation, caps/pruning, retention setting) · CT-017 deterministic
road-quality scoring (8 m segments, additive stats, format v2 + v1
migration with backup) · CT-018 trip history and quintile-aligned A/B
comparison UI · CT-019 route bottlenecks (worst grade, roughest segment,
hypothetical-load binding point, honest Unknown coverage).

### v0.4 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Static validation + version sync | `validate_repo.py --product teamster --expected-version 0.4.0 --require-binary` | PASS |
| Solution build | `build.ps1 -Configuration Release` | PASS — 0 errors (8 pre-existing benign warnings) |
| Unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **287/287 PASS** — adds the CT-020 retention gate test |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | 568/568 PASS |
| Durability spot checks (kill-during-write, world isolation) | `dotnet test --filter FullyQualifiedName~TripPersistenceTests` (Release, real filesystem) | **15/15 PASS** — a simulated kill leaves the previous file intact and the next write swaps cleanly; a wrong-world file is refused untouched (filename AND header); unknown future versions refused; v1 files backed up before migration; malformed rows skipped and reported; pruning caps hold |
| Retention over many trips | new gate test: 200 real read-merge-prune-write cycles at a 50-trip cap | PASS — cap held, newest trips kept, ids renumber densely, file size bounded (≤10% drift after the cap), segment scores keep all 200 trips' history while raw trips prune |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only confirmed |
| Save/network mutation audit | grep `ZDO.Set\|SetOwner\|InvokeRPC` in Teamster source | PASS — sole hit is the CartBrakeAdapter doc comment stating their absence |
| Sidecar write-path audit | grep every `File.*` write API + all `SidecarFileStore` callers | PASS — every write API lives in `SidecarFileStore.cs`; every caller path comes from `SidecarPathFor` → `BepInEx/config/ConcernedCatMods/ConcernedTeamster/`; no world-save write exists |
| Real-trip campaign rows (record hauls; verify history/comparison/bottlenecks and score sanity against them) | in-game, interactive | **MANUAL — pending** (HUMAN_ATTENTION.md CT-016..CT-019 entries; listed below) |

Defects: no `DEF-teamster-v0.4-*` issues were needed; at seal time the
only open `sprint:teamster-v0.4` issues are this gate leaf and the
controller — no open P0/P1.

### Pending manual claims added by v0.4

1. Real-haul sidecar recording check: attach → pull a route → detach →
   inspect the file; logout flush; world-switch isolation (CT-016 entry).
2. Real-trip score sanity: a smooth built road scores less rough than raw
   meadows; a mud/water crossing shows a lower drag-proxy speed (CT-017
   entry).
3. Trip History panel screenshots: sorting, A/B selection, two-step
   deletion, real side-by-side comparison (CT-018 entry).
4. In-game bottleneck view on real recorded routes: located meter/percent
   points match where the haul actually struggled (CT-019 entry).

### Gate decision

All automatable v0.4 gates are green; manual items are pending by design
for an internal RC. Sprint controller #127 closes with this seal.

## v0.3 RC1 — "Descent Safety and Recovery Guidance" (sealed 2026-09-04)

| Item | Value |
|---|---|
| Version | 0.3.0 (internal; no publication) |
| Source commit | `8d3898a62333213a0e8af67cd8cb6eb8245daf54` (branch `chore/ct-015-v03-rc`, merged to main via PR) |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.3.0.zip` |
| ZIP SHA-256 | `06358e856b17bc1d3f0cc1062b06c9d3d603a785c16f839fa7df6d33d6b3898c` |
| DLL SHA-256 | `db4c4923e68026478e71d19034fde250a0a7bc9940ef5d1a9554c39c46b283ce` |
| DLL identity | AssemblyVersion 0.3.0.0, InformationalVersion `0.3.0+8d3898a62333213a0e8af67cd8cb6eb8245daf54` |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md, CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB |
| Built against | Valheim 0.221.12 (network 36, buildid 21981559), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.3.0 across csproj/Plugin.cs/thunderstore.toml/CHANGELOG — validator-asserted (`--expected-version 0.3.0 --require-binary`) |

### Sprint scope sealed in this RC

CT-011 descent/runaway risk model (3D dominance, bounded lookahead) ·
CT-012 parking brake (explicit, reversible, save-proof by construction;
capability now 27 probed members) · CT-013 stuck diagnostics (confusion-
matrix classifier, Unclear honesty) · CT-014 recovery guidance
(mutation-audited advisory steps with load-model-traced quantities).

### v0.3 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Static validation + version sync | `validate_repo.py --product teamster --expected-version 0.3.0 --require-binary` | PASS |
| Solution build | `build.ps1 -Configuration Release` | PASS — 0 errors |
| Unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **241/241 PASS** — adds risk-model 3D monotonicity grid + shipped-descent-file reproducibility, brake lifecycle matrix (every engage refusal and release path), diagnostics confusion matrix, guidance presenter suite |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | 568/568 PASS |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above |
| Brake persistence audit | grep for `ZDO.Set|SetOwner|InvokeRPC` in Teamster source | PASS — sole hit is the doc comment stating their absence; the one mutation is runtime `Rigidbody.constraints` |
| Guidance mutation audit | grep for brake-adapter references in the guidance layer | PASS — 0 hits |

Defects: no `DEF-teamster-v0.3-*` issues were needed; two in-scope test
defects (a risk-test mass not dominated by its intended row; a stuck-test
model whose Climbs row shadowed the Marginal region) were caught during
implementation review and fixed before their PRs merged.

### Pending manual claims added by v0.3

1. Descent protocol runs (sets × ramps × entry speeds) for Measured rows
   (CT-011 entry).
2. Brake slope hold/release demonstration incl. wheel-joint behavior and
   authority-handoff release (CT-012 entry).
3. Staged stuck scenarios: blocked wheel, grounded chassis, true overload
   (CT-013 entry).
4. Guidance walkthrough from stuck to freed (CT-014 entry).

### Gate decision

All automatable v0.3 gates are green; manual items are pending by design
for an internal RC. Sprint controller #120 closes with this seal.

## v0.2 RC1 — "Cargo and Load Planning" (sealed 2026-09-04)

| Item | Value |
|---|---|
| Version | 0.2.0 (internal; no publication) |
| Source commit | `709a7dc23108ba22a5c0bf466b83380a0b1f36b2` (branch `chore/ct-010-v02-rc`, merged to main via PR) |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.2.0.zip` |
| ZIP SHA-256 | `9834c4faf98e2deed9038b6e4abc10ddb2d8a3bfde28580a159ae208feb8f60c` |
| DLL SHA-256 | `3525219320db583ebacc31bc0b1b665a2bf9c2e807707aee068e78e4078d6fad` |
| DLL identity | AssemblyVersion 0.2.0.0, InformationalVersion `0.2.0+709a7dc23108ba22a5c0bf466b83380a0b1f36b2` |
| ZIP contents (6 entries) | `manifest.json`, `icon.png` (256×256), `README.md`, `CHANGELOG.md`, `LICENSE`, `plugins/TheConcernedCat.ConcernedTeamster.dll` — audited: **only Teamster's own DLL**, no game/framework binaries, no PDB |
| Built against | Valheim 0.221.12 (network 36, buildid 21981559), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.2.0 across csproj, `Plugin.cs`, `thunderstore.toml`, `CHANGELOG.md` — validator-asserted with `--expected-version 0.2.0 --require-binary` |

### Sprint scope sealed in this RC

CT-006 immutable cargo manifest (quality-scaled weights, unreadable-slot
markers, tracker-bounded refresh) · CT-007 sortable/filterable manifest UI
(deterministic sort matrix, localized filtering, explicit states) · CT-008
calibration protocol + versioned data + dominance-only LoadModel (honest
Unknown; 0 measured rows, stated) · CT-009 load/grade warnings
(anti-flicker hysteresis, actionable non-color text, Unknown-never-warns).

### v0.2 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Repository/package static validation | `python tools/validate_repo.py --product teamster --expected-version 0.2.0 --require-binary` | PASS |
| Solution build | `pwsh ./scripts/build.ps1 -Configuration Release` | PASS — 0 errors |
| Domain/adapter unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **186/186 PASS** — manifest totals/ordering/immutability, tracker call-count bounds, presenter sort/filter matrices, load model dominance + 9,801-query monotonicity grid + shipped-file reproducibility, warning hysteresis single-transition-pair + evaluation-count discipline |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | 568/568 PASS |
| Package build + audit | `pwsh ./scripts/package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only confirmed |
| Manifest-vs-vanilla-accounting consistency | code-level: line weights are the game's own `GetWeight()`; float-order caveat documented in CART_INTERNALS.md | PASS (by construction; in-game visual check pending) |

Defects: no `DEF-teamster-v0.2-*` issues were needed (one in-scope test
defect — an oscillation-test baseline off-by-one — was caught and fixed
inside CT-009 before merge). No open P0/P1 with the sprint label.

### Pending manual claims added by v0.2 (owner smoke checklist accumulator)

Carried v0.1 items remain (dossier below). New:

1. Manifest-vs-container screenshot: same items/counts/weights as the
   vanilla container UI, including quality-scaled gear (CT-006 entry).
2. Manifest panel UX: sorting, filter typing, localized names, full-cart
   responsiveness feel, ▲▼ glyph rendering (CT-007 entry).
3. Calibration protocol runs (5 sets × 3 ramps × 2 reps) to produce the
   first `Measured` rows; gravity note (CT-008 entry).
4. Warning transcript on a built test slope: caution rise/hold/release,
   panel row, optional HUD hint while pulling (CT-009 entry).

### Gate decision

All automatable v0.2 gates are green; manual items are pending by design
for an internal RC. Sprint controller #114 closes with this seal.

## v0.1 RC1 — "Cart Truth" (sealed 2026-09-04)

| Item | Value |
|---|---|
| Version | 0.1.0 (internal; no publication) |
| Source commit | `3bdc06daf415fa1213429f24a3e78d3a27067139` (branch `feat/ct-005-cart-status-panel-rc`, merged to main via PR) |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.1.0.zip` |
| ZIP SHA-256 | `9f6b667948f893d8be99afe9a03efc7aacb9dc4cf29360d9bf3a5ef8455beeff` |
| DLL SHA-256 | `e9cbd3234c44216bffc337f38967df3584778ea93ff472ae436888e072f0258f` |
| DLL identity | `TheConcernedCat.ConcernedTeamster.dll`, AssemblyVersion 0.1.0.0, InformationalVersion `0.1.0+3bdc06daf415fa1213429f24a3e78d3a27067139` |
| ZIP contents (6 entries) | `manifest.json`, `icon.png` (256×256), `README.md`, `CHANGELOG.md`, `LICENSE`, `plugins/TheConcernedCat.ConcernedTeamster.dll` — audited: **only Teamster's own DLL ships**, no game/framework binaries, no PDB |
| Built against | Valheim 0.221.12 (network 36, buildid 21981559), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.1.0 across `ConcernedTeamster.csproj`, `Plugin.cs`, `thunderstore.toml`, `CHANGELOG.md` (validator-enforced) |

### Sprint scope sealed in this RC

CT-001 bootstrap · CT-002 verified cart adapter + capability probe (18
members) · CT-003 bounded telemetry sampler with fail-closed resets ·
CT-004 deterministic grade math + surface classification · CT-005 Cart
Status panel over a headless presenter.

### v0.1 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Repository/package static validation | `python tools/validate_repo.py` | PASS (both products; Teamster adapter-isolation check active) |
| Solution build | `pwsh ./scripts/build.ps1 -Configuration Release` | PASS — 0 errors (4 pre-existing benign warnings) |
| Domain/adapter unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **127/127 PASS** — probe (18-member mirror, every simulated-missing-member disable path), sampler (interval, attempt budget, round-robin, store cap, eviction, reset, zero-allocation steady state), grade fixtures (flat/slopes/crest/dip/noisy no-oscillation), paint table, presenter (every displayed string, stale/no-cart/off states, sticky selection) |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | 568/568 PASS |
| Package build + audit | `pwsh ./scripts/package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only confirmed |
| Version synchronization | validator | PASS |

Defects found by automation during the sprint: none open (no `DEF-teamster-v0.1-*` issues were needed).

### Pending manual claims (owner smoke checklist accumulator)

Never marked PASS by automation; each is ledgered in `HUMAN_ATTENTION.md`
with its safe default. All require the TCT profiles (automated by CT-043)
and a disposable world:

1. Clean load: BepInEx log shows the Teamster banner + `Cart telemetry
   capability ENABLED: 18 game members verified.` + sampler/panel armed
   lines; no errors (CT-002/CT-003 entries).
2. Vanilla truth baseline (TCT-Clean vs TCT-Dev): displayed mass/cargo vs
   known cargo set — the displayed-vs-expected cargo spot check (CT-003
   entry).
3. Grade spot check on a built dirt slope vs flat ground, including the
   pull-handle heading sign convention (CT-004 entry).
4. Panel visual/UX: Cart button visible in-world only, panel opens/closes
   (button, Escape, optional shortcut), rows readable, drag works, stale
   marking visible when walking away from a cart (CT-005).
5. Cart lifecycle: build/attach/detach/destroy/rebuild — panel follows
   reality with no stale identity.
6. World lifecycle: logout/login, world switch, character switch — no
   leaked telemetry, no exceptions.
7. Uninstall safety: remove the DLL, load the world, vanilla behavior, no
   missing-object errors.
8. 30-minute hauling session with the panel open: no visible frame-time
   spikes attributable to Teamster; log volume bounded.

### Gate decision

All automatable v0.1 gates are green; manual items are pending by design
for an internal RC (they accumulate into the owner smoke checklist and the
v0.9/v1.0 gates). Sprint controller #108 closes with this seal.
