# Placement authority — the check ladder

Issue: [CF-SET-003 (#280)](https://github.com/Weakened/ConcernedCatMods/issues/280).
Parent: [#273](https://github.com/Weakened/ConcernedCatMods/issues/273).
Decisions inherited: [`SETTLEMENT_AUTHORITY.md`](SETTLEMENT_AUTHORITY.md) §3.

**Status: in progress.** The game-free decision core is written and tested. The
game-facing adapter is **not written yet**; the table in §3 is the verified map
it will be built from. Nothing here has been observed placing a piece.

Read from the installed **1.0.12** `assembly_valheim.dll`
(SHA256 `27a766a8…c393a84`), same binary as
[`WORKER_ACTOR_SPIKE.md`](WORKER_ACTOR_SPIKE.md).

---

## 1. The problem, restated from the binary

`Player.TryPlacePiece(Piece)` calls `UpdatePlacementGhost(flashGuardStone: true)`
and then switches over **fourteen** `PlacementStatus` failures before delegating:
`NoBuildZone`, `BlockedbyPlayer`, `PrivateZone`, `MoreSpace`, `NoTeleportArea`,
`Invalid`, `NoRayHits`, `ExtensionMissingStation`, `WrongBiome`,
`NeedCultivated`, `NeedDirt`, `NotInDungeon`, `DeepSnow`, `NoSnow`.

`Player.PlacePiece(Piece piece, Vector3 pos, Quaternion rot, bool doAttack = true, bool cheated = false)`
is `public void` and performs **none** of them. It instantiates, sets the
creator, calls `PrivateArea.Setup`, `WearNTear.OnPlaced()` and effects. Cost is
consumed separately by the caller.

So `PlacePiece` alone bypasses validity, ward and cost — which #273's gate 4
forbids — and `TryPlacePiece` cannot be driven on somebody else's behalf,
because it reads `m_placementStatus` off the player's own placement ghost.

## 2. The decision core, and the property it guarantees

`src/Shared/Settlement/Placement/PlacementGate.cs`, engine-free.

Each check answers one of **three** values, not two:

| `CheckOutcome` | Meaning | Effect |
|---|---|---|
| `Passed` | allowed | — |
| `Failed` | the game says no | refuse, `Denied` |
| `Unavailable` = **0** | could not be established | refuse, `CouldNotEstablish` |

Two design choices carry the leaf's go/no-go:

1. **`Unavailable` is zero**, so a forgotten or uninitialised answer refuses.
   The safe case is the one you get by default, not the one you must remember to
   write. Pinned by `UnavailableIsZero_SoAForgottenAnswerFailsClosed`.
2. **The gate owns the required set.** `PlacementAnswers.Evaluate()` allows only
   when *every* member of `PlacementCheck` has been answered `Passed`. A check
   that is never mentioned is not skipped — it refuses, naming itself. So adding
   a member to the enum makes every existing caller refuse until it is taught to
   answer it, which is the correct direction for a safety ladder to break in.
   Pinned by `OmittingAnySingleCheck_Refuses_NamingThatCheck`, which walks every
   check in turn.

"Denied" and "could not establish" stay distinct all the way to the player,
because only one of them is worth retrying after moving. Among several refusals,
a definite denial outranks an unestablished check, and the report order is fixed
so a piece inside somebody's ward says *ward* rather than *you also need a
workbench*.

## 3. The verified map for the adapter

Every row below was confirmed present, by signature, in the installed assembly.
**Confirming that an API exists is not the same as confirming our use of it
matches vanilla's** — the right-hand column is what the adapter must still be
written and observed to do.

| Check | Vanilla surface (verified present) | Notes for the adapter |
|---|---|---|
| `Ward` | `PrivateArea.CheckAccess(Vector3 point, float radius = 0f, bool flash = true, bool wardCheck = false)` | call with `flash: false, wardCheck: true` — a worker must not flash guard stones at a player |
| `BuildStation` | `CraftingStation.HaveBuildStationInRange(string name, Vector3 point)`; requirement from `Piece.m_craftingStation` | null `m_craftingStation` means no station needed → `Passed` |
| `BuildZone` | `Location.IsInsideNoBuildLocation(Vector3 point)` | |
| `Ground` | `ZoneSystem.GetSolidHeight(Vector3, out float)` / `GetGroundHeight` | ground that cannot be measured is `Unavailable`, not flat |
| `Biome` | `Heightmap.FindBiome(Vector3 point)` (static) vs `Piece.m_onlyInBiome` | `m_onlyInBiome` is a flags enum; `None` means unrestricted |
| `Clearance` | vanilla computes this from the **placement ghost's own colliders** in `UpdatePlacementGhost` (`MoreSpace`, `BlockedbyPlayer`, `NoRayHits`, `Invalid`) | **this is the one with no faithful reimplementation** — see §4 |
| `TeleportArea` | `Piece.m_onlyInTeleportArea && !EffectArea.IsPointInsideArea(point, EffectArea.Type.Teleport)` | exact vanilla expression |
| `DungeonRule` | `!Piece.m_allowedInDungeons && player.InInterior() && !EnvMan.instance.CheckInteriorBuildingOverride() && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DungeonBuild)` | exact vanilla expression. Note it reads the **placing player's** interior state, which is faithful here precisely because §3 of the ADR has the host player place |
| `GroundCover` | `Piece.m_groundOnly`, `m_cultivatedGroundOnly`, `m_notOnFloor`, `m_notOnWood`, `m_notOnTiltingSurface`, `m_noInWater`, `m_clipEverything` | several are surface-material questions answered from the raycast hit vanilla already has and we do not |
| `Cost` | `Piece.m_resources`, consumed by `Player.ConsumeResources(Piece.Requirement[], int qualityLevel, int itemQuality = -1, int multiplier = 1)` | consumed against **reserved** custody only. The reservation itself is CF-SET-006 (#283); until that lands this check has no real custody to read and must answer `Unavailable` |

## 4. The go/no-go, and the honest answer so far

The leaf's condition is: *proceed only if every check that cannot be faithfully
reimplemented can be turned into a refusal with a named reason.*

**Structurally, yes** — that is exactly what §2 guarantees, and it is proved
rather than asserted.

**But two checks currently have no faithful reimplementation**, and the
consequence must be stated plainly rather than designed around:

- **`Clearance`.** Vanilla decides `MoreSpace` / `BlockedbyPlayer` / `NoRayHits`
  / `Invalid` from the placement ghost's own colliders, cast at the ghost's exact
  transform. We have no ghost. Anything we substitute — a sphere overlap, a
  bounding box from the prefab — is an *approximation*, and an approximation that
  passes where vanilla would have failed is precisely the silent skip gate 4
  forbids.
- **`Cost`.** There is no custody ledger to reserve against until CF-SET-006
  (#283) lands.

So the adapter's first honest version answers both `Unavailable`, and **every
placement is refused** until each is either faithfully implemented or
deliberately, separately authorised. That is a working leaf, not a broken one:
a runtime that refuses everything and says exactly which check it could not make
is the correct starting point, and each check becoming real is then a visible,
reviewable change rather than a silent loosening.

**What this means for the graph:** CF-SET-008 (staged construction) cannot place
anything real until `Clearance` is resolved. That is a genuine finding about the
first cottage, not a detail — and it is better known now than discovered at
construction time.

## 5. Not proved

- **Nothing has been observed placing a piece.** No adapter exists yet.
- No claim here that any check matches vanilla's behaviour; only that the API it
  would use exists with the stated signature.
- The four acceptance criteria in #280 that require a real refusal (ward named,
  station named, cost from reservation, no check skipped) are **not met**; only
  the last is structurally guaranteed by the core.
