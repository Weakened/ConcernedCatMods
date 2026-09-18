# Concerned Steward

A settlement steward who keeps your fires burning.

This is an **early slice**. It does one job — tending the fires you marked,
with wood from the chest you marked — and it does that job honestly rather
than doing many jobs approximately.

## What he actually does

1. You mark a settlement area and one supply chest.
2. You recruit him.
3. He looks at the fires inside your settlement area, picks the one closest to
   going out, walks to your chest, takes real wood out of it, walks to the
   fire, and puts the wood in — one piece at a time, through the game's own
   "use item on fire" path, the same one your own character uses.
4. Anything he did not burn, he walks back and puts in the chest.

He walks the whole way. He is never teleported, never shoved, and his position
is never written by this mod — he moves with Valheim's own motor and its own
pathfinding, like any other creature.

## What he will not do

- **He will not create wood.** Every piece of wood he puts on a fire came out
  of your chest first, and that was measured, not assumed. Valheim has two
  methods that would let a mod set a fire's fuel level directly with no item
  consumed; this mod calls neither, and a test proves their names appear
  nowhere in it.
- **He will not take from any chest but the one you marked.** Not the nearest
  one, not whichever is handy, not one he passes on the way.
- **He will not work outside the area you marked.**
- **He will not touch a fire or a chest that this game session does not own.**
  (If you are curious: on an unowned fire, Valheim's own code takes the wood
  out of your hands and then throws the fuel away. He refuses rather than
  meeting that.)
- **He will not take ownership of anything**, claim a bed, block a doorway,
  fight, carry loot, or open a door.
- **He will not run in multiplayer yet.** He works when you are the host, not
  on a dedicated server, and with nobody else connected. Otherwise he stops and
  says so.

## Turning him on

He is **off by default** and stays off until you turn him on:

```
[Steward] StewardRuntimeEnabled = true
```

Installing the mod changes nothing until you do that.

Then, in the console (F5):

```
cs_steward area 32       mark a settlement area, 32 m around you
cs_steward depot         mark the chest you are looking at as the supply depot
cs_steward recruit       invite him in
cs_steward status        what he is doing, and why he is not doing anything
cs_steward tend on       let him tend fires
cs_steward tend off      stop him
```

`cs_steward status` always explains itself. If he is standing still, it will
tell you which of the checks refused and what to do about it.

## His name

He does not have one yet. He is "the Steward" until he is named, and this build
uses a neutral placeholder on purpose rather than inventing one that would then
be stuck in your save. His internal identity is the role, not the name, so
naming him later will not lose his work.

## Compatibility

Concerned Steward is its own mod. It does not require, reference, or interfere
with Concerned Cartographer, Concerned Teamster or Concerned Foreman, and it
works with any of them installed or none. It is client-side and it makes no
network calls of its own.

## Reporting a problem

Open an issue at
<https://github.com/Weakened/ConcernedCatMods>, and please include the output
of `cs_steward status`.
