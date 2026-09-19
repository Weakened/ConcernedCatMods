# Concerned NPC

**The shared runtime behind Concerned Cat companions.**

This package is not a mod you install for what it does. On its own it does nothing at all: it patches nothing,
registers nothing, spawns nothing, reads nothing and writes nothing. It loads, says so once in the log, and stops.

It is here so the Concerned Cat companions can share one set of answers to the questions that are the same for every
one of them, and so that a fix to those answers can reach you without every mod being rebuilt.

## What it answers, on behalf of the mods that use it

* **Who am I after a reload?** One identity, one body, found again the same way every time.
* **Where is camp?** An approximate boundary grown from what you actually built, not from the whole map.
* **Which chests may I use?** Only the ones you marked, for only what you marked them for.
* **Where am I allowed to work?** A work area you set, which refuses honestly when it is empty or gone.
* **What is my job, and in what order?** Look first, plan the whole job, work out what it needs, take it once, walk a
  sensible round, then reconcile. Not one item, one trip, repeat.
* **What happens when I am interrupted?** A dead, logged-out or interrupted companion resumes or reports. Nothing is
  duplicated and nothing quietly disappears.

## What it deliberately does not know

Anything about the roles. It has no idea what a cart is, what resin is for, or why anybody wants a shelter. The mods
decide **what** their companions want to do; this decides **how** it is planned, carried out and recovered.

## Installing

Install it because a Concerned Cat mod asks for it. Your mod manager will do that for you. If you install one of those
mods without this package, BepInEx will tell you so at load rather than leaving you with a mod that half works.

## Privacy and safety

No telemetry, no network calls, no analytics. It reads and writes nothing of its own; the mods that use it own their
own data, in their own files, which remain yours to delete.

## Support

Issues and source: [github.com/Weakened/ConcernedCatMods](https://github.com/Weakened/ConcernedCatMods)
