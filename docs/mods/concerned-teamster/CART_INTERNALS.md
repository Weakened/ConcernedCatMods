# Valheim cart internals — verified findings (CT-002, extended CT-003..CT-012)

This document records the **verified** surface of Valheim's cart implementation
that Concerned Teamster depends on. Nothing here is guessed: every member was
read out of the local game assembly with metadata-only reflection
(`System.Reflection.MetadataLoadContext`) and its semantics confirmed by
decompiling the implementation (ILSpy `ilspycmd` 11.0.0.9375). No game file was
copied, modified, or executed during the inspection.

Per the architecture rule, **no Valheim type name may appear outside
`src/ConcernedTeamster/Adapters/`**. Code may only reference members listed in
this document; when the game changes, the startup capability probe disables
cart features with one WARN line instead of guessing (fail closed).

## Verified game build

| Item | Value |
|---|---|
| Valheim version | **0.221.12** (network version 36) |
| Steam build id | 21981559 (app 892970) |
| Unity | 6000.0.61f1 |
| Assembly inspected | `valheim_Data/Managed/assembly_valheim.dll` |
| Assembly size / date | 2,126,848 bytes, last write 2026-02-20 UTC |
| Assembly SHA256 | `3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004` |
| Inspection date | 2026-09-04 |

The Valheim version string comes from the BepInEx `LogOutput.log` of the local
TCC-Dev profile (`Valheim version: 0.221.12 (network version 36)`, logged
2026-08-26), which ran exactly this assembly. `Version.GetVersionString` cannot
be executed off-game, so the runtime banner resolves it reflectively.

## The cart component: `Vagon`

`public class Vagon : UnityEngine.MonoBehaviour, Hoverable, Interactable` —
declared in `assembly_valheim.dll` (no namespace). The vanilla cart prefab
carries this component; Teamster never depends on prefab names, only on the
component type and its static instance registry.

### Members the CT-002/CT-003 adapter reads

| Member | Verified signature | Semantics (decompiled) |
|---|---|---|
| `m_baseMass` | `public float m_baseMass` (prefab default `20f`) | Empty-cart physics mass before cargo. |
| `m_itemWeightMassFactor` | `public float m_itemWeightMassFactor` (prefab default `1f`) | Cargo-weight-to-mass multiplier. |
| `m_container` | `public Container m_container` | The cart's cargo container; may be null on malformed prefabs. |
| `IsAttached()` | `public bool IsAttached()` | True when a local `ConfigurableJoint` (`m_attachJoin`) exists; otherwise falls back to the replicated ZDO bool `ZDOVars.s_attachJointHash`, so **observers see remote attachment state**. |
| `IsAttached(Character)` | `public bool IsAttached(Character character)` | Local-truth check: compares `m_attachJoin.connectedBody.gameObject` with the character's GameObject. Only meaningful on the client that owns the joint (pulling is client-local physics). |
| `m_instances` | `private static List<Vagon> m_instances` | CT-003 discovery: every live networked cart registers in `Awake` (skipped when its ZDO is null — ghost/placement copies never appear) and unregisters in `OnDestroy`. No world scans needed. Private: compile-time access via the publicized reference, presence probed at startup. |

### Supporting members (all public, other Valheim types)

| Member | Verified signature | Use |
|---|---|---|
| `ZNetView.IsValid()` | `public bool IsValid()` | Guards every ZDO access; `Vagon.Awake` disables the component when its ZDO is null (ghost/placement copies). |
| `ZNetView.GetZDO()` | `public ZDO GetZDO()` | Network object handle. Obtained via `GetComponent<ZNetView>()` (Unity API), the same object `Vagon.Awake` caches privately. |
| `ZDO.m_uid` | `public ZDOID m_uid` | Network-stable cart identity. |
| `ZDOID.ToString()` | `public override string ToString()` → `GetUserID(UserKey) + ":" + ID` | Stable `"<userId>:<id>"` identity string used as the snapshot `CartId`. |
| `Container.GetInventory()` | `public Inventory GetInventory()` | Cargo inventory access. |
| `Inventory.GetTotalWeight()` | `public float GetTotalWeight()` | Total cargo weight — the exact number vanilla feeds into cart mass. |
| `Player.m_localPlayer` | `public static Player m_localPlayer` | Local player for the pull-state check; `Player : Humanoid : Character` (verified), so it is assignable to `IsAttached(Character)`. CT-003 also uses it as the session signal (null in menus and between worlds → telemetry reset) and as the discovery origin (`transform.position`). |

