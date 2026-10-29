# Changelog

## 0.5.0

A worker can rest as a presentation figure, under the same one-body rule (#295, #298).

* `NpcBodyContract.ForWorkerWithPresentation(prefab, keyPrefix)` and `NpcBodyContract.Admits(kind)` are new. The
  contract is still a worker contract: the same prefab name and key prefix, validated the same way. A shipped role
  that switches to it therefore changes nothing on disk, and its saved bodies are found exactly as before.
* The arbiter now grants such an identity either kind of body, but **never both at once**. A presentation claim is
  refused while a worker body is held, and a worker claim is refused while the figure is held
  (`RefusedOtherKindExists`). A presentation claim also still needs the identity to be resting. A hand-over is
  release-then-claim by whoever holds the body now. Nothing in the library tears a body down on anyone else's behalf.
* A role registered with `ForWorker` or `ForPresentation` behaves exactly as in 0.4.0.
* Stated limit: "resting only" is checked when the figure is claimed. A job entering the identity's mode while a
  figure's lease is held is not refused by the library; the role holding the figure must take it down when its
  identity stops resting. Concerned Foreman does so on its next pass (at most half a second).
* This is an additive minor version that widens the public surface by two members. Every in-repository consumer pin
  moves with it, so no product can compile against one contract and install another.

A figure can be dressed, and the one dresser lives here (#295, lifted from Concerned Foreman).

* `Body.Dressing.NpcFigureDresser` dresses an extracted figure from an `Appearance.NpcLookSpec`: which body it
  wears, hair, beard, colouring and garments. It also stands the figure (`StandAt`) and takes it down (`TakeDown`).
  It is Thorstein's dresser from Concerned Foreman, moved rather than copied. Foreman now uses it and keeps no copy,
  and Concerned Steward and Concerned Teamster can dress Sunniva and Gunnar with it.
* What a role asks for is its own data: `NpcLookSpec`, `NpcLookRequest` and `NpcBodyModel`. The rule that resolves
  a request against what the installed game offers is `NpcLookPlan`. Its order is a player's override, the owner's
  label, the label's localization token, a documented prefab id, a shape, the family, then nothing. Slider readings
  are turned into colours by `NpcLookColour`, which is the character screen's own conversion. A slot with no request
  is left alone, which is how a role asks for no beard.
* Two things Thorstein never needed are new:
  * **A second body.** `NpcBodyModel.Female` is the game's model index 1, the one the character screen's toggle
    selects. The dresser puts it on the way the game's equipment component does. The bodies are read from the source
    character's own list before the extraction destroys the source, and `NpcPresentationFigure` carries them, by
    reference only. If they cannot be read, `ModelApplied` stays false and the figure keeps the first body.
  * **A localization token** as a request key (for example `$customization_hair12`). It finds the same choice in
    any language, and it counts as a match.
* `NpcCustomizationPalette` reads the character screen's colour endpoints once per process. Every consumer shares the
  one cache.
* Every public member of the dresser acts on the figure it was built for, never on an object it is handed. A
  consumer's own pinned audits therefore cannot be bypassed through the library. The engine writes are confined to
  one internal file. `tools/validate_repo.py`'s "NPC figure-parts audit" pins every creation, destruction,
  re-parenting, pose write and mesh swap in the whole library by path and exact spelling. A product that grows its
  own dresser is refused by the "#295 one-dresser audit". Concerned Cartographer's Hulgi keeps his own pre-library
  copy, exempted by name.
* Not observed in game: neither the second body nor the token key. Thorstein's look is unchanged by the move, apart
  from the wording of three log lines.
* Still additive. A role that does not use these types is unaffected.

## 0.4.0

The public surface gains the mint and the transfer recorder that Concerned Teamster's Gunnar deposit needs (#415, #374,
D15; `7d0de3d`).

* Four types become public:
  * `NpcContainerPermit`: a permit to use one container. Its constructor stays **private**, and the static
    `Issue(...)` is the only way to get one. `Issue` re-reads the container's own access, so a refused container
    yields no permit.
  * `NpcTransferPlan`: what a move intends.
  * `ContainerMoveOutcome` and `ContainerMoveResult`: one recorder for a move, measured on both sides. Any
    disagreement between the two sides is recorded as Uncertain.
* The rule that a permit cannot be forged did not depend on these types being internal. It depends on the private
  constructor and the single mint. A reflection test and the repository validator's D15 permit-mint audit both check
  that.
* Still withheld: `NpcContainerGate`, `NpcContainerPlace`, the permission book, the sighting and the assignment.
* This is an additive minor version. Every in-repository consumer pin moves with it.

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
