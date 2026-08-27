# Concerned Cartographer v1.0 — Release Dossier

Prepared by the autonomous conveyor (Tankard Olafsson) per OPS-001 rev 2.
The single remaining gate is the human smoke test
(`PRE_RELEASE_SMOKE_TEST.md`); nothing has been published.

## 1–5. Release candidate identity

- **Version:** 1.0.0
- **RC commit:** `6ee4ee90d18d83ac9af274a7bb795262f81af060` (main; includes the
  SEC-1.0-001 sync hardening, issue #87, and the developer/provenance
  documentation kit, PR #88, whose package-README attribution ships in the
  ZIP — supersedes RCs `ff2bc798` and `53f371c`)
- **ZIP:** `artifacts\thunderstore\TheConcernedCat-ConcernedCartographer-1.0.0.zip`
- **ZIP SHA-256:** `0E30E9A4B8ABFB95CCD23393B3F7E85A81843E5F4EFD08670EA34EED169CE5DE` (209,218 bytes)
- **Plugin DLL SHA-256:** `BF82967E6D023C0F7857510069A60A33820FCB61753B5F05DAA773591A21A3D3` (253,952 bytes; the DLL inside the ZIP and the DLLs deployed to the TCC-Dev/TCC-Compat profiles are hash-identical; source-identical to the DLL of RC `53f371c` — only doc files changed between them)
- **Package audit:** ZIP root contains exactly `manifest.json`, `README.md`,
  `CHANGELOG.md`, `LICENSE`, `icon.png` (256×256),
  `plugins/TheConcernedCat.ConcernedCartographer.dll`. No PDBs, game DLLs,
  saves, logs, or secrets. Dependencies pinned:
  denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2.

## 6. Sprints and issues

Every sprint v0.3→v1.0 shipped through its internal gate; all 42 child
issues and 8 controllers (#8, #27–#81) are closed with evidence comments.
Shipped versions on main with tags: 0.3.0, 0.4.0, 0.5.0, 0.6.0, 0.7.0,
0.8.0, 0.9.0; 1.0.0 is merged to main awaiting its post-smoke tag.

## 7. Defects

Full-conveyor totals: 8 defects filed and fixed across v0.1–v0.2
(#82–#86 plus three pre-OPS fixes); **zero** open P0/P1/P2 at the RC.
Notable finds: chunk-recovery MethodAccessException (P1, silent
fail-closed — caught by log review), terraforming-inks-roads (P2), ink
contrast (P3).

Post-RC audit: SEC-1.0-001 (#87) — owner-requested adversarial audit of
the sync receive path found and fixed 7 hardening gaps, the worst a
decompression bomb (size cap was checked only after unbounded
decompression). All fixed in the RC identified above: bounded gzip,
revision sanity cap, non-finite float rejection, string-length caps,
deletion names in the sync preview, author display sanitization, and
declared-length verification.

## 8. Automated evidence (at the RC commit)

- **243/243 tests** in the game-free core suite (CI-run): road geometry
  and suppression, codecs and journal recovery for all three entity
  families, migration matrix across every shipped format, pin/route
  operations with undo-convergence properties, query/clustering,
  survey bounds, sync policy/planner including tombstone
  no-resurrection, localization safety, and the SEC-1.0-001 hardening
  suite (decompression-bomb rejection, revision/float/string bounds,
  deletion-name previews, display sanitization).
- Validator green with `--expected-version 1.0.0`; solution builds with
  0 errors (1 known benign MSB3245).
- Scale: 10,000-pin suites (<200 ms total), 10 km road compaction
  (6,667→186 pts, 8 ms), 10k query <500 ms bound (measured far lower).

## 9. Actual in-game evidence (genuinely observed, v0.1–v0.2 era)

Owner-verified during earlier campaigns: road survey/classification,
construction capture, reconciliation, recovery (post-fix), repair tools,
persistence, world isolation, uninstall safety, v1→v3 road migration with
backups, Pinnacle+MapRoutes coexistence, fresh-profile ZIP install,
30-minute stability. **Everything v0.3+ is implemented fail-closed but
NOT in-game verified** — that is exactly what the smoke test covers.

## 10. Manual-only items

The complete list is `PRE_RELEASE_SMOKE_TEST.md` sections 1–13 (each row
with setup/action/expected/evidence/blocking). Genuinely human-only:
every visual/UX row, the two-client collaboration section (7), NoMap and
controller feel (8), live compat sessions (9), upgrade/uninstall
rehearsals (10), soak (11), Thunderstore preflight (12).

## 11–15. Result summaries

- **World isolation/persistence:** per-UID sidecar family; isolation
  verified in-game in v0.1; crash-safe journals property-tested; smoke 4.x
  re-verifies live.
- **Multiplayer/tombstone/conflict:** structurally guaranteed and
  property-tested (stale clients cannot resurrect deletions; conflicts
  converge); live two-client confirmation is smoke section 7.
- **Compatibility matrix:** detection + policies for 6 known mods;
  Pinnacle/MapRoutes verified live in v0.1-era; others are smoke rows.
- **Performance/soak:** automated numbers above; feel/soak is smoke 11.
- **Localization/controller/accessibility:** framework + template +
  overrides shipped; select-on-open chains + opt-in gamepad bindings;
  UiScale/HighContrast/non-color cues; visual rows in smoke 8.

## 16. Known limitations

Documented per version in CHANGELOG plus HUMAN_ATTENTION deferrals: pin
color/size not map-rendered; no server-side sync store; no MapRoutes
import; console-proximity (not map-click) selection; author labels not
authentication; survey matches loaded objects only.

## 17. HUMAN_ATTENTION summary

Seven open ledger entries, **none marked must-resolve-before-release**;
all are documented limitations or deferred alternatives with reversible
defaults.

## 18. Smoke test

`docs/mods/concerned-cartographer/PRE_RELEASE_SMOKE_TEST.md`

## 19. Remaining Git commands (run after the smoke test passes)

```powershell
git tag -a concerned-cartographer/v1.0.0 -m "Concerned Cartographer 1.0.0 - Stable Living Atlas" 6ee4ee90d18d83ac9af274a7bb795262f81af060
git push origin concerned-cartographer/v1.0.0
gh release create concerned-cartographer/v1.0.0 artifacts/thunderstore/TheConcernedCat-ConcernedCartographer-1.0.0.zip --title "Concerned Cartographer 1.0.0" --notes-file src/ConcernedCartographer/Package/CHANGELOG.md
```

(main already contains the RC via the sprint merge.)

## 20. Thunderstore upload data (owner-only)

- File: `TheConcernedCat-ConcernedCartographer-1.0.0.zip`
- Team/namespace: **TheConcernedCat** · Community: **valheim**
- Categories: **mods, client-side, utility, ai-generated**
- Dependencies: denikson-BepInExPack_Valheim 5.4.2333, ValheimModding-Jotunn 2.29.2
- Version: 1.0.0 · Upload via thunderstore.io web UI, or
  `pwsh ./scripts/publish.ps1 -Version 1.0.0` with `TCLI_AUTH_TOKEN` set
  only in that shell.

## 21. DO NOT RELEASE IF

- Any **BLOCKS** smoke row fails and cannot be fixed + re-verified.
- The ZIP hash on disk no longer matches this dossier.
- A human ZIP inspection finds anything beyond the six audited entries.
- The two-client tombstone test (smoke 7.4) shows a resurrected deletion.
- Any world save, character file, or foreign mod's data is modified in
  any test.
- The fresh-profile install (smoke 10.4) fails to reach the main menu
  cleanly.
