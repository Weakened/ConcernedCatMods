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

## Compatibility

CT-038 researches the exact current cart/physics/inventory mods before any
compatibility claims; the matrix template is:

| Mod (exact name/version) | Load together | Teamster readouts sane | Their features intact | Notes |
|---|---|---|---|---|

Better Carts precedence (CT-037): when a physics-altering cart mod is present,
Teamster must either measure the modified reality accurately or clearly label
readings as unavailable — never display vanilla numbers as truth under altered
physics.

## Multiplayer (v0.6)

- Ownership: only the vanilla-authoritative controller's client mutates
  anything; observers observe.
- Unmodded coexistence: a vanilla peer sees fully vanilla behavior.
- Malformed/stale network input: bounds-checked, dropped, logged once.
- Dedicated server: no server plugin required; client behavior validated
  against a dedicated world in `TCT-Dedicated`.

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
