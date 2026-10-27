# Concerned Ladders

Vertical ladders for compact Valheim builds. Three 2 m hammer pieces - **Vertical Wood Ladder**,
**Vertical Copper Ladder** and **Vertical Iron Ladder** - that stack into one continuous climb, with
an original climbing animation, quick top exits, grabs from the water and in midair, jump-off, drop,
side dismounts, a fast slide, and a Climbing skill.

Built and tested against Valheim **1.0.20** with BepInExPack Valheim 5.4.2351 and Jötunn 2.30.2.

## In action

**Climb out of the water onto a dock.** Swim up to a ladder, press Interact, climb out.

![A character swims up to a wooden ladder hanging off the end of a dock, grabs it, climbs out of the water and walks onto the dock](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/water-to-dock.gif)

**Climb up and down.** Each hand and foot takes its own rung.

![Side view of a character climbing a three-section wooden ladder up a tower, pausing, and climbing back down to the ground](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/climb.gif)

**Step off at the top, or get on from the floor above.**

![A character climbs to the top of the ladder and steps off onto the wooden floor at the top of the tower](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/top-exit.gif)

![A character standing on the top floor gets onto the ladder below the edge and climbs down](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/top-entry.gif)

**Catch a ladder in midair.** Jump, press Interact, and you are on the rungs.

![A character jumps beside the ladder, catches it in midair, climbs a little and then drops off](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/midair-catch.gif)

**Jump off, step off to the side, or slide down.**

![A character hanging on the ladder jumps off backwards and lands on the grass](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/jump-off.gif)

![A character partway up the ladder steps sideways onto a small floor beside it](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/side-dismount.gif)

