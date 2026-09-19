# Claude Code instructions for ConcernedCatMods

You are working in a Valheim mod monorepo with multiple independent products. The active implementation target is the **Concerned Companions** epic (#264, issue key `CC-NPC`, label `mod:cartographer`): Hulgi, the Broken Compass, and the reusable companion foundation in `src/Shared/Companions`. **Concerned Teamster** completed its v1.0 conveyor and is not the active target; **Concerned Cartographer** is in public beta. Open P0/P1 regressions in either shipped product preempt companion work, and other work in either product proceeds only through its own issues.

## Operating mode

- Begin by reading `docs/NAMING_CONVENTIONS.md`, then the project, architecture, test-plan, and execution documents for the product you are working on under `docs/mods/concerned-teamster/` or `docs/mods/concerned-cartographer/`.
- Companion work follows epic #264: take the lowest-numbered open unblocked `CC-NPC` leaf, one issue per branch/PR, close with evidence, continue immediately.
- Teamster conveyor work, if it resumes, follows `docs/mods/concerned-teamster/AUTONOMOUS_EXECUTION.md`: lowest-numbered open unblocked CT leaf, one issue per branch/PR, evidence-commented closure, continue immediately.
- Work on exactly one GitHub issue at a time. When the owner asks for parallel agents, that means one issue per agent and one agent per worktree; only the lead integrates into `main`, deploys, or operates the game (`docs/settlement/cart-and-collection/DECISIONS.md` D14).
- Prefer a vertical slice that can be manually proved in game over a broad speculative implementation.
- Keep changes small enough for an independent Codex review.
- Explain uncertainty around Valheim internals instead of inventing APIs.

## Non-negotiable safety

- Never commit or distribute game DLLs, publicized assemblies, profile folders, saves, world files, or tokens.
- Never invoke `tcli publish` or create a public release without explicit human approval.
- Never modify the user's real world files. Test in a disposable world and a dedicated mod-manager profile.
- Do not silently weaken validation or remove acceptance criteria to make a task appear complete.
- Companions additionally: local-only presentation. No networked NPC, ZDO creation, custom RPC, native save entity, collision blocking, enemy targeting, combat, loot, or storage. The single owner-approved exception (2026-09-16, widened the same day by the owner's door-permission rule): a companion may open a vanilla door to go into or out of a building and close it behind him, through the door's own `UseDoor` RPC, only for a door the local player has allowed companions to use (per door, off by default, or every door under the `Companions/DoorAccess = AllDoors` setting) and could open themselves (no keyed or can't-close doors, guard-stone access respected). A door companions may not use is never opened or walked through, open or shut. Which doors are allowed is a local setting kept beside the companion data, never written to the world. Sleeping in a spare bed is a local pose only: no claim, no attach, no change to the bed. Nothing else in the world is changed. Never instantiate a live `Player`/`BaseAI` and strip components afterwards. Feature access is monotonic and independent of the companion existing: ambiguous evidence grants, and nothing (a lost bed, a death, a hidden companion, a corrupt sidecar) ever revokes it. "Ambiguous evidence grants" applies to feature access only: worker authority, recruitment into a worker runtime, and resource or cart custody fail closed.
- Workers (owner brief 2026-09-17, `docs/settlement/cart-and-collection/DECISIONS.md` D1, D3, D9, D14): the companion rules above govern presentation. While an identity performs an explicitly ordered job, its body is instead owned by that product's opted-in worker runtime (Foreman: `docs/mods/concerned-foreman/SETTLEMENT_AUTHORITY.md`; Teamster: the Gunnar decisions), built from an inactive prefab clone and moved only through the vanilla motor. The presentation body and the worker body never coexist. Work runs only when opted in, as the host, not dedicated, with no other peers connected; otherwise it refuses and every existing utility keeps working. A worker body may keep its identity and its own inventory (carried materials, issued tools) in its own network object; mod data is never written into a vanilla object.
- Teamster additionally: preserve vanilla cart mass/physics by default; no zero-weight defaults, cart teleports, recovery cheats, stamina bypass, pathfinding, world-save mutation, or server-authority takeover. Mutating conveniences must be explicit, reversible, fail-closed, and authorized by their own issue. No compile-time dependency between Teamster and Cartographer. The single scoped carve-out (owner brief 2026-09-17, #313/#314): the Gunnar worker runtime, off by default under its own `TeamsterFeature`, may pathfind **his own body**, rely on cart ownership the host already holds, and attach and detach a player-assigned cart through vanilla's own `Vagon.AttachTo`/`Detach`. It still never teleports a cart, changes cart mass or physics defaults, writes forces or velocities, bypasses stamina, toggles the parking brake, or writes mod data into a vanilla object. "No pathfinding" continues to mean no autopilot for the player's own cart.

  A **second scoped carve-out** (owner decision 2026-09-19, #381): Gunnar's collection role may pick up
  loose branches and stones through vanilla's own `Pickable.Interact`, in the single file
  `Adapters/Workers/GunnarCollectionPort.cs` and nowhere else, failing closed. `validate_repo.py`
  permits **one pinned call** in that one file - matched verbatim, so `.Interact(` on a cart, a
  container or a door still fails there - and refuses the token everywhere else, inside
  `Adapters/Workers` and out. Every other forbidden token still fails inside the authorized file.
  Proved by `tools/tests/test_teamster_carveout.py`, which plants each escape an independent review
  found and requires the validator to refuse; all six fail against the unfixed validator.

  **Not yet reachable, and the docs must not imply otherwise.** There is no collection
  `TeamsterFeature`: the port and its job have no call site, and the `featureEnabled` argument is
  supplied by a caller that does not exist. The slice is inert. When it is wired, it goes behind an
  off-by-default feature like the first carve-out, and that is when "a player who has not opted in gets
  none of it" becomes a statement about behaviour rather than about dead code. The port needs no RPC of its own - `Pickable.Interact` runs `RPC_Pick` and the
  ownership claim inside vanilla, on a pickable this process already owns - so felling a tree
  (`TreeBase.Damage`) and the cosmetic hammer animation (`ZSyncAnimation.SetTrigger`) were **not**
  authorized and each needs its own owner decision. Ownership takeover, teleports, forces, cart
  interaction and arbitrary RPC are untouched.

## Local commands

```powershell
pwsh ./scripts/bootstrap.ps1
pwsh ./scripts/verify.ps1      # the gate: solution build, every test, validator
pwsh ./scripts/build.ps1
pwsh ./scripts/deploy.ps1
python ./tools/validate_repo.py
pwsh ./scripts/package.ps1
```

**Build evidence comes from `scripts/verify.ps1` and nothing else.** `dotnet test
<sln>` does **not** build non-test product projects, so a green solution-wide
test run can be true over product assemblies that do not compile — that is how a
red `main` shipped at `694a6e3` (#360). CI cannot close it either: every step in
`repo-checks.yml` is `dotnet test` against a test project, because the products
reference the licensed game assemblies no runner has. Quote verify's summary
block; never quote a bare `dotnet test`.

A build may be impossible on a machine without the user's licensed Valheim installation and the configured BepInEx profile. In that case, complete static checks, clearly report the missing dependency, and do not claim the build passed. `verify.ps1` refuses to run there rather than degrading to a green that means less than it looks.

## Completion report

Always report:

- issue addressed;
- files changed;
- build/static checks run and exact outcomes;
- manual test steps still required;
- assumptions or compatibility risks.
