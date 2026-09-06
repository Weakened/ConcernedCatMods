# Concerned Teamster release dossier

Internal release candidates are sealed here with exact hashes and the honest
split between automated evidence and pending manual claims, following the
dossier discipline proven on Concerned Cartographer. Publication of anything
is owner-only, always.

## v0.8 RC1 — "Compatibility, Recovery, Scale" (sealed 2026-09-06)

| Item | Value |
|---|---|
| Version | 0.8.0 (internal; no publication) |
| Source commit | `859976ea4a692cd7c751d227236ac36dc7f09601` (branch `feat/ct-040-v08-rc-seal`; the src/ tree at this commit is byte-identical to the earlier version-sync commit `9b718c2`, which only docs-only commits sit on top of). Sealed on merge to main via the CT-040 PR. |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.8.0.zip` |
| ZIP SHA-256 | `9aa033e837d0f47d7869968e6fbc168e864115e4ae21384a3ea2e65e49851851` (142,165 B) — **a point-in-time fingerprint of this specific build's zip, not a reproducibility guarantee**; see the note below |
| DLL SHA-256 | `3a278c507a251a3e09b8690237f52581196e7e1034d18fd6b1afd2d1b3dc2118` (245,760 B) — confirmed stable across 2 consecutive clean rebuilds on this machine, but see the note below |
| DLL identity | AssemblyVersion 0.8.0.0, InformationalVersion `0.8.0+859976ea4a692cd7c751d227236ac36dc7f09601` (read back directly from the built DLL, confirming it correctly names its exact source commit) |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md, CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB, no foreign DLL |
| Built against | Valheim 0.221.12 (buildid 21981559 — re-verified in the Steam manifest at seal time, unchanged since the v0.7 seal), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.8.0 across csproj/Plugin.cs/thunderstore.toml + the CHANGELOG `## 0.8.0` section, validator-asserted (`--expected-version 0.8.0 --require-binary`) |

