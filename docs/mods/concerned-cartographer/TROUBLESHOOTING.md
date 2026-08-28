# Concerned Cartographer — Troubleshooting

## Start with the log

Primary log:

```text
<active profile>/BepInEx/LogOutput.log
```

Search for:

```text
Concerned Cartographer
```

Include in bug reports:

- Valheim version;
- Concerned Cartographer version;
- BepInEx version;
- Jötunn version;
- mod-manager profile/mod list;
- exact reproduction;
- relevant log excerpt.

## Where to send what

- Ordinary bugs and feature requests: the GitHub issue tracker
  (https://github.com/Weakened/ConcernedCatMods/issues) — first stop.
  `cc_atlas support` produces a sanitized report safe to attach.
- Security vulnerabilities, privacy/crash-reporting questions, or logs
  and information that should not be posted publicly:
  **support@theconcernedcat.com**.
- Optional anonymous crash reporting (opt-in, `PRIVACY.md`) reaches the
  maintainer automatically when enabled — never by email.

## Plugin does not load

Check:

1. the profile was launched modded;
2. BepInEx is installed;
3. Jötunn is installed;
4. the DLL is under the active profile's `BepInEx/plugins`;
5. no second old DLL exists elsewhere in plugins;
6. package dependencies match the supported versions.

## Builds but crashes in game

Compilation only proves the reference/publicized surface compiled.

Likely post-Valheim-update breakpoints:

- private method/field changed;
- Harmony target changed;
- private access is rejected at runtime;
- map lifecycle changed;
- Unity UI hierarchy changed.

Check adapters first:

- `GroundPaintProbe`
- `ConstructionCapture`
- `ChunkRecoveryScanner`
- `MinimapReflection`
- `PinAdapter`
- `RoadOverlayRenderer`

## Roads do not appear

Check source/config toggles, map layer visibility, world UID, current profile sidecar location and logs.

Use:

```text
cc_roads status
```

## Old roads do not recover

Recovery is deliberately bounded and only scans loaded/explored terrain.

Check:

- `RecoverLoadedChunks`;
- recovery budget;
- player proximity;
- map area explored;
- road is not too broad for recovery heuristic;
- no recovery-source error logged.

## Map position looks offset

Use calibration diagnostics and known world positions.

Remember the map texture has finite resolution, so sub-texel error is expected.

## Pins duplicate after restart

Treat as release-blocking.

Preserve:

- `.pins.tsv`;
- `.pins.tsv.journal`;
- screenshot;
- log;
- exact adoption/edit/logout steps.

Do not manually delete the only copy before diagnosis.

## Deleted pin returned

For local pins inspect revision/tombstone persistence.

For collaborative pins, stale-client resurrection is a P1 release blocker. Capture both clients' logs and the sync sequence (`cc_sync inbox`/`preview` output on both sides).

## Sharing does not arrive

Check on both clients:

1. `cc_sync status` — the transport registers once the routed-RPC system is alive;
2. the sending pin/route is Table/Server scoped (Private never travels);
3. the receiver's `cc_sync inbox` (nothing auto-applies — a share always waits there);
4. the log for "Ignored an atlas share" warnings: oversized, corrupt, or over-cap envelopes are rejected by design;
5. both sides run the same protocol version (same mod version).

## Route/backup problems

- `cc_routes status` summarizes the route atlas; routes have their own sidecar (`<world-uid>.routes-atlas.tsv` + journal) with the same snapshot/journal recovery as pins.
- `cc_atlas backups` lists snapshots; `cc_atlas restore <n>` takes a safety backup first. After a restore, relog so the restored snapshot is authoritative.
- `cc_atlas support` writes a sanitized report (versions, settings, counts, sizes only) safe to attach to a bug report.

## Sidecar corruption

First copy the entire Concerned Cartographer config folder somewhere safe.

Do not edit the only copy.

Road backups may include `.v1.bak` and `.pre-reconcile.bak`. Pin snapshot+journal recovery should skip malformed trailing rows.

## World fails after disabling the mod

Concerned Cartographer is designed to keep private data outside Valheim saves.

If disabling the mod makes a world unplayable:

- treat as P0/P1;
- preserve world backup/logs;
- do not keep trying destructive fixes;
- report immediately.

## Contributor diagnostic rule

Do not “fix” a bug by adding a broad catch that silently hides data corruption.

A good fail-closed fix:

- isolates the failing adapter;
- logs once/actionably;
- preserves stored data;
- allows unrelated features to continue;
- adds a regression test where possible.
