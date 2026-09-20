# Valheim 1.0.14 fire and fuel API audit

**Purpose.** CC-NPC-013 (#340) makes the Steward tend settlement fires with real
wood. Everything the adapter is allowed to call had to be established against
the installed binary *before* any adapter existed, because the alternative —
writing against a remembered API and finding out in game — is how a mod ends up
conjuring or destroying a player's items.

**What was read.**

| | |
|---|---|
| Assembly | `valheim_Data\Managed\assembly_valheim.dll` (Steam install) |
| SHA-256 | `f64998168a0dd37ec774816808f914ed68376be1b9670cd05a6c2f27c8017fb6` |
| `Version.CurrentVersion` | `new GameVersion(1, 0, 14)` |
| Tool | `ilspycmd` (dotnet global tool) |
| Date | 2026-09-18 |

Types decompiled in full: `Fireplace`, `Container`, `Inventory`, `ItemDrop`,
`ZNetView`, `ZRoutedRpc`, `ZDO`, `ZDOVars`, `Humanoid`, `Character`, `Player`,
`Piece`, `PrivateArea`, `Smelter`, `CookingStation`.

Re-verify against the live binary before trusting any of this on a later game
build. Valheim internals are unstable by policy (`AGENTS.md`), and this audit is
a snapshot, not a contract.

---

## 1. `Fireplace` is the hearth type

`public class Fireplace : MonoBehaviour, Hoverable, Interactable, IHasHoverMenu`.

It is the component behind every player-built fire: campfire, hearth, bonfire,
standing torches. `Fire` is a *different* type — the spreading, damaging cinder
fire — and has no fuel of its own. The Steward never touches `Fire`.

**Which prefabs carry a `Fireplace` is data, not API.** Prefab names live in the
game's asset bundles, so no amount of reading the assembly can prove one exists
— the same lesson `WORKER_ACTOR_SPIKE.md` records for creature prefabs. The
adapter therefore **discovers `Fireplace` components in the loaded world** and
never names a prefab.

### Fields that matter

| Member | Type | Meaning |
|---|---|---|
| `m_fuelItem` | `ItemDrop` | **What this fire burns.** The item identity comes from the target, never from a constant in our code. |
| `m_maxFuel` | `float` | Capacity, in fuel units. |
| `m_secPerFuel` | `float` | Burn rate. `0` means it never consumes. |
| `m_infiniteFuel` | `bool` | Never accepts fuel and never needs it. |
| `m_canRefill` | `bool` | False for fires that cannot be fuelled at all. |
| `m_canTurnOff` | `bool` | Whether plain `Interact` toggles instead of fuelling. |

### Where the fuel level lives

In the ZDO, as a **float**, under `ZDOVars.s_fuel` (`"fuel".GetStableHashCode()`).
There is no public getter; `GetHoverText`, `IsBurning` and every RPC read
`m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)` directly. Reading it is a read;
nothing is mutated by asking.

`ZDOVars.s_state` (`int`, default `1`) is the on/off toggle. `2` is off.

---

## 2. The only legitimate mutation path

Three members change the fuel level. Only one of them consumes an item.

```csharp
// LEGITIMATE - consumes exactly one real item, adds exactly one fuel.
public bool UseItem(Humanoid user, ItemDrop.ItemData item)

// LEGITIMATE for a player at a keyboard; unsuitable here (see 2.2).
public bool Interact(Humanoid user, bool hold, bool alt)

// FORBIDDEN - conjures fuel from nothing. No item is consumed.
public void AddFuel(float fuel)   // -> RPC_AddFuelAmount
public void SetFuel(float fuel)   // -> RPC_SetFuelAmount
```

### 2.1 `UseItem` is the one the Steward uses

Decompiled body, fuel branch:

```csharp
if (item.m_shared.m_name == m_fuelItem.m_itemData.m_shared.m_name && !m_infiniteFuel)
{
    if ((float)Mathf.CeilToInt(m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)) >= m_maxFuel)
    {
        user.Message(...);   // "can't add more"
        return true;         // <-- TRUE, and nothing happened
    }
    Inventory inventory = user.GetInventory();
    user.Message(...);
    inventory.RemoveItem(item, 1);
    m_nview.InvokeRPC("RPC_AddFuel");
    return true;             // <-- TRUE, and one unit moved
}
```

and `RPC_AddFuel`:

```csharp
private void RPC_AddFuel(long sender)
{
    if (m_nview.IsOwner())
    {
        float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
        if (!((float)Mathf.CeilToInt(num) >= m_maxFuel))
        {
            num = Mathf.Clamp(num, 0f, m_maxFuel);
            num++;
            num = Mathf.Clamp(num, 0f, m_maxFuel);
            m_nview.GetZDO().Set(ZDOVars.s_fuel, num);
            m_fuelAddedEffects.Create(...);
            UpdateState();
        }
    }
}
```

Four consequences, each of which the implementation depends on:

1. **`UseItem` returns `true` whether or not anything happened.** A refusal for
   "already full" and a successful unit are both `true`. *The return value
   therefore proves nothing* and is never used as evidence. The accepted delta
   is **measured**, from the fuel float and the worker's item count, before and
   after.

   This is not a new shape, and it should not be read as one. It is the same
   rule the settlement custody layer already arrived at independently:
   `src/Shared/Settlement/Custody/TransferExecutor.cs` classifies every transfer
   from *"the measured deltas of both inventories"* and never from what `Add`
   or `Remove` returned (CONTRACTS.md §5.2, step 5). Two different vanilla
   surfaces, the same conclusion — the game's return values describe what a call
   attempted, not what it achieved. A third adapter should assume this before it
   checks, not after.

2. **"Full" is `Mathf.CeilToInt(fuel) >= m_maxFuel`,** not `fuel >= m_maxFuel`.
   At `m_maxFuel = 10`, a fire at `9.5` is full and refuses; a fire at `9.0`
   accepts and lands on `10.0`. So the accepted delta of one unit is
   `min(1.0, m_maxFuel - fuel)`, and it is zero whenever `ceil(fuel) >= maxFuel`.
   The Steward evaluates the same predicate before withdrawing anything, so an
   already-fuelled hearth costs no wood.

3. **The item is removed by vanilla, from the user's own inventory, before the
   RPC.** We never remove it ourselves and we never add fuel ourselves. One call
   moves exactly one unit of one real item.

4. **`RPC_AddFuel` does nothing unless `m_nview.IsOwner()`.** See section 3 —
   this is the sharpest edge in the whole audit.

### 2.1.1 `UseItem` has no range check, and that is a finding

`Fireplace` contains no distance test anywhere — not in `UseItem`, not in
`Interact`, not in `CanUseItems`. The only thing that stops a player fuelling a
hearth from across their base is `Player.m_maxInteractDistance` (default `5f`),
applied earlier, at the hover targeting.

**A modded worker never goes through that targeting.** So a naive adapter that
is otherwise perfectly honest — real wood, measured deltas, vanilla's own call
— would let the Steward stand at the chest and light every fire in the
settlement without moving. Legitimate by every other rule, and obviously a cheat
to anyone watching.

The Steward is therefore held to vanilla's own number: he must be within **5 m**
of the fire, checked in the adapter that performs the mutation, in addition to
the upkeep loop's refusal to feed before it has walked there.

### 2.2 Why not `Interact`

`Interact` does the same fuel work, but it also:

- calls `m_nview.ClaimOwnership()` when the ZDO has no owner — an ownership
  write the Steward is not permitted to make; and
- toggles the fire off instead of fuelling it when
  `m_canTurnOff && !hold && !alt && fuel > 0f`.

`UseItem` does neither. It is strictly narrower and strictly safer, so it is the
only entry point the adapter calls.

### 2.3 `AddFuel` / `SetFuel` are forbidden

Both route to RPCs that write `ZDOVars.s_fuel` with **no item consumed**
(`RPC_AddFuelAmount`, `RPC_SetFuelAmount`). They are resource conjuring by
definition. The Steward must never call them, and a test asserts that the names
`AddFuel`, `SetFuel`, `RPC_AddFuelAmount`, `RPC_SetFuelAmount`, `SetFuel`,
`ClaimOwnership` and `SetOwner` appear nowhere in the product's sources.

---

## 3. Ownership decides whether the mutation is observable — or happens at all

`ZNetView.InvokeRPC(string, params object[])` forwards to
`ZRoutedRpc.InvokeRoutedRPC(m_zdo.GetOwner(), m_zdo.m_uid, ...)`, whose dispatch
is:

```csharp
if (targetPeerID == m_id || targetPeerID == 0L)
{
    HandleRoutedRPC(routedRPCData);   // synchronous, inline, this call stack
}
if (targetPeerID != m_id)
{
    RouteRPC(routedRPCData);          // over the wire
}
```

Three cases, and only one of them is safe:

| ZDO owner | What happens | Verdict |
|---|---|---|
| **This process** | `HandleRoutedRPC` runs `RPC_AddFuel` **inline, before `UseItem` returns**. `IsOwner()` is true, so the fuel is written. | **The only case the Steward acts in.** The delta is measurable immediately, with no waiting and no polling. |
| Another peer | The RPC is serialised and routed. Nothing is observable locally. The item is *already gone* from our inventory. | Refused before acting. An unobservable mutation is an uncertain one, and uncertainty is never replayed. |
| **Nobody** (`owner == 0`) | `HandleRoutedRPC` runs locally **and** broadcasts — but `RPC_AddFuel`'s own `if (m_nview.IsOwner())` is **false**, so no fuel is added. `inventory.RemoveItem(item, 1)` already ran. | **Vanilla loses the item here.** Refused before acting; see below. |

The unowned case is the important find. It is a real, reachable item-loss path
in vanilla's own code, and a worker that fuelled fires without checking
ownership would hit it far more often than a player does, because it acts on
fires it walked to rather than fires it is standing in front of.

**Rule the adapter enforces:** a fire is an eligible target only while
`nview != null && nview.IsValid() && nview.IsOwner()`. It is re-checked
immediately before `UseItem` and again after, and a fire that changed hands
mid-job is abandoned, not fought over. **The Steward never calls
`ClaimOwnership`.**

This composes with, rather than replaces, `WorkAuthorityPolicy`: host, not
dedicated, no other peers connected. Per-object ownership is the finer-grained
check underneath the session-wide one.

---

## 4. Counting items the way vanilla matches them

`Fireplace.UseItem` selects on `item.m_shared.m_name` and nothing else.

`Inventory.CountItems(name)` and `Inventory.HaveItem(name)` default to
`matchWorldLevel: true`, which additionally requires
`item.m_worldLevel >= Game.m_worldLevel`. `ItemDrop.ItemData.m_worldLevel` is
stamped at creation (`= Game.m_worldLevel`) and round-trips through save/load.

So on a world whose level has been raised, `CountItems("$item_wood")` can report
**zero** for wood that `UseItem` would happily burn. Measuring the withdrawal
with one predicate and performing it with another is exactly how a conservation
argument develops a hole.

**Rule:** every count on the fuel path enumerates `GetAllItems()` and matches
`m_shared.m_name` ordinally — vanilla's own predicate at the point of mutation —
never `CountItems`/`HaveItem`.

---

## 5. Messages are inert on a worker body

`Fireplace.UseItem` calls `user.Message(...)` on three branches.

- `Character.Message(MessageHud.MessageType, string, int, Sprite, bool)` is
  `public virtual` with an **empty body**.
- `Humanoid` (which is `Humanoid : Character`) does **not** override it.
- Only `Player` overrides it, and that override early-outs unless
  `m_nview.IsOwner()` — the local player.

The worker body is a cloned creature, so it is a `Humanoid` and not a `Player`.
Every `user.Message` on the fuel path is therefore a no-op: no HUD spam, no
localisation work, nothing for the player to see. This is checked rather than
assumed, because a `MessageHud.MessageAll` on a worker path would be a visible
defect in a live game and invisible in every test.

---

## 6. The depot side: `Container`

`public class Container : MonoBehaviour, Hoverable, Interactable`.

| Member | Use |
|---|---|
| `GetInventory()` | The real `Inventory`. Withdrawal is `Inventory.RemoveItem`; the Steward never writes the container's ZDO. |
| `IsOwner()` | `m_nview.IsOwner()`. Same rule as section 3: not owned here, not touched. |
| `IsInUse()` | True while a player has it open. The Steward refuses rather than racing an open UI. |
| `m_privacy` | `Public` / `Private` / `Group`. `CheckAccess` is **private**; `Private` grants only to `m_piece.GetCreator() == playerID`, and `Group` returns `false` unconditionally in this build. |
| `m_checkGuardStone` | Whether the container respects ward access. |

`Container.RPC_RequestOpen` calls `m_nview.GetZDO().SetOwner(uid)` — an
ownership transfer. The Steward does **not** use the open path; it reads
`GetInventory()` on a container this process already owns and that is not in use.

Ward access is `PrivateArea.CheckAccess(point, radius, flash, wardCheck)`,
already established by `WORKER_ACTOR_SPIKE.md` and used unchanged.

---

## 7. Future seams, recorded and not built

`Smelter` and `CookingStation` have the **same fuel shape** as `Fireplace`:

| | `Fireplace` | `Smelter` | `CookingStation` |
|---|---|---|---|
| Fuel item | `m_fuelItem` | `m_fuelItem` | `m_fuelItem` |
| Capacity | `m_maxFuel` (`float`) | `m_maxFuel` (`int`) | `m_maxFuel` (`int`) |
| Stored at | `ZDOVars.s_fuel` | `ZDOVars.s_fuel` | `ZDOVars.s_fuel` |
| Add RPC | `RPC_AddFuel` (owner-gated, +1) | `RPC_AddFuel` (owner-gated) | `RPC_AddFuel` (owner-gated) |

`Smelter` additionally has `RPC_AddOre` and `ZDOVars.s_queued` for the
production-input seam, and named conversions in `m_conversion`.

This is why the Steward's fuel port is `IFuelTargetPort` rather than
`IFireplacePort`: the second and third implementations are a known shape, not a
guess. **#340 implements `Fireplace` only.** Nothing else is written, and no
abstraction is added that the fireplace implementation does not itself need.

---

## 8. What this audit forbids

Recorded here so the prohibition has a citation rather than a memory:

- `Fireplace.AddFuel`, `Fireplace.SetFuel`, and any direct
  `ZDO.Set(ZDOVars.s_fuel, ...)` — resource conjuring.
- `ZNetView.ClaimOwnership` and `ZDO.SetOwner` on anything the Steward did not
  create — ownership seizure.
- `Fireplace.Interact` — claims ownership, and may toggle instead of fuel.
- Acting on any `Fireplace` or `Container` this process does not own — either
  unobservable, or (unowned) silently destroys the item.
- `Inventory.CountItems` / `HaveItem` on the fuel path — a different predicate
  from the one vanilla mutates with.
- Inferring success from `UseItem`'s `bool` — it is `true` for a refusal.
- Naming any fireplace prefab — prefab names are asset-bundle data.

---

## 9. Which pieces are serviced by a maintenance round, and which are excluded

**Added for #382**, which asks for "every supported fuel-consuming light" and,
explicitly, for no blind patching of decorative ones. Re-read against the same
decompiled 1.0.14 build.

### 9.1 Four component types carry a fuel API. One of them is a light.

Searched by the two facts a fuel API cannot be written without: a `m_fuelItem`
field, and a read or write of `ZDOVars.s_fuel`. Both searches return the same
four types and nothing else.

| Type | Fuel API | Serviced? | Why |
|---|---|---|---|
| `Fireplace` | `m_fuelItem`, `m_maxFuel`, `m_secPerFuel`, `s_fuel`, `UseItem` | **Yes** | The component behind every player-built fire: campfire, hearth, bonfire, standing torches, braziers. It is the only one of the four whose purpose is light. |
| `Smelter` | `m_fuelItem`, `m_maxFuel` (`int`), `s_fuel`, `RPC_AddOre`, `m_conversion` | No | A production station, not a light. Its fuel is an input to a recipe a player chose, and `m_maxFuel` is legitimately **zero** on some of its prefabs, so "fuel" is not even universal within the type. Topping one up changes what somebody is smelting. |
| `CookingStation` | `m_useFuel`, `m_useFueldWhileEmpty`, `m_fuelItem`, `m_maxFuel`, `s_fuel` | No | Not a light. Worse, `m_useFueldWhileEmpty` is `true` by default, so fuelling one with nothing on it burns a player's coal for nothing. |
| `ShieldGenerator` | `m_fuelItems` (a **list**), `m_maxFuel`, `s_fuel`, `GetFuel`/`SetFuel` | No | Not a light. It accepts several different items, so which one to feed it is a decision a player makes and a worker should not. |

**Nothing else in the assembly has a fuel level at all.** `Beacon`, `Demister`,
`LightFlicker` and `LightLod` are the light-ish components, and none of them has
a `m_fuelItem`, a `s_fuel`, an `Interactable` surface or any state. They are
renderers. That is the structural reason a decorative light can never be patched
by this product: **it produces no observation**, because the adapter builds one
from a `Fireplace` component and there is nothing else to build one from. It is
not a filter that could be got wrong.

`Fire` — the spreading cinder fire — is a different type again, has no fuel of
its own, and is never touched.

### 9.2 Three `Fireplace` pieces are found and still not serviced

Each is a decision about need rather than permission, and each has its own
verdict so a player can be told which.

| Verdict | Read from | Why it is not a stop |
|---|---|---|
| `NeverConsumes` | `m_secPerFuel <= 0` | `UpdateFireplace` only decays fuel when `m_secPerFuel > 0`. A piece that burns nothing reads the same in an hour, so it is never the one closest to going out. |
| `NotLit` | `ZDOVars.s_state == 2` | A fire a player switched off burns nothing and has no deadline. Without this read, an unlit brazier sitting at zero fuel would out-rank every real fire in the settlement, permanently. `UseItem` does **not** check the state, so this is the round's judgement and not vanilla's refusal. |
| `NotDue` | `fuel * m_secPerFuel >= threshold` | It has burning time left. This is what "prioritise what is closest to going out rather than topping everything to full" looks like as a verdict. |

Plus the two vanilla already refuses, kept as verdicts of their own:
`m_infiniteFuel` (the fuel branch of `UseItem` is gated on `!m_infiniteFuel`) and
`m_canRefill == false` (`UseItem` returns `false` immediately).

### 9.3 Why urgency is measured in seconds and not in fuel

`m_secPerFuel` is per-prefab. A hearth at 4 of 10 and a torch at 4 of 10 are the
same number and not the same situation, and a queue ordered by fuel level sends
a worker to the wrong one. The round therefore ranks by `fuel * m_secPerFuel` —
how long this piece has left at its own rate — and tops a piece up to a stated
number of minutes of light rather than to `m_maxFuel`.

Topping up uses `FuelMath.UnitsToReach`, which keeps both vanilla facts that
make the arithmetic correct: acceptance is `Mathf.CeilToInt(fuel) >= m_maxFuel`
(a **ceiling**, so a fire at 9.5 of 10 refuses), and one accepted unit is worth
`Clamp(Clamp(fuel, 0, max) + 1, 0, max)`, which is less than one at the top of
the range. Dividing the gap by one is wrong in both directions.

### 9.4 What #382 adds to the forbidden list

Nothing. Every mutation is still exactly one `Fireplace.UseItem` per unit,
through the same adapter, with the same ownership and reach preconditions and
the same measured deltas. The round decides *which* pieces and *how many units*;
it does not add a way to change one.
