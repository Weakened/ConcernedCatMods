# Concerned Foreman

**Point at a building or a station and Foreman explains why it is stable,
exposed, comfortable, or about to fail.**

> **This build is an early spike, not a playable release.** It contains
> the first slice of the opt-in settlement runtime, an older ladder climb that is
> **off by default**, and nothing of the diagnostics half yet. **Nothing in it has
> been observed working in a game.** If you are looking for a mod that does
> something useful today, this is not yet it.

---

## What is actually in 0.1.0

**A settlement worker that walks to a point and stops**, and an older, opt-in
ladder climb.

The worker sounds small, and it is, deliberately. The question it exists to
answer is whether a modded creature can be made to do *only* what it is told —
no wandering off, no reacting to a passing boar, no joining a fight — because
every later piece of a settlement depends on the answer being yes.

---

## Ladders: use Concerned Ladders

**Climbable ladders are a separate mod now: Concerned Ladders**
(`TheConcernedCat-ConcernedLadders` on Thunderstore). Install that if you want to
climb ladders.

Concerned Foreman still contains its older ladder climb, but it is **off by
default** and Foreman patches no ladder unless you turn it on. Leave it off if
you use Concerned Ladders: **Concerned Ladders will not run while Foreman's
climb is switched on.** At the defaults, both mods can be installed together and
Concerned Ladders' ladders are the ones you climb.

Concerned Foreman has no build-time dependency on Concerned Ladders. Neither mod
needs the other.

### Turning Foreman's climb on anyway

Only if you do **not** use Concerned Ladders and want Foreman's climb on the
game's own `wood_stepladder` and grausten ladder:

```
[Ladders]
PlayerClimbing = true    # off by default; Concerned Ladders stops working while this is on
```

An early development build of Foreman used a setting called `Enabled` here and
wrote `Enabled = true` into its config file. That value **no longer turns the climb
on**, so an old config stays off. `Enabled` is still there for one job:
**`Enabled = false` switches the climb off** whatever `PlayerClimbing` says. That is
the setting Concerned Ladders tells you to change if it finds Foreman's climb
running.

| Setting | Default | What it does |
|---|---|---|
| `PlayerClimbing` | `false` | Foreman's climb instead of vanilla's teleport. Off: nothing is patched at all. |
| `Enabled` | `true` | Off switch only: `false` turns the climb off whatever `PlayerClimbing` says. |
| `AutoMount` | `true` | With the climb on, walking into a ladder starts it. Off means Use only. |
| `ClimbSpeed` | `1.0` | Multiplier on the climb speed (0.25–3). |
| `StaminaCost` | `0` | Stamina per second of climbing. |
| `UseTeleport` | `false` | Give the Use key back to vanilla's teleport. |
| `NpcClimbing` | `false` | For settlement workers later. Nothing reads it in this build. |

None of Foreman's climb has been watched working in a real game, and it will not
be developed further: player climbing belongs to Concerned Ladders.

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
- **Requires Concerned NPC**, which the storefront installs for you. It is a
  shared runtime rather than a mod with features of its own: it ships no
  gameplay, and it is what makes "one body per settlement worker" a rule the
  whole game agrees on instead of a rule each of our mods keeps privately. If
  it is missing, Concerned Foreman says so in one line at startup rather than
  failing later.
- Independent of every other Concerned Cat **mod**. Foreman, Cartographer,
  Teamster and Steward share no gameplay, no data and no load order; install
  any of them alone. Foreman, Teamster and Steward each ask for Concerned NPC,
  and if you have more than one of them it is still installed only once.
  Cartographer does not use it at all.

## Support

Issues and source: <https://github.com/Weakened/ConcernedCatMods>

Free and donation-supported. No paid features, ever.
