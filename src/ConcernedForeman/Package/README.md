# Concerned Foreman

**Point at a building or a station and Foreman explains why it is stable,
exposed, comfortable, or about to fail.**

> **This build is an early spike, not a playable release.** It contains the
> first slice of the opt-in settlement runtime and nothing of the diagnostics
> half yet. Nothing in it has been observed working in a game. If you are
> looking for a mod that does something useful today, this is not yet it.

---

## What is actually in 0.1.0

One thing: **a settlement worker that walks to a point and stops.**

That sounds small, and it is, deliberately. The question it exists to answer is
whether a modded creature can be made to do *only* what it is told — no
wandering off, no reacting to a passing boar, no joining a fight — because every
later piece of a settlement depends on the answer being yes.

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

## What it will not do

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
