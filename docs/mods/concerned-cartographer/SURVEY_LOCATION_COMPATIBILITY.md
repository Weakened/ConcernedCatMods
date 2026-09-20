# Survey loaded-location surface compatibility (issues #258, #385)

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

`Location.m_exteriorRadius` (a public field) is read so the scan range is
measured from the location's **boundary** rather than its centre: the test
is `distance <= scanRadius + min(exteriorRadius, 32 m)`. `SurveySweep`
applies that `MaxFootprintBonusMeters` clamp, so discovery stays bounded
even if a location (or a mod-added one) reports an absurd radius. At the
maximum configurable scan radius this reaches 132 m for a location versus
100 m for a networked object — still a bounded nearby-loaded read, never a
world-database query.

Reads are split so the added surface does not change the per-frame cost
profile. `TryReadPlacement` is the cheap positional read taken for every
examined entry; `TryReadName` — where a surface pays for `GetComponent`
and for `UnityEngine.Object.name`, an interop call that allocates a fresh
string on every access — runs only for an entry the sweep has already
accepted as in range. That is exactly the ordering the inline scanner loop
had before this change.

Surface **order** matters too. The engine's observation cap ends a sweep,
so the scanner walks the small loaded-location surface FIRST; otherwise a
pending list already full of berry bushes could end every sweep on the
networked surface and starve dungeon discovery — the original symptom,
reintroduced by the fix.

## Covered scope

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
`DungeonSurveyDiscoveryTests` pins `V1StarterSet()` to a golden copy of the
file that actually shipped, because the upgrade only fires on a
byte-identical match — drift there would silently re-open this issue for
every existing player.

### Deliberately NOT covered

The reviewed list above is a scope, not a claim of completeness. The audit
reports `locationsNotCoveredByDungeonRules` (202 of the 212 entries) so the
gap stays visible. Mistlands and Ashlands entrances — infested mines,
Dvergr town and boss entrances, charred fortresses — are tracked in
issue #260 and were not added here because their in-game classification was
not verified during this fix.

### A second surface for the same site: runestones

The pre-existing `runestone*` starter rule now also matches world
locations, because lore runestone sites exist on both surfaces in the
installed catalog:

| Surface | Installed asset |
|---|---|
| Networked prop | `Assets/world/Props/RuneStones/RuneStone_BlackForest.prefab` |
| World location | `Assets/world/Locations/BlackForest/Runestone_BlackForest.prefab` |

Both spellings clean to the same identity, so the rule's 80 m duplicate
radius collapses the pair into one observation. The audit asserts both
assets still exist, so this behaviour rests on evidence rather than on an
assumed name. It is disclosed in the changelog because it can produce
"Points of interest" observations a 1.0.2 player never saw.

## Third-party locations: OreMines (issue #385)

Issue #385, "feature: Auto Pin compat with the OreMines" (reported by
kyoknightly), is the same surface seen from the other side. blacks7ar's
**OreMines** adds its mines as ordinary Valheim world locations, so they
were already on the `Location.s_allLocations` surface described above and
already inside every bound — they simply matched no rule, and a rule that
matches nothing is a silent no-op. The reporter had to pin a copper, a tin
and a silver mine by hand.

The fix is therefore rule identity only:

| Starter rule | OreMines location prefabs it covers |
|---|---|
| `bom_flintmine*` | `BOM_FlintMine01`, `BOM_FlintMine02` |
| `bom_coppermine*` | `BOM_CopperMine01`, `BOM_CopperMine02` |
| `bom_tinmine*` | `BOM_TinMine01`, `BOM_TinMine02` |
| `bom_coalmine*` | `BOM_CoalMine01`, `BOM_CoalMine02` |
| `bom_ironmine*` | `BOM_IronMine01`, `BOM_IronMine02` |
| `bom_silvermine*` | `BOM_SilverMine01`, `BOM_SilverMine02` |
| `bom_blackmetalmine*` | `BOM_BlackMetalMine01`, `BOM_BlackMetalMine02` |
| `bom_flametalmine*` | `BOM_FlametalMine01`, `BOM_FlametalMine02` |

Each is `cc:mine` / "Resources" with location-scale bounds (80 m duplicate
radius, 8 h expiry) — a mine is a fixed place, not a respawning deposit.

### Provenance of these names, and its limits

OreMines is **not installed on the audit machine**, so unlike the vanilla
table above these names were not read out of an installed asset catalog and
`audit-cartographer-survey-api.ps1` cannot assert them.

- **Primary source:** the eight mine types OreMines 1.2.1 documents for its
  `UpgradeWorld` commands, recorded in the issue #385 triage note against
  the public package page
  (`https://thunderstore.io/c/valheim/p/blacks7ar/OreMines/versions`).
- **Corroboration, partial:** the reporter, running OreMines in a live
  world, was told to add `bom_coppermine*` / `bom_tinmine*` /
  `bom_silvermine*` rules by hand and reported back that he had it working.
  That supports the `BOM_<Ore>Mine` shape for three of the eight; it is a
  user report, not an observation of ours.
- **Consequence if a name is wrong:** a rule that matches nothing. A wrong
  name is a silent no-op, never a misfire — but also never an error, which
  is exactly why it must be checked in game rather than assumed. Live
  verification with OreMines installed is still owed.

Rules are prefixes, so a later `03` variant is covered; they are per mine
type rather than a blanket `bom_*`, which would pin every prop and piece
the mod ships and would reach into the author's other mods sharing the
prefix.

### What is deliberately absent

No assembly reference, no `BepInDependency`, no foreign type, no version
check, no patch. Compatibility is by location name only, so with OreMines
absent the rules are inert text that matches nothing:
`OreMinesSurveyCompatibilityTests` sweeps a vanilla-only world through both
the previous and the current starter set and requires the two observation
lists to be identical. No `ZoneSystem` world-database lookup, no fog
reveal, and no change to any scan radius, duplicate radius, expiry or
observation budget.

Existing players receive the identities through the same in-place starter
migration #258 established, now factored into `SurveyStarterUpgrade` in the
domain so it is under test: an untouched `survey-rules.tsv` from any
shipped starter set (pre-RC8, RC8/RC9, v1.0-v1.1.0, or v1.0.3-v1.2.2, the
last pinned to a golden copy in the tests) is rewritten with the current
set, and a file the player touched at all — a rule added, removed,
disabled, a bound retuned, a blacklist row or even a comment of his own —
is left exactly as it is.

## Fail-closed behaviour

- Field missing or reshaped → surface unavailable, survey continues, panel says so.
- Destroyed location in the snapshot → entry skipped, still counted against the sweep budget.
- Snapshot is **not** truncated: the loaded-location set is already bounded by the loaded zones, and a fixed cap would have silently dropped whichever locations loaded last — exactly the ones the player is walking toward.
- One shared per-tick budget across both surfaces, so the added surface cannot raise the per-frame cost.
- Rules, duplicate radius, stable identity, rejection memory, base exclusion, expiry, the observation cap and the Accept review are unchanged — a dungeon is a reviewable observation, never an automatic pin.
