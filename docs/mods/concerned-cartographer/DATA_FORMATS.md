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

### Markers the mod writes for itself

Two files are not data a person edits: the profile's generated author identity,
and the marker saying the first-run tip has been shown. Since 1.2.2 they live in
a `state` subfolder of the product's directory:

```text
BepInEx/config/ConcernedCatMods/ConcernedCartographer/state/author-id.dat
BepInEx/config/ConcernedCatMods/ConcernedCartographer/state/onboarding-shown.dat
```

A mod manager's configuration editor offered `author-id.txt` for editing
(#304), and that file is a generated GUID: editing or deleting it changes who
the atlas believes wrote this profile's entries, which is what the
non-owner-delete policy is keyed on.

**What actually fixes this is the `.dat` extension, not the subfolder.** Read
from the installed Thunderstore Mod Manager bundle (1.124.2,
`APP_NAME="r2modman"`, core 3.2.18): the configuration editor is rooted at the
**whole profile**, excluding only `dotnet`, `_state` and a plugin's
`manifest.json`, and then filters by
`SUPPORTED_CONFIG_FILE_EXTENSIONS = [".cfg", ".txt", ".json", ".yml", ".yaml", ".ini"]`.
It descends into `state/` like any other folder. `author-id.txt` was listed
because `.txt` is on that list; `.tsv` never has been, which is why no sidecar
has ever appeared there. So 1.2.2 shipped two changes together and the
subfolder got the credit that belonged to the rename.
`validate_repo.py` now enforces the extension rule for every name written into
the product's directory, checked against `CartographerConfigFiles`.

**This has not been checked against Gale.** Gale is an independent Rust
implementation, it is not installed here, and nothing above was measured from
it. The reporter has not confirmed either.

**The subfolder still earns its place, for a different reason.**
`CartographerLegacyProbe` decides new-player versus
returning-player by listing this directory with `Directory.GetFiles` and asking
whether every name is one this build writes for itself. `GetFiles` does not
return subdirectories, so a marker under `state` is invisible to it. A rename in
place would not have been: `author-id.dat` in the product directory was a name
that listing did not recognise, so every new player was classed as a returning
one and the #264 introduction could never have run again (#343). Both `.dat`
names, and the companion sidecar's `.corrupt` quarantine name, are now in
`CartographerFirstRunFiles` as well, so a file left behind by a failed adoption
is still not mistaken for a player's own doing.

**Upgrading.** Two prior locations are honoured, **newest first**:
`author-id.dat` in the product directory (the build between, commit `6903a65`)
and then `author-id.txt` (the original). A file still sitting in either one is
proof that adoption never finished, and it predates the marker — so it wins over
the marker beside it, which is either a partial copy of it or a value this build
minted during a failure.

Adoption reads, checks (an author identity must parse as a GUID), stages to a
temporary file, copies onto the marker, and reads **the marker** back; only then
is every prior copy removed. A failure at any point leaves the prior file exactly
where it is and says why in the log, and the next start tries again.

**A new identity is the last resort.** If a prior file exists but cannot be read,
no identity is created: creating one writes a marker that reads as usable on every
later start, and nothing would look at that file again. The session adds no audit
labels instead, and tries again next time. Minting a value that cannot be saved is
refused for the same reason — it would be stamped into pin and route records as
their owner and never come back.

A fresh identity is generated only when there is nothing anywhere to adopt. That
matters because it is not a cosmetic difference: entries this profile wrote under
an older identity would then be owned by an identity it no longer has, and the
non-owner-delete policy would refuse to delete them. A prior file is never
removed until the marker has been written and read back, precisely so that case
stays recoverable by hand.


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

## Terrain intent (negative road coverage)

File:

```text
<world-uid>.terrain-intent.tsv
```

Format v1 (DEF-v1.0-005): a header row, then one row per excluded 1 m
ground cell (cell indices, invariant culture):

```text
cc-terrain-intent\tv1
cell\t<cx>\t<cz>
```

Semantics:

- a cell is excluded when one of the local player's own successful
  Level/Raise/Cultivate/Reset operations covered it (brush radius plus a
  1 m margin) — dirt paint there is a terraforming side effect, never a
  road;
- traversal and chunk recovery refuse Dirt observations inside excluded
  cells; Construction (explicit Pathen/Paved) is never gated, and its
  brush clears the cells it covers;
- bounded: 250,000 cells per world with oldest-first eviction beyond the
  cap; adds/clears are idempotent set operations, so overlapping and
  repeated operations converge;
- world-isolated by filename; derived only from the local player's own
  operations, so it can never reveal unexplored terrain.

### Terrain-intent migration rule

- readers skip malformed rows individually and never discard the file for
  one bad row;
- an **unknown header** (newer format or foreign file) loads as EMPTY for
  the session, and the file is rewritten as v1 on the next save. This is
  deliberate: the mask is derived safety data — the worst downgrade cost
  is that already-leveled ground can re-ink until terraformed again; no
  user-authored data is involved.

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

Author identity (`state/author-id.dat`, a GUID per profile) is labeling for audit columns, not authentication.

## Localization overrides (v0.7+)

`cartographer-strings.tsv` — optional `key<TAB>value` overrides for the built-in string catalog. Malformed rows are skipped; unknown keys are ignored. A template can be written from `cc_atlas`.

## Backups (v0.8+)

`cc_atlas backup` copies the atlas sidecars into `backups/<world-uid>-<timestamp>-<label>/`. Backup folders double as the export/import format (copy them between machines/profiles). `restore <n>` takes its own safety backup first and clears journals so the restored snapshot is authoritative after relog.

**All five per-world sidecars, and one list of them (#367).** A backup covers
`.roads.tsv`, `.pins.tsv`, `.routes-atlas.tsv`, `.survey-rejected.tsv` and
`.terrain-intent.tsv`. It used to cover the first three: the backup tools kept
their own copy of the list, so a player's rejected-observation memory (what he
already said no to, which is what stops the survey re-offering it) and his
terrain-intent exclusion mask were in no backup, could not be restored, and were
missing from the support report while it read as complete. There is now one list,
`CartographerWorldSidecars`, and it is the same list the fresh-install probe
treats as proof a player used this mod — which `CartographerPaths` already named
as the authority. Adding a suffix there backs it up, restores it, clears its
journal and reports on it in the same change.

**Names on disk are invariant, always (#367).** The folder name was composed with
string interpolation, which formats under the player's own culture, while
`cc_atlas backups` globbed for the invariant world uid. Where the two disagree —
`NumberFormatInfo.NegativeSign` is culture data and several cultures use U+2212
rather than ASCII hyphen-minus, so a negative world uid is the realistic trigger
— the backup is written successfully under a name the lister cannot find:
`cc_atlas backups` shows nothing, `restore` cannot offer it, and nothing warns,
because both halves succeed. Every name on disk now comes from
`AtlasBackupNaming`, by concatenation under `CultureInfo.InvariantCulture`, never
by interpolation. Log lines and console replies are not names and stay in the
player's own culture, where their number formatting is the correct one.

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
