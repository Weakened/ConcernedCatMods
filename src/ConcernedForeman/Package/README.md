# Concerned Foreman

**Point at a building or a station and Foreman explains why it is stable,
exposed, comfortable, or about to fail.**

> **This build is an early spike, not a playable release.** It contains
> climbable ladders and the first slice of the opt-in settlement runtime, and
> nothing of the diagnostics half yet. **Nothing in it has been observed working
> in a game.** If you are looking for a mod that does something useful today,
> this is not yet it.

---

## What is actually in 0.1.0

Two things: **ladders you climb instead of teleport up**, and **a settlement
worker that walks to a point and stops.**

The worker sounds small, and it is, deliberately. The question it exists to
answer is whether a modded creature can be made to do *only* what it is told —
no wandering off, no reacting to a passing boar, no joining a fight — because
every later piece of a settlement depends on the answer being yes.

---

## Ladders you can climb

> Valheim players should not be forced to build staircases everywhere just to
> move vertically.

Valheim's ladders teleport you. Walk up to one, press Use, and you are simply
somewhere else. This makes them **climbable**: you walk into a ladder, your
character takes hold of it, and forward climbs up while back climbs down. Stop
halfway, reverse, step off at the bottom, step over the edge at the top, or jump
off whenever you like.

It uses **the game's own ladder pieces**. There is no new ladder in the build
menu, no new recipe, and nothing of this mod's in your world:

- `wood_stepladder` — the wooden step ladder you already build;
- the grausten stone ladder;
- and anything else in the game that is built as a ladder and measures like one.

### What changed about vanilla ladders

Only one thing: **the Use key climbs instead of teleporting.** Everything else
about a ladder is the game's — its recipe, its build menu entry, its damage
states, repairing it, ward rules, its supports, and how it is saved. Nothing is
written into your world, no ladder is reserved, moved or changed by climbing it,
and **uninstalling Concerned Foreman leaves every ladder you built standing** —
they simply go back to teleporting.

The hammer is untouched. Placement, rotation and snapping are exactly as they
were.

### Turning it off

One line, and the game is the game again:

```
[Ladders]
Enabled = false          # ladders teleport again, and nothing is patched at all
```

With `Enabled = false` when the game starts, Concerned Foreman patches nothing:
no motor hook, no interaction hook. Ladders behave as if the mod were not
installed.

If you like climbing but want the old teleport back on the Use key:

```
[Ladders]
UseTeleport = true       # Use teleports, as vanilla; walking into a ladder still climbs
AutoMount = false        # ...and if you want neither, this stops walking into one from climbing
```

The rest of the section, none of which you have to touch:

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Climb instead of teleport. |
| `AutoMount` | `true` | Walking into a ladder starts the climb. Off means Use only. |
| `ClimbSpeed` | `1.0` | Multiplier on the climb speed (0.25–3). |
| `StaminaCost` | `0` | Stamina per second of climbing. Zero, because Valheim charges nothing for its own stairs. |
| `UseTeleport` | `false` | Give the Use key back to vanilla's teleport. |
| `NpcClimbing` | `false` | For settlement workers later. Nothing reads it in this build. |

### What it will not do yet, honestly

**None of this has been watched working in a real game.** It is built, it is
tested where a test can reach, and it is unproven. Everything below is a known
limitation of *this* build, not a plan:

- **The climbing animation is not a hand-made climbing animation.** Valheim has
  no climb clip and this mod ships no game art, so the pose is built from the
  game's own wall-running lean. It may not look right to you yet.
- **Tall runs are not proven.** Whether two vanilla ladder pieces snap end to end
  into one long ladder has not been observed. If they do not, each piece is its
  own climb and you will stop and re-grab at each join.
- **Multiplayer is untested.** The design sends nothing and reserves nothing, so
  the worst case should be cosmetic for onlookers — but nobody has watched two
  players on one ladder.
- **No workers climb.** `NpcClimbing` exists, is off, and nothing reads it.
- **A ladder on a moving ship ends the climb** the moment the ship moves. That is
  the safe direction, not the right one, and it is a known gap.
- **Jumping off is a let go, not a push.** You drop where you are.
- **In a very dense build** a ladder can occasionally go unnoticed from an
  awkward angle; step towards it and it will not.

If a climb ever goes wrong, it ends the same way every time: your character gets
gravity, control and collisions back and falls normally. Being stuck on a ladder
is the one outcome the design does not allow.

---

## The settlement worker

### The settlement runtime is off by default

Concerned Foreman's promise is read-only building diagnostics, and that half is
client-safe. The settlement runtime is the other half: it acts on shared world
state, so **installing this mod never enrols you in it**. Turn it on in the
config, deliberately, or it does nothing.

```
[Settlement]
SettlementRuntimeEnabled = false   # <- you have to change this
```

### Trying it

With the runtime on, in a **disposable test world** you do not mind losing:

| Command | What it does |
|---|---|
| `cf_worker status` | What the runtime thinks: authority, worker, path-request count |
| `cf_worker spawn` | Puts one worker in front of you |
| `cf_worker goto <x> <z>` | Sends it to a point |
| `cf_worker stop` | Cancels the order |
| `cf_worker despawn` | Removes the worker |

After `spawn` and before any `goto`, the worker should stand completely still.
That is the interesting part, not the walking.

---

## What the worker will not do

- **No offscreen work.** The worker acts only in loaded ground, and tells you so
  rather than pretending otherwise.
- **Solo and local host only.** On a dedicated server the runtime refuses to act
  rather than taking ownership it was not granted.
- **No combat, no aggravation, no loot, no faction behaviour.**
- **It refuses rather than guessing.** A goal it cannot reach, cannot reach
  safely, or is not allowed to act on is declined with a reason — and then it
  stops asking, instead of retrying forever.

---

## Compatibility

- Valheim **1.0.12**, BepInEx 5.4.2333, Jötunn 2.29.2.
- Independent of every other Concerned Cat mod. Foreman, Cartographer and
  Teamster share no assembly, no data and no load order; install any of them
  alone.

## Support

Issues and source: <https://github.com/Weakened/ConcernedCatMods>

Free and donation-supported. No paid features, ever.
