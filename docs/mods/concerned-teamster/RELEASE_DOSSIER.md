# Concerned Teamster release dossier

Internal release candidates are sealed here with exact hashes and the honest
split between automated evidence and pending manual claims, following the
dossier discipline proven on Concerned Cartographer. Publication of anything
is owner-only, always.

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
