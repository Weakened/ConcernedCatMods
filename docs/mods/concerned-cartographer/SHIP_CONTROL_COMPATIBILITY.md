# Valheim 1.0 ship-control compatibility spike

- Issue: CC-RF-003 (#242)
- Parent: Optional Route Follow (#104)
- Audit date: 2026-09-10
- Status: **installed-assembly compatibility PASS; disposable-profile live matrix pending**

This spike verifies the control boundary that a later sailing Route Follow
slice may use. It does not implement steering, change package behavior, or
claim a live Valheim test.

## Audited environment

No game, Unity, loader, or framework binary is committed or packaged.
Hashes identify only the licensed local inputs inspected on BLD.

| Input | Identity | SHA-256 |
|---|---|---|
| Valheim | 1.0.7, Steam build 25185596; `assembly_valheim.dll` 2,566,144 B | `A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC` |
| Unity | 6000.0.75f1; `globalgamemanagers` 205,668 B | `FB918473A11E666800CA644AEAA6FE6C6A82609CB3E8EEBA14315CB1F6276E10` |
| Ship asset catalog | `resources.assets` 79,463,604 B | `F0510AA4BDE5D3D8243B65EB51452D4A9CC4D4251DC7F8C150B88BB1FF8BE560` |
| BepInEx | 5.4.23.3; 130,048 B | `E9AC3A950E91E71B13DF5480B36CE06AF27E981A688F0E62125B674D03A0713A` |
| Jötunn | 2.29.2.0; 516,096 B | `F65751BC15E7AE7466B0F7D3B38C758397741C99A50890DEB19ACF738376BFB1` |

## Installed vessel surface

The installed 1.0.7 asset catalog exposes exactly four player-facing ship
names. The Ashlands longship is the additional variant beyond the original
three.

| Asset key | Player-facing name | Static result |
|---|---|---|
| `ship_raft` | Raft | present |
| `ship_karve` | Karve | present |
| `ship_longship` | Longship | present |
| `ship_longship_ashlands` | Drakkar | present |

Valheim implements ship behavior through the shared `Ship` and
`ShipControlls` types, rather than one control type per vessel. The catalog
proves the installed variant set; the live matrix below still must confirm
that every prefab carries the expected shared controls. A future unexpected
`ship_*` display key makes the audit fail for review instead of silently
excluding a new vessel.

## Verified control path

1. `ShipControlls.Interact` sends `RequestControl` with the player ID.
2. The ship's current `ZNetView` owner accepts only a player actually
   aboard the ship, writes `ZDOVars.s_user`, and returns the result.
3. On success, the local player starts doodad control with that exact
   `ShipControlls` instance.
4. `Player.SetControls` forwards raw input through
   `IDoodadController.ApplyControlls`.
5. `ShipControlls.ApplyControlls(Vector3, Vector3, bool, bool, bool)`
   delegates the movement vector to `Ship.ApplyControlls(Vector3)`.
6. `Ship.ApplyControlls` maintains vanilla forward/back edge state,
   integrates the rudder input, and sends Forward, Backward, or Rudder RPCs
   to the ship object's current owner.
7. The owner alone applies water, sail, steering, collision, and damage
   forces in `Ship.CustomFixedUpdate`.
8. The owner publishes vanilla speed/rudder ZDO state. Standard
   `ZSyncTransform` publishes position/rotation and non-owners interpolate.

The helmsman and physics owner are therefore not necessarily the same peer.
A client helmsman is valid. Route Follow must never require
`Ship.IsOwner()`, call `SetOwner`, or introduce a new movement protocol.

## Audited members

| Purpose | Valheim 1.0.7 member |
|---|---|
| Safe interception point | `ShipControlls.ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)` |
| Local controlled ship | `Player.GetControlledShip()` |
| Exact doodad identity | `Player.GetDoodadController()` |
| Granted helm user | `ShipControlls.GetUser()`, `HaveValidUser()` |
| Vanilla control path | `Ship.ApplyControlls(Vector3 dir)` |
| Read-only sail state | `Ship.IsSailUp()`, `GetSpeedSetting()` |
| Read-only rudder state | `Ship.GetRudderValue()`, `GetRudder()` |
| Read-only wind state | `Ship.GetWindAngleFactor()`, `GetWindAngle()` |
| Lifecycle | `ShipControlls.OnUseStop(Player)`, `Player.StopDoodadControl()` |
| Network ownership | `Ship.IsOwner()`, `ZNetView.IsOwner()` |

## Required adapter boundary for #243

The later runtime adapter should be a narrow Harmony prefix on
`ShipControlls.ApplyControlls`. It must run synchronously where vanilla
already receives helm input; a separate `Update` call into
`Ship.ApplyControlls` would race vanilla input and duplicate RPC cadence.

Before modifying an argument, the adapter must prove all of these:

- the feature is explicitly enabled and a sailing route is active;
- `Player.m_localPlayer` is alive and present;
- `GetControlledShip()` returns this controls object's ship;
- `GetDoodadController()` is this exact `ShipControlls` instance;
- `HaveValidUser()` is true and `GetUser()` equals the local player ID;
- the ship/controls Unity objects are live and the world/session key matches;
- route projection and the control state are valid and finite.

It must snapshot raw `moveDir` first. Any player rudder or sail input
(`x` or `z` outside a small tested dead zone) cancels Route Follow in
that same call and passes the vector through unchanged. Vanilla jump,
attack, secondary attack, dodge, Use-to-leave, and invalid-controller paths
already stop doodad control; the route state must also observe that loss and
cancel without attempting one final steering write.
Only after every gate passes may the prefix replace `moveDir.x` with a
finite value clamped to [-1, 1]. It must preserve `y`, `z`, look
direction, run, autorun, and block exactly.

The synthetic value is a **rudder direction/rate input**, not an absolute
rudder angle. Vanilla integrates it into `m_rudderValue` using its own
speed and fixed timestep, then retains its 0.2-second Rudder RPC cadence.
The adapter must not reflect or write private rudder fields and must not
call `Ship.Rudder` directly.

### Sail and wind contract

The first sailing slice keeps sail power entirely manual:

- never call `Forward`, `Backward`, or `Stop`;
- never change `Ship.Speed` or its private state;
- never write `EnvMan`, wind direction, wind intensity, sail force, body
  velocity, transform, or rigidbody forces;
- use sail/wind getters only for UI or a fail-safe decision;
- cancel when bounded progress/stuck/cross-track rules say the vanilla wind
  and physics cannot hold the route;
- do not tack, avoid obstacles, dock, boost speed, or push through collision.

This preserves the exact movement that an unmodded peer already receives.
A dedicated server does not need Cartographer merely to relay ordinary ship
RPC/ZDO/transform state.

## Cancellation and lifecycle contract

The state owner in #243 must cancel and clear references on:

- any manual rudder or sail input;
- leaving the helm or losing the granted-user identity;
- ship/controls destruction or Unity-null transition;
- route edit, deletion, archive, deselection, or world mismatch;
- excessive cross-track error, non-finite geometry, no-progress timeout, or
  route end;
- teleport, death, map/world transition, logout, plugin disable, or patch
  failure.

Cancellation restores full vanilla behavior by doing nothing to the current
input call. It must not send a compensating Stop/Rudder RPC because that
would overwrite the player's vanilla state.

If any inspected type/member/signature moves after a Valheim update, patch
installation must fail closed: log one actionable warning, keep Route Follow
disabled for that session, and leave the original method unmodified.
Partial binding and guessed reflection fallbacks are forbidden.

## Repeatable static evidence

Run on a machine with the licensed Valheim install configured:

```powershell
pwsh ./scripts/audit-cartographer-ship-api.ps1 -Configuration Release
dotnet test ./src/ConcernedCartographer.Tests/ConcernedCartographer.Tests.csproj -c Release
python ./tools/validate_repo.py
```
`audit-cartographer-ship-api.ps1` builds Cartographer unless
`-SkipBuild` is supplied, decompiles only the required installed types,
asserts the control/authority/replication members and vessel catalog, and
prints sanitized JSON identities. It never copies an inspected binary.

## Disposable-profile live matrix

No row is claimed as passed by this spike. Record game/mod versions, host
topology, disposable world, steps, result, log excerpt, and video reference.

| Row | Setup and action | Required result | Status |
|---|---|---|---|
| V1 | Raft, solo: take/leave helm; exercise rudder and every sail step | Shared controls bind; vanilla behavior unchanged with feature off | Pending owner |
| V2 | Karve, same sequence | Same; no missing member or repeated warning | Pending owner |
| V3 | Longship, same sequence | Same | Pending owner |
| V4 | Drakkar, same sequence including Ashlands-ready behavior | Same; no variant-specific bypass | Pending owner |
| A1 | Player-hosted: client (not host) takes helm and steers | Client is granted control; owner simulates; ordinary motion reaches both peers | Pending owner |
| A2 | Dedicated server without Cartographer; one modded helmsman, one unmodded observer | No server dependency; observer sees ordinary ship motion | Pending owner |
| M1 | While following, apply manual left/right then sail forward/back | Follow cancels on the first raw input; that exact input reaches vanilla | Pending #243 |
| W1 | Tailwind, crosswind, headwind/no-progress cases | Vanilla wind/physics remain authoritative; no tack/boost; unsafe case cancels | Pending #243 |
| L1 | Leave helm with Use, jump/attack/dodge, and move out of range | Follow state clears; no later RPC or stale reference | Pending #243 |
| L2 | Route edit/delete/archive/deselect and route end | Immediate clean cancellation | Pending #243 |
| L3 | Death, portal/teleport, logout/relog, world switch, plugin disable | State clears across every boundary; vanilla control remains usable | Pending #243 |
| L4 | Destroy a disposable test ship while follow is active | Unity-null path cancels without exception or world/save damage | Pending #243 |
| F1 | Simulate adapter bind failure in a test build | One warning; feature disabled; untouched vanilla controls | Pending #243 |
| S1 | Review server and client logs after the matrix | No ownership transfer, custom movement RPC, force/transform write, or repeated error | Pending #243 |

Use a dedicated `TCC-Compat`-style profile and disposable world. Destructive
ship testing must never use the owner's normal world. Publication remains
blocked until the #243 live rows pass.

## Residual risks and explicit non-claims

- Static catalog rows do not prove prefab component wiring; V1-V4 do.
- The typo `ApplyControlls` is Valheim's real API spelling and must be
  treated as an unstable internal member.
- `Ship.GetLocalShip()` means the latest ship the local player is aboard,
  not necessarily the ship they control; it is insufficient as a helm gate.
- Static IL proves current authority and RPC paths, not latency feel under a
  real host/dedicated-server session.
- No steering algorithm, route state, config, UI, package version, tag,
  release, or publication is part of #242.
