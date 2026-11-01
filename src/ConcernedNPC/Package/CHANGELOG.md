# Changelog

## 0.5.0

Six additive changes share this version:
* a worker can rest as a presentation figure (#295, #298, Concerned Foreman's Thorstein);
* a job can say that doing a target leaves the NPC carrying something (#381, Concerned Teamster's Gunnar);
* that figure can be dressed by the one dresser, lifted here from Foreman (#295) so Sunniva and Gunnar share it;
* a worker body can wear its character's own hair and beard, locally (#295 S6, owner decision 2026-10-29);
* one shared companion keepsake, the small local object that starts an introduction (#295 S2, owner decision
  2026-10-30);
* a presentation figure can be talked to with the use key (#295 S3).

### A worker may rest as a presentation figure (#295, #298)

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

### A target may leave the NPC carrying something (#381)

* `JobTarget` gains an additive constructor taking `carries`: the units doing that target leaves the NPC holding - a
  loose stone picked up, a branch gathered. Every existing constructor call means exactly what it meant before
  (`carries` is zero), so no consumer changes behaviour by upgrading.
* The tour partitioner fills a trip with what a target leaves him carrying exactly as it fills one with what a trip
  fetches. Before this, a collecting job's targets cost a trip nothing, so a whole area of loose stones came back as
  one trip whatever the NPC could carry. It now comes back as the fewest loads that fit, never one target a trip, and
  `PlannedStep.Tour` says which load each stop belongs to.
* The library still never learns what was gathered or where it goes: what happens at the end of a load is the role's.
* A trip topped up from below its first target's priority band now takes the highest band that still fits before
  the nearest (#382). Targets that all share one priority - as Concerned Teamster's collection does - are partitioned
  exactly as before; a role that packs its own trips by priority, as Concerned Steward now does, gets those trips back
  instead of one trip's target pushed out by a nearer, less important one.

### A figure can be dressed, and the one dresser lives here (#295, lifted from Concerned Foreman)

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
* Not observed in game: neither the second body nor the token key. Thorstein's look is unchanged by the move. About
  five log lines are now worded for any companion rather than for him.
* `Apply` runs once per dresser. A second call would have attached a second set of pieces. If the source's body list
  cannot be read, the figure is still built, with the first body. The palette's interval and give-up are measured in
  real time, so two plugins polling it do not age it twice as fast.

### A worker body may wear its character's own hair and beard (#295 S6, owner decision 2026-10-29)

* **`Body.Dressing.NpcWorkerDresser`** puts a character's player hair and beard pieces on that role's own worker
  body, as local children under its head joint, with the same pieces and checks as the figure dresser.
* **It changes nothing else.** The worker prefab, its saved data and its network object are untouched. The body's
  renderer and material are left alone too; only the pieces are tinted.
* **Plain look on failure.** A piece that will not attach, or is not drawn where it was put on that rig, is taken
  off, and the body keeps its plain look.
* **It dresses only the right body.** `TryDress` refuses any object that is not the registered role's own saved
  worker body: the saved prefab and the saved identity must both match. The validator pins that this guard runs
  first.
* **One dressing for both.** The dressing is now one internal type, `NpcDressing`, used by both dressers. The figure
  dresser keeps everything it did before, and a worker body never gets a body swap, a skin tint or garments.
* Recorded as `docs/settlement/cart-and-collection/DECISIONS.md` **D18**. **Not observed in game.**

### One shared companion keepsake (#295 S2, owner decision 2026-10-30)

* `Body.Keepsakes.NpcKeepsake` places the small object a player examines to start a companion's introduction.
  It is lifted from Concerned Cartographer's Broken Compass so companions can share one copy; Concerned Foreman's
  carpenter's square uses it. Gunnar's buckle (#457) and Sunniva's flint have not moved to it yet. Hulgi's own compass predates the library and stays in Cartographer.
* The role supplies `Keepsakes.NpcKeepsakeSpec`: the name, the verb, the game prefabs whose look it borrows, and a
  scale.
* **Local in the strongest sense.**
  * No network view, so it is never saved or replicated, and a peer without the mod sees nothing.
  * Not an item. Examining it calls the role back and touches no inventory.
  * Its collider is on a layer characters walk through, or is a trigger, so it never blocks anybody.
* **The game prefab is never woken.** It is cloned under an inactive holder only to read its meshes, and destroyed
  in the same method. The keepsake is a fresh object holding those meshes by reference, with shared materials only.
* **Pinned like the dresser.** `tools/validate_repo.py` pins its engine writes in the NPC census. A new
  "NPC keepsake audit" requires its helpers and behaviour to stay internal, confines the helpers to `NpcKeepsake`,
  pins its public surface, refuses any network view in its folder, and refuses a companion product that builds its
  own render-only keepsake. **Not observed in game.**

### A presentation figure can be talked to (#295 S3)

* `NpcFigureDresser.EnableTalk(name, verb, onTalk, log)` lets the player talk to a figure with the use key. It
  adds a small child to the dresser's own figure: a body-sized capsule on a layer characters walk through (or a
  trigger), with the game's own hover and use through the internal `NpcFigureTalk`. Using it calls the role back.
  Nothing in the world changes, and the point goes when the figure is taken down.
* Its engine writes are pinned in the NPC census; `NpcFigureTalk` is internal and confined to `Body/Dressing`;
  `Action` joins the dresser's allowed parameter types for the callback. **Not observed in game.**

### Versioning

* An additive minor version that widens the public surface by four members: `NpcBodyContract.ForWorkerWithPresentation`,
  `NpcBodyContract.Admits`, one `JobTarget` constructor and the `JobTarget.Carries` property. It also adds the
  dressing types: `Appearance.*` and `Body.Dressing.NpcFigureDresser` / `NpcWorkerDresser` /
  `NpcCustomizationPalette`, and the keepsake types: `Keepsakes.NpcKeepsakeSpec` and `Body.Keepsakes.NpcKeepsake`.
  A role that does not
  use them is unaffected. Every in-repository consumer pin moves with it, so no product can compile against one
  contract and install another.

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
