# v1.0 Definition-of-Done matrix (CT-046)

Status of every capability in the approved v0.9→v1.0 functional bar,
following Concerned Cartographer's own `V1_DEFINITION_OF_DONE.md`
pattern. **Done** = implemented, automated-tested where automatable,
shipped through an internal gate. **Done\*** = implemented with the
in-game visual/interaction rows deferred to `PRE_RELEASE_SMOKE_TEST.md`
(the same OPS-001 runtime-honesty convention Cartographer uses).
**Deferred** = deliberately not in v1.0, with the recorded decision.

## Cart truth and telemetry

| Capability | Status | Where |
|---|---|---|
| Verified cart capability probe, fail-closed on a game change | Done\* | v0.1 (CT-002); 22 `GameMemberProbeTests` incl. every simulated-missing-member path; live startup banner is smoke §1 |
| Bounded, budgeted telemetry sampler | Done | v0.1 (CT-003); scheduling/budget/rotation/eviction/reset/zero-allocation fast paths tested; CT-040 scale tests prove the dense-cluster ceiling is a documented tradeoff, not a defect |
| Deterministic grade math + surface classification | Done\* | v0.1 (CT-004); flat/slope/crest/dip/noisy fixtures; live grade-vs-terrain spot check is smoke §2 |
| Cart Status panel | Done\* | v0.1 (CT-005); 22 presenter tests (every string/state); visual placement/readability is smoke §2 |

## Cargo and load planning

| Capability | Status | Where |
|---|---|---|
| Quality-scaled cargo manifest | Done\* | v0.2 (CT-006); vanilla-weight-API fidelity tested; live manifest-vs-container check is smoke §3 |
| Sortable/filterable manifest UI | Done\* | v0.2 (CT-007); full sort/filter matrix, localized filtering; screenshot is smoke §3 |
| Calibrated safe-load model, honest "uncalibrated" | Done\* | v0.2 (CT-008); dominance-only model with zero Measured rows shipped by design; real calibration protocol runs are smoke §3 row 3.3 |
| Anti-flicker load/grade warnings | Done\* | v0.2 (CT-009); hysteresis single-transition-pair property tested; live transcript is smoke §3 row 3.4 |

## Descent safety and the parking brake

| Capability | Status | Where |
|---|---|---|
| 3D-dominance descent-risk model | Done\* | v0.3 (CT-011); monotonicity grid tested; zero Measured rows shipped by design, same honesty convention as load calibration; real descent protocol runs are smoke §4 row 4.1 |
| Explicit, reversible, save-proof parking brake | Done\* | v0.3 (CT-012); full engage/release lifecycle matrix tested (every refusal + every release path incl. authority loss); live slope hold/release demo is smoke §4 rows 4.2–4.4 |
| Stuck-cause diagnostics | Done\* | v0.3 (CT-013); confusion-matrix classifier tested, "cause unclear" honesty path; staged scenarios are smoke §4 rows 4.5–4.7 |
| Numbered recovery guidance | Done\* | v0.3 (CT-014); mutation-audited presenter (zero adapter references), quantity math tested; walkthrough is smoke §4 row 4.8 |

## Trip recording and road quality

| Capability | Status | Where |
|---|---|---|
| Per-world sidecar trip recording | Done\* | v0.4 (CT-016); atomic writes, cap-splitting, crash-injection tested on a real filesystem; live haul-to-file check is smoke §5 row 5.1 |
| Deterministic 8 m road-quality scoring | Done\* | v0.4 (CT-017); byte-identical incremental-vs-batch, v1→v2 migration recompute tested; real-trip sanity check is smoke §5 row 5.2 |
| Trip History with A/B comparison | Done\* | v0.4 (CT-018); 14 presenter tests (sort matrix, alignment, two-step deletion); screenshots are smoke §5 row 5.3 |
| Route bottleneck location | Done\* | v0.4 (CT-019); planted-worst-grade/roughness location tests; real-route check is smoke §5 row 5.4 |

## Optional Cartographer integration

| Capability | Status | Where |
|---|---|---|
| Runtime capability probe, no compile-time coupling | Done\* | v0.5 (CT-021); 12-member contract, drift tripwire in `validate_repo.py`; live present/absent/version-floor rows are smoke §6 |
| Route picker | Done\* | v0.5 (CT-022); 13 presenter tests over fake catalogs incl. mid-session mutation; screenshot is smoke §6 row 6.1 |
| Budgeted terrain profiler | Done\* | v0.5 (CT-023); 28 tests (budget/cancel, sampled/unsampled partition, fingerprint cache); real-route profile is smoke §6 row 6.3 |
| Route problem report | Done\* | v0.5 (CT-024); 14 presenter tests, read-only-integration validator audit; real report is smoke §6 row 6.4 |
| Coexistence (both/either/neither installed, version floor) | Done\* | v0.5 (CT-025); Cartographer regression stays 568/568 green with Teamster present; live dual-mod session is smoke §6 rows 6.2, 6.5, 6.6 |

## Multiplayer trust and authority

