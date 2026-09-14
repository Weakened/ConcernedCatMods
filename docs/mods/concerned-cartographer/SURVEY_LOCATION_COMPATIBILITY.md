# Survey loaded-location surface compatibility (issue #258)

Compatibility note for the loaded-world surface the survey reads to offer
dungeon entrances. Written for issue #258, "bug: Auto pinning dungeons not
working" (reported against Concerned Cartographer 1.0.2 on Valheim 1.0.12:
Burial Crypt, Troll Cave and Bear Cave were never offered).

## Audited build

| Component | Audited here | Reporter's environment |
|---|---|---|
| Valheim | 1.0.12 (`Version.CurrentVersion = new GameVersion(1, 0, 12)`) | 1.0.12 |
| Unity | 6000.0.75f1 | not stated |
| BepInEx | 5.4.23.3 (`BepInEx.dll`) | 5.4.2350 |
| Jötunn | 2.29.2 (`Jotunn.dll`) | 2.30.0 |
| Cartographer | source at the #258 fix | 1.0.2 |

The loader versions differ from the reporter's. That difference is not
believed to be material here — the surface is a Valheim field, not a loader
API — but it is recorded rather than glossed over, and it means the audit
below is evidence about the game, not about the reporter's exact stack.

Reproduce with:

```powershell
pwsh ./scripts/audit-cartographer-survey-api.ps1
```

The script reads the installed assemblies and asset catalog. It never
launches the game, so it is a **static audit** and proves nothing about
in-game behaviour; live evidence is recorded separately in the issue.

## Why the networked surface alone could never work

Before the fix the scanner walked exactly one surface:
`ZNetScene.m_instances`, the `Dictionary<ZDO, ZNetView>` of objects
instantiated from ZDOs. A Valheim dungeon entrance is not in it.

1. A world location is placed as a `LocationProxy`. That proxy is the only
   networked object: `LocationProxy.Awake` does
   `m_nview = GetComponent<ZNetView>()`. Its GameObject is named
   `LocationProxy(Clone)`.
2. The proxy then calls
   `ZoneSystem.instance.SpawnProxyLocation(...)`, which enters
   `SpawnLocation(..., SpawnMode.Client, ...)`.
3. In the `SpawnMode.Client` branch, every enabled `ZNetView` in the
   location prefab is deactivated **before** the prefab is instantiated:

   ```csharp
   ZNetView[] array3 = enabledComponentsInChildren;
   for (int j = 0; j < array3.Length; j++) array3[j].gameObject.SetActive(value: false);
   gameObject = SoftReferenceableAssets.Utils.Instantiate(location.m_prefab, pos, rot);
   ```

So no GameObject named `Crypt3(Clone)`, `TrollCave02(Clone)` or
`BearCave(Clone)` is ever registered in `ZNetScene.m_instances`. The
`crypt*` and `trollcave*` survey rules could not fire however close the
player stood. The only names present nearby are the proxy itself and the
location's server-spawned networked pieces (`dungeon_forestcrypt_door`,
`TreasureChest_forestcrypt`, …), none of which carry the dungeon's name.

## The surface the survey now reads

`Location.s_allLocations` — `private static List<Location>`, appended in
`Location.Awake`, removed in `Location.OnDestroy`. It therefore holds the
**already-instantiated locations of currently loaded zones**, bounded the
same way the ZNetScene surface is bounded.

It is explicitly **not** `ZoneSystem.m_locationInstances`, the world-wide
location database. Reading that would reveal locations the player has never
visited, which the fog and NoMap contracts forbid.

Resolution is by name via `AccessTools.Field(typeof(Location), "s_allLocations")`
with a shape check (static, assignable to `List<Location>`). A rename
degrades to `SurveyScanner.LocationSurfaceAvailable == false`, which the
Survey panel reports as "no location surface" — the survey keeps running on
the networked surface instead of silently missing dungeons again.

`Location.m_exteriorRadius` is read so that standing inside a dungeon's own
footprint counts as nearby. `SurveySweep` clamps that bonus to
`MaxFootprintBonusMeters` (32 m), so discovery stays bounded even if a
location (or a mod-added one) reports an absurd radius.

## Installed dungeon-entrance location identities

Read from `valheim_Data/StreamingAssets/SoftRef/manifest` (212 location
prefabs in the installed catalog). The audit asserts each of these exists
**and** is matched by a shipped `cc:dungeon` starter rule:

| Location prefab | Biome folder | In-game name |
|---|---|---|
| `Crypt2`, `Crypt3`, `Crypt4` | BlackForest | Burial Chambers |
| `HalfBurried_ForestCrypt` | BlackForest | half-buried Burial Chamber |
| `Hildir_crypt` | BlackForest | Hildir's crypt |
| `TrollCave02` | BlackForest | Troll Cave |
| `BearCave` | BlackForest | Bear Cave |
| `MountainCave02` | Mountains | Frost Cave |
| `Hildir_cave` | Mountains | Hildir's cave |
| `SunkenCrypt4` | Swamp | Sunken Crypt |

`BearCave`, `HalfBurried_ForestCrypt`, `Hildir_crypt` and `Hildir_cave`
matched **no** rule in the shipped v1.0/v1.1.0 starter set, so they were a
second, independent reason the reporter saw nothing. An untouched starter
`survey-rules.tsv` is upgraded in place; an edited file is never modified.

## Fail-closed behaviour

- Field missing or reshaped → surface unavailable, survey continues, panel says so.
- Destroyed location in the snapshot → entry skipped, still counted against the sweep budget.
- Snapshot bounded by `LoadedLocationSightingSource.MaxLocationsPerSweep` (96).
- One shared per-tick budget across both surfaces, so the added surface cannot raise the per-frame cost.
- Rules, duplicate radius, stable identity, rejection memory, base exclusion, expiry, the observation cap and the Accept review are unchanged — a dungeon is a reviewable observation, never an automatic pin.
