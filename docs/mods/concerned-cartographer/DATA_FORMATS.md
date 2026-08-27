# Concerned Cartographer — Data Formats and Migration Rules

This document describes persistent data owned by Concerned Cartographer for contributors writing migrations, import/export tools or recovery utilities.

## Core rule

Concerned Cartographer persists its own sidecar/config data.

It must **not** use Valheim world-save files as its private database.

## Root

Typical BepInEx config root:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/
```

Exact path depends on the active mod-manager profile.

## Road atlas

File:

```text
<world-uid>.roads.tsv
```

Current architecture supports legacy rows and writes the newest canonical road format.

The architecture currently describes the newest row concept as:

```text
<stroke-guid>\t<kind>\t<point-index>\t<x>\t<y>\t<z>\t<source>\t<flags>\t<format-marker>
```

Semantics:

- `stroke-guid` identifies a polyline;
- `kind` is Dirt/Paved;
- point indices are ordered;
- source stores Traversal/Construction/ChunkRecovery provenance;
- flags include hidden state;
- coordinates use invariant culture.

### Road backups

Potential files:

```text
<world-uid>.roads.tsv.v1.bak
<world-uid>.roads.tsv.pre-reconcile.bak
```

The first protects legacy migration. The second protects the last saved atlas before destructive reconciliation/tool changes in a session.

### Road migration rule

A newer writer should:

1. read supported prior formats;
2. preserve semantic state/provenance when possible;
3. back up before a rewrite older versions cannot read;
4. write one current canonical format;
5. never discard an entire atlas because one row is malformed.

## Pin atlas

Snapshot:

```text
<world-uid>.pins.tsv
```

Journal:

```text
<world-uid>.pins.tsv.journal
```

Every pin has a stable ID and monotonic revision.

Persistence principle:

> replay snapshot + journal, then choose the highest revision per identity.

Deletion is durable tombstone state, not simply absence.

### Crash behavior

- mutations queue a complete pin row;
- autosave appends queued rows to the journal;
- world switch/quit writes an atomic snapshot and absorbs the journal;
- malformed/truncated rows are skipped while valid prior rows remain.

## Free-text escaping

`AtlasText` percent-encodes delimiter-dangerous characters.

Current escaped characters include:

- `%`
- tab
- newline
- carriage return
- comma

Do not invent a second escaping scheme for new TSV sidecars unless a migration explicitly requires it.

## Saved views

File: `views.tsv` (profile-level, world-independent).

Saved views are profile preferences, not world entities.

Current data contains:

- name;
- query;
- Dirt visibility;
- Paved visibility;
- pin visibility;
- clustering flag.

Applying a view must never mutate atlas entities.

## Survey Rules

File: `survey-rules.tsv` (profile-level, intentionally copyable between players).

Survey Rules are intentionally shareable text configuration.

Concepts include:

- exact prefab pattern;
- prefix pattern (`*` suffix);
- blacklist rows;
- icon suggestion;
- category;
- duplicate radius;
- expiry.

Survey rules must never include secrets or machine-specific private paths.

## Route atlas (v0.5+)

Files:

```text
<world-uid>.routes-atlas.tsv
<world-uid>.routes-atlas.tsv.journal
```

Each route serializes as one **meta row** plus its **point rows**, all stamped with the route's revision. Snapshot and journal share the row format; parsing keeps, per identity, only the rows of the highest revision seen, so replay is idempotent and a truncated trailing line costs at most itself.

- Meta v1: 17 fields. Meta v2: 19 fields (marker `2`), adding `OwnerAuthor`/`LastAuthor`. Both parse; v2 is written.
- Point rows: 8 fields (id, revision, index, x, y, z, `P`, marker `1`).
- Route identity: `cc:route:<guid>`. Deletion is a durable tombstone.

## Pin format versions

- Pins v1: 22 fields (marker `1`).
- Pins v2: 24 fields (marker `2`), adding `OwnerAuthor`/`LastAuthor` at indices 18/19 (position moves to 20–22). Both parse; v2 is written.

## Parse-boundary bounds (SEC-1.0-001)

`AtlasLimits` is enforced inside the pin/route/road codecs on every parse (local files and network rows alike):

- revisions above `1e12` are malformed (overflow/lockout protection);
- NaN/Infinity coordinates and size scales are malformed;
- string fields truncate gracefully: name 200, category/icon 100, notes 10 000, at most 64 tags of 64 chars.

New codecs must apply the same bounds.

## Collaboration protocol (v0.6+, protocol version 1)

Transport: `ZRoutedRpc` RPC `CC_AtlasShare`, broadcast to everybody, client-to-client (the server only relays; there is no server-side persistence).

Envelope (ZPackage): protocol version (int), author id (string), author name (string), compressed length (int), compressed payload (byte array). The payload is gzip of a UTF-8 text block: a `PINS` section of pin v2 rows, then a `ROUTES` section of route rows.

Receive-path caps, enforced in order: version match; author strings sanitized (markup/control chars stripped, length capped); self-echo dropped; declared length ≤ 320 000 bytes; declared length must equal actual; **bounded** decompression aborts beyond 4 000 000 bytes (never use unbounded `Utils.Decompress`); at most 20 000 rows; rows parsed by the malformed-skipping codecs.

Semantics: only Table/Server-scoped entities travel (Private never leaves the machine); tombstones travel so deletions propagate; incoming state lands in a review inbox and **nothing auto-applies**; a strictly higher revision wins, equal-revision divergence is a conflict (taking the remote side creates a NEW local revision so both sides converge); non-owner deletions are rejected; the preview lists deletions by name.

Author identity (`author-id.txt`, a GUID per profile) is labeling for audit columns, not authentication.

## Localization overrides (v0.7+)

`cartographer-strings.tsv` — optional `key<TAB>value` overrides for the built-in string catalog. Malformed rows are skipped; unknown keys are ignored. A template can be written from `cc_atlas`.

## Backups (v0.8+)

`cc_atlas backup` copies the atlas sidecars into `backups/<timestamp>/`. Backup folders double as the export/import format (copy them between machines/profiles). `restore <n>` takes its own safety backup first and clears journals so the restored snapshot is authoritative after relog.

## Schema/version change checklist

Whenever persistent data changes:

- [ ] increment/document format/protocol version;
- [ ] retain old reader support where practical;
- [ ] add migration fixture;
- [ ] add malformed/corrupt fixture;
- [ ] create backup before destructive migration;
- [ ] test World A/World B isolation;
- [ ] test interrupted write;
- [ ] test downgrade or explicitly document unsupported downgrade;
- [ ] update `ARCHITECTURE.md`;
- [ ] update this file;
- [ ] update package README migration notes;
- [ ] add final human smoke-test case.

## Manual recovery philosophy

Where practical:

- keep sidecars human-readable;
- document backup filenames;
- document restore steps;
- retain backups until migration is proven;
- never require a player to repair the Valheim world file because of our sidecar format.