### Cargo members the CT-006 adapter reads (all public)

Verified by decompiling `ItemDrop` from the same assembly on 2026-09-04.
Read-only: no container, inventory, stack, or item member is written.

| Member | Verified signature | Semantics (decompiled) |
|---|---|---|
| `Inventory.GetAllItems()` | `public List<ItemDrop.ItemData> GetAllItems()` | The container's live item list (occupied slots). Also has `(string, List)` / `(ItemType, List)` overloads; the parameterless one is probed exactly. |
| `ItemDrop.ItemData.m_stack` | `public int m_stack = 1` | Stack count. |
| `ItemDrop.ItemData.m_shared` | `public SharedData m_shared` | Shared item definition; may be broken on modded items → per-item try/catch yields an explicit unreadable-slot marker. |
| `ItemDrop.ItemData.m_dropPrefab` | `public GameObject m_dropPrefab` | Prefab asset reference; its `.name` is the stable item id. May be null → id falls back to the name token. |
| `ItemDrop.ItemData.GetWeight` | `public float GetWeight(int stackOverride = -1)` | **The stack's true weight as vanilla charges it**: `m_shared.m_weight * stack`, plus quality scaling when `m_scaleWeightByQuality != 0`. Used for line weights so manifest totals match the game's own accounting. |
| `ItemDrop.ItemData.GetNonStackedWeight` | `public float GetNonStackedWeight()` | Per-unit weight with the same quality scaling; used for the unit-weight column. |
| `ItemDrop.ItemData.SharedData.m_name` | `public string m_name = ""` | Localization token (for example `$item_stone`); displayed via token→id→"unknown item" fallback. |

