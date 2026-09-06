# Concerned Teamster v0.9 feature and default freeze

CT-041. This document is the frozen surface for the v0.9 public beta:
every shipped feature and every config default, as of this freeze. From
this point through the end of the v0.9 sprint (controller #157), changes
inside v0.9 are **defect fixes only** — no new feature, no changed
default, without its own dedicated issue outside this sprint. CT-042
through CT-045 build the beta's docs, profiles, and defect burn-down on
top of exactly this surface; they do not add to it.

## Feature list (frozen)

Everything below shipped in v0.1 through v0.8 and is live in the beta.
See `Package/CHANGELOG.md` for the per-version delivery history and
`RELEASE_DOSSIER.md` for each version's sealed evidence.

1. **Cart Status panel** (v0.1) — total mass with base/cargo breakdown,
   live terrain grade and climb/descend state, ground surface,
   attachment/pull state, data freshness.
2. **Cargo manifest and load planning** (v0.2) — sortable/filterable
   manifest with quality-scaled weights, a calibrated safe-load model
   (honest "uncalibrated" when unmeasured), live load/grade warnings.
3. **Descent safety and recovery** (v0.3) — calibrated descent-risk model,
   explicit reversible parking brake (never saved), stuck-cause
   diagnostics, numbered vanilla-legal recovery steps.
4. **Trip recording and road quality** (v0.4) — per-world sidecar trip
   recording, 8 m segment road-quality scoring, a Trips panel with
   history, A/B comparison, and route bottleneck analysis.
5. **Optional Cartographer integration** (v0.5) — reflection-only,
   no-hard-dependency route picker and terrain profiler when Concerned
   Cartographer 0.10.0+ is present; absent otherwise, with no error.
6. **Multiplayer trust and authority** (v0.6) — an enforced read/act/
   observe policy; the brake only engages under live local authority;
   cooperative diagnostics; every network-derived value bounded as
   hostile input; Teamster sends nothing and takes no ownership.
7. **UX, controller, accessibility, localization** (v0.7) — panel scaling
   (0.8–1.3×), WCAG AA contrast, non-color cues everywhere, full gamepad
   focus/accelerator coverage, a 277-key localization catalog with
   English fallback, first-run onboarding, three settings presets.
8. **Compatibility awareness** (v0.8) — a researched, GUID-only registry
   of known mods (BetterCarts, ItemStacks, ValheimPlus) with a documented
   Coexist/Adapt/Warn policy; a Compat panel always shows what was
   detected; an unrecognized mod is silent.
9. **Recovery, migration, and support bundle** (v0.8) — bounded backup
   rotation for the trip sidecar, a pure tested persist-retry plan,
   config schema migration, and a sanitized diagnostic export (see
   `RECOVERY.md`).
10. **Feedback path** (v0.9 / CT-041, this freeze) — a Report a Bug button
    on the Support panel routing to the public GitHub issue tracker, with
    guidance on what to attach (the support bundle) and what never to
    attach.

Read-only, bounded telemetry with hard performance caps underlies all of
the above; every game-facing surface is verified at startup and fails
closed with one actionable log line if a game update changes cart
internals (unchanged since v0.1, not re-listed as a numbered feature).

## Default-value audit (frozen)

Every `TeamsterSettings` config default, its source of truth, and its
safety classification. "Safe/observational" means the default changes no
gameplay by itself; "opt-in surface" means the default only makes an
*explicit, per-use* mutating action available, never performs it
automatically. No default in this table auto-engages a mutating action.

| Section.Key | Default | Source of truth | Classification |
|---|---|---|---|
| General.Enabled | `true` | `TeamsterDefaults.Enabled` | Safe/observational — master switch for reading cart state only |
| Diagnostics.DebugLogging | `false` | `TeamsterDefaults.DebugLogging` | Safe/observational |
| Telemetry.SampleIntervalSeconds | `0.5` s | `TelemetrySamplerOptions.DefaultSampleIntervalSeconds` | Safe/observational — read cadence only |
| Telemetry.SearchRadiusMeters | `30` m | `TelemetrySamplerOptions.DefaultSearchRadiusMeters` | Safe/observational |
| Telemetry.MaxCartsPerTick | `2` | `TelemetrySamplerOptions.DefaultMaxCartsPerTick` | Safe/observational — performance bound |
| Telemetry.MaxTrackedCarts | `8` | `TelemetrySamplerOptions.DefaultMaxTrackedCarts` | Safe/observational — performance bound |
| Ui.PanelShortcut | *(empty)* | `KeyboardShortcut.Empty` (BepInEx type; not Domain-testable) | Safe/observational — no accelerator bound; the visible Cart button is always the primary path |
| Warnings.PanelWarningsEnabled | `true` | `TeamsterDefaults.PanelWarningsEnabled` | Safe/observational — advisory text only |
| Warnings.HudWarningHintsEnabled | `false` | `TeamsterDefaults.HudWarningHintsEnabled` | Safe/observational |
| Warnings.SteepGradeCautionPercent | `18` % | `WarningOptions.DefaultSteepGradeCautionPercent` | Safe/observational — display threshold only |
| Risk.LookaheadPoints | `3` | `LookaheadOptions.DefaultPoints` | Safe/observational — read-ahead sample count |
| Brake.Enabled | `true` | `TeamsterDefaults.BrakeEnabled` | **Opt-in surface** — controls whether the brake *button* exists, not whether it is engaged. Engaging it is always an explicit click, always reversible, never written to a save (see `RECOVERY.md`). No default engages the brake. |
| Trips.Enabled | `true` | `TeamsterDefaults.TripsEnabled` | Safe/observational — recording only; never touches a Valheim save |
| Trips.RecordSpacingSeconds | `1` s | `TripRecorderOptions.DefaultRecordSpacingSeconds` | Safe/observational |
| Trips.MaxSamplesPerTrip | `600` | `TripRecorderOptions.DefaultMaxSamplesPerTrip` | Safe/observational — bound |
| Trips.MaxTripsRetained | `50` | `TripRecorderOptions.DefaultMaxTripsRetained` | Safe/observational — bound |
| Ui.Scale | `1.0`× | `UiScaleOptions.DefaultScale` | Safe/observational |
| Onboarding.Dismissed | `false` | `TeamsterDefaults.OnboardingDismissed` | Safe/observational |
| General.Profile | `Standard` | `TeamsterDefaults.DefaultProfile` | Safe/observational — a preset selector; every preset (including Standard) keeps the brake opt-in |
| General.LastAppliedProfile | `Standard` | `TeamsterDefaults.DefaultProfile` | Internal bookkeeping; equals `General.Profile`'s default so the profile-apply pass never fires on a fresh install (see `DefaultFreezeSnapshotTests .FreshInstall_...`) |
| Internal.ConfigSchemaVersion | `1` for a fresh v0.9 install, `0` (`PreVersioning`) is the value any pre-CT-039 upgrade reads before migrating | `ConfigSchemaVersion.Current` / `.PreVersioning` | Internal bookkeeping |

Locked by `src/ConcernedTeamster.Tests/DefaultFreezeSnapshotTests.cs`,
which asserts every Domain-testable value above directly against its
source-of-truth constant — an accidental future edit to any of them fails
that suite. `TeamsterSettings.Bind` (the BepInEx-facing glue itself) is
architecturally outside the Domain-only test project and stays untested
wiring by design, matching every other Adapters-adjacent file in this
codebase; the snapshot test locks the *values* it reads instead, per the
"pure decision, mechanical executor" split.

## Privacy audit

**Finding: no automatic data egress exists.** Concerned Teamster is
client-side only. It:

- Sends no data over the network, automatically or otherwise.
- Makes no HTTP, socket, or web request of any kind.
- Reads Valheim's own RPC/ownership system never (CT-026's existing
  audit) and calls no general internet-egress API (CT-041's new audit,
  `check_teamster_no_internet_egress` in `tools/validate_repo.py`) —
  both are CI-gated source scans across every shipped `.cs` file, not a
  one-time manual claim.
