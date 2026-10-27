# Changelog

## 0.5.0

A job can now say that doing a target leaves the NPC carrying something, so a collecting job is planned in loads
(#381, Concerned Teamster's Gunnar).

* `JobTarget` gains an additive constructor taking `carries`: the units doing that target leaves the NPC holding - a
  loose stone picked up, a branch gathered. Every existing constructor call means exactly what it meant before
  (`carries` is zero), so no consumer changes behaviour by upgrading.
* The tour partitioner fills a trip with what a target leaves him carrying exactly as it fills one with what a trip
  fetches. Before this, a collecting job's targets cost a trip nothing, so a whole area of loose stones came back as
  one trip whatever the NPC could carry. It now comes back as the fewest loads that fit, never one target a trip, and
  `PlannedStep.Tour` says which load each stop belongs to.
* The library still never learns what was gathered or where it goes: what happens at the end of a load is the role's.
* An additive minor version that widens the public surface by one constructor and one property. Every in-repository
  consumer pin moves with it, so no product can compile against one contract and install another.

## 0.3.0

The durable interruption machinery becomes an actual consumer API and Concerned Steward adopts it around its shipped
maintenance loop (#379).

* A role can now provide its own plan codec and absolute path, open an atomic plan journal, write a plan through
  `NpcPlanRun`, and reconstruct it through the shared conservative interruption policy.
* The immutable reservation and material values carried by a plan cross the package boundary. Reservation books,
  custody ledgers, transfer executors, paths, row tags, and schemas remain private to their owners.
* This additive minor version deliberately widens the public surface. Every in-repository consumer pin moves with it
  so no product can compile against one contract and install another.
* The library still registers no role, names no file, patches nothing, and mutates no world object on its own.

## 0.2.0

The public surface gains two types, so that a player's container permissions can be reached at all (#374).

* `NpcContainerDesk` and `NpcContainerDecision`: which containers a player has opened to NPCs, the cycle a key press
  walks through, and the records a consuming mod persists. Four states — OFF, TAKE, DEPOSIT, BOTH — with **OFF the
  default**, and off is the absence of a record, so an empty or unreadable store means "nothing is enabled" rather than
  "nothing is known".
* **Why this is a version of its own.** The permission model was already complete and tested in 0.1.0, and reachable by
  nothing: every type in its namespace was internal, no mod imported it, and no player could set a permission. "Off by
  default" was therefore a statement about dead code. A consuming mod that uses the desk needs this version, which is
  what the storefront pin now says.
* It is a **facade**, and what it withholds is deliberate: the permit a transfer spends stays unforgeable from outside
  the library, and so do the gate, the assignment, the transfer recorder and the place-matching rule. A mod does not
  need to mint a permit; it needs to know what the player allowed, to change it, and to write it down.
* Nothing else changed. No new patch, prefab, command, world state or file of its own — the library still names no file
  and writes nothing, and the consuming mod owns the spelling on disk.

## 0.1.0

First package. It carries the runtime contracts the Concerned Cat companions are being moved onto, and nothing else:
no patch, no prefab, no command, no world state, no file of its own. Installed by itself it loads, says so, and stops.

* The package itself, as a new kind of thing in this repository: a **library**, which several mods may reference,
  rather than a **product**, which never references another. The rules that make that safe are enforced by the
  repository validator rather than left to a comment. A consuming mod references it with the library's DLL kept out
  of its own package, pins it on the storefront, and declares a hard dependency on it, so a missing library is a clear
  message at load instead of a failure later.
* It depends on no Concerned Cat mod, and the validator fails the build if it ever does.

Nothing in this release has been observed in game.