| Capability | Status | Where |
|---|---|---|
| Enforced read/act/observe policy | Done | v0.6 (CT-026); policy is the tested single source of truth the brake enforces through; fail-closed `Unknown` |
| Topology-independent authority logic | Done\* | v0.6 (CT-027); 8 `MultiplayerScenarioTests` driving handoff/flap/observer sequences; live two-topology campaign is smoke §7 |
| Cooperative effort diagnostics | Done\* | v0.6 (CT-028); 21 tests (full single-actor matrix, combined-effort explanation); the real read-surface feed (contact/motion alignment) and staged scenario are smoke §7 row 7.4 |
| Hardened, bounded network input | Done\* | v0.6 (CT-029); 43 tests incl. a 10k-iteration seeded fuzz sweep; live join/leave/disconnect lifecycle is smoke §7 row 7.5 |
| Unmodded-peer coexistence | Done\* | v0.6 (CT-026/030); validator-audited (sends nothing, takes no ownership); live confirmation is smoke §7 row 7.3 |

## UX, controller, accessibility, localization

| Capability | Status | Where |
|---|---|---|
| Deterministic focus order + accelerator-conflict checking | Done\* | v0.7 (CT-031); 22 tests (traversal wrap, reachability, chord conflicts); live gamepad walkthrough is smoke §8 row 8.1 |
| Full localization framework, English-complete catalog | Done | v0.7 (CT-032); 274+ keys, CI-gated hardcoded-string audit (planted literals/dead keys/re-hardcoded sentences all fail it) |
| UI scale (0.8–1.3×) + WCAG AA contrast + non-color cues | Done\* | v0.7 (CT-033); geometric no-self-clip guarantee, contrast-ratio math, sensitivity check documented; live scale/contrast confirmation is smoke §8 rows 8.2–8.3 |
| Onboarding + three config profiles, brake always opt-in | Done\* | v0.7 (CT-034); idempotence + brake-exclusion tested across all three profiles; live hint/profile-switch check is smoke §8 rows 8.4–8.6 |

## Compatibility

