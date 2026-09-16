# Companion compatibility note — installed Valheim surfaces

Issue: [CC-NPC-002 (#266)](https://github.com/Weakened/ConcernedCatMods/issues/266).
Epic: [#264](https://github.com/Weakened/ConcernedCatMods/issues/264).

Everything below was read from the **assemblies installed on this machine**, by
metadata reflection only. No game code was executed, no game bytes were copied,
and no meshes, textures, animations, or DLLs were exported.

## Audited build

| Item | Value |
|---|---|
| Game version | **1.0.12** (network version 40) |
| Source of the version | `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\Player.log`, launch of 2026-09-13 12:47 |
| Steam build id | `25253764` (app 892970) |
| `assembly_valheim.dll` | SHA256 `27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`, 2 568 192 bytes, 2026-09-11 |
| `assembly_utils.dll` | SHA256 `9333361c9d2a2a941c1e6d47e76220689fed1761de8b5d123f7f42f26be3132a` |
| Method | `System.Reflection.MetadataLoadContext` over `valheim_Data\Managed` |

> **The version in the repository's older logs is stale.** The `TCC-Dev`,
> `TCC-Compat`, `TCC-v1-Smoke` and `TCC-v1-Smoke-RC2` profile logs all still
> read `0.221.12 (network version 36)` from August. The game has since updated
> to 1.0.12, and only `TCC-Package` (BepInExPack 5.4.2350) has launched against
> it. Any companion game testing must use a profile refreshed against 1.0.12;
> a stale profile will produce misleading results.

## Verdict key

- **Verified** — present in the installed assembly with exactly this signature.
- **Unverified** — present, but its behaviour under our intended use is not
  proven by metadata alone and needs an in-game check.
- **Unavailable** — not present in this build. Anything relying on it must have
  a capability check and a fallback.

---

## 1. Identity: character and world

| Surface | Signature | Verdict |
|---|---|---|
| World UID | `long ZNet.GetWorldUID()` | Verified |
| `ZNet` singleton | `static ZNet ZNet.instance { get; }` | Verified |
| Profile | `PlayerProfile Game.GetPlayerProfile()`, `static Game Game.instance { get; set; }` | Verified |
| **Character ID** | `long PlayerProfile.GetPlayerID()` | Verified |
| Per-world profile data | `WorldPlayerData PlayerProfile.GetWorldData(long worldUID)` | Verified |

`GetPlayerID()` returns the stable numeric profile id, not a name — exactly what
`CharacterId` needs. `PlayerProfile.GetName()` exists but must not be used as an
identity: two characters can share a name and a rename must not lose progress.

## 2. Respawn anchor

| Surface | Signature | Verdict |
|---|---|---|
| Has a claimed bed | `bool PlayerProfile.HaveCustomSpawnPoint()` | Verified |
| Claimed bed position | `Vector3 PlayerProfile.GetCustomSpawnPoint()` | Verified |
| Clear a bed claim | `void PlayerProfile.ClearCustomSpawnPoint()` | Verified — **read only; never call** |
| World start position | `static Vector3 PlayerProfile.m_originalSpawnPoint` (non-public) | Unverified |
| Start location lookup | `bool ZoneSystem.GetLocationIcon(string name, out Vector3 pos)` | Verified |
| Closest location | `bool ZoneSystem.FindClosestLocation(string name, Vector3 point, out LocationInstance closest)` | Verified |
| Bed object | `Vector3 Bed.GetSpawnPoint()`, `long Bed.GetOwner()`, `bool Bed.IsMine()`, `bool Bed.IsCurrent()` | Verified |

**Anchor resolution order.** `HaveCustomSpawnPoint()` then `GetCustomSpawnPoint()`
gives the claimed bed. When there is no claim, fall back to the world start
location. `m_originalSpawnPoint` is a non-public static and the exact name of the
start location (`"StartTemple"` in earlier builds) is *data*, not a constant in
the assembly — **the `StartTemple` type does not exist in this build**, so the
name must be confirmed at runtime, with `ZoneSystem.GetLocationIcon` failing
closed to "no anchor" rather than guessing a position.

`PlayerProfile` also exposes `GetDeathPoint()`, `GetLogoutPoint()` and
`GetHomePoint()`. **None of them is an anchor.** They are places a player passes
through; anchoring to one would drag the companion across the map behind an
ordinary journey.

## 3. Local visual construction

This is the surface the design brief is most cautious about, and the audit
changes the plan for the better.

| Surface | Signature | Verdict |
|---|---|---|
| Networking component | `ZNetView` — `ZDO GetZDO()`, `void InvokeRPC(string, object[])`, `bool m_persistent` | Verified — **must never be created** |
| AI base | `BaseAI` — `ZNetView m_nview`, `static List<BaseAI> GetAllInstances()` | Verified — **must never be created**; instances self-register |
| Networked animation | `ZSyncAnimation` — `void SetTrigger(string)`, `void RPC_SetTrigger(long, string)` | Verified — **must never be used**; it sends an RPC |
| Local animation | `Animator Character.m_animator` (non-public) | Verified — the local-only path |
| Visual equipment | `VisEquipment` — a `MonoBehaviour` carrying `SkinnedMeshRenderer m_bodyModel`, `PlayerModel[] m_models`, `ZNetView m_nview` (non-public), `ZNetView m_nViewOverride` (public) | Verified |
| Non-player appearance | `void VisEquipment.SetupFacialHairNonPlayer()`, `bool VisEquipment.m_isArmorStand` | Unverified |
| NPC appearance via ZDO | `void VisEquipment.SetupNpcHair(ZDO)`, `void VisEquipment.SetupNpcBeard(ZDO)` | Verified — **must never be used**; takes a ZDO |
| Skinned attachment | `GameObject VisEquipment.AttachItem(int, int, Transform, bool, bool, int)` (non-public) — for a child named `attach_skin`: parent to `m_bodyModel.transform.parent`, zero the local position and rotation, then for every `SkinnedMeshRenderer` under it assign `rootBone = m_bodyModel.rootBone` and `bones = m_bodyModel.bones` | Verified by decompilation of `assembly_valheim.dll` 1.0.12 — the contract #305 rests on |
| Armour attachment | `List<GameObject> VisEquipment.AttachArmor(int, int, int)` (non-public) — same bone assignment for `attach_skin`; every other `attach_<joint>` child is parented to the joint found by name under `m_visual` | Verified by decompilation of `assembly_valheim.dll` 1.0.12 |

**A skinned customization mesh is bound by ARRAY, not by joint.** The game never
re-maps bone to bone: it hands the piece `m_bodyModel.bones` whole, in the body's
own order, because Unity skins by index and a vanilla hair or armour mesh's
bindposes are authored against the player skeleton's array layout. Matching the
same names in the piece's own order produces a complete, plausible binding that
draws the mesh somewhere else entirely — 0.99 m away, in #305's case — and no
count or name check can tell the two apart. This is the one place where copying
the game's line literally matters more than writing a more careful-looking one.

**`BaseAI` self-registers into a static list.** `BaseAI.GetAllInstances()` and the
backing `m_instances` mean an instantiated AI is reachable process-wide the
moment it wakes, which is precisely why "instantiate a live actor and strip
components afterwards" is not an option: by the time we could strip anything, it
has already registered.

**The render-only path this build supports.** `VisEquipment` is an ordinary
`MonoBehaviour` whose `m_nview` is a *field*, not a required dependency, and
Valheim 1.0 added explicit non-player handling (`m_isArmorStand`,
`SetupFacialHairNonPlayer`, `m_npcHairChance`). That makes a renderer/skeleton
subtree with a `VisEquipment` and an `Animator`, and **no** `ZNetView`,
`Character`, `Player`, or `BaseAI`, the intended shape for Hulgi.

Whether `VisEquipment` tolerates a null `m_nview` through `Awake`/`OnEnable`,
and how the prefab must be instantiated inactive to control what runs first, are
**Unverified** and are pending in-game evidence rows. The design brief's caution
stands: prove local construction before enabling callbacks.

## 4. Appearance

| Surface | Signature | Verdict |
|---|---|---|
| Set hair item | `void VisEquipment.SetHairItem(int itemHash)` | Verified |
| Set beard item | `void VisEquipment.SetBeardItem(int itemHash)` | Verified |
| Set hair colour | `void VisEquipment.SetHairColor(Vector3 color)`, `void Player.SetHairColor(Vector3)` | Verified |
| Read hair colour | `Vector3 Player.GetHairColor()` | Verified |
| Apply visuals | `void VisEquipment.UpdateVisuals()`, `void VisEquipment.UpdateColors()` | Verified |
| Resolve a prefab by name | `bool ObjectDB.TryGetItemPrefab(string name, out GameObject prefab)` | Verified |
| Prefab by name | `GameObject ZNetScene.GetPrefab(string name)`, `List<string> ZNetScene.GetPrefabNames()` | Verified |
| Equipment slots | `VisSlot` enum — `… Beard = 9, Hair = 10` | Verified |
| `Player.SetHair(string)` | — | **Unavailable** |
| `Player.SetBeard(string)` | — | **Unavailable** |
| `HelmetHairType` as a top-level type | — | **Unavailable** (nested under `VisEquipment`) |

> **API change worth flagging.** Earlier Valheim builds exposed
> `Player.SetHair(string)` / `Player.SetBeard(string)`. In 1.0.12 they are gone:
> `Player` has only `GetHairColor`, `SetHairColor`, `SetSkinColor`,
> `GetPlayerModel`, `SetPlayerModel`. Hair and beard are set **only** through
> `VisEquipment.SetHairItem`/`SetBeardItem`, by **item hash**. Any tutorial,
> older mod, or model recollection that reaches for `Player.SetHair` is wrong
> for this build.

**The concrete preset list cannot be established by this audit, and is not
claimed.** `VisEquipment.m_hairPrefabPrefix`, `m_hairPrefabCount`,
`m_beardPrefabPrefix` and `m_beardPrefabCount` are serialized fields whose
*values* live in Unity asset bundles under `StreamingAssets/SoftRef/Bundles`,
not in the assemblies. Those bundles are compressed, and unpacking them would be
asset extraction, which this project does not do.

So the preset names are a **runtime enumeration**, not a constant:

1. Enumerate the actual prefab names through `ObjectDB` / `ZNetScene` at runtime
   and log them (a read-only console command ships with CC-NPC-004).
2. Resolve Hulgi's hair and beard from that list by name.
3. **Fallback chain when a name is absent:** first alternate preset in the same
   family → whatever the model's default hair/beard is → no hair item at all.
   A missing preset changes how Hulgi looks; it never fails his construction and
   never blocks the introduction.
4. Strawberry blond is a **colour**, applied with `SetHairColor(Vector3)`,
   independent of which preset is found. The exact vector is a tuning value to
   be confirmed on screen, not a fact this audit can supply.

## 5. Sitting and idle animation

| Surface | Signature | Verdict |
|---|---|---|
| Chair attach point | `Transform Chair.m_attachPoint` | Verified |
| **Chair animation name** | `string Chair.m_attachAnimation` | Verified (field), value is per-prefab data |
| Seat occupancy | `bool Chair.IsInUse()` | Verified |
| Seat range | `float Chair.m_useDistance`, `bool Chair.InUseDistance(Humanoid)` | Verified |
| Player attach | `void Player.AttachStart(Transform, GameObject, bool, bool, bool, string, Vector3, Transform)` | Verified — **player only; never call for the companion** |
| Sitting query | `bool Character.IsSitting()` | Verified |
| Emotes | `bool Player.StartEmote(string emote, bool oneshot)`, `bool Player.InEmote()` | Verified — player only |

**No animation key is hardcoded anywhere.** `Chair.m_attachAnimation` is a string
*field on each chair prefab*, so the correct sitting animation is read off the
actual chair at runtime. There is no assembly constant to rely on, and inventing
one would be exactly the failure the brief warns about. Ground sitting uses the
same mechanism via the humanoid's own animator; the state name is Unverified and
must be confirmed in game before seating is reported as working.

`Player.AttachStart` is the *player's* attach path — it moves the camera, hides
weapons, and manipulates colliders. The companion never calls it, never claims a
seat, and never sends an attachment message. `Chair.IsInUse()` is how the
companion yields to a real occupant.

**Furniture seating stays reported as pending** until it is actually observed.
`SeatAvailability.Unverified` in the shared layer exists for exactly this, and
the planner treats it as no seat at all.

## 6. Placement probing

| Surface | Signature | Verdict |
|---|---|---|
| Ground height, normal and object | `bool ZoneSystem.GetSolidHeight(Vector3 p, out float height, out Vector3 normal, out GameObject go)` | Verified |
| Ground height with margin | `bool ZoneSystem.GetSolidHeight(Vector3 p, out float height, int heightMargin)` | Verified |
| Blocked test | `bool ZoneSystem.IsBlocked(Vector3 p)` | Verified |
| **Loaded-area test** | `bool ZoneSystem.IsZoneLoaded(Vector3 point)` | Verified |
| Ground data | `void ZoneSystem.GetGroundData(ref Vector3 p, out Vector3 normal, out Biome biome, out BiomeArea biomeArea, out Heightmap hmap)` | Verified |
| Heightmap at a point | `static Heightmap Heightmap.FindHeightmap(Vector3 point)`, `static bool Heightmap.GetHeight(Vector3, out float)` | Verified |
| Lava | `bool Heightmap.IsLava(Vector3 worldPos, float lavaValue)` | Verified |
| **Fire warmth** | `static EffectArea EffectArea.IsPointInsideArea(Vector3 p, EffectArea.Type type, float radius)` | Verified |
| Effect area kinds | `EffectArea.Type` — `Heat = 1, Fire = 2, PlayerBase = 4, Burning = 8, Teleport = 16, NoMonsters = 32, WarmCozyArea = 64, PrivateProperty = 128` | Verified |
| Fire state | `bool Fireplace.IsBurning()` | Verified |
| Location geometry | `Location` — `float m_exteriorRadius`, `float m_interiorRadius`, `bool m_clearArea` | Verified |
| Interior test | `static bool Character.InInterior(Vector3 position)` | Verified |

`EffectArea.IsPointInsideArea(p, EffectArea.Type.Heat, radius)` answers "is this
spot warm" without touching any fire's internals — it is the right source for
`PlacementProbeSample.DistanceToFire`, and `Type.Burning` and `Type.Fire` are
the right sources for the `Fire` hazard rejection. `IsZoneLoaded` is what keeps
the probe inside loaded ground, satisfying "bounded loaded areas only".

Water has no single call here; the probe derives it from ground height against
the water level, which is **Unverified** and needs an in-game check.

## 7. Interaction and UI

| Surface | Signature | Verdict |
|---|---|---|
| Hover | `interface Hoverable` — `string GetHoverName()`, `string GetHoverText()`, `float GetHoverOffset()` | Verified |
| Interact | `interface Interactable` — `bool Interact(Humanoid user, bool hold, bool alt)`, `bool UseItem(Humanoid user, ItemData item)` | Verified |
| Hover target | `GameObject Player.GetHoverObject()`, `float Player.m_maxInteractDistance` | Verified |
| Localization | `string Localization.Localize(string text)`, `void Localization.AddWord(string key, string text)`, `string GetSelectedLanguage()` — in **`assembly_guiutils.dll`**, not `assembly_valheim.dll` | Verified |
| Messages | `void MessageHud.ShowMessage(MessageHud.MessageType type, string text, int amount, Sprite icon, bool showDespiteHiddenHUD, bool log)` | Verified |
| Character message | `void Character.Message(MessageType type, string msg, int amount, Sprite icon, bool log)` | Verified |
| HUD visibility | `bool Hud.IsVisible()`, `bool Hud.m_userHidden` | Verified |
| Rune-style reader | `void TextViewer.ShowText(TextViewer.Style style, string topic, string textId, bool autoHide)`, `bool TextViewer.IsVisible()`, `Style` = `Rune, Intro, Raven` | Unverified |
| Inventory open | `bool InventoryGui.IsVisible()` | Verified |

> `Character.Message` carries the trailing `bool log` parameter. This is the
> 1.0 signature that broke shipped Cartographer 0.10.1; the repository's
> `Runtime/VanillaMessage.cs` adapter already accounts for it and this audit
> confirms it is still current at 1.0.12.

**Story UI decision.** `TextViewer.ShowText(Style.Rune, …)` is tempting — it is
the vanilla rune-stone reader, with the game's own paging and input handling.
But its `textId` parameter may be a localization token rather than literal text,
and whether it suppresses gameplay input is not knowable from metadata. It is
therefore recorded as a **candidate requiring in-game verification**, not a
decision.

The default for CC-NPC-003 is the **panel stack Cartographer already ships and
has shipped for a year** — `CcSidePanel`, `CcTextFocus`, `MapInputGate`,
`PlayerInputGate` — which already handles controller navigation and safe focus
release. Reusing proven code beats adopting a vanilla surface whose semantics
cannot be confirmed statically.

## 8. Progression signals for dialogue

| Surface | Signature | Verdict |
|---|---|---|
| **Known biomes** | `HashSet<string> Player.m_knownBiome` (non-public) | Verified (field), contents Unverified |
| Known-biome query | `bool Player.IsBiomeKnown(BiomeSector biome)` | Verified |
| Biome name | `static string BiomeSector.GetBiomeName(Heightmap.Biome biome)` | Verified |
| Biome name | `static string Heightmap.BiomeToString(Heightmap.Biome biome)` | Verified |
| Biome enum | `Heightmap.Biome` — `None=0, Meadows=1, Swamp=2, Mountain=4, BlackForest=8, Plains=16, AshLands=32, DeepNorth=64, Ocean=256, Mistlands=512` | Verified |
| Fog of war | `bool Minimap.IsExplored(Vector3 worldPos)` | Verified |

`m_knownBiome` is a **`HashSet<string>`**, not a set of enum values — this build
stores biome *names*. That matches the shared layer's `DialogueContext`, which
keys on strings, and the adapter reads the set read-only through reflection.

The exact string spellings are produced by the game, so they are **Unverified**
until logged in game. Until then `DialogueContext.Empty` is the correct state:
no biome is known, no biome-gated line is eligible, and the companion says
something ungated instead. The failure mode of guessing here is a spoiler, so
guessing is not an option.

Progression is read from the **local** character only. `Minimap.IsExplored`
covers own-or-shared exploration and is used for nothing but fog respect —
another player's discoveries never gate a line.

## 9. Things this build has that we deliberately do not use

Valheim 1.0 ships its own NPCs. `NpcTalk` exists, with `Say(string text, string trigger)`,
`QueueSay(List<string>, string, EffectList)`, `RandomTalk()`, greet/bye ranges,
and aggravation handling. `VisEquipment` has matching `SetupNpcHair(ZDO)` /
`SetupNpcBeard(ZDO)`.

It is the wrong tool here. `NpcTalk` is bound to a `Character` with a `ZNetView`,
its appearance path takes a `ZDO`, and it carries aggravation and faction
behaviour. Hulgi is a personal, local-only, non-combat presence that unmodded
peers never see. Adopting the vanilla NPC stack would mean networked entities,
ZDOs, and save participation — all of which this epic forbids.

Also present and deliberately unused: `Tameable`, `Trader`, `StoreGui`,
`MonsterAI`, `Ragdoll`, `Attack`, `ZDO.SetOwner`.

## 10. Unverified list — the in-game evidence this audit owes

| # | Claim needing observation | Consumer |
|---|---|---|
| 1 | `VisEquipment` survives a null `m_nview` through `Awake`/`OnEnable` | CC-NPC-004 |
| 2 | Instantiating inactive genuinely defers the callbacks we care about on this Unity build | CC-NPC-004 |
| 3 | The concrete hair and beard preset names available in `ObjectDB` | CC-NPC-004 |
| 4 | The `SetHairColor` vector that actually reads as strawberry blond | CC-NPC-004 |
| 5 | The ground-sitting animator state name | CC-NPC-004 |
| 6 | `Chair.m_attachAnimation` values on real chairs, and that posing on one looks right | CC-NPC-004 |
| 7 | The world start location's lookup name for the no-bed anchor | CC-NPC-004 |
| 8 | The water test derived from ground height against water level | CC-NPC-004 |
| 9 | Exact `m_knownBiome` string spellings | CC-NPC-005 |
| 10 | Whether `TextViewer.ShowText` localizes `textId`, and whether it blocks input | CC-NPC-003 |
| 11 | That an unmodded peer sees nothing | CC-NPC-004 |

Every one of these has a documented fallback in the sections above. **None of
them blocks the introduction, the unlock, or the player's tools** — the worst
case for any of them is that Hulgi looks or sits differently than intended, or
that a capability is disabled with an actionable notice.

## 11. Rule for every surface above

All of it is reached through narrow adapters with capability checks, per
`AGENTS.md`. A member that is missing at runtime disables its feature with an
actionable log line and a player-facing notice. It never crashes, never
fabricates a success, and never takes away access the player already has.
