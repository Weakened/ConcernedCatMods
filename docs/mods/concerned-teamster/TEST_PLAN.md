# Concerned Teamster test plan

Testing follows the discipline proven on Concerned Cartographer: automate
everything automatable, record exact evidence, and never mark a manual-only
in-game observation PASS. Manual-only claims stay pending and accumulate in the
final owner smoke checklist.

## Test layers

| Layer | Runs where | Gate |
|---|---|---|
| Domain unit tests (`ConcernedTeamster.Tests`) | any machine, CI | every PR |
| Static repository/package validation (`tools/validate_repo.py`) | any machine | every PR |
| Build against local game references (`scripts/build.ps1`) | dev machine with Valheim | every PR on the dev machine |
| In-game manual campaigns | dev machine, disposable worlds | sprint RC |
| Compatibility matrix | dev machine, dedicated profiles | v0.8 and RCs |
| Multiplayer scenarios | player-hosted + dedicated server | v0.6 and RCs |
| Performance/long-run budgets | dev machine | v0.1 baseline, formal in v1.0 |

## Standard profiles

Teamster uses its own mod-manager profiles so Cartographer testing never
contaminates cart evidence:

```text
TCT-Clean      — no mods; vanilla cart behavior baseline
TCT-Dev        — BepInEx + Jötunn + local Concerned Teamster DLL
TCT-Compat     — TCT-Dev plus researched compatibility targets (CT-038)
TCT-Dedicated  — dedicated-server validation profile (v0.6)
```

Profile automation is delivered by CT-043. Until then, profiles are created
manually per the end-to-end guide. All in-game testing uses disposable worlds
(for example `TCT_Mod_Test`); never a valuable world.

## Domain unit tests

- GradeMath: synthetic terrain fixtures — flat, uniform slopes, crests, dips,
  noisy samples; assert grade sign, magnitude, and stability.
- LoadModel: cargo aggregation, safe-load curves against recorded calibration
  data (CT-008); boundary and overflow behavior.
- RiskModel: monotonicity (more mass or grade never lowers risk), threshold
  hysteresis, calibration-table lookup.
- Telemetry: snapshot immutability, sampler budget accounting, world-switch
  reset.
- Persistence (v0.4+): round-trip, versioned migration, malformed-row skip,
  atomic-write failure injection, cross-world isolation.
- Recovery and migration (CT-039): bounded backup rotation on a real
  filesystem, the backup-before-rewrite decision (refused/malformed/
  migrating all mandate a backup; clean does not), config schema migration
  ladder.
