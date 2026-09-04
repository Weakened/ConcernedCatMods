# Valheim cart internals — verified findings (CT-002)

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

### Members the CT-002 adapter reads (all public)

| Member | Verified signature | Semantics (decompiled) |
|---|---|---|
| `m_baseMass` | `public float m_baseMass` (prefab default `20f`) | Empty-cart physics mass before cargo. |
| `m_itemWeightMassFactor` | `public float m_itemWeightMassFactor` (prefab default `1f`) | Cargo-weight-to-mass multiplier. |
| `m_container` | `public Container m_container` | The cart's cargo container; may be null on malformed prefabs. |
| `IsAttached()` | `public bool IsAttached()` | True when a local `ConfigurableJoint` (`m_attachJoin`) exists; otherwise falls back to the replicated ZDO bool `ZDOVars.s_attachJointHash`, so **observers see remote attachment state**. |
| `IsAttached(Character)` | `public bool IsAttached(Character character)` | Local-truth check: compares `m_attachJoin.connectedBody.gameObject` with the character's GameObject. Only meaningful on the client that owns the joint (pulling is client-local physics). |

### Supporting members (all public, other Valheim types)

| Member | Verified signature | Use |
|---|---|---|
| `ZNetView.IsValid()` | `public bool IsValid()` | Guards every ZDO access; `Vagon.Awake` disables the component when its ZDO is null (ghost/placement copies). |
| `ZNetView.GetZDO()` | `public ZDO GetZDO()` | Network object handle. Obtained via `GetComponent<ZNetView>()` (Unity API), the same object `Vagon.Awake` caches privately. |
| `ZDO.m_uid` | `public ZDOID m_uid` | Network-stable cart identity. |
| `ZDOID.ToString()` | `public override string ToString()` → `GetUserID(UserKey) + ":" + ID` | Stable `"<userId>:<id>"` identity string used as the snapshot `CartId`. |
| `Container.GetInventory()` | `public Inventory GetInventory()` | Cargo inventory access. |
| `Inventory.GetTotalWeight()` | `public float GetTotalWeight()` | Total cargo weight — the exact number vanilla feeds into cart mass. |
| `Player.m_localPlayer` | `public static Player m_localPlayer` | Local player for the pull-state check; `Player : Humanoid : Character` (verified), so it is assignable to `IsAttached(Character)`. |

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
| `Vagon.m_instances` | `private static List<Vagon> m_instances` — populated in `Awake` (only when the ZDO exists), removed in `OnDestroy` | CT-003 cart discovery without world scans. Private: needs publicized-assembly access and its own probe entry. |
| `Vagon.m_name` | `public string m_name` (default `"Wagon"`) | Display name (localization token comes from the prefab). |
| `Vagon.m_body` / `m_bodies` | `private Rigidbody m_body` / `private Rigidbody[] m_bodies` | CT-003 velocity and physics-mass telemetry (private; own probe entries). |
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
  `Rigidbody.velocity` to `linearVelocity`. CT-003 must verify the exact
  property against `UnityEngine.PhysicsModule.dll` before reading velocity.

## Re-verification procedure (game updates)

1. Re-run the metadata dump against the updated `assembly_valheim.dll`
   (MetadataLoadContext over `valheim_Data/Managed`, core assembly
   `mscorlib`); diff the member table above.
2. Decompile `Vagon.UpdateMass`, `IsAttached`, `AttachTo`, `Detach` and confirm
   the semantics notes still hold.
3. Update the "Verified game build" table (version, build id, SHA256, date).
4. If a required member changed: update the adapter + probe together in one
   issue; the probe's WARN line is the runtime tripwire until that lands.