Consistency note: `Inventory.GetTotalWeight()` (CT-003's cargo weight) and a
manifest's sum of `GetWeight()` values describe the same cargo; tiny
float-summation-order differences are possible and documented rather than
hidden.

### Terrain members the CT-004 adapter reads (`Heightmap`, all public)

Verified by decompiling `Heightmap` from the same assembly on 2026-09-04.
`Heightmap` is the game's terrain tile component; **Teamster touches only
getters — no terrain write member is referenced anywhere** (the adapter has
no write path, enforced by review and the read-only member list below).

| Member | Verified signature | Semantics (decompiled) |
|---|---|---|
| `Heightmap.GetHeight` | `public static bool GetHeight(Vector3 worldPos, out float height)` | Finds the loaded heightmap containing the point and reads the **live** world height (terrain modifications included). Returns false (height 0) when no heightmap is loaded there — the adapter's grade-unavailable path. |
| `Heightmap.FindHeightmap` | `public static Heightmap FindHeightmap(Vector3 point)` | Returns the loaded tile containing the point, or null → surface unavailable. (The `(point, radius, List)` overload exists too; Cartographer uses it.) |
| `Heightmap.WorldToVertex` | `public void WorldToVertex(Vector3 worldPos, out int x, out int y)` | World position → vertex coordinates on that tile. |
| `Heightmap.GetPaintMask(int, int)` | `public Color GetPaintMask(int x, int y)` | Bounds-checked pixel read of the paint mask; out-of-range returns black — which is also the game's own "nothing painted" value (`m_paintMaskNothing`), so the edge case reads as untouched ground. |
| Paint channel constants | `public static Color m_paintMaskDirt = (1,0,0,1)`, `m_paintMaskCultivated = (0,1,0,1)`, `m_paintMaskPaved = (0,0,1,1)`, `m_paintMaskNothing = (0,0,0,1)` | The channel encoding Teamster's `TerrainPaint.Classify` mirrors (red = dirt, green = cultivated, blue = paved). Constants are documented truth; the adapter reads raw channels, not these fields. |

Grade geometry: heights are sampled 1.5 m ahead of and behind the cart
center along its heading — the XZ direction from cart center to the pull
handle (`m_attachPoint`, semantics-anchored "front"), falling back to the
transform's forward axis; a cart lying on its side (both axes vertical)
reports grade unavailable. Grade % = rise over the 3 m horizontal run × 100.

Descent lookahead (CT-011) reuses exactly these members — `GetHeight` at
the cart plus at 4 m-spaced points along the same heading (count bounded
by config, hard max 5) — no new game surface; a single failed height query
makes the whole reading unavailable rather than a partial guess.

### The parking brake mechanism (CT-012 — Teamster's only mutation)

Verified on this build's `UnityEngine.PhysicsModule.dll` (2026-09-04):
`public RigidbodyConstraints constraints { get; set; }` on `Rigidbody`, and
the `RigidbodyConstraints` enum (`None = 0` … `FreezeAll = 126`). While the
brake is engaged, the cart's **root** rigidbody (the same public
`GetComponent<Rigidbody>()` path used for velocity) gets `FreezeAll`; on
release the captured pre-engage value is restored and the body woken
(`WakeUp()`, already used by the game's own `Detach`). Authority is checked
with `ZNetView.IsOwner()` (decompile-verified, now probed) — the brake
never requests or transfers ownership.

**Why a reloaded world is brake-free by construction:** Valheim persists
ZDO data; `Rigidbody.constraints` is Unity component state rebuilt from the
prefab on every load. The brake performs no `ZDO.Set`, no RPC, and no
sidecar write — the only mutation in the entire adapter layer is this one
property assignment, and it cannot reach a save file.

### Unity engine members the CT-003 adapter reads (verified on this build)

Verified by metadata dump of the game's own Unity modules on 2026-09-04
(`UnityEngine.PhysicsModule.dll` / `UnityEngine.CoreModule.dll`, Unity
6000.0.61f1):

| Member | Verified signature | Use |
|---|---|---|
| `Rigidbody.linearVelocity` | `public Vector3 linearVelocity { get; set; }` — **the pre-Unity-6 `velocity` property still exists but is `[Obsolete]` on this build** | Cart motion (read only): speed magnitude and vertical component. Probed at startup like game members, resolved from `UnityEngine.PhysicsModule`. |
| `Rigidbody.mass` | `public float mass { get; set; }` | Not read yet; reserved with the owner-lag caveat above. |
| `Time.unscaledTimeAsDouble` | `public static double unscaledTimeAsDouble { get; }` | Sampler clock (unscaled: telemetry staleness keeps advancing while the game is paused). Core Unity API, compile-verified. |
| `Component.GetComponent<T>()` / `Component.transform.position` | core Unity API | Rigidbody/ZNetView lookup and distance filtering; compile-verified, not probed. |

### Verified semantics that shape the domain model

- **Mass formula** (`Vagon.UpdateMass`, decompiled): every 5 seconds
  (`InvokeRepeating("UpdateMass", 0f, 5f)`), **owner only**, and only when
  `m_container != null`:
  `mass = m_baseMass + container.GetInventory().GetTotalWeight() * m_itemWeightMassFactor`,
  then `SetMass` divides that total evenly across all child rigidbodies
  (`m_bodies`, cart body + wheels). Consequences:
  - Teamster's `TotalMass` snapshot value recomputes this exact formula from
    live fields, so it can be **fresher** than the physics engine's value (up
    to 5 s stale) and is always available to non-owners (who never run
    `UpdateMass`).
  - Physics truth (`Rigidbody.mass`) is intentionally **not** read in CT-002;
    if a later issue needs it, it must probe the private `m_bodies` field and
    document the owner-lag semantics.
- **Attachment lifecycle** (`AttachTo`/`Detach`/`FixedUpdate`, decompiled):
  attach adds a `ConfigurableJoint` on the cart connected to the character's
  rigidbody and sets ZDO `s_attachJointHash = true`; detach destroys the joint
  and clears the flag (owner only). Non-owners force-`Detach()` local joints in
  `FixedUpdate`, so `IsAttached(Character)` is only ever true on the pulling
  client — exactly what "local pull state" means in CT-002.
