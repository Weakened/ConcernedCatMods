# Living beside TheGreatestMap, HUDCompass and Round Minimap (#419)

- Issue: #419 (reported by `Truncated`)
- Research date: 2026-10-27. Versions checked: TheGreatestMap **4.5.2**, HUDCompass **1.2.1**,
  Round Minimap **1.4.1**; Concerned Cartographer `main` at `89ce8b6`; Jötunn **2.29.2** (the version Cartographer
  references).
- Status: **code and unit tests done; every in-game row is THIRD-PARTY LIVE TEST PENDING.** Nothing below says
  "compatible" as an observation. It describes what each mod was read to do and what Cartographer now does about it.

## 1. What was checked, and how

No third-party binary or source is committed, copied or redistributed. The DLL-only mods were downloaded into a
temporary folder outside the repository and inspected there for type and method names, patch attributes and the
specific method bodies cited below. The folder was then deleted.

| Mod | Where it is published | Latest checked | Depends on | Plugin GUID (evidence) | Source |
|---|---|---|---|---|---|
| TheGreatestMap (DeathMonger) | [Thunderstore](https://thunderstore.io/c/valheim/p/DeathMonger/TheGreatestMap/) | 4.5.2, uploaded 2026-10-27; frequent releases since 2026-09-11 | BepInExPack 5.4.2350 only; ServerSync embedded; **no Jötunn** | `DeathMonger.TheGreatestMap`: the `[BepInPlugin]` attribute in the 4.5.2 DLL, and the README's config file name `DeathMonger.TheGreatestMap.cfg` | [gbahns/ValheimMods](https://github.com/gbahns/ValheimMods/tree/main/TheGreatestMap) is the package's website, but it is **stale** (manifest 1.8.1). Everything below comes from the 4.5.2 DLL. |
| HUDCompass (Neobotics) | [Thunderstore](https://thunderstore.io/c/valheim/p/Neobotics/HUDCompass/), [Nexus 2674](https://www.nexusmods.com/valheim/mods/2674/) | 1.2.1, uploaded 2026-10-09; CHANGELOG 1.2.0 says "Updated for Valheim 1.0" | BepInExPack 5.4.2100 only | `neobotics.valheim_mod.hudcompass`: the `[BepInPlugin]` attribute; the string is present in the 1.2.1 DLL | closed source (DLL only) |
| Round Minimap (Ithilias) | [Hexium](https://valheim.hexium.gg/mods/Ithilias/RoundMinimap) only. **Not on Thunderstore** (its API returns 404 for `Ithilias/RoundMinimap`) and no Nexus listing was found. | 1.4.1, 2026-10-21, "Client-only (Valheim 1.0)" | BepInExPack only | `ithilias.roundminimap`: `RoundMinimap/src/Plugin.cs` (`Guid = "ithilias.roundminimap"`) | [Ithilias/valheim-mods](https://github.com/Ithilias/valheim-mods), MIT |

Cartographer recognises the three mods by these exact GUIDs (`Domain/Atlas/NeighborMapMods.cs`). The match is
case-insensitive but exact, so a similarly named mod is never mistaken for one of them.

## 2. What each mod hooks

### TheGreatestMap 4.5.2

- **Minimap patches.** These come from Harmony attributes in the DLL; there are no priorities except where stated.
  - `Start` postfix at priority 0, so it runs last. It registers its own icon sprites into `m_icons`.
  - `UpdatePins` prefix, which grows `m_visibleIconTypes` to cover its own types.
  - `UpdatePins` postfix, which styles **its own** pins and brings portals to the front.
  - `AddPin` prefix.
  - `RemovePin(PinData)` prefix and postfix. The prefix **refuses to remove its own markers** unless Shift is held or
    the server allows it. Other pins pass through.
  - `RemovePinUnderPointer` prefix. On its own markers it opens its marker menu instead.
  - `OnMapLeftClick` and `OnMapDblClick` prefixes. These gate vanilla editing behind its "Require Map Out To Edit"
    option.
  - `OnPinTextEntered` prefix and postfix.
  - Postfixes on `ClearPins`, `SetMapData`, `IsPointVisible` and `SetMapMode`.
  - Prefixes on `GetMapData` and `GetSharedMapData`.
  - `AddSharedMapData` prefix and postfix.
  - An unscaled-time transpiler on `Update`, `UpdateMap` and related methods, because it **pauses the game while the
    large map is open**.
- **Ownership of hand-placed markers.** Its "Share Placed Markers" option is synced and on by default. With it, every
  marker placed through the vanilla naming flow is *adopted*: the `OnPinTextEntered` postfix, or the naming box closing,
  calls `ClientPins.AdoptLocalPin`, which starts tracking that very `PinData` and shares it with the server.
- **`AddPin` prefix.** It returns no pin only while a cartography table is being read **and** "Table Carries Player
  Markers" is off, and only for hand-placeable types. Cartographer never adds pins inside a table read (that read is one
  synchronous vanilla call), so its `AddPin` calls are unaffected.
- **It removes pins it does not own.** Two console commands do this:
  - `tgm_clearlocal` calls `Minimap.RemovePin(PinData)` on **every** hand-placeable pin that is not its own.
  - `tgm_import` adopts such pins, and removes any that sit within 1 m of one of its own markers.
  - It also has a sweep after a map load or table read. That sweep only removes pins with a non-zero owner ID, and
    Cartographer's markers always have owner ID 0, so it does not affect them.
- **Its own pin types and sharing.**
  - It takes pin types **100 to 999**.
  - Its `GetSharedMapData` prefix keeps every type ≥ 100, and the five hand-placeable types unless "Table Carries
    Player Markers" is on, out of cartography-table data.
- **Map UI.**
  - Its marker and pause toggles are clones of a vanilla rail button. They are reparented to `m_largeRoot` and
    anchored top-right; they do not sit inside the vanilla rail.
  - Its legend and menus live under its own roots.
  - Its "MapFrame" lets the player resize and move the large map by rewriting `m_largeRoot`'s size and position. It
    adds a `RectMask2D` to the large pin roots.
- **Round Minimap.** When Round Minimap's shape is applied, its `MinimapClip` reflects into
  `RoundMinimap.MinimapShape.IsApplied`. It then calls `SetActive(false)` on every child of `m_pinRootSmall` outside
  the circle, and re-shows only the children it hid itself.
- **Map textures.** It does not replace the minimap's material or textures. It does not reference Jötunn.

### HUDCompass 1.2.1

- **Minimap patches.**
  - `Start` postfix.
  - Two `UpdatePins` postfixes, which draw its dynamic cart, ship and portal markers.
  - `AddPin` postfix, which can remove a fresh death pin when there is no tombstone.
  - `OnTogglePublicPosition` postfix.
  - `ToggleIconFilter` prefix that only logs.
- **Its markers are its own.**
  - The dynamic markers are GameObjects instantiated from `m_pinPrefab` under `m_pinRootSmall` and `m_pinRootLarge`.
    They are **not** added to `m_pins`.
  - They use types 6 (portal) and 8 (ship and cart), and they follow `m_visibleIconTypes`.
- **The compass bar** iterates **all** of `Minimap.m_pins`, so Cartographer's markers appear on it. Its "C – Ignore"
  settings ("Pin Names" with `*` wildcards, "Pin Types") hide them there.
- No material, texture, mask or rotation work was seen.

### Round Minimap 1.4.1

- **Patches.** `Minimap.Update` postfix and `Hud.UpdateStatusEffects` postfix (`src/Patches.cs`).
- **The disc and the rotation.**
  - Both are produced by one `BaseMeshEffect`, `CircleMeshEffect`, added to the `m_mapImageSmall` GameObject only.
    It replaces the image's quad with a disc and rotates the sampled UVs (`src/CircleMeshEffect.cs` and
    `src/MinimapShape.cs`).
  - Nothing is reparented, no material is replaced, and no `RectTransform` is rotated.
  - Rotation is on by default ("Rotate minimap") and applies only to the small map.
- **Pins.**
  - Every frame it re-positions each `m_pins[i].m_uiElement` and pin name rotated about the centre, keeping icons
    upright. Pins past the edge are hidden with zero scale, never `SetActive`.
  - "Turn icons from other mods" applies the same treatment to any other child of `m_pinRootSmall`; HUDCompass is
    the named case.
- No Jötunn code.

## 3. Where Cartographer's own hooks meet them

| Cartographer hook | Meets | Finding |
|---|---|---|
| Vanilla rail hiding (`EnforceVanillaPaletteVisibility`, `OrphanChromeSweep`) | TheGreatestMap's toggles | TGM clones the vanilla icon button and moves the clone to `m_largeRoot`. The sweep only hides ancestors of vanilla rail objects and never the large root, so TGM's buttons are not hidden. With TGM present the rail is kept visible anyway (below). |
| Enhanced palette (claims pins placed through the vanilla naming flow) | TGM's adoption of the same flow | **Two owners for one pin.** Both would track the same `PinData`. TGM would then refuse Cartographer's own re-render removals of it with "Right-click the marker for its menu…" messages, and the marker could double. **Fixed below.** |
| `PinDeletionWatch` (vanilla `RemovePin(PinData)`; until now the only evidence that tombstones a managed marker) | `tgm_clearlocal`, `tgm_import`, and TGM's refusal prefix | `tgm_clearlocal` would have **deleted the player's Cartographer markers for good**. A removal TGM refused would still have been reported, because the report ran in a prefix. **Fixed below.** |
| Managed marker renderings | `tgm_import` | **Not fixable from Cartographer's side; documented instead.** `tgm_import` adopts every saved hand-placeable pin that is not TGM's own, and that includes Cartographer's renderings. Afterwards both mods claim the same pin. TGM then refuses Cartographer's own re-render removals with "Right-click the marker for its menu…", which can leave a TGM-owned copy beside Cartographer's. A delete from TGM's marker menu is not a pointed-at removal, so Cartographer redraws the marker. No data is lost. The `cc_atlas compat` line tells players not to run `tgm_import` while they have Cartographer markers (L14). |
| `MapInputGate` click prefixes | TGM's click prefixes | Cartographer skips vanilla clicks **only** while an explicit route-drawing mode is on. Otherwise the clicks pass untouched and TGM's "map out to edit" gate applies normally. |
| `MapInputGate` wheel prefix and postfix on `UpdateMap` | TGM's transpiler on `UpdateMap` | Different patch kinds on the same method compose; nothing is replaced. |
| Large-map vector ink (`RoadVectorLayer`), a child of `m_mapImageLarge` | TGM's resizable map frame | The ink is map-extent geometry and was not clipped, so on a shrunk or moved map it could draw past the map's edge. **Fixed below** (clip frame). |
| Road and route texture ink on the minimap (Jötunn `MapOverlay`) | Round Minimap's disc and rotation | Jötunn draws every mod's overlays into one shared `RawImage`, `CustomLayerSmall`, a child of `m_mapImageSmall` (Jötunn `MinimapManager.SetupOverlays`, v2.29.2). Round Minimap reshapes only the map image's mesh, so that layer stays square and north-up: the ink would point the wrong way and spill into the corners. **Degraded gracefully below**; the real fix is upstream (§6). |
| Managed markers (vanilla `m_pins`, owner ID 0, vanilla types only) | Round Minimap; TGM's minimap clip; HUDCompass | They are ordinary vanilla pins. Round Minimap rotates and clips them, TGM's clip treats them like any pin, and HUDCompass lists them. Cartographer never uses pin types ≥ 100, so TGM's 100–999 range does not collide. |
| Cluster markers (unsaved vanilla pins) | HUDCompass compass; `tgm_clearlocal` | They appear on HUDCompass's compass as "N pins" or "N × Category". HUDCompass's own Ignore → Pin Names setting can hide them, or Cartographer's clustering can be turned off. `tgm_clearlocal` may remove them; they are display-only and are rebuilt. |

## 4. What Cartographer does now

All of it is detection by exact GUID through BepInEx's public `Chainloader.PluginInfos`, plus Cartographer's own
objects. There is no hard dependency, nothing of another mod is patched, and nothing of another mod is
reflected into.

1. **One pin, one owner (TheGreatestMap).** TGM counts as a pin manager
   (`NeighborMapModPresence.OwnsVanillaPinFlow` → `CompatibilityRegistry.PinManagerPresent`), as Pinnacle and
   PinAssistant already do:
   - Cartographer's enhanced palette and its adopt prompts step aside.
   - The vanilla rail stays visible.
   - Markers placed by hand belong to TGM: recording, sharing, filters and actions are TGM's.
   - Cartographer's markers are made only from its own surfaces (toolbar, Quick Pin, survey). These use
     `AddPin` directly, which TGM does not adopt, so they stay Cartographer's and travel by `cc_sync`, not by TGM's
     sharing.
2. **A marker is deleted only by the player** (`PinTombstoneRule` five-input overload, `PinDeletionWatch`):
   - The report now happens **after** the call, and only if the pin really left the list. A removal another mod
     refused is not a deletion.
   - Every vanilla user delete path (right-click, long touch, gamepad) goes through
     `Minimap.RemovePin(Vector3, float)`. A scope around that call marks the removal as **pointed at**; a finalizer
     closes the scope even if the call throws.
   - While a mod that removes others' pins without pointing is installed (TGM), only a pointed-at removal
     tombstones. A `tgm_clearlocal` keeps the marker and the next repair pass redraws it.
   - Without such a mod, the rule is exactly the old one, so Pinnacle's own delete UI still deletes.
   - If the pointer scope cannot be installed, nothing counts as pointed at, which can only keep a marker, never
     lose one.
3. **Road layer above or below pins** (`Map/RoadLayerPosition`, default `BelowPins`, which is the shipped
   behaviour).
   - The large-map vector ink now lives in a frame that covers exactly the map image and clips to it with a
     `RectMask2D`. That also fixes the spill on a resized TGM map.
   - With `AbovePins`, the frame becomes the sibling directly after `m_pinRootLarge`, laid over the image every
     frame. It is drawn over the pin icons and under anything the map draws later. The pin names are *expected* to be
     later, but this has not been verified; L7 checks it.
   - It falls back to the shipped placement if the pin root is missing or is not inside the large map
     (`RoadLayerPlacement`).
4. **Round Minimap** (`Map/ReshapedMinimapRoads`, default `Hide`):
   - While Round Minimap is installed **and** its `CircleMeshEffect` is active on the minimap image, Cartographer
     holds its road and route texture ink off the **minimap only**. It uses the existing one-presentation rule, so
     the player's layer switches and Jötunn checkboxes are untouched.
   - `Show` draws it anyway.
   - The large map is never affected.
   - The probe reads only which components sit on a vanilla object and compares a type *name*. A rename upstream
     degrades to "draw the ink", the pre-#419 behaviour.
5. **HUDCompass**: detection and a `cc_atlas compat` line only. Nothing about it needs changing in Cartographer.

## 5. What is not possible from Cartographer's side, and why

- **Road ink rotating with Round Minimap.**
  - The ink is in Jötunn's shared overlay layer. That one `RawImage` holds every Jötunn-overlay mod's ink, not just
    Cartographer's.
  - Making it turn with the disc would need two things. First, the current rotation angle, which is internal state
    of Round Minimap with smoothing; reading it means reflecting into a private class. Second, a mesh effect added to
    Jötunn's shared object.
  - Doing that from Cartographer would be private reflection into one mod and surgery on a library's shared object,
    for a result that breaks on either mod's next release.
  - The sound fix is for the mod that reshapes the map image to reshape its overlay children too (§6).
- **Road ink above pins on the minimap.** Same layer: it is a child of the minimap image, and the pins are a later
  sibling. Lifting it would mean moving Jötunn's shared layer, and every other mod's ink with it.
- **Cartographer markers as TGM-shared markers.** TGM shares only what it adopts. Making Cartographer's markers TGM
  markers would mean calling TGM's internals, a hard dependency. Players who want a marker shared through TGM place
  it by hand, and TGM owns it.
- **Cart, boat and portal markers.** These are HUDCompass's dynamic markers and need nothing from Cartographer.

## 6. Upstream suggestion (for the owner to send, not sent)

To Round Minimap (MIT, actively maintained):

> Round Minimap reshapes `m_mapImageSmall` with `CircleMeshEffect`, but Jötunn-based mods draw their overlays into a
> child `RawImage` of that image (`CustomLayerSmall`, created by Jötunn's `MinimapManager.SetupOverlays`). That layer
> keeps its square, north-up shape, so road and route overlays point the wrong way on the round minimap. Would you
> consider adding the same effect, with the same radius and angle, to `Graphic` children of `m_mapImageSmall` (or to
> Jötunn's `CustomLayerSmall` specifically)? It would fix every Jötunn overlay mod at once. Alternatively, a public
> read-only angle (for example a public static property) would let overlay mods follow it without reflection.

If that lands, `Map/ReshapedMinimapRoads = Show` becomes correct, and its default can be revisited.

## 7. Live test matrix: THIRD-PARTY LIVE TEST PENDING

Run in a disposable profile and world with TheGreatestMap 4.5.2+, HUDCompass 1.2.1+ and Round Minimap 1.4.1+
(installed from Hexium). None of these rows has been run.

| # | Check | Expected | Status |
|---|---|---|---|
| L1 | `cc_atlas compat` with all three installed | Lists The Greatest Map, HUDCompass and Round Minimap with their notes | THIRD-PARTY LIVE TEST PENDING |
| L2 | Place a marker by hand (double-click) | TGM adopts and shares it; Cartographer does not claim it; no "Right-click the marker for its menu" spam | THIRD-PARTY LIVE TEST PENDING |
| L3 | TGM's filter, legend, marker menu, pause toggle and map resize | All work; the vanilla rail stays visible; TGM's buttons are not hidden | THIRD-PARTY LIVE TEST PENDING |
| L4 | Make a Cartographer marker (Quick Pin), then run `tgm_clearlocal` | The marker vanishes, is redrawn by the next repair pass (≤ autosave interval), and the log shows "kept the atlas entry and will redraw it" | THIRD-PARTY LIVE TEST PENDING |
| L5 | Right-click a Cartographer marker with TGM installed | It is deleted, as without TGM | THIRD-PARTY LIVE TEST PENDING |
| L6 | Shrink and move the large map with TGM's frame | Road and route ink stays inside the map frame and aligned with terrain | THIRD-PARTY LIVE TEST PENDING |
| L7 | `Map/RoadLayerPosition = AbovePins` | Large-map ink draws over pin icons, under pin names, aligned at every zoom; back to `BelowPins` restores the shipped look without a restart | THIRD-PARTY LIVE TEST PENDING |
| L8 | Round Minimap on, rotation on | Cartographer markers turn with the minimap; road and route ink is absent from the minimap; it is present on the large map | THIRD-PARTY LIVE TEST PENDING |
| L9 | `Map/ReshapedMinimapRoads = Show` | Ink returns to the minimap (square, north-up, as documented) | THIRD-PARTY LIVE TEST PENDING |
| L10 | Round Minimap's "Round minimap" off | Ink returns to the square minimap within about a second | THIRD-PARTY LIVE TEST PENDING |
| L11 | HUDCompass compass bar | Cartographer markers listed; HUDCompass's Ignore → Pin Names hides them; its cart, ship and portal markers are unaffected | THIRD-PARTY LIVE TEST PENDING |
| L12 | Large map open with TGM's map pause | Cartographer toolbar, panels and vector ink keep working while paused | THIRD-PARTY LIVE TEST PENDING |
| L13 | BepInEx log over a 15-minute session | No Harmony errors and no repeated Cartographer warnings | THIRD-PARTY LIVE TEST PENDING |
| L14 | `tgm_import` with Cartographer markers present (documenting a known limit) | Record what happens: expect TGM to adopt them, "Right-click…" messages when Cartographer re-renders, and no Cartographer marker lost | THIRD-PARTY LIVE TEST PENDING |
