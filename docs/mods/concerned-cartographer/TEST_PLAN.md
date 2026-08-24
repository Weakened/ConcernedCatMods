# Concerned Cartographer test plan

Record date, game version, mod versions, profile, world, result, log excerpt, and screenshot/video reference for every manual run.

## Static checks

```powershell
python ./tools/validate_repo.py
pwsh ./scripts/build.ps1 -Configuration Debug
pwsh ./scripts/package.ps1 -Configuration Release
```

## Smoke test

- [ ] Start `TCC-Dev` in a disposable world.
- [ ] BepInEx reports Concerned Cartographer 0.1.0 loaded.
- [ ] No exception or repeated warning is emitted by the mod.
- [ ] Open/close the map repeatedly.
- [ ] Logout to menu and re-enter without stale overlay references.

## Terrain classification matrix

Test at least five samples of each:

- [ ] untouched meadow is not classified as a road;
- [ ] cultivated soil is not classified as a road;
- [ ] dirt Pathen is classified as Dirt;
- [ ] paved terrain is classified as Paved;
- [ ] path edges do not flicker excessively between road/no-road;
- [ ] standing on a building piece does not create a terrain road.

## Road recording

- [ ] Walking continuously along dirt creates one coherent stroke.
- [ ] Walking continuously along paved terrain creates one coherent stroke.
- [ ] Switching dirt → paved starts the correct new stroke.
- [ ] Leaving the path ends the active stroke.
- [ ] Teleporting does not draw a line across the world.
- [ ] Doubling back does not cause unbounded duplicate sampling.

## Map behavior

- [ ] Dirt overlay appears on the full map.
- [ ] Paved overlay appears on the full map.
- [ ] Both appear on the minimap.
- [ ] Each layer can be toggled independently through Jötunn's overlay UI.
- [ ] Unexplored fog hides road data by default.
- [ ] World reload redraws the same atlas.

## Persistence and isolation

- [ ] A sidecar file is created under BepInEx config, not the Valheim save folder.
- [ ] Restarting the game preserves the atlas.
- [ ] World A data does not appear in World B.
- [ ] Returning to World A restores only World A data.
- [ ] One intentionally malformed row is skipped without losing valid rows.
- [ ] Removing the mod leaves the world playable and unchanged.

## Performance

- [ ] Run for 30 minutes while traversing/creating roads.
- [ ] No repeated allocations/log spam are obvious in the BepInEx console.
- [ ] No visible hitch occurs for normal incremental samples.
- [ ] Map/world load full redraw completes acceptably on the test PC.
- [ ] Sidecar file size is recorded after 1 km and 10 km of surveyed road.

## Compatibility

In `TCC-Compat`:

- [ ] Pinnacle loads and its pin editor/search still work.
- [ ] MapRoutes loads and its manual routes still render.
- [ ] Concerned Cartographer overlays can be toggled.
- [ ] No Harmony or UI conflict is visible in logs.

## Package installation

- [ ] `icon.png` is exactly 256×256.
- [ ] ZIP root contains `manifest.json`, `README.md`, `icon.png`, `CHANGELOG.md`, `LICENSE`, and `plugins/`.
- [ ] ZIP contains only the Concerned Cartographer DLL under `plugins/`.
- [ ] Import ZIP into a fresh profile.
- [ ] Dependencies install automatically.
- [ ] Fresh-profile smoke test passes.

## Public alpha gate

- [ ] All smoke, classification, map, persistence, and package tests pass.
- [ ] Performance test has no release-blocking result.
- [ ] Compatibility test passes or a limitation is prominently documented.
- [ ] README matches actual behavior and limitations.
- [ ] Changelog and all version fields match.
- [ ] `AI Generated` category is selected.
- [ ] Human reviewer inspects the final ZIP for secrets, saves, game DLLs, and unrelated files.
- [ ] Human explicitly approves the final `tcli publish` invocation.
