# Concerned Ladders

Vertical ladders for compact Valheim builds. Three 2 m hammer pieces: Wood
Ladder, Copper Ladder and Iron Ladder. Stack matching or mixed materials into a
continuous run.

**0.2.1 is an owner-test candidate, not a release.** It is built against
Valheim 1.0.20 and tested in single player. Multiplayer, dedicated-server and
controller play have not been tested yet.

## Controls

| Action | What it does |
|---|---|
| **Interact** (Use) | Grab the ladder you are looking at: from the ground, an upper floor, beside or behind the rails, or in midair while jumping or falling. Within about 1.5 m of the rungs, measured from your chest; standing on a floor at the top, within about 1.5 m of the rail tops. While climbing, Interact steps off onto a safe landing. |
| Forward / backward | Climb up / down. Neutral holds. Camera pitch does not matter. |
| **Run** + movement | Climb faster (2.43 m/s instead of 1.8). Costs stamina; without stamina you keep climbing normally. |
| Backward + **Run** at Climbing 20+ | Slide down at 4.5 m/s. Below level 20 it is a fast descent. |
| **Jump** | Hop off upward and in the direction you are steering (away from the ladder when neutral). Uses the game's own jump and stamina; without enough stamina you simply let go. |

Climb either face of a freestanding ladder. At the top of a run the climber steps
onto a floor beyond the rails or to either side; a closed roof or hatch keeps you
on the ladder. All controls are the game's own bindings, including remaps.

**Climbing skill.** Normal and fast climbing raise a native Climbing skill. Higher
levels make fast climbing and sliding cheaper (up to 25% at level 100); level 20
unlocks sliding. The skill uses the game's own skill screen, saving and death
rules.

**Animation.** Climbers use a custom procedural climbing pose: hands and feet
take hold of the actual rungs and move with the distance climbed. Other players'
clients pose a climber from that player's position on the ladder; this has not
yet been checked with a second player.

## Building and recipes

| Piece | Per 2 m segment | Station |
|---|---|---|
| Wood Ladder | 4 Wood | Workbench |
| Copper Ladder | 2 Copper, 1 Wood | Forge |
| Iron Ladder | 2 Iron, 1 Wood | Forge |

These are initial balance values. Recipes use native resource discovery and
hammer placement, support, ward, repair and refund behavior. Cycle Snap shows
Top, Bottom and the four corners. All three materials climb at the same speed.

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

Changing `General/ClimbingEnabled` stops climbing but keeps the pieces and the
Climbing skill registered. Back up the world and remove these custom ladders
with the mod still installed before uninstalling. Loading and saving a world
without a custom-piece mod can remove its objects, and saving a character without
the mod is not promised to keep the Climbing skill. Never use a personal world or
character for candidate testing.

## Development

Specification and test plan live under `docs/mods/concerned-ladders/` in
[ConcernedCatMods](https://github.com/Weakened/ConcernedCatMods).
Report issues with the exact game version, dependency versions, source commit,
candidate DLL hash, and a minimal reproduction.
