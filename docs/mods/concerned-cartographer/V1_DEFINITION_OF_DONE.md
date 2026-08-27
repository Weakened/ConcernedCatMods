# v1.0 Definition-of-Done matrix (CC-060)

Status of every capability in the approved v1 functional bar. **Done** =
implemented, automated-tested where automatable, shipped through an
internal gate. **Done\*** = implemented with the in-game visual/interaction
rows deferred to PRE_RELEASE_SMOKE_TEST.md (OPS-001 runtime honesty).
**Deferred** = deliberately not in v1.0, with the recorded decision.

## Roads

| Capability | Status | Where |
|---|---|---|
| Direct Pathen/paved construction capture | Done\* | v0.2; terraforming excluded (0.2.0 fix) |
| Progressive loaded-road recovery | Done\* | v0.2; fog-gated, narrowness heuristic, budgeted |
| Repaint/removal reconciliation | Done\* | v0.2; journaled, `.pre-reconcile.bak` |
| Compact / spatially indexed geometry | Done | v0.2; segment index, 97% compaction measured |
| Correction/repair tools | Done\* | v0.2 `cc_roads` |
| World-isolated persistence | Done\* | per-UID sidecars since v0.1 |
| No world-save mutation | Done | no terrain/save write API anywhere; uninstall rows in smoke |

## Pins

| Capability | Status | Where |
|---|---|---|
| Safe vanilla-pin adoption | Done\* | v0.3; zero-touch adoption, single-claim reconcile |
| In-place editing without delete/recreate | Done\* | v0.3 workbench; identity/revision preserved |
| Stable IDs / revisions | Done | `cc:pin:<guid>`, monotonic, tested |
| Full metadata (name/icon/category/color/size/notes/tags/status/scope) | Done | color/size stored but not map-rendered (HUMAN_ATTENTION, documented) |
| Move/duplicate/archive/delete/restore | Done | v0.3, tombstoned deletes |
| Batch tools | Done | one-undo-step batch edits |
| Duplicate merge | Done | provenance-preserving, bucketed scan |
| Bounded undo/redo | Done | depth 20, forward-revision convergence proven |
| Curated stable icon registry | Done | append-only namespaced IDs, fallback preserves identity |
| Safe foreign-pin ownership | Done | four-class model; foreign untouchable through every path |

## Atlas UX

| Capability | Status | Where |
|---|---|---|
| Unified Atlas Drawer | Done\* | v0.4, hotkey L |
| Search/query/filtering | Done | token language, display-only, 10k benchmarked |
| Saved views | Done | profile-level presets |
| Semantic zoom | Done\* | zoom tiers via m_largeZoom |
| Clustering/decluttering | Done | pure, lossless, deterministic |
| Quick context pins | Done\* | hover-only, no radar |
| Bounded opt-in Survey Rules | Done\* | off by default, structural caps, review-before-commit |

## Routes

| Capability | Status | Where |
|---|---|---|
| Freehand + waypoint routes | Done\* | v0.5, Modifier+LMB modes |
| Partial erase | Done | splits into runs, undoable |
| Editing/split/merge | Done | v0.5 |
| Lock/archive | Done | lock rejects geometry edits (tested) |
| Distance/composition estimates | Done | configurable speeds |
| Road-aware snapping/routing | Done | A* over road graph, junction-crossing tested |
| Persistence and rendering | Done\* | own overlay, snapshot+journal |

## Collaboration

| Capability | Status | Where |
|---|---|---|
| Private/table/server scopes | Done | Server flows as Table (no server store — HUMAN_ATTENTION) |
| Stable revisions/change journal | Done | since v0.3 |
| Durable deletion tombstones | Done | no-resurrection property-tested |
| Sync preview | Done\* | inbox + plan summary; nothing auto-applies |
| Duplicate/conflict resolution | Done | keep-local default, take-remote converges |
| Incremental peer sync | Done\* | full-state envelopes, revision reconciliation (server persistence deferred) |
| Permissions | Done | non-owner-delete rejection |
| Offline/reconnect/stale semantics | Done | property-tested |
| **Deleted shared entities never resurrect** | **Done** | the golden guarantee; structural + tested |

## Quality

| Capability | Status | Where |
|---|---|---|
| NoMap/table mode | Done\* | table-proximity gate, fail-open |
| Controller path | Done\* | select-on-open chains + opt-in gamepad bindings; full feel-pass is a smoke row |
| Rebindable controls | Done | every binding is a config entry |
| Localization framework | Done | catalog + template + overrides; console stays English (documented) |
| UI/accessibility scaling + non-color cues | Done\* | UiScale, HighContrast, styles/icons/labels |
| Onboarding/safe defaults | Done\* | one-time tip; conservative defaults |
| Compatibility adapters | Done\* | GUID registry + policies; live sessions are smoke rows |
| Import/export | Done | backup folders as interchange format |
| Backup/restore | Done | timestamped, safety-backup-first |
| Sanitized support bundle | Done | sanitized by construction |
| Large-atlas scale | Done | 10k pins / 10 km roads suites; long-run soak is a smoke row |
| Migration from every published format | Done | migration matrix test |
| Safe disable/uninstall | Done\* | pins persist as vanilla; sidecars inert; smoke row |

## Deferred (recorded in HUMAN_ATTENTION.md)

- Pin color/size map rendering (metadata only).
- Dedicated-server-side persistence (peer-to-peer only).
- MapRoutes route import (coexistence only).
- Map-click pin selection (proximity + cursor selection instead).