- Support bundle (CT-039): sanitization against realistic planted content
  (paths, world UIDs, this mod's own real log-line shapes), composition
  with and without data present.
- UI presenters: headless rendering of panel view-models from fixed snapshots.

## Vanilla truth baseline (v0.1)

Before trusting any Teamster number, record vanilla behavior in `TCT-Clean`:

1. Empty cart on flat ground — note pull feel and speed.
2. Cart loaded with a known cargo set (for example full stacks of stone) on the
   same flat ground.
3. The same loaded cart on a marked uphill and downhill grade.

The same scenarios repeat in `TCT-Dev`; Teamster's displayed mass, grade, and
pull state must match the physically observed situation. Discrepancies are
defects or calibration items — never silently accepted.

## In-game campaign skeleton (per sprint RC)

- Clean load: BepInEx log shows the Teamster banner, no errors, no warnings
  besides intentionally disabled capabilities.
- Cart lifecycle: build cart, attach, detach, destroy, rebuild; panel state
  follows reality with no stale data.
- World lifecycle: logout/login, world switch, character switch; no leaked
  state, no exceptions.
- Feature scenarios listed by the sprint's leaf issues.
- Uninstall safety: remove the DLL, load the world, confirm vanilla behavior
  and no missing-object errors.

## Performance

- No visible frame-time spikes attributable to Teamster while hauling for 30
  minutes with the panel open.
- Log volume bounded (no per-frame or per-sample logging).
- Sampler stays within its configured budget; measured evidence at each RC and
  formally in CT-048.

### Scale evidence (CT-040)

Informal measurement against the worst-case *configured* bounds — loose
sanity thresholds, not the formal, gated performance budgets CT-048
establishes in v1.0. Exact numbers (one real run, this dev machine) are in
`RELEASE_DOSSIER.md`'s v0.8 RC entry; the automated tests assert generous
margins, not these exact figures, since wall-clock varies by machine.

| Scenario | Bound exercised | Automated test |
|---|---|---|
| Trip sidecar at max retention | `TripRecorderOptions.MaxMaxTripsRetained` (500 trips, 20 samples each) | `Scale_MaxTripsRetainedRoundTrip_StaysCorrectAndReasonablyFast` |
| Cargo manifest, many distinct items | 200 synthetic entries (a generous stress input — see the test's own comment for why this is not a claim about vanilla's real cart capacity) | `Create_ManyDistinctEntries_StaysCorrectAndFast` |
| Telemetry sampler, dense cart cluster, long session | `TelemetrySamplerOptions.MaxMaxTrackedCarts`/`MaxMaxCartsPerTick`, 50 candidates, 2,000+ due ticks | `Tick_MaxTrackedCartsOverALongSession_NeverExceedsTheCap_ReachesBeyondOnePerTickBatch`, `Tick_MaxScaleLongSession_AllocationPerTickDoesNotGrowOverTime` |

**Scale finding, not a defect:** at `MaxMaxTrackedCarts` (32) with more
nearby candidates than that, the sampler's practically-reachable tracked
count stabilizes measurably below 32 (27 measured at the most favorable
interval/per-tick combination), because `EvictAfterSeconds` is floored at
2 seconds regardless of how small the sample interval gets, and the
round-robin window advances by only one candidate per tick — so a
sample's freshness window, not the configured cap, ends up governing
coverage in a cart cluster this dense. The hard cap itself is never
violated (asserted every tick). This trades maximum coverage for
guaranteed freshness (a stale tracked cart is never shown), which matches
this mod's own fail-closed philosophy — see the cited test's comment for
the full derivation. Recorded here so "configured maximum" and
"practically reachable" are never assumed to be the same number.

## Compatibility

CT-038 researched the exact current cart/physics/inventory mod landscape
before any compatibility claims (Thunderstore listings, selected by
download count and directness of effect on cart mass/weight); the matrix:

| Mod (exact name/version) | Load together | Teamster readouts sane | Their features intact | Notes |
|---|---|---|---|---|
| BetterCarts (TastyChickenLegs) 1.0.6+ | pending in-game | pending in-game | pending in-game | Registered `Adapt`/`AffectedAspect.CartMassOrPhysics` (CT-037) — its `Vagon.SetMass` Harmony prefix reduces cart mass by a default 20% (`Patches/CartConfigs.cs`: `cartMassReduction = 0.2f`), so every load-advice consumer substitutes its "unavailable" notice while it is detected; the in-game row confirms this holds for real rather than deciding it. |
| ItemStacks (mtnewton) 1.2.0 | pending in-game | pending in-game | pending in-game | Registered `Adapt`/`AffectedAspect.CartMassOrPhysics` (CT-038) — reduces every item's weight by a default 90% (`ItemTracker.SetWeight` overwrites `m_shared.m_weight` directly), on by default, so cargo weight and cart mass both drop to roughly a tenth of vanilla out of the box; 306,955 downloads, over 6x BetterCarts'. |
| ValheimPlus (`org.bepinex.plugins.valheim_plus`) 0.9.9.11 | pending in-game | pending in-game | pending in-game | Registered `Warn`/`AffectedAspect.None` (CT-038) — its Wagon cart-mass section ships disabled and, disabled, its patch reproduces vanilla's mass formula exactly (verified from the patch, the section's own defaults, and the shipped config template); only an explicit player opt-in changes cart mass, which presence-only detection cannot observe, so this is a Compat-panel note rather than a gate trip. |