- Writes only its own files under `BepInEx/config/ConcernedCatMods/
  ConcernedTeamster/` (sidecars, support bundles); Valheim saves are
  never touched.

**The one intentional exception:** the Report a Bug button (below) calls
`UnityEngine.Application.OpenURL`, which asks the player's OS to open a
browser to the public GitHub issue tracker — the same as the player
typing the address themselves. This is excluded from the egress audit by
name (see `TEAMSTER_INTERNET_EGRESS_TOKENS`'s own comment) because it
carries no Teamster data anywhere; it only ever fires on an explicit
click, never automatically, and it is the single outbound action this
entire mod ever takes.

## Feedback path

Where beta users report a problem:

- **In-game:** the Cart Status panel's Support button opens the Support
  Bundle panel, which now has a **Report a Bug** button (opens
  `Domain.Support.FeedbackLinks.IssuesUrl`, the public GitHub issue
  tracker) alongside its existing **Export** button.
- **In the README:** the `## Support` section links the same tracker for
  bugs/feature requests, and an email for anything that should not be
  public.

What to include: the exported support bundle (the Support panel's Export
button) — versions, config, compatibility status, sidecar summaries,
recent Teamster-only log lines, all already sanitized (see
`SupportBundleSanitizer` and `RECOVERY.md`).

What never to include: a world save file, a screenshot showing another
player's name, your Steam friend code, or anything else you consider
private. The sanitizer already strips URLs, coordinates, file paths,
save/world names, and IP addresses from the bundle itself, but a raw
save file or an unedited screenshot is the *player's* upload, not the
mod's — outside what any in-mod sanitizer can reach.

## Evidence

- Freeze document: this file.
- Snapshot test output: `dotnet test --filter FullyQualifiedName~DefaultFreezeSnapshotTests` — see the CT-041 PR evidence for the exact pass count.
- Privacy audit: `python tools/validate_repo.py --product teamster` — see the `[interop] CT-041 privacy audit` line for the exact scanned-file and violation count.
