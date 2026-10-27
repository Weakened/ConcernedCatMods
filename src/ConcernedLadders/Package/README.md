# Concerned Ladders

Vertical ladders for compact Valheim builds. Three 2 m hammer pieces: Wood
Ladder, Copper Ladder and Iron Ladder. Stack matching or mixed materials into a
continuous run.

**0.2.6 is an internal owner test candidate, not a release.** It is built against
Valheim 1.0.20 and tested in single player. Multiplayer, dedicated-server and
controller play have not been tested yet.

## Controls

| Action | What it does |
|---|---|
| **Interact** (Use) | Grab the ladder you are looking at: from the ground, an upper floor, beside or behind the rails, or in midair while jumping or falling. Within about 1.5 m of the rungs, measured from your chest; standing on a floor at the top, within about 1.5 m of the rail tops. While climbing, Interact steps off onto a safe landing. |
| Forward / backward | Climb up / down. Neutral holds. Camera pitch does not matter. |
| **Run** + movement | Climb faster (1.2 m/s instead of 0.9). Costs stamina; without stamina you keep climbing normally. |
| Backward + **Run** at Climbing 20+ | Slide down at 4.5 m/s. Below level 20 it is a fast descent. |
| **Jump** | Hop off upward and in the direction you are steering (away from the ladder when neutral). Uses the game's own jump and stamina; without enough stamina you simply let go. |
| **Crouch** | Drop: let go where you are and fall normally. No hop, no stamina. |

Climb either face of a freestanding ladder. At the top of a run the climber steps
onto a floor beyond the rails or to either side; a closed roof or hatch keeps you
on the ladder. All controls are the game's own bindings, including remaps.

**Climbing skill.** Normal and fast climbing raise a native Climbing skill. Higher
levels make fast climbing and sliding cheaper (up to 25% at level 100); level 20
unlocks sliding. The skill uses the game's own skill screen, saving and death
rules.

**Animation.** An original climbing animation authored in Blender: each hand and foot takes its own turn - a hand
reaches up, the opposite foot recovers knee first, then the other hand and foot - every grip and step on a real rung.
The body rises a little more slowly while a foot comes up and catches up as the leg pushes. Grabbing the ladder places
a hand, a low foot, the other hand and the other foot in turn; a midair catch grabs with the hands first. Stop and
every limb settles on the nearest rung; climb down and the motion retraces itself. Near a flush top the hands take
the rails and the step onto the floor uses the game's own walk. Other players' clients do not show it yet.

**Building from the ladder.** You can mount and climb with the hammer equipped and place,
repair or remove pieces while holding on; your hammer arm uses the game's own animation. In build
mode Interact also cycles snap points, as usual. A new ladder section built above you extends the
climb; removing the section you hold lets you fall.

## Building and recipes

| Piece | Per 2 m segment | Station |
|---|---|---|
| Wood Ladder | 4 Wood | Workbench |
| Copper Ladder | 2 Copper, 1 Wood | Forge |
| Iron Ladder | 2 Iron, 1 Wood | Forge |

These are initial balance values. Recipes use native resource discovery and
hammer placement, support, ward, repair and refund behavior. All three materials climb at the
same speed. A free-standing stack follows the game's own structural support, so a very tall stack
needs bracing against the building.

**Snapping.** Cycle Snap offers Auto, Bottom, Top, Left side and Right side. Aimed at a floor, the ladder goes on
the floor edge nearest your aim, hanging below it (Auto, Top, Left side, Right side; they stand it on the edge when
there is no room below) or standing on it (Bottom). Aimed at a wall, it goes on the wall face toward you, standing on
the wall's bottom edge or hanging from its top edge (Top). Auto, Top and Bottom centre it; Left side and Right side
line its left or right rail up with that corner of the edge or wall, as you face the climbing side. The rails rest
against the real face. Aimed at a ladder, Auto, Top and Bottom extend that column into one straight line (the side
modes refuse there). Rotating the hammer turns a ladder snapped to a floor or wall about the middle or the chosen
corner, up to 45 degrees either way and never into the surface; a turned section does not join the run below it.
The game's own placement rules and building reach still apply.

**Materials.** The Wood Ladder has square timber rails and rungs. The Copper Ladder has slim rounded copper rails,
the Iron Ladder slim flat iron bars, both with round rungs at the same spacing. All three climb the same.

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