![A character slides quickly down the ladder from the top to the ground](https://raw.githubusercontent.com/Weakened/ConcernedCatMods/48a6c59fc199de2078a31004de3ab7c9feabe2e3/docs/media/concerned-ladders/slide.gif)

## Building

Equip the **Hammer** and open the **Building** tab. The ladders use the game's normal resource
discovery: each one appears once you have found its materials.

| Piece | Cost per 2 m section | Needs nearby | Health |
|---|---|---|---|
| Vertical Wood Ladder | 4 Wood | Workbench | 200 |
| Vertical Copper Ladder | 2 Copper, 1 Wood | Forge | 400 |
| Vertical Iron Ladder | 2 Iron, 1 Wood | Forge | 800 |

All three climb exactly the same. Placement, support, wards, repair, damage and refunds are the
game's own. A free-standing stack follows the game's structural support, so a tall wooden stack
needs bracing against the building (an unbraced wood stack holds about five sections).

**Snapping.** Cycle Snap offers Auto, Bottom, Top, Left side and Right side.

- **Floor:** aimed at a floor, the ladder goes on the floor edge nearest your aim. It hangs below the edge, or stands on it with Bottom.
- **Wall:** aimed at a wall, it goes on the wall face toward you, standing on the wall's bottom edge or hanging from its top edge (Top).
- **Centring:** Auto, Top and Bottom centre it. Left side and Right side line one rail up with that corner, as you face the climbing side.
- **Ladder:** aimed at another ladder, Auto, Top and Bottom extend the column in a straight line.
- **Rotation:** rotating the hammer turns a ladder snapped to a floor or wall up to 45 degrees either way. A turned section does not join the run below it.

## Controls

The ladder uses the game's own bindings, so if you rebind a key in Settings, the ladder follows.
The keys below are the PC defaults.

| Action | Default key | What it does |
|---|---|---|
| Get on | **E** (Interact) | Grab the ladder you are looking at: from the ground, from a floor above, from beside or behind it, in midair while jumping or falling, or while swimming. Reach is about 1.5 m. |
| Climb up / down | **W** / **S** | Climb at 0.9 m/s. Let go of the keys and you hold still. Camera pitch does not matter. |
| Climb faster | **Left Shift** (Run) + W/S | 1.2 m/s, costs stamina. Out of stamina, you keep climbing at normal speed. |
| Slide down | **Left Shift** + **S** at Climbing 20+ | Slide down at 4.5 m/s. Below level 20 this is the faster climb instead. |
| Step off | **E** while climbing | Step onto a floor at the top, a floor beside the ladder partway up, or the ground at the bottom. |
| Jump off | **Space** (Jump) | Hop off upward and in the direction you steer, or straight away from the ladder. It uses the game's jump and stamina. If you are over-encumbered or out of stamina, you just let go. |
| Let go | **Left Ctrl** (Crouch) | Drop where you are and fall normally. |

Near the top, the climber steps onto a floor beyond the rails or to either side. If there is nowhere
to step off, or a closed roof or hatch is overhead, climbing stops about 1.3 m below the top, with
both hands still on the rungs.

You can climb with the hammer out and build, repair or remove pieces while holding on. Removing the
section you are holding makes you fall. A new section built above you extends the climb.

Gamepad buttons are wired to the same game actions, but controller play has not been tested yet.

## Climbing skill

Concerned Ladders adds a **Climbing** skill to the game's skill screen.

- **Earning:** you earn Climbing for each whole metre actually climbed at normal or fast speed. Holding still, sliding, jumping and dropping earn nothing, so hanging on a ladder cannot farm it.
- **Effect:** higher levels make fast climbing and sliding cheaper, up to 25% less stamina at level 100.
- **Slide:** level 20 unlocks the slide.
- **Stamina:** fast climbing costs 6 stamina per second and sliding 2 per second, before the skill saving, the world's stamina setting, and the movement modifiers from your equipment and status effects. Normal climbing is free.
- **Saving and death:** the skill is saved with your character and loses levels on death like any other skill.
- **Jump:** a powered jump-off trains the game's own Jump skill. No other vanilla skill is changed.

**Falling.** While you hold on, you take no fall damage. When you let go or jump, the game's normal
fall damage is measured from where you let go.

## Multiplayer

**Every player and the server need the mod, at the same version.** The ladders are custom building
pieces that a game without the mod cannot load. The mod also tells Jötunn that every client and the
server must have it at a matching version, and Jötunn checks that when a player connects.

- **Tested:** single player.
- **Not tested yet:** dedicated servers, hosting for or joining other players, mixed modded and unmodded players, and mismatched versions.
- **Other players' view of a climber:** the climbing animation is applied only in the climber's own game. It is not sent to other players, and what they see has not been tested.

## Configuration

`BepInEx/config/com.theconcernedcat.valheim.concernedladders.cfg`

| Setting | Default | Meaning |
|---|---|---|
| `General` / `ClimbingEnabled` | `true` | Turns climbing on or off. Placed ladders and the Climbing skill stay registered either way. |

## Known limitations

- Other players do not see the climbing animation (see Multiplayer).
- Multiplayer, dedicated servers and controllers are untested.
- Uninstalling deletes placed ladders and resets the Climbing skill (see Uninstalling).
- A free-standing wooden stack taller than about five sections falls apart on reload because of vanilla structural support. Brace it, or use copper or iron.
- Grabbing from the water: the swimmer bobs, so it can take a second Interact to aim at the ladder.
- Concerned Foreman's own ladder climbing conflicts. While it is active, Concerned Ladders refuses to mount and says so. Turn off Foreman's `Ladders/Enabled` and restart to use both mods.

## Troubleshooting

- **No ladders in the build menu.** Open the Hammer's Building tab. You may not have discovered the materials yet: Wood for the wooden ladder, Copper or Iron for the metal ones.
- **"Move closer to climb".** Stand within about 1.5 m of the rungs and look at the ladder.
- **Run does not climb faster.** You are out of stamina.
- **Run + S does not slide.** Climbing is below level 20.
- **Can't connect to a server.** Install the same Concerned Ladders version on the server and every client.
- **Reporting a bug.** Attach `BepInEx/LogOutput.log`. The mod's startup line shows its exact version.

## Uninstalling

**Remove every ladder from your world first, while the mod is still installed.**
Tested 2026-10-27 on a disposable world and character:

- **Placed ladders:** if the world is loaded without the mod, the game deletes every placed ladder for good and refunds nothing. Reinstalling does not bring them back.
- **Climbing skill:** a character loaded without the mod loses the skill, and it is back at 0 after reinstalling.
- **Everything else:** floors and walls built around the ladders, and every other skill, were unaffected.

To keep everything, deconstruct the ladders with the hammer before removing the mod. Turning off
`ClimbingEnabled` instead keeps both the ladders and the skill.

## Credits and license

Made by **The Concerned Cat** (Eren Cansunar), developed with AI coding assistance. The climbing
animation is original, authored in Blender for this mod. The ladders reuse the game's own materials
at runtime. No game files are bundled.

MIT License (see `LICENSE` in this package). Requires
[BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and
[Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).

## Support

- Bugs and suggestions: <https://github.com/Weakened/ConcernedCatMods/issues>
- Private or sensitive reports: <support@theconcernedcat.com>