**Precedence policy (CT-037):** when a mod affecting cart mass or physics is
present, Teamster must either measure the modified reality accurately or
clearly label readings as unavailable — never display vanilla-calibrated
numbers as truth under altered physics. Implemented generically:
`Domain/Compatibility/CompatibilityAdvisoryGate.CartMassAdviceReliable`
answers this from the compatibility registry's `AffectedAspect` tag (never a
specific mod's GUID), read through one shared wrapper,
`Adapters/CompatibilityAdapter.CartMassAdviceReliable`. Every LoadModel- or
RiskModel-derived consumer substitutes a fixed unavailable notice instead of
its normal output whenever that is false: cart warnings
(`CartTelemetryPump.TryGetWarning`), stuck diagnosis
(`StuckDetector.Classify`, via a `CartDiagnosis.LoadAdviceUnavailable`
verdict that recovery guidance inherits automatically), the route profile's
bottleneck line, the route report's overall and per-section advice, the
trip-history load-binding line, and the descent-risk debug log (the one
consumer with no player-facing panel today). The parking brake needs no
gate — `BrakeFacts`/`BrakeLifecycle` have no mass field and never call
`LoadModel`/`RiskModel`, so "brake policy under altered physics is explicit
and fail-closed" holds structurally, with nothing to substitute.

Proven with both fake mass-altering probes and the real shipped registry in
`CompatibilityAdvisoryGateTests` (reliable when nothing mass-altering is
registered or detected, unreliable the instant one is, demonstrated generic
by using an entirely different fake GUID/name for the same assertion, and
asserted directly against each shipped entry — including that the
`Warn`/`None` ValheimPlus entry, unlike the two `Adapt`/`CartMassOrPhysics`
entries, never trips the gate even when detected), plus dedicated gate
tests on every consumer above (`StuckDetectorTests`,
`RecoveryGuidancePresenterTests`, `RouteProfilerTests`,
`RouteReportPresenterTests`, `RouteBottleneckTests`). **Research finding:**
the specific mod originally referenced as "Better Carts" in `PROJECT.md`
could not be pinned to one exact, GUID-verified Thunderstore package after
two research passes (CT-037 and CT-038) — see `COMPATIBILITY.md`'s research
table; the We_Haul "Better Cart" candidate remains unregistered for that
reason. Three real mass-relevant mods are now registered
(`BetterCarts`, `ItemStacks`, `ValheimPlus`), but no in-game observation
with any of them actually installed has run yet — pending
(`HUMAN_ATTENTION.md`).

## Recovery, migration, and support bundle (CT-039)

Full design in `RECOVERY.md`. Summary of the acceptance-criteria evidence:

| Criterion | Evidence |
|---|---|
| Old-version fixtures migrate with backups | `TripPersistenceTests` (sidecar v1→v2 backup-then-rewrite, real filesystem); `ConfigSchemaMigrationTests` (config version 0→1, the real migration every pre-CT-039 install goes through) |
| Documented rollback | `RECOVERY.md`: restore the newest `.bak-<reason>-1` file over the live sidecar to undo a sidecar migration/refusal; config migration needs no rollback procedure because it cannot lose data (see `RECOVERY.md` for why) |
| Injected corruption quarantines the bad file and preserves valid data | `TripPersistenceTests` (rotation keeps prior generations distinct instead of clobbering); `TripPersistPlanTests` (a backup is now mandated for the malformed-rows case too, closing the one scenario that previously took none) |
| Recovery events surface to the user in plain language | `RecoveryEvent` shown in the Support Bundle panel's export, not just the BepInEx log |
| Bundle sanitization test proves the exclusion list | `SupportBundleTests` — realistic planted world UIDs, paths, usernames, and this mod's own actual log-line shapes, asserted absent from the composed bundle |

No in-game observation of an actual corrupted sidecar, a real v1→v2
migration, or an opened/read exported bundle has been run yet — pending
(`HUMAN_ATTENTION.md`), never claimed PASS.

## Feature freeze, defaults, and privacy (CT-041)

Full detail in `FEATURE_FREEZE.md`. Summary of the acceptance-criteria
evidence:

| Criterion | Evidence |
|---|---|
| Freeze document committed | `FEATURE_FREEZE.md` — every v0.1–v0.9 feature and every config default, with a safety classification |
| Default snapshot test locks the frozen defaults | `DefaultFreezeSnapshotTests` — asserts every Domain-testable default directly against its source-of-truth constant, plus a Standard-profile-matches-fresh-install consistency check |
| Feedback button and README both route to the documented path | `SupportBundlePanel`'s new Report a Bug button and the Package README's `## Support` section both point at `Domain.Support.FeedbackLinks.IssuesUrl`; `FeedbackLinksTests` locks the constant |
| Privacy audit confirms no automatic data egress exists | `tools/validate_repo.py`'s new `check_teamster_no_internet_egress` — a CI-gated source scan for internet-egress-capable APIs across every shipped `.cs` file, not a one-time manual claim; `Application.OpenURL` (the Report a Bug button's one outbound action, always explicit-click, carries no data) is the sole documented exception |

No in-game observation of the Report a Bug button actually opening a
browser has been run yet — pending (`HUMAN_ATTENTION.md`), never claimed
PASS.

## Public docs, media, and security audit (CT-042)

Full detail in `SECURITY_AUDIT.md`. Summary of the acceptance-criteria
evidence:

| Criterion | Evidence |
|---|---|
| Every README claim traces to shipped behavior or is listed as a limitation | Package README's feature/compatibility/privacy sections cross-checked against `FEATURE_FREEZE.md`, `COMPATIBILITY.md`, and `PRIVACY_INVENTORY.md` during this leaf; no unverified claim added |
| Media reflects the current build (no mockups) | No screenshots exist yet — none were fabricated. Real-gameplay capture is an owner task (`Prepare-TCC-Screenshot-Profile.ps1` exists for exactly this) and is recorded pending in `HUMAN_ATTENTION.md`, never claimed done |
| Audit checklist committed with all findings resolved or filed as defects | `SECURITY_AUDIT.md` — 2 findings, 1 fixed inline (a doc-comment accuracy correction), 1 filed (DEF-teamster-v0.9-002 / #217, P2, deferred to CT-044) |
| Validator passes with the final metadata | `validate_repo.py --product teamster` — unchanged pass, no metadata changed this leaf (categories were already correct; version stays 0.8.0 until CT-045's seal) |

## Profile family and lifecycle rehearsal (CT-043)

Full detail in `PROFILE_REHEARSAL.md`. Summary of the acceptance-criteria
evidence:

| Criterion | Evidence |
|---|---|
| Profile scripts are idempotent and scoped to TCT profiles | `deploy.ps1 -Product ConcernedTeamster -Profile Dev\|Compat\|Dedicated`; idempotence of its `Copy-Item -Force` primitive proven by `rehearse-teamster-lifecycle.ps1`'s §5 (two runs, identical SHA-256); Cartographer's own deploy path independently confirmed unchanged |
| Fresh/upgrade/uninstall rehearsals each have recorded evidence; migrations ran where expected | `rehearse-teamster-lifecycle.ps1` §§1–4 — real chain-integrity check across all 8 sealed tags, a real fresh-install of the current package, a real upgrade from an actually-rebuilt v0.7.0 (via a temporary `git worktree`) to current, and a real uninstall check; migration *logic* coverage is `ConfigSchemaMigrationTests`/`TripPersistPlanTests`/`TripPersistenceTests` (unchanged by this leaf, already exhaustive) |
| No rehearsal step requires undocumented manual fiddling | Every manual step (creating each profile once, installing the three compat mods) is documented in `PROFILE_REHEARSAL.md`, not left implicit |
| Owner-facing rehearsal doc committed | `PROFILE_REHEARSAL.md` |

No TCT-* mod-manager profile exists on this machine yet (only Cartographer's
TCC-* family does) — creating one is a one-time GUI action per
`PROFILE_REHEARSAL.md`, deliberately not attempted by this leaf's
tooling. The owner smoke-checklist rows this rehearsal adds are itemized
in `HUMAN_ATTENTION.md`'s CT-043 entry, never claimed PASS.

## Multiplayer (v0.6)

- Ownership: only the vanilla-authoritative controller's client mutates
  anything; observers observe.
- Unmodded coexistence: a vanilla peer sees fully vanilla behavior.
- Malformed/stale network input: bounds-checked, dropped, logged once.
- Dedicated server: no server plugin required; client behavior validated
  against a dedicated world in `TCT-Dedicated`.

### CT-027 authority scenario matrix

The authority POLICY logic (CT-026) is topology-independent — each client
decides from its own live authority — so the sequences the real topologies
produce are proven off-game by `MultiplayerScenarioTests`, and the in-game
observation of the same rows is structured pending-manual (never PASS until
observed on a real server). "Automated" rows cite the proving test.

| Scenario row | Automated evidence | In-game (TCT profiles) |
|---|---|---|
| Owner keeps the brake across continued owning ticks | `Handoff_OwnerKeepsBrakeAcrossContinuedOwningTicks` (survives owning ticks, releases when ownership leaves) | pending |
| No engaged brake ever persists on a non-authority tick | `Handoff_RapidOwnershipFlaps…` per-tick invariant (engaged ⇒ local authority) | pending |
| Authority handoff mid-haul releases the brake same tick | `Handoff_OwnerEngagedThenAuthorityLeaves_BrakeReleasesSameTick` | pending |
| Receiving client cannot engage a cart it does not yet own | `Handoff_ReceivingClientCannotEngageWhileRemote` | pending |
| No mutating action executes without authority (any state) | `NoMutationWithoutAuthority_AcrossFeaturesAndAmbiguousStates` (policy, incl. Remote) | pending |
| Observer of a remote cart: owner-fresh readouts labeled remote | `Observer_RemoteCartOwnerFreshReadingsAreLabeled` | pending |
| Panel re-labels / brake control hides on live handoff | `Observer_RemoteCartOwnerFreshReadingsAreLabeled` (label decision); button-visibility observation is manual | pending |
| Dedicated server: client logic identical to player-hosted | `BrakeDecision_IsPureFunctionOfFacts_TopologyNotAnInput` (purity) + validator CT-026 audit (no server component, sends nothing) | pending |
| Unmodded peer sees fully vanilla behavior | validator CT-026 audit: Teamster sends nothing / takes no ownership | pending |

### Topology-specific caveats (CT-027)

- **Panel state on handoff is not stale by construction.** The engage control
  becomes visible only when the live-authority gate holds
  (`CartStatusHudController.RefreshBrakeButton`: `facts.IsLocalAuthority` plus
  reach/attach checks), re-evaluated on each panel refresh (~4 Hz while the
  panel is open); an already-engaged cart keeps showing a *Release* control
  regardless of authority so release always stays reachable, and the brake
  lifecycle auto-releases on the first tick after authority leaves. Crucially,
  mutation is gated at toggle/tick time (`BrakeService`→`EvaluateToggle`/
  `EvaluateTick`), not by button visibility, so even a ≤250 ms stale button
  yields at most a no-op click, never an unauthorized mutation. Observation
  panels keep showing the cart (observation is allowed from any client) with
  owner-fresh values labeled remote. The in-game confirmation that the button
  updates and the panel re-labels on a real handoff is pending-manual.
- **Teamster ships no server component.** A dedicated server needs no Teamster
  plugin; all decisions are client-side, so the dedicated and player-hosted
  rows share one logic path (asserted). The only per-topology difference is
  which client the game reports as owner — an input to the policy, not a
  branch in it.
- **Authority is read, never forced.** The handoff itself is vanilla; Teamster
  only observes the resulting ownership. It cannot cause, delay, or block a
  handoff.

## Package and release-candidate gate

- `tools/validate_repo.py` passes (extended for Teamster in CT-001).
- Version synchronized across csproj, `Plugin.cs`, `thunderstore.toml`, and
  `CHANGELOG.md`.
- ZIP contains only Teamster's own DLL, package metadata, license, changelog,
  and icon; no game binaries, saves, or secrets.
- Fresh-profile install of the ZIP loads clean.
- Sprint RCs are sealed with recorded hashes in the release dossier pattern.

## Honesty rules

- Automation-verifiable claims must include the exact command and output.
- In-game visual/feel claims require recorded evidence (log excerpt,
  screenshot, or video) or stay pending.
- The final v1.0 smoke test and Thunderstore publication are owner-only and
  driven by the checklist accumulated across all pending manual claims.
