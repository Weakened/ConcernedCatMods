# Human attention ledger

Questions that deserved owner awareness but did not block safe progress.
Per OPS-001 rev 2, each records the safe reversible default chosen so work
could continue. Items marked "Must resolve before public release: Yes" are
repeated in PRE_RELEASE_SMOKE_TEST.md.

### 2026-08-26 — Atlas Core stays a source-linked module, not a second shipped DLL

- Version / issue: v0.3 / CC-019 (#28)
- Question: CC-019 says "pure .NET project"; a literal second `Core.csproj` would ship two DLLs, which the packaging contract (only the mod's own DLL under `plugins/`) and validator forbid.
- Safe reversible default selected: keep the proven pattern — pure, Unity-free sources under `src/ConcernedCartographer/Domain/**` compiled directly into the test project and CI; one shipped DLL.
- Why work continued: satisfies the acceptance intent (CI tests without Valheim; no game references in core code) without changing the packaging contract.
- Risk / alternative: a real Core assembly with ILRepack could come later; purely additive.
- Must resolve before public release: No
- Status: Open

### 2026-08-26 — Roads keep their own sidecar; the "atlas store" is a family of sidecars

- Version / issue: v0.3 / CC-020 (#29)
- Question: CC-020 says "migrate v0.1/v0.2 road files into the new atlas store"; folding the proven roads.tsv (v1→v3 migration, backups, suppression-index structures) into one unified store file would be a risky rewrite.
- Safe reversible default selected: the atlas store is the per-world sidecar family under one persistence layer — `<uid>.roads.tsv` (existing, already versioned/migrated/backed up) plus `<uid>.pins.tsv` with snapshot+journal. Shared conventions: atomic writes, `.vN.bak` before format rewrites, journal recovery.
- Why work continued: road migration/backup evidence already exists and shipped in 0.2.0; a unified single-file store can be introduced later behind the same persistence interface.
- Risk / alternative: two files per world instead of one; no data risk.
- Must resolve before public release: No
- Status: Open

### 2026-08-27 — No dedicated-server persistence component in v0.6 sync

- Version / issue: v0.6 / CC-041 (#53)
- Question: CC-041 names "peer/server synchronization"; PROJECT.md lists "no dedicated-server component" as a non-goal, and a server-side store cannot be verified by automation here.
- Safe reversible default selected: synchronization is peer-to-peer over routed RPCs (the server relays them); Server-scoped entities flow to all connected peers like Table-scoped ones. No server-side persistence, no server plugin.
- Why work continued: every client keeps the full shared state with revisions and tombstones, so a rejoining peer re-syncs from any other peer; a true server store can be added later without protocol changes (same envelopes).
- Risk / alternative: an empty server with no peers online holds no atlas; communities wanting server-authoritative storage must wait.
- Must resolve before public release: No (documented limitation)
- Status: Open

### 2026-08-27 — Sync author identity is labeling, not authentication

- Version / issue: v0.6 / CC-037, CC-043 (#49, #55)
- Question: Valheim's modding surface offers no way to cryptographically authenticate which player sent a routed RPC; the author field in sync envelopes is self-declared.
- Safe reversible default selected: author identity is used for audit labels and the non-owner-cannot-delete policy, while every structural protection (revision monotonicity, tombstone no-resurrection, malformed-row skipping, envelope/row caps) holds regardless of the claimed author. Nothing auto-applies — every incoming change goes through the preview inbox.
- Why work continued: the threat model for a co-op map mod is misbehaving clients corrupting data, and that is covered structurally; impersonation only mislabels an audit line the player reviews anyway.
- Risk / alternative: a malicious modded client could claim another player's name in the sync preview.
- Must resolve before public release: No (documented in README security notes)
- Status: Open

### 2026-08-26 — Per-pin custom color/size not rendered on the vanilla map in v0.3

- Version / issue: v0.3 / CC-022, CC-024 (#31, #33)
- Question: vanilla `Minimap.PinData` has no per-pin tint/size; rendering custom colors requires patching the pin UI element pipeline, which cannot be visually verified from automation and risks conflicts with Pinnacle-style mods.
- Safe reversible default selected: color and display-size are first-class stored metadata, editable in the workbench and usable by search/legend; map rendering of them is deferred (pins render with vanilla visuals). Documented in README known limitations.
- Why work continued: no data loss — the metadata is persisted and versioned; rendering can be layered on later without migration.
- Risk / alternative: users may expect the color to show on the map immediately.
- Must resolve before public release: No (documented limitation)
- Status: Open
