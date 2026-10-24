# Concerned Ladders — architecture and continuation map

## Product boundary

`Plugin.cs` binds the local climb enable setting, registers custom build pieces,
installs the traversal adapter, updates it, and tears it down. Package identity
and native save prefab IDs are listed in `PROJECT.md`. No compile-time reference
to another Concerned Cat product or the Concerned NPC library exists.

The original Foreman ladder implementation remains in place. It supplies useful
historical context and the pure `src/Shared/Ladders` primitives; its engine motor
is not copied wholesale. Its existing tests are not proof of this product.

## Source map

| Path | Responsibility | Allowed engine contact |
|---|---|---|
| `Pieces/LadderPieceRegistration.cs` | Three inactive prefab clones, original geometry/material references, snap points, recipes and registration | Yes, during registration |
| `Pieces/LadderLocalization.cs` | Localized piece/menu strings | Jotunn localization only |
| `Domain/` | Geometry, finite-value guards, chain connectivity and movement/exit decisions | None |
| `Runtime/LadderSegment.cs` | Custom marker, native hover/interaction, loaded piece registry | Yes |
| `Runtime/` | Local-owner runtime, input adapter, capsule queries, narrow motion hooks, walking pose | Yes, only the selected local climber |
| `src/Shared/Ladders/` | Existing BCL points, geometry and other adopted primitives | None |
| `src/ConcernedLadders.Tests/` | Source-linked pure-domain tests | No game assemblies |
| `LaddersPaths.cs` | Canonical future diagnostic data-root contract | Does not create or write a folder |
| `tools/prepare_ladders_profile.py` | Deterministic dependency-only `.r2z` | No device/profile mutation |
| `scripts/deploy-ladders-profile.ps1` | Validated exact candidate install into explicitly selected profile | Own DLL only |

The local runtime code is the authority for method/property names; do not rebuild
it from this map. Read the current source before changing it.

## Prefab lifecycle

Wait for `PrefabManager.OnVanillaPrefabsAvailable`. Create a `CustomPiece` clone
under Jotunn's inactive parent; keep `activeSelf` suitable for future placed
instances. Never instantiate an active gameplay object and strip it later.
Keep root Piece/ZNetView/WearNTear, native effects and structural support values.
Remove clone-only donor geometry, colliders, LODs and snap children. Rebind wear
visual/fragment references to the new roots. Use a separate always-active
collision shape and direct-child `snappoint` transforms.

Use explicit `PieceConfig.Usage` tags supported in Jotunn 2.30.2. Add exactly
three custom pieces, with stable IDs and explicit recipes/stations. Unsubscribe
after success. Partial registration is an error; startup logs must identify the
missing piece and cannot label the profile ready.

Never pick an arbitrary first material from a donor: snow/LOD/effects materials
may come first. The initial named material donors are woodwall/woodwall,
Copper/copper, and woodiron_pole/Ironbeam_mat. Clone before darkening for worn
visuals. Local play must verify shader/lighting/UV appearance; source inspection
cannot establish art quality.

## Traversal contract

One local player owns one temporary session. The ladder component is shared
scene geometry, never the storage location for mutable per-player position or
attachment transforms. Only this mod's dedicated marker is admitted; no vanilla
Ladder component is attached to its prefabs. This keeps the generic Foreman
Ladder.Interact prefix out of the interaction path.

The runtime establishes ownership and capability before consuming movement. The
native `Player.TakeInput` capability is bound once; unavailable input/motor APIs
refuse mounting with a useful message. Do not introduce guessed compatibility
fallbacks that bypass menus, wards, collision or network ownership.

A chain is an endpoint graph of loaded valid segments, subject to finite value,
orientation, distance, upright and length limits. Connection tolerance only
absorbs floating-point placement error. It cannot bridge visibly missing rungs,
opposite-facing ladders or parallel neighboring shafts. A bounded connected run
is discovered from the selected segment, rather than freezing a small nearby
sphere around the original entry point.

Sweep the actual scaled player capsule for every move and every stage of entry
and exit, checking destination overlap too. Ignore the local player's own
colliders and only the exact connected ladder pieces as appropriate; do not
ignore the surrounding building, terrain or other characters. The collider
remains enabled. Finite, bounded physics-step movement is the single writer.

Transitions above a ledge are multi-stage and checked again as the world changes.
Headroom, support surface normal and reachable standing space are required. A
blocked transition holds; disappearance/invalid geometry releases. Jump release
uses normal fall semantics. Grounded-only mounts prevent fall-reset catches.

## Narrow patches and restoration

Patches are named and live under `com.theconcernedcat.valheim.concernedladders.motor`.
They return immediately for all other actors and whenever no valid owned session
exists. Only the chosen player's movement is overridden. Never change terrain,
world saves, unrelated pieces, inventories, remote body ownership or global
physics settings. Existing native player synchronization carries motion and
locomotion parameters; no arbitrary custom RPC is needed.

Capture only state actually modified. Restore on every normal/error/lifecycle
exit, and avoid writing into a player whose ownership or external attach/teleport
state has changed. Clear stale references on world/character changes. Do not
restore a stale pre-climb velocity, force the collider enabled, or leave gravity
off. Fall-height handling must be narrowly justified and tested against the
installed game: no global immunity and no nonfinite values.

The initial product refuses a mount if the legacy Foreman ladder motor Harmony
owner is installed. It neither unpatches it nor rewrites another mod's config.
This is an explicit temporary limitation, not a claim of compatible coexistence.

## Build and dependency boundary

New product JotunnLib NuGet: 2.30.2. BepInExPack profile pin: 5.4.2351.
The package's MSBuild tooling still needs a licensed local Valheim installation
and the matching mod-profile references from local Environment.props. A NuGet
restore is not a product compile. The full local gate is `scripts/verify.ps1`.

Tests link game-free production code into net10.0. They can prove geometry and
state decisions. They do not prove Unity collision execution, shader references,
current private method signatures, network synchronization or animation output.
Do not create large invented game stubs to turn those unknowns into a green build.

Package only the mod's own DLL, README, CHANGELOG, LICENSE and original icon. The
`.r2z` installs dependency references only; DLLs are rejected by the supported
profile importer. Import/build/deploy are separate checked steps.

## Explicitly deferred

Bone appearance, authored animation/IK, sound polish, NPC traversal, moving
structures, hatches, rope ladders, extra sizes, custom skills, stamina tuning,
deep late-game progression and full Foreman motor arbitration. These must not
delay a reliable test of the three requested pieces.

## Verified external sources (2026-10-24)

- [Jotunn 2.30.2 changelog](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/CHANGELOG.md)
- [PieceConfig](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Configs/PieceConfig.cs)
- [CustomPiece](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Entities/CustomPiece.cs)
- [Piece registration tutorial](https://valheim-modding.github.io/Jotunn/tutorials/pieces.html)
- [Network compatibility](https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html)
- [Material names](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Documentation/data/prefabs/material-list.md)
- [BepInExPack publisher](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn publisher](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
- [r2modman profile import source](https://github.com/ebkr/r2modmanPlus/blob/9776867fff5411048a17bca3e9e7e34779737e56/src/utils/ProfileUtils.ts)

Primary-source use of game APIs informs the candidate; only the local licensed
build establishes their current compatibility. No third-party ladder source is
copied into this product.
