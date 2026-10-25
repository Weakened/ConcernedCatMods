# Concerned Ladders

Vertical ladders for compact Valheim builds. Three 2 m hammer pieces: wood,
copper and iron. Stack matching or mixed materials into a continuous run.

**0.1.1 is an owner-test candidate, not a release.** It is built against
Valheim 1.0.20 and tested in single player. Multiplayer, dedicated-server and
controller play have not been tested yet.

**The owner's 25 October playtest revision is not implemented in 0.1.1.**
Its midair catches, two-sided climbing, powered directional Jump, sprint/stamina,
Climbing skill/slide and snap-text requirements are defined in
`docs/mods/concerned-ladders/OWNER_REVISION_2026_10_25.md`. The controls below
describe the earlier candidate; they are not acceptance criteria for its
replacement. Do not repackage 0.1.1 as that replacement. A new candidate needs
the implementation, new version, current behavior evidence and matching
package/installed-DLL identity.

## Earlier 0.1.1 controls

Use your normal **Interact** binding to mount from a supported landing. Forward
and backward climb up and down; neutral input holds. **Interact** requests a safe
dismount. **Jump** deliberately lets go. A blocked top keeps you on the ladder so
you can descend or release. Keyboard/controller remapping and all exits must pass
the live test plan before a release.

The initial animation is the player's existing locomotion cycle, driven while
moving and stopped while holding. An authored climbing clip and hand/foot IK are
not included. The camera remains under your control.

## Building and recipes

| Piece | Per 2 m segment | Station |
|---|---|---|
| Wood Ladder | 4 Wood | Workbench |
| Copper Ladder | 2 Copper, 1 Wood | Forge |
| Iron Ladder | 2 Iron, 1 Wood | Forge |

These are initial balance values. Recipes use native resource discovery and
hammer placement, support, ward, repair and refund behavior. No extra climbing
skill or stamina drain in the earlier 0.1.1 implementation. The owner revision
replaces that limitation; do not treat it as a prohibition on the new work.
All three materials currently climb at the same speed.

## Installation and compatibility

Required: BepInExPack Valheim **5.4.2351**, Jötunn **2.30.2**, and this mod's own
DLL. No Concerned NPC, Foreman, Teamster, animation framework, or second ladder
mod is required. These dependency versions were checked against publisher
sources; compatibility with the particular installed game must still be tested.

Install the same candidate on the server and every client. This is a custom
piece mod, not a client-only mod. This candidate uses exact patch-version
matching. Do not rename the custom prefab IDs after worlds have been created.

Foreman's existing ladder motor currently conflicts: Concerned Ladders refuses
mounting while that motor is installed. Use the dedicated `TCC-Ladders-Dev`
profile, or disable Foreman's `Ladders/Enabled` and restart before combining them.
The mod does not unpatch Foreman or change another mod's settings.

## Existing builds and removal

Changing `General/ClimbingEnabled` stops climbing but keeps the pieces registered.
Back up the world and remove these custom ladders with the mod still installed
before uninstalling. Loading and saving a world without a custom-piece mod can
remove its objects. Never use a personal world for candidate testing.

## Development

Specification and test plan live under `docs/mods/concerned-ladders/` in
[ConcernedCatMods](https://github.com/Weakened/ConcernedCatMods).
Report issues with the exact game version, dependency versions, source commit,
candidate DLL hash, and a minimal reproduction.
