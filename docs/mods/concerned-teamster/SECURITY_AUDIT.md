# Concerned Teamster v0.9 security self-audit

CT-042. A committed, evidence-backed pass over the four areas the v0.9
sprint's security self-audit scope names: dependency pins, secrets, the
package copy-list, and adapter fail-closed behavior. No pre-existing
audit-checklist template exists elsewhere in this repo (Concerned
Cartographer's own release docs use ad-hoc, per-release adversarial
passes rather than a reusable checklist) — this document's structure is
CT-042's own scope, made concrete and checked against the actual v0.8
source tree at the time of this audit (commit history at `feat/ct-042-*`,
built on the CT-041 freeze).

Every finding below is either **resolved in this same PR** or **filed as
a defect** against the active sprint, per the Definition of Done. None
found are release-blocking (no P0/P1).

## 1. Dependency pins

| Check | Result |
|---|---|
| `Package/thunderstore.toml` dependencies pinned to exact versions | PASS — `denikson-BepInExPack_Valheim = "5.4.2333"`, `ValheimModding-Jotunn = "2.29.2"`, no range operators |
| `ConcernedTeamster.csproj` NuGet reference pinned | PASS — `<PackageReference Include="JotunnLib" Version="2.29.2" />`, exact match to the Thunderstore dependency above |
| No floating/wildcard version anywhere in package metadata | PASS — grepped `thunderstore.toml` and `.csproj` for `*`, `^`, `~`, `>=`; none found |
| Matches `docs/DEVELOPMENT.md`'s repo-wide dependency policy ("pin Thunderstore dependencies... pin the Jötunn NuGet package... never commit game or loader DLLs") | PASS |

## 2. No secrets in repo or package

| Check | Result |
|---|---|
| Pattern scan for credential-shaped strings (`-----BEGIN`, `api_key=`, `password=`, `secret=`, AWS/GitHub/Slack token prefixes) across `src/ConcernedTeamster/` and `docs/mods/concerned-teamster/` | PASS — zero matches |
| Search for credential-shaped filenames (`.env`, `*credential*`, `*.pem`, `*.pfx`, `*.key`) | PASS — none found |
| Package copy-list (below) includes no config file, no local path, no machine-specific value | PASS |
| `support.md`/`SupportBundleSanitizer` itself does not leak local secrets into a shared bundle | Already proven by CT-039's `SupportBundleTests` (realistic planted paths/usernames/URLs asserted absent from composed output); re-confirmed still passing in this leaf's full-suite run |

## 3. Package copy-list minimal