| Capability | Status | Where |
|---|---|---|
| GUID-only mod detection registry, Coexist/Adapt/Warn policy | Done | v0.8 (CT-036); exhaustively unit-tested with fake and real registry entries |
| BetterCarts precedence (Adapt/CartMassOrPhysics) | Done\* | v0.8 (CT-037); verified from published source (`Vagon.SetMass` Harmony prefix); live matrix row is smoke §9 row 9.1 |
| ItemStacks + ValheimPlus registered | Done\* | v0.8 (CT-038); both verified from published source; live matrix rows are smoke §9 rows 9.2–9.4 |
| We_Haul "Better Cart" registration | **Deferred** | GUID could not be verified after two research passes (CT-037, CT-038) — no linked source repository, decompiled-source viewer unreadable through available tooling; verifying further means inspecting a compiled binary, judged beyond an autonomous research pass. Owner can supply the GUID to turn this into a one-line registry addition (`HUMAN_ATTENTION.md`'s CT-038 entry) |
| Presence-only detection limit (can't see another mod's *live config*) | **Deferred** | Documented architectural limit, not a defect — ValheimPlus's Wagon section is config-gated, not presence-gated; `Warn` policy is the correct, honest response given the detection mechanism's actual reach (`HUMAN_ATTENTION.md`'s CT-038 entry) |

## Recovery, migration, and support bundle

| Capability | Status | Where |
|---|---|---|
| Bounded backup rotation (3 generations/reason) | Done | v0.8 (CT-039); real-filesystem rotation tests, closes DEF-teamster-v0.4-001 |
| Pure, tested persist-retry plan (no silent data loss) | Done | v0.8 (CT-039); `TripPersistPlanTests`, world-scoped retry queue |
| Config schema migration ladder | Done\* | v0.8 (CT-039); the one real pre-CT-039→current migration tested; live migration observation is smoke §10 row 10.2 |
| Sanitized support bundle | Done\* | v0.8 (CT-039); realistic-content sanitization tests; live export/read is smoke §10 row 10.3 |
| NavigationCatalog gap: CompatibilityPanel's own Close button | **Deferred** | Cosmetic accessibility-catalog gap noticed in passing during CT-039; the main panel's Compat button is cataloged, the sub-panel's own Close entry is not. No functional impact (Escape/click both work); left for a future accessibility pass (`HUMAN_ATTENTION.md`'s CT-039 entry) |

## Release engineering (v0.9)

| Capability | Status | Where |
|---|---|---|
| Feature/default freeze, snapshot-locked | Done | v0.9 (CT-041); `FEATURE_FREEZE.md` + `DefaultFreezeSnapshotTests` |
| Privacy-safe feedback path | Done\* | v0.9 (CT-041); `FeedbackLinksTests` locks the constant; live Report-a-Bug click is smoke §12 |
| Public docs, privacy statement, AI disclosure | Done | v0.9 (CT-042); cross-checked against `FEATURE_FREEZE.md`/`COMPATIBILITY.md`/`PRIVACY_INVENTORY.md` |
| Real-gameplay media (screenshots/GIF) | **Deferred to owner** | No screenshot exists yet; requires an interactive session (`Prepare-TCC-Screenshot-Profile.ps1` exists for this); never fabricated (`HUMAN_ATTENTION.md`'s CT-042 entry) |
| Security self-audit | Done | v0.9 (CT-042); `SECURITY_AUDIT.md` — dependency pins, secrets, copy-list, adapter fail-closed review |
| Scripted profile-family deploy paths | Done | v0.9 (CT-043); `deploy.ps1 -Profile Dev\|Compat\|Dedicated` |
| File-level lifecycle rehearsal | Done | v0.9 (CT-043); `rehearse-teamster-lifecycle.ps1` against real, actually-built artifacts incl. a real historical v0.7.0 worktree build |
| TCT-* mod-manager profiles created for real | **Deferred to owner** | Profile creation is a mod-manager GUI action this repo's tooling deliberately does not drive (`PROFILE_REHEARSAL.md`); none exists on this machine yet |
| Zero open P0/P1/P2 defects | Done | v0.9 (CT-044); DEF-teamster-v0.9-002 fixed |
| Compiled pre-release smoke checklist | Done | v0.9 (CT-044); `PRE_RELEASE_SMOKE_TEST.md`, a 36-row cross-reference table over 16 numbered sections (§0–§15), never run |
| Sealed v0.9.0 RC + owner packet | Done\* | v0.9 (CT-045); `RELEASE_DOSSIER.md` + `OWNER_PACKET_v0.9.md`; the smoke checklist itself is the pending row |

## Performance, memory, network, long-run stability (formal budgets)

| Capability | Status | Where |
|---|---|---|
| Informal scale evidence against configured worst-case bounds | Done | v0.8 (CT-040); `RELEASE_DOSSIER.md`'s v0.8 scale table; loose sanity thresholds, not formal budgets |
| Formal, gated performance/memory/network/long-run budgets | **Not yet done — CT-048's own scope** | This leaf (CT-046) only audits and cross-references; establishing and gating the formal budgets is CT-048's explicit job, not duplicated here |

## Full regression across every standard profile

| Capability | Status | Where |
|---|---|---|
| Domain unit tests + interop audits, every PR | Done | 622 Teamster + 568 Cartographer tests, three zero-violation interop audits (authority/no-network, no-force, no-internet-egress), every merge this whole conveyor |
| Full automated suite reruns clean from a fresh checkout | Done | v1.0 (CT-047); `REGRESSION_REPORT_v1.0.md` — a separate `git clone` (not this conveyor's working tree), full build+test+package cycle, 622+568 tests, zero new defects |
| Regression run against each of TCT-Clean/Dev/Compat/Dedicated specifically | **Done\* — automated layer only** | v1.0 (CT-047); `REGRESSION_REPORT_v1.0.md`'s campaign-rerun table — every profile's underlying test suite reruns clean; no TCT-* profile exists yet for the in-game layer, itemized pending per profile in the same report |

## Final docs, localization, controller, accessibility, migration, compat sign-off

| Capability | Status | Where |
|---|---|---|
| Everything listed above under its own capability area | Done\* / Deferred as itemized above | — |
| A consolidated final sign-off pass (re-verifying nothing regressed since each leaf's own seal) | **Not yet done — CT-049's own scope** | This leaf's matrix is the audit CT-049 re-verifies against, not a substitute for that leaf |

## Deferred (recorded in HUMAN_ATTENTION.md)

- We_Haul "Better Cart" registration (GUID unverifiable after two research
  passes; owner-suppliable).
- The presence-only compatibility-detection architectural limit
  (config-gated mod behavior is outside what GUID-presence detection can
  observe by construction).
- NavigationCatalog's missing entry for CompatibilityPanel's own Close
  button (cosmetic, no functional impact).
- Real-gameplay media capture and the four TCT-* mod-manager profiles'
  actual creation (both require an interactive session/GUI action this
  repo's tooling deliberately does not perform on the owner's behalf).
- DEF-teamster-v0.8-001 (#214, P3): release DLL/ZIP hashes are not
  bit-reproducible across rebuilds — a release-engineering traceability
  gap, no correctness impact.
- DEF-teamster-v0.9-001 (#216, P3): one allocation test is flaky under
  full-suite Debug timing — a test-harness sensitivity, not a product
  defect.

## Audit conclusion

No new defect was found during this audit beyond the two already-filed,
already-deferred P3s. Every capability through v0.9 has either full
automated proof (`Done`) or full automated proof of its logic plus a
specifically-itemized, cross-referenced pending in-game row (`Done\*`) —
no capability has a blank, an unproven claim, or a claim resting on
"it probably works." The remaining gaps are exactly the ones the v1.0
sprint's own remaining leaves (CT-047 regression, CT-048 formal budgets,
CT-049 final sign-off) already exist to close, plus a small, honestly
bounded set of owner-only actions (media capture, profile creation,
supplying a third-party GUID) no autonomous leaf can complete on its own.
