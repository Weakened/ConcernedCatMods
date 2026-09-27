# Changelog

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