`thunderstore.toml`'s `[build]`/`[[build.copy]]` blocks are the complete
list of what ships; verified against the actual sealed v0.8 ZIP
(`RELEASE_DOSSIER.md`'s v0.8 RC1 entry, 6 entries):

```
manifest.json                                    (tcli-generated from [package])
icon.png                                         (own icon)
README.md                                        (this package's own)
CHANGELOG.md                                     (own)
LICENSE                                          (repo root, MIT)
plugins/TheConcernedCat.ConcernedTeamster.dll    (own DLL only)
```

No PDB, no foreign DLL, no game/framework binary — audited every RC seal
since v0.1 and re-confirmed unchanged by this leaf (CT-042 touches no
`[build]`/`[[build.copy]]` line).

## 4. Adapter fail-closed review

Every file under `Adapters/` was reviewed for how it handles an
unexpected failure. Two structurally different, both legitimate,
fail-closed patterns are in use:

- **Checked-return-value fail-closed** (no exceptions needed): file I/O
  (`SidecarFileStore`, and everything built on it — `TripRecordingService`,
  `SupportBundleExporter`) and brake physics
  (`CartBrakeAdapter.TryEngage`/`TryRelease`) report failure as a
  `bool`/`out string? error` result, never relying on `catch`. This is
  arguably the more disciplined pattern for expected failure modes
  (disk full, permission denied, a destroyed cart) since it does not
  depend on exception-based control flow.
- **try/catch-and-disable-for-the-session** (every `Ui/*Panel.cs` file):
  wraps the panel's methods in try/catch, logs one `LogError` line, and
  sets a `_failed` flag so the panel becomes inert rather than risking a
  repeat failure every frame or every click.

Two findings from cross-checking every adapter against these two
patterns:

| Finding | Severity | Outcome |
|---|---|---|
| `SupportBundleExporter.Export`'s doc comment claimed "Never throws," but `GatherSidecarSummaries`'s directory enumeration could in principle throw on a filesystem race (a sidecar deleted or re-permissioned between listing and reading) | LOW (documentation accuracy; already fail-safe in practice via the caller's try/catch) | **Fixed in this PR** — comment corrected to name the narrow exception and note `SupportBundlePanel.HandleExportClicked`'s existing outer catch already contains it |
| `CartTelemetryPump.Update()` — the per-frame driver for sampling, warnings, descent risk, stuck diagnostics, trip recording, and the brake tick — has no top-level try/catch of its own, unlike every `Ui/*Panel.cs` file. An uncaught exception here would not crash the game (Unity's per-frame dispatch survives a `MonoBehaviour.Update` throwing) but would most likely repeat every frame instead of failing closed with one log line, as the mod's own stated principle promises | MEDIUM/P2 (no known reproduction; structural hardening gap, not an active bug) | **Filed as DEF-teamster-v0.9-002 (#217)**, deferred to CT-044 — CT-042 is a documentation/audit leaf, not a code-change leaf, and this fix touches the core telemetry driver, which deserves its own focused review rather than a rushed change bundled into an audit PR |

No other adapter file was found missing both patterns; the two reflective
probes (`CompatibilityAdapter`, `CartographerCapability`) only touch
`Chainloader.PluginInfos` and null-conditional metadata reads directly —
the actual reflection risk (reading a live Cartographer object's fields)
is isolated in `Domain/Cartographer/CartographerRouteReader`, a separate,
narrowly-scoped reader, not spread across the capability adapter itself.

## 5. Privacy audit (cross-reference)

CT-041 already added a CI-gated `check_teamster_no_internet_egress`
validator check (zero internet-egress-capable APIs anywhere in Teamster
source, `Application.OpenURL` named as the one data-free exception) and
`PRIVACY_INVENTORY.md` already documents the complete stored/displayed/
logged inventory. Not re-litigated here; both remain green and are
summarized in the package README's own privacy paragraph (this leaf).

## 6. Repository-wide security policy cross-check

`SECURITY.md`'s "Security boundaries" section (written in Cartographer-
specific language, but its principles are repo-wide) — checked against
Teamster's actual behavior:

| Boundary | Teamster status |
|---|---|
| Never require elevated/admin privileges | PASS — a normal BepInEx plugin DLL |
| Never execute downloaded code | PASS — no dynamic code loading anywhere |
| Send no telemetry | PASS — CT-041's privacy audit |
| Treat network payloads as untrusted | PASS — CT-029's network-input hardening (v0.6), unchanged |
| Never mutate Valheim world saves as private persistence | PASS — every write goes through `SidecarFileStore` to `BepInEx/config/ConcernedCatMods/ConcernedTeamster/`, audited every RC seal |
| AI-generated code is not exempt from review; network/filesystem/deserialization/reflection/migration changes receive extra scrutiny | PASS — every leaf touching these areas (CT-021 reflection, CT-029 hardening, CT-039 migration/filesystem) went through an independent review round before merging, most with two rounds after round-1 fixes introduced their own new findings |

## Outcome

- Findings: 2 (1 fixed inline, 1 filed as DEF-teamster-v0.9-002 / #217, P2, deferred to CT-044).
- No P0/P1 findings; the sprint gate is not blocked.
- Re-run this audit's mechanical checks (dependency pins, secret scan, copy-list) at the v0.9 RC seal (CT-045) to confirm nothing regressed between CT-042 and the seal.
