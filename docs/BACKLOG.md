# Initial backlogs

GitHub issues are the authoritative backlog. This file records how each
product's backlog was seeded.

## Concerned Teamster (active)

The complete v0.1-v1.0 issue graph — ten `SPRINT Teamster vX.Y` controllers and
fifty leaves `CT-001`..`CT-050`, each with goal, scope, acceptance criteria,
dependencies, and Definition of Done — is generated idempotently by
`scripts/setup-teamster-github.ps1`. Selection and workflow rules live in
`docs/mods/concerned-teamster/AUTONOMOUS_EXECUTION.md`.

| Sprint | Theme | Leaves |
|---|---|---|
| v0.1 | Cart Truth | CT-001..CT-005 |
| v0.2 | Cargo and Load Planning | CT-006..CT-010 |
| v0.3 | Descent Safety and Recovery Guidance | CT-011..CT-015 |
| v0.4 | Road Quality and Trip Profiles | CT-016..CT-020 |
| v0.5 | Optional Cartographer Integration | CT-021..CT-025 |
| v0.6 | Multiplayer Trust and Authority | CT-026..CT-030 |
| v0.7 | UX, Controller, Accessibility, Localization | CT-031..CT-035 |
| v0.8 | Compatibility, Recovery, Scale | CT-036..CT-040 |
| v0.9 | Public Beta Hardening | CT-041..CT-045 |
| v1.0 | Stable Teamster | CT-046..CT-050 |

## Concerned Cartographer initial backlog (historical)

| ID | Title | Owner label | Exit evidence |
|---|---|---|---|
| CC-001 | Bootstrap and prove plugin/map lifecycle | owner-claude | Clean load/log proof and overlay lifecycle test |
| CC-002 | Detect Pathen and paved terrain beneath player | owner-claude | Logged/visual classification on both terrain types |
| CC-003 | Render separate dirt and paved overlays | owner-codex | Map/minimap screenshots and toggle proof |
| CC-004 | Persist atlas per world UID | owner-codex | Restart and cross-world isolation proof |
| CC-005 | Capture successful terrain-paint actions | owner-shared | New road appears without traversing every point |
| CC-006 | Backfill roads from loaded terrain chunks | owner-shared | Existing-road recovery with bounded cost |
| CC-007 | Compatibility pass: Pinnacle and MapRoutes | owner-codex | Compatibility matrix and logs |
| CC-008 | In-place pin editor and richer legend | owner-shared | UX acceptance tests |