**Hash-reproducibility note (found by this RC's independent review, filed as
DEF-teamster-v0.8-001 / #214, P3, deferred):** rebuilding this exact
committed source from a clean state does **not** reliably reproduce the
same DLL bytes across different machines/environments (three distinct DLL
hashes were observed across the review's build and two of my own, despite
identical size and a correctly-named `InformationalVersion` every time),
and the ZIP hash changes on every single rebuild regardless of DLL
stability (root cause: `tcli build` embeds a build-time timestamp per zip
entry). Both hashes above are recorded as an honest fingerprint of the
actual artifact built, tested, and considered for this seal — proof that
*this specific file* wasn't silently swapped or corrupted after
validation — not as a claim that a third party rebuilding from source will
get byte-identical output. This property has existed since the v0.1 RC;
CT-040 is simply the first leaf where an independent rebuild-and-compare
was attempted, so it is the first to document it.

### Sprint scope sealed in this RC

CT-036 the runtime compatibility and capability framework (GUID-only mod
detection across four probe paths, a documented Coexist/Adapt/Warn policy)
· CT-037 Better Carts coexistence validated directly from its published
source and reclassified Adapt/CartMassOrPhysics after research found its
`Vagon.SetMass` Harmony prefix, extending the gate to every load-advice
consumer · CT-038 ItemStacks and ValheimPlus researched and registered
from verified source (Adapt/CartMassOrPhysics and Warn/None respectively)
· CT-039 bounded per-reason backup rotation, a pure tested persist-retry
plan that survives transient I/O failure and world switches without
losing or cross-contaminating trips, player-visible recovery events,
config schema migration, and a sanitized support-bundle export + panel ·
CT-040 worst-case scale evidence (max-retention trips, many-entry
manifests, dense-cluster long-session sampling) and this seal.

### v0.8 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Static validation + version sync | `validate_repo.py --product teamster --expected-version 0.8.0 --require-binary` | PASS |
| Solution build | `package.ps1 -Product ConcernedTeamster -Configuration Release` (invokes `build.ps1 -Configuration Release`) | PASS — 0 errors (3 pre-existing benign warnings) |
| Teamster unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **609/609 PASS** — +67 executed cases over the v0.7 baseline of 542 (CT-036 compatibility framework, CT-037 Better Carts correction, CT-038 two more registry entries, CT-039 migration/backup/support-bundle, CT-040 scale) |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | **568/568 PASS** — unchanged with the v0.8 work present |
| Scale campaign (CT-040) | `dotnet test --filter` over the four new scale tests (Release) | PASS — see scale measurement table below |
| Recovery/migration/support-bundle campaign rerun | `dotnet test --filter FullyQualifiedName~TripPersistenceTests\|TripPersistPlanTests\|ConfigSchemaMigrationTests\|SupportBundleTests` (Release) | **47/47 PASS**, fresh per-test temp-directory fixtures (no shared/stale state between runs) |
| Compatibility matrix campaign rerun | `dotnet test --filter FullyQualifiedName~CompatibilityFrameworkTests\|CompatibilityAdvisoryGateTests` (Release) | **18/18 PASS** |
| Cross-product independence + Cartographer contract + integration read-only | `validate_repo.py` interop lines | PASS — 4 trees independent, contract 12/12, 9 integration files read-only |
| Authority policy / no-force audits | `validate_repo.py` interop lines | PASS — 9 features documented, 149 Teamster source files, 0 violations (grown from 112 files at the v0.6 seal as CT-031..040 landed; no v0.8 leaf touches multiplayer/force paths) |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only (0 foreign/PDB entries), 6 entries |
| Corruption/recovery live campaign (kill mid-write, migrate a real v1 sidecar, export and read a real support bundle) | in-game | **MANUAL — pending** (the retry/discard/migration/sanitization LOGIC is unit-proven off-game per CT-039; live confirmation is pending by design for an internal RC) |
| Compatibility live campaign (BetterCarts / ItemStacks / ValheimPlus installed, one at a time and combined) | in-game, TCT-Compat | **MANUAL — pending** (the three compatibility-matrix rows in TEST_PLAN.md; GUID detection and the policy gate are unit-proven off-game) |
| Long real hauling session at configured-max scale | in-game | **MANUAL — pending** (the sampler ceiling/allocation-flatness finding below is proven synthetically against a fake cart world; a real dense-cart-cluster session is pending) |
| Standard suite (clean load, cart/world lifecycle, uninstall safety) | in-game | **MANUAL — pending** (carried from every prior RC; unchanged by v0.8's compatibility/recovery/scale-only scope) |

### Scale measurement table (CT-040)

One real run on this dev machine (Release build); loose, generous bounds
are what the automated tests actually assert (see `TEST_PLAN.md`'s scale
evidence section) so these exact figures are not brittle to normal
machine-to-machine or JIT/GC noise:

| Scenario | Configured bound exercised | Measured result |
|---|---|---|
| Trip sidecar at max retention | 500 trips (`MaxMaxTripsRetained`) × 20 samples each | Compose+write 23 ms; read+parse 27 ms; file size 474,750 B (~464 KiB) |
| Cargo manifest, many distinct items | 200 synthetic entries (stress input, not a vanilla-capacity claim) | Compose <1 ms (rounds to 0 ms) |
| Telemetry sampler, dense cluster, long session | 50 candidate carts, `MaxMaxTrackedCarts` 32, `MaxMaxCartsPerTick` 8, 2,000 ticks at `MinSampleIntervalSeconds` | Steady-state tracked-cart count stabilizes at **27**, never exceeds 32 (asserted every tick); all 50 candidates got at least one attempt (no starvation) |
| Telemetry sampler allocation, long session | Two consecutive 2,000-tick windows, 0.5 s interval, 50 candidates, both past warm-up | First window 2,434,192 B; second window 2,438,168 B (+0.16%) — flat, no growth with session length |

The tracked-cart ceiling (27 of a configured 32) is a **documented
tradeoff, not a defect**: `EvictAfterSeconds` floors at 2 s regardless of
how small the sample interval gets, and the round-robin window advances
by one candidate per tick, so in a cluster this dense a sample's
freshness window governs coverage before the hard cap does. This trades
maximum coverage for guaranteed freshness (a stale tracked cart is never
shown) — see `TelemetrySamplerTests.cs`'s
`Tick_MaxTrackedCartsOverALongSession_...` test comment for the full
derivation.

### Pending manual claims added by v0.8

1. Corruption/recovery walkthrough: kill the process mid-write and confirm
   the prior sidecar survives via rotated backup; load a real pre-v1
   sidecar and confirm migration + recovery-event surfacing; export a
   support bundle from a live session and confirm it reads clean with no
   world/player/path leakage (CT-039 entries).
2. Compatibility walkthrough: install BetterCarts, ItemStacks, and
   ValheimPlus (one at a time and combined) and confirm the Compat panel
   and load-advice gate match the researched, tested policy for each
   (CT-037/CT-038 entries; the three "pending in-game" rows in
   `TEST_PLAN.md`'s compatibility matrix).
3. Scale spot check: a long real hauling session in a dense cart cluster,
   watching for the documented tracked-cart-ceiling behavior and for any
   frame-time or log-volume regression the synthetic tests can't observe
   (CT-040 entry).
4. Standard suite (carried from every prior RC): clean load, cart/world
   lifecycle, uninstall safety.

### Defects

One defect filed against `sprint:teamster-v0.8` during this leaf's own
independent review: **DEF-teamster-v0.8-001 (#214, P3)** — release DLL/ZIP
builds are not bit-reproducible across rebuilds/environments (see the
hash-reproducibility note above). P3, no safety/correctness/gameplay
impact, deferred to a future hardening leaf per its own rationale — does
not block this gate (no open P0/P1 with the sprint label). The one
previously-open Teamster defect, #189 DEF-teamster-v0.4-001 (P3, sidecar
backup hardening), was fixed and closed as part of CT-039.

### Gate decision

All automatable v0.8 gates are green; the live campaign rows (corruption/
recovery, compatibility, scale spot check, standard suite) are pending by
design for an internal RC. Sprint controller #151 closes with this seal.

## v0.7 RC1 — "UX, Controller, Accessibility, Localization" (sealed 2026-09-06)

| Item | Value |
|---|---|
| Version | 0.7.0 (internal; no publication) |
| Source commit | `e3a177c357d41e67fe86ceccc655559a32d68deb` (branch `chore/ct-035-v07-rc`; supersedes the pre-review seal at `2601ec5` — the independent review found a stale packaged README.md, a catalog-count error, a dropped Release-config citation, and an unenforced version-sync check, all fixed before this rebuild). Sealed on merge to main via the CT-035 PR. |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.7.0.zip` |
| ZIP SHA-256 | `33c7257e74b71e6d4ea920185ebd4eb73a4b057bfe4f7d6eed4a771e07ff619e` (130,648 B) |
| DLL SHA-256 | `31a7a7e42094824f1d4d82db07bf289c282ad0a5c7068b8e0f0558db849c1e16` (218,624 B) |
| DLL identity | AssemblyVersion 0.7.0.0, InformationalVersion `0.7.0+e3a177c357d41e67fe86ceccc655559a32d68deb` |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md, CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB, no foreign DLL |
| Built against | Valheim 0.221.12 (buildid 21981559 — re-verified in the Steam manifest at seal time, unchanged since the v0.6 seal one day earlier), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.7.0 across csproj/Plugin.cs/thunderstore.toml + the CHANGELOG `## 0.7.0` section. `scripts/package.ps1` now derives `--expected-version` from the csproj automatically (this RC's own hardening, CT-035) rather than relying on someone typing it by hand each seal, so csproj/Plugin.cs/thunderstore.toml drift is caught on every future package build, not just this one |

### Sprint scope sealed in this RC

CT-031 controller navigation (deterministic focus catalog + ring) and
accelerator conflict checking (internal + external, buttons-first audited)
· CT-032 the localization framework and full 244-key catalog externalization
across every panel, with a CI-gating hardcoded-string audit · CT-033 UI
scale (0.8–1.3, applied per-panel via root-transform scaling), a WCAG AA
contrast audit and fix (outline added where missing after a sensitivity
check found a real risk), and a tested non-color-cue invariant · CT-034
first-run onboarding, three documented config profiles (brake opt-in in
every one, idempotent apply), a discoverability audit, and config
migration safety for the new profile surface · CT-035 integration and this
seal.

### v0.7 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Static validation + version sync | `validate_repo.py --product teamster --expected-version 0.7.0 --require-binary` | PASS |
| Solution build | `package.ps1 -Product ConcernedTeamster -Configuration Release` (invokes `build.ps1 -Configuration Release`) | PASS — 0 errors (3 pre-existing benign warnings) |
| Teamster unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **542/542 PASS** — +84 executed cases over the v0.6 baseline of 458 (CT-031 navigation/conflict, CT-032 audit + catalog, CT-033 scale/contrast/cue, CT-034 onboarding/profiles) |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | **568/568 PASS** — unchanged with the v0.7 work present |
| Cross-product independence + Cartographer contract + integration read-only | `validate_repo.py` interop lines | PASS — 4 trees independent, contract 12/12, 9 integration files read-only |
| Authority policy / no-force audits | `validate_repo.py` interop lines | PASS — unchanged from v0.6 (no v0.7 leaf touches multiplayer/force paths) |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only (0 foreign/PDB entries) |
| Controller-only full walkthrough (gamepad focus + accelerators) | in-game, real gamepad | **MANUAL — pending** (CT-031 logic unit-proven off-game; the live gamepad read surface needs verification before wiring, per HUMAN_ATTENTION) |
| Localization fallback checks (missing key → English + once-only log) | in-game | **MANUAL — pending** (proven off-game by `TeamsterStringsTests`; the in-game log excerpt itself is unrecorded) |
| Scale/contrast spot checks | in-game, TCT-Dev at 0.8/1.0/1.3 scale | **MANUAL — pending** (CT-033 HUMAN_ATTENTION entry: neighboring-panel crowding, actual canvas-bounds fit, wood-panel background color-pick) |
| Onboarding fresh-profile run | in-game, TCT-Clean | **MANUAL — pending** (CT-034 HUMAN_ATTENTION entry: hint text wrap/placement, profile-switch visibility, manual-edit survival across a restart) |
| Standard suite (clean load, cart/world lifecycle, uninstall safety) | in-game | **MANUAL — pending** (carried from every prior RC; unchanged by v0.7's UI/config-only scope) |

### Campaign — automated coverage vs pending live rows

Every v0.7 acceptance criterion decidable without a running game is green:
the navigation/conflict logic, the full localization catalog and its
fallback/placeholder rules, the scale-clamp and contrast-ratio math (plus
the tallest-panel-vs-reference-canvas margin), and the onboarding/profile
state machines are all unit-proven. The rows that need an actual Valheim
session — real gamepad focus movement, an on-screen fallback log excerpt,
visual scale/contrast at each setting, and a fresh-profile onboarding
walkthrough — are itemized pending in `HUMAN_ATTENTION.md` (the CT-031,
CT-033, and CT-034 entries; CT-032's entry is Resolved) and carried to the
owner smoke checklist. No manual row is marked PASS.

### Defects

No defect filed against `sprint:teamster-v0.7`; none open with the sprint
label at seal time (`gh issue list --label sprint:teamster-v0.7 --label bug`
returns empty). The one open Teamster defect, #189 DEF-teamster-v0.4-001
(P3), remains sidecar-backup hardening scoped to v0.4 — not in any v0.7
leaf's scope — and stays deferred to the v0.8 migration/recovery line
(CT-039) by its own rationale.

### Gate decision

All automatable v0.7 gates are green; the live campaign rows (controller,
localization, scale/contrast, onboarding, standard suite) are pending by
design for an internal RC. Sprint controller #145 closes with this seal.

## v0.6 RC1 — "Multiplayer Trust and Authority" (sealed 2026-09-05)

| Item | Value |
|---|---|
| Version | 0.6.0 (internal; no publication) |
| Source commit | `12dbd1617160e1433ac3320c22e568b66c182cc1` (branch `chore/ct-030-v06-rc`; version-sync + changelog committed before the RC rebuild so the shipped DLL names this exact commit). Sealed on merge to main via PR #199. |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.6.0.zip` (`.sha256` sidecar written beside it) |
| ZIP SHA-256 | `b1929a245a2b347651adb86536b2f3b0555e6dfca7300a7502e360cacd2da100` (117,197 B) |
| DLL SHA-256 | `439025e1018f0059f0031c2d4941c9b6f93a3bc824f58834dc2d535b5b302c49` (183,808 B) |
| DLL identity | AssemblyVersion 0.6.0.0, InformationalVersion `0.6.0+12dbd1617160e1433ac3320c22e568b66c182cc1` |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md (refreshed to v0.6 scope), CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB, no foreign DLL |
| Built against | Valheim 0.221.12 (buildid 21981559 — Steam manifest at seal time), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.6.0 across csproj/Plugin.cs/thunderstore.toml + the CHANGELOG `## 0.6.0` section, all validator-asserted (`--expected-version 0.6.0 --require-binary`) |

### Sprint scope sealed in this RC

CT-026 enforced authority policy (single-source-of-truth matrix; brake
mutation gated to live local authority; fail-closed Unknown; owner-fresh
observations remote-labeled) · CT-027 player-hosted + dedicated-server
authority scenarios proven at the logic layer (topology-independent) ·
CT-028 cooperative push/pull diagnostics (helping/hindering/idle +
combined-effort explanation) with a zero-force audit · CT-029 network-input
hardening (bounds/validate/fuzz) + committed privacy inventory · CT-030
integration and this seal.

### v0.6 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Static validation + version sync | `validate_repo.py --product teamster --expected-version 0.6.0 --require-binary` | PASS |
| Solution build | `build.ps1 -Configuration Release` | PASS — 0 errors (8 pre-existing benign warnings) |
| Teamster unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **458/458 PASS** — +87 executed cases over the v0.5 baseline of 371 (CT-026 policy, CT-027 scenarios, CT-028 coop, CT-029 hardening incl. a 10k-iteration seeded fuzz sweep) |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | **568/568 PASS** — unchanged with the v0.6 work present |
| Authority policy audit | `validate_repo.py` interop line | PASS — 9 features documented, no outbound-network/ownership calls (0 violations) |
| No-force / no-teleport audit | `validate_repo.py` interop line | PASS — 112 source files, no force/impulse/velocity-write/teleport calls (0 violations) |
| Cross-product independence + Cartographer contract + integration read-only | `validate_repo.py` interop lines | PASS — 4 trees independent, contract 12/12, 9 integration files read-only |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only (0 foreign/PDB entries) |
| Full multiplayer campaign on both topologies (CT-027 scenarios + co-op + hardening spot checks, live) | in-game, TCT-Dedicated + player-hosted | **MANUAL — pending** (the authority/coop/hardening LOGIC is unit-proven off-game; the live two-client and dedicated-server runs are pending by design for an internal RC) |

### Multiplayer campaign — automated coverage vs pending live rows

Every v0.6 acceptance criterion that can be decided without a running server
is green (policy matrix, brake authority gate, handoff logic, coop
classification, input bounds/fuzz, privacy audit). The rows that require a
real session — authority actually moving between two clients, an unmodded
peer observed as unaffected, join/leave/disconnect mid-haul, and the staged
co-op scenario — are itemized pending in HUMAN_ATTENTION.md (CT-021..CT-029
entries) and carried to the owner smoke checklist. No manual row is marked
PASS.

### Defects

No defect filed against `sprint:teamster-v0.6`; none open with the sprint
label at seal time. The one open Teamster defect, #189 DEF-teamster-v0.4-001
(P3), is sidecar-file backup hardening scoped to v0.4 — not network input,
not in any v0.6 leaf's scope — and remains deferred to a dedicated leaf by
its own rationale.

### Gate decision

All automatable v0.6 gates are green; the live two-topology multiplayer
campaign rows are pending by design for an internal RC. Sprint controller
#139 closes with this seal.

## v0.5 RC1 — "Optional Cartographer Integration" (sealed 2026-09-05)

| Item | Value |
|---|---|
| Version | 0.5.0 (internal; no publication) |
| Source commit | `f74279f177bca6a7d8132a60a4a521f6a90210ca` (branch `chore/ct-025-v05-rc`; version-sync + changelog committed before the RC rebuild so the shipped DLL names this exact commit, not a dirty-tree build). Sealed on merge to main via PR #194. |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.5.0.zip` (`.sha256` sidecar written beside it for drift detection) |
| ZIP SHA-256 | `483b23899557a8e4e69cdcb9bd6e8cc6f498f7f565dcd6ce216635edc8a94654` (112,948 B) |
| DLL SHA-256 | `d5d6d12390421b09dbd1e8ebbb7b8dea055ba00d5f7f8035f8f42db31fb73a52` (177,152 B) |
| DLL identity | AssemblyVersion 0.5.0.0, InformationalVersion `0.5.0+f74279f177bca6a7d8132a60a4a521f6a90210ca` |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md (refreshed to v0.5 scope), CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB, no foreign DLL |
| Built against | Valheim 0.221.12 (buildid 21981559 — re-verified in the Steam manifest `appmanifest_892970.acf` at seal time), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.5.0 across csproj/Plugin.cs/thunderstore.toml, validator-asserted (`--expected-version 0.5.0 --require-binary`); the CHANGELOG `## 0.5.0` section is validator-asserted too |

### Sprint scope sealed in this RC

CT-021 runtime Cartographer capability + version adapter (GUID/version
floor 0.10.0, 12-member reflective read contract, four detection paths,
no compile-time dependency either direction) · CT-022 route picker
(eligibility rule, id-keyed selection surviving renames and invalidating
explicitly) · CT-023 budgeted route profiler (per-frame-capped terrain
sampling, honest sampled/unsampled partition, grade histogram, worst
segments, LoadModel-verbatim bottleneck, geometry-fingerprint cache) ·
CT-024 route report (numbered problem sections, located unsampled spans,
model-traced recommendations) · CT-025 coexistence validation + this seal.

### v0.5 campaign results (automated)

| Campaign item | Method | Result |
|---|---|---|
| Static validation + version sync | `validate_repo.py --product teamster --expected-version 0.5.0 --require-binary` | PASS |
| Solution build | `build.ps1 -Configuration Release` | PASS — 0 errors (8 pre-existing benign warnings) |
| Teamster unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **371/371 PASS** — +84 executed cases over the v0.4 baseline of 287, across the five new v0.5 test files (CT-021 gate + route-read contract, CT-022 picker, CT-023 profiler/cache/bottleneck, CT-024 report) |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | **568/568 PASS** — unchanged with the Teamster integration present in the solution |
| Cross-product independence audit | `validate_repo.py` interop line | PASS — 4 project trees, no ProjectReference/Reference/PackageReference/Compile/using/InternalsVisibleTo coupling either direction |
| Cartographer contract drift tripwire | `validate_repo.py` interop line | PASS — 12/12 contract members present at source level (kind-pinned patterns) |
| Integration read-only audit (CT-021..024 path) | `validate_repo.py` interop line | PASS — 9 integration files free of mutating/invoking reflection (SetValue/SetField/.Invoke(/GetMethod(/GetMethods(/InvokeMember/Activator.CreateInstance/CreateDelegate) |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only confirmed (0 foreign/PDB entries) |
| Coexistence matrix (both mods in-game; Teamster alone; Cartographer alone; version-mismatch floor) | in-game, TCT-Compat profile | **MANUAL — pending** (see below; the four detection paths themselves are unit-proven off-game by the CT-021 gate tests) |

### Coexistence matrix — automated coverage vs pending in-game rows

| Row | What it proves | Automated evidence | In-game status |
|---|---|---|---|
| Both mods loaded, integration on | Detection → Available; routes list, profile, report | CT-021 `Evaluate_CompleteSurfaceAtFloorVersion_Available` + reader/picker/profiler/report suites over fake catalogs | pending |
| Teamster alone | Integration hidden with one INFO line; no Routes button | CT-021 `Evaluate_NotFound_Absent`; picker/report never instantiated when `IsAvailable` is false | pending |
| Cartographer alone | Unaffected by Teamster's presence | Cartographer 568/568 regression green with Teamster in the solution | pending |
| Version mismatch | Below-floor / missing-version hides integration | CT-021 `Evaluate_VersionBelowFloor_VersionTooLow`, `Evaluate_MissingVersion_VersionTooLow` | pending |

No new exceptions can arise in either mod's logs from the integration by
construction — the whole path is reflection reads guarded to fail closed —
but the "no new log exceptions during real coexistence runs" acceptance row
is an in-game observation and stays pending.

### Pending manual claims added by v0.5

1. In-game coexistence matrix in TCT-Compat: all four rows behave per spec
   with captured logs, and neither mod logs a new exception (CT-025).
2. Cartographer-installed integration walkthrough: Routes button appears,
   route picker lists and selects, profiler runs with UNSAMPLED shown over
   unloaded terrain, report renders problem sections and a load line
   matching the pulled cart (CT-021..024 HUMAN_ATTENTION entries).
3. Cartographer-absent check: no Routes button, one INFO line stating the
   integration is hidden (CT-021/022 entries).

### Defects

No defect filed against `sprint:teamster-v0.5`. No open P0/P1 with the
sprint label at seal time. (The one open Teamster defect, #189
DEF-teamster-v0.4-001, is P3, scoped to v0.4, and deferred to the v0.6
hardening line by its own rationale.)

### Gate decision

All automatable v0.5 gates are green; the coexistence-matrix rows that
require a running game are pending by design for an internal RC. Sprint
controller #133 closes with this seal.

## v0.4 RC1 — "Road Quality and Trip Profiles" (sealed 2026-09-05)

| Item | Value |
|---|---|
| Version | 0.4.0 (internal; no publication) |
| Source commit | `b788272fb471707ad44cb8766b00d1cdf7903240` (branch `chore/ct-020-v04-rc`, merged to main via PR; supersedes the pre-review seal at `6ad2c8f` — the focused review found package-metadata and evidence-accuracy defects, fixed before merge) |
| ZIP | `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-0.4.0.zip` (`.sha256` sidecar written beside it for drift detection) |
| ZIP SHA-256 | `30e877b85efc72f81caddee7ddd69a7ddb14d94e1f2e677ca947f12b7688f8fc` (97,592 B) |
| DLL SHA-256 | `b7b301c3ac5b5313d76267377cd66b24cb24648c86e5a7a932c7ed4f830346ce` (141,312 B) |
| DLL identity | AssemblyVersion 0.4.0.0, InformationalVersion `0.4.0+b788272fb471707ad44cb8766b00d1cdf7903240` |
| ZIP contents (6 entries) | manifest.json, icon.png (256×256), README.md (refreshed to v0.4 scope), CHANGELOG.md, LICENSE, plugins/TheConcernedCat.ConcernedTeamster.dll — **own DLL only**, no PDB |
| Built against | Valheim 0.221.12 (network 36, buildid 21981559 — re-verified in the Steam manifest at seal time), Unity 6000.0.61f1, BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Version sync | 0.4.0 across csproj/Plugin.cs/thunderstore.toml — validator-asserted (`--expected-version 0.4.0 --require-binary`); the CHANGELOG `## 0.4.0` section is now ALSO validator-asserted (check added by this gate after review caught the dossier claiming it; negative-tested against 0.5.0) |

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
| Durability spot checks (kill-during-write, world isolation) | `dotnet test --filter FullyQualifiedName~TripPersistenceTests` (Release, real filesystem) | **15/15 PASS** — a simulated kill leaves the previous file intact and the next write swaps cleanly; a wrong-world file is refused with zero trips loaded (filename AND header); unknown future versions refused; the backup primitive copies byte-for-byte; malformed rows skipped and reported; pruning caps hold. Honest attribution: v1-migration *detection and recompute* are proven at the domain level (parse flag + `MergeAndCompose`); the service's backup *call ordering* before a migrating/refused rewrite is IO wiring verified by review only — ledgered as DEF-teamster-v0.4-001 (P3, deferred) |
| Retention over many trips | gate test: 200 real read-merge-prune-write cycles at a 50-trip cap, where the merge step is `TripSidecar.MergeAndCompose` — the same production code `TripRecordingService.Persist` runs (extracted during review so the gate cannot drift from the shipped cycle) | PASS — cap held, newest trips kept, ids renumber densely, file size bounded (≤10% drift after the cap), segment scores keep all 200 trips' history while raw trips prune |
| Package build + audit | `package.ps1 -Product ConcernedTeamster` + ZIP listing | PASS — hashes above, own-DLL-only confirmed |
| Save/network mutation audit | grep `ZDO.Set\|SetOwner\|InvokeRPC` in Teamster source | PASS — sole hit is the CartBrakeAdapter doc comment stating their absence |
| Sidecar write-path audit | grep every `File.*` write API + all `SidecarFileStore` callers | PASS — every write API lives in `SidecarFileStore.cs`; every caller path comes from `SidecarPathFor` → `BepInEx/config/ConcernedCatMods/ConcernedTeamster/`; no world-save write exists |
| Real-trip campaign rows (record hauls; verify history/comparison/bottlenecks and score sanity against them) | in-game, interactive | **MANUAL — pending** (HUMAN_ATTENTION.md CT-016..CT-019 entries; listed below) |

Defects: the focused review of the gate PR (#188) found package-metadata
staleness (README/description still describing v0.1), one user-facing
CHANGELOG claim contradicting shipped refusal semantics, two
evidence-attribution gaps in this dossier's draft, and gate-test
fidelity/diagnosability issues — all fixed in-branch before merge
(commit `b788272`). One deferred defect was filed:
**DEF-teamster-v0.4-001 (P3)** — refused/migrate sidecar backups use a
fixed name with `overwrite: true` (a repeat refusal clobbers the prior
backup) and the service's backup call-ordering has no automated coverage
because `TripRecordingService` is BepInEx-bound and untestable; deferred
with rationale per the sprint contract. No open P0/P1 with the sprint
label at seal time.

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