- **Interaction/ownership**: `Interact` requests ZDO ownership via
  `RPC_RequestOwn`; the owner transfers unless the cart is `InUse()`. The
  read-only adapter must never call `Interact`, `UseItem`, or any RPC.
- **`m_playerExtraPullMass`**: `public float m_playerExtraPullMass` (default
  `0f`) — when non-zero, attach applies `Character.SetExtraMass` to the puller
  and detach clears it. Not read in CT-002; documented because it affects
  future pull-effort modeling (CT-008).

### Members verified for later leaves (documented, not referenced in code yet)

| Member | Verified signature | Reserved for |
|---|---|---|
| `Vagon.m_name` | `public string m_name` (default `"Wagon"`) | Display name (localization token comes from the prefab). |
| `Vagon.m_body` / `m_bodies` | `private Rigidbody m_body` / `private Rigidbody[] m_bodies` | Physics-mass telemetry if ever needed. CT-003 reads velocity through public `GetComponent<Rigidbody>()` instead — `Awake` proves it is the same root body (`m_body = GetComponent<Rigidbody>()`). |
| `Vagon.m_attachJoin` | `private ConfigurableJoint m_attachJoin` | Note the game's spelling: **`m_attachJoin`**, not `m_attachJoint`. |
| `Vagon.m_attachedObject` | `private GameObject m_attachedObject` | Identifying the puller GameObject. |
| `Vagon.InUse()` | `public bool InUse()` | Container-open or attached check (calls `Container.IsInUse()`). |
| `Inventory.NrOfItems()` / `GetAllItems()` / `SlotsUsedPercentage()` / `GetEmptySlots()` | `public int NrOfItems()`, `public List<ItemDrop.ItemData> GetAllItems()`, `public float SlotsUsedPercentage()`, `public int GetEmptySlots()` | CT-006 cargo manifest. |
| `Container.IsOwner()` | `public bool IsOwner()` | Multiplayer work (v0.6). |
| `ZNetView.IsOwner()` | `public bool IsOwner()` | Multiplayer work (v0.6). |
| `Version.GetVersionString` | `public static string GetVersionString(bool includeMercurialHash)` | Environment banner (resolved reflectively; the bool parameter is why compile-time binding is avoided). |
| `Version.CurrentVersion` | `public static GameVersion CurrentVersion { get; }` | Environment banner fallback. |

### Known hazards recorded during the spike

- `Vagon.Awake` calls `Heightmap.ForceGenerateAll()` — cart spawn already
  forces terrain generation; Teamster must never add work there.
- `Vagon` audio fields (`m_wheelLoops`, pitch/volume tuning) and
  `m_loadVis` visualization are irrelevant to telemetry and stay untouched.
- `UnityEngine.Object` lifetime: destroyed components compare `== null` via
  Unity's operator overload but are **not** reference-null. Adapter code uses
  `== null` checks (not `is null`) on Unity objects and wraps every read in a
  fail-closed try/catch.
- The dump shows `Rigidbody`-typed members under Unity 6000; Unity 6 renamed
  `Rigidbody.velocity` to `linearVelocity`. Resolved in CT-003: verified
  `linearVelocity` exists and legacy `velocity` is `[Obsolete]` on this build
  (see the Unity members table above); the adapter reads `linearVelocity`
  and probes it at startup.
- The CT-003 capability is deliberately all-or-nothing: if any probed member
  (including `linearVelocity`) goes missing after a game update, all cart
  telemetry disables with the one WARN line. Per-cart runtime gaps (a cart
  with no container or no rigidbody) are instead flagged per field
  (`CargoDataAvailable`, `VelocityAvailable`) — "unavailable", never a
  defaulted number presented as truth.

## Re-verification procedure (game updates)

1. Re-run the metadata dump against the updated `assembly_valheim.dll`
   (MetadataLoadContext over `valheim_Data/Managed`, core assembly
   `mscorlib`); diff the member table above.
2. Decompile `Vagon.UpdateMass`, `IsAttached`, `AttachTo`, `Detach` and confirm
   the semantics notes still hold.
3. Update the "Verified game build" table (version, build id, SHA256, date).
4. If a required member changed: update the adapter + probe together in one
   issue; the probe's WARN line is the runtime tripwire until that lands.
