# Codex instructions for ConcernedCatMods

## Mission

Ship small, stable, testable Valheim mods. The active implementation target is the **Concerned Companions** epic (#264, issue key `CC-NPC`): Hulgi, the Broken Compass, and the reusable companion foundation in `src/Shared/Companions`. **Concerned Teamster** finished its v1.0 conveyor; **Concerned Cartographer** is in public beta. Open P0/P1 regressions in either shipped product preempt companion work; other changes to either product happen only through that product's issues. Work from an issue and satisfy only that issue's acceptance criteria.

## Read first

Before changing a product, read `docs/NAMING_CONVENTIONS.md`, then that product's documents:

- Concerned Teamster: `docs/mods/concerned-teamster/PROJECT.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`, `AUTONOMOUS_EXECUTION.md`
- Concerned Cartographer: `docs/mods/concerned-cartographer/PROJECT.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`
- the relevant GitHub issue and its Definition of Done

## Hard rules

- Never commit, copy, upload, or package Valheim/Unity/BepInEx/Jötunn binaries other than the mod's own compiled DLL.
- Never expose or request `TCLI_AUTH_TOKEN` in source, logs, prompts, commits, issues, or PRs.
- Never publish to Thunderstore without explicit human approval after the manual release checklist passes.
- Do not change package namespace, package name, plugin GUID, or assembly name without an explicit migration issue.
- Do not work in unrelated mod folders.
- Do not let two agents edit the same working tree at the same time. Parallel agents work one issue per agent in their own worktree; only the lead integrates into `main`, deploys, or operates the game.
- Preserve client-side behavior until a multiplayer-sync design is approved.
- Treat Valheim internal APIs as unstable. Keep them behind narrow adapters and log actionable failures.
- Products never reference each other at compile time; cross-product integration is runtime capability detection only. Code shared between products lives under `src/Shared/<Area>` and is compiled into each consumer as source; it must contain no Unity/BepInEx/Jötunn types and no product namespace.
- Companions: local-only presentation. No networked NPC, ZDO, custom RPC, native save entity, combat, or loot. Feature access is monotonic and never depends on the companion existing; that grant applies to feature access only, never to worker authority or custody.
- Workers: while an identity performs an explicitly ordered job, its body is owned by that product's opted-in worker runtime (inactive prefab clone, vanilla motor, host with no other peers), never alongside its presentation body. A worker body may keep its identity and inventory in its own network object; mod data is never written into a vanilla object (`docs/settlement/cart-and-collection/DECISIONS.md`).
- Steward additionally (owner decision 2026-10-28, `docs/mods/concerned-steward/DECISIONS.md` S1): `cs_steward retire` may remove Sunniva's own worker body (a `CS_Steward` body this game owns) through vanilla `ZNetView.Destroy()`, only when her pack, fuel ledger, loop, durable plan, record and loss ledger all show nothing held or open; no forcing word, no other object, no ownership takeover, no drop. One validator-pinned file (`Runtime/StewardRetirementPort.cs`), proved by planted escapes in `tools/tests/test_steward_retire_audit.py`; its known limits as a text audit are stated in S1. At rest she is a player-style presentation figure, and her worker body may wear her own hair (owner decision 2026-10-29, `docs/settlement/cart-and-collection/DECISIONS.md` D18): presentation and worker body never coexist.
- Teamster: preserve vanilla cart mass and physics by default. No zero-weight defaults, cart teleports, recovery cheats, stamina bypass, pathfinding, world-save mutation, or server-authority takeover. Behavior-mutating features must be explicit, reversible, fail-closed, and authorized by their own issue. Scoped carve-out (#313/#314): the opt-in Gunnar worker runtime may pathfind his own body, rely on host-held cart ownership, and attach/detach a player-assigned cart through vanilla `Vagon.AttachTo`/`Detach`; it never teleports carts, changes cart mass or physics, writes forces or velocities, bypasses stamina, toggles the brake, or writes mod data into a vanilla object. Two further scoped carve-outs (owner decision 2026-09-29, `docs/settlement/cart-and-collection/DECISIONS.md` D15/D16): the collection runtime may move items out of Gunnar's own inventory into a vanilla `Container` the player explicitly marked `Deposit` or `Both`, measured on both sides with the remainder left in him and a fault recorded uncertain; and a **Teamster** worker body (Gunnar) that **genuinely dies** may return what it carries through vanilla's own item drop - on its own death and on no other lifecycle event whatever, never a retirement, a cancellation, a despawn, a zone unload, a logout, a world change or a plugin teardown. Each lives in one validator-pinned file and grants nothing wider. D15 and D16 are implemented and pinned (D16 fails closed, owner confirmation 2026-10-28). D17 (2026-10-28): the own-body pathfinding carve-out also covers his on-foot collection tour (loads of up to 225 units, at most 24 sources a trip; a small load is not refused); a cart-assisted round needs its own grant. **D20, the chest take (owner decision 2026-10-31), is implemented and pinned**: behind its own off-by-default switch `Workers/GunnarChestTakeEnabled` (which also needs `Workers/GunnarCollectionEnabled`), Gunnar may take the one item the player names (`ct_collect source`, then `ct_collect take <item> [count]`) out of a chest the player pointed at and marked `Take`/`Both`, into his own inventory, standing at it, bounded by the D17 carry limit, measured on both sides, only what arrived counted, the remainder left in the chest, a fault halfway recorded uncertain and never retried blind. It uses D15's three calls with the roles exchanged, in `Adapters/Workers/GunnarTakePort.cs` alone, pinned by the scope audit and `#381 chest-take audit`, with planted escapes in `tools/tests/test_teamster_take_audit.py`; every gate is re-asked in the frame of the move. Never observed in game. **D21, the idle hammer (owner decision 2026-10-31), is implemented and pinned**: behind its own off-by-default switch `Workers/GunnarIdleHammerEnabled` (effective only while `Workers/GunnarHaulingEnabled` is on), while idle beside his assigned, unhitched cart Gunnar may walk to it and face it with his own body's vanilla motor and play the vanilla Hammer's own swing through one `ZSyncAnimation.SetTrigger` on his own body, in `Adapters/Workers/GunnarIdleHammerPort.cs` alone; `.SetTrigger(` is refused everywhere else in Teamster and its shared trees (`D21 idle-hammer audit`, plants `IdleHammerIsCosmetic`), and `CartUpkeepDriverTests` prove an hour of it changes no state. Whether the Dverger body can play that trigger is not established; never observed in game. **Decided 2026-10-31 by the owner; everything here except Thorstein's felling (built below), Gunnar's chest take (D20, above) and Gunnar's idle hammer (D21, above) is not yet implemented** (each gets its own pinned file, off-by-default switch and plants when it is built; until then none is authorized in code. Today the validator already refuses any Teamster `.Damage(`, and any Teamster inventory call outside the pinned collection, D15, D16 and D20 calls - though D15's deposit can already reach a cart-mounted chest the player marked `Deposit`): his collection tour may load his own picked items into his explicitly assigned cart's container (measured); and the small-tree felling decision (D19) also covers Gunnar (#381), whose scope is not yet set. **Thorstein's half of D19 is built (#282, #296):** he fells only `Beech_small1`, `Beech_small2`, `FirTree_small` and `FirTree_small_dead`, through `Destructible.Damage` with his issued axe, inside an explicit work area (never the default camp circle), at least 15 m from players and player-built pieces, behind `Collection/ThorsteinFellsSmallTrees` (off by default) - one hit per `cf_collect fell`, and inside a collection order one hit per swing, only after the loose branches are gone - and takes only the Wood his own hit dropped, by instance id in the same call, never more than the order still needs; the rest stays on the ground. The one hit is pinned in `Runtime/Collection/WorldFellingPort.cs` and reached only from the command and the order's strike in `WorldSourcePickupPort.cs` (the D19 felling audit and the #282 strike audit, with plants); never observed in game. `TreeBase.Damage`/`TreeLog.Damage` (full-size trees) were also approved but stay unused and unauthorized in code until a mature-tree scope is chosen (refused today in Teamster through `.Damage(`, and in all of Foreman by name).

## Required workflow

1. Create or use a dedicated branch: `feat/<issue>-<slug>`, `fix/<issue>-<slug>`, or `chore/<issue>-<slug>` (issue keys: `cc-###` Cartographer, `ct-###` Teamster).
2. Make the smallest coherent change.
3. Run `pwsh ./scripts/build.ps1` when the local game dependencies are available.
4. Run `python ./tools/validate_repo.py` on every machine.
5. Record manual game-test evidence in the PR template.
6. Summarize changed files, commands run, results, and unresolved assumptions.

## Review priorities

1. Game startup and world safety
2. Cross-world data isolation
3. Lifecycle across login/logout/world switch (map overlays; cart telemetry and panels)
4. Performance and allocations in `Update`/sampling paths
5. Compatibility (Cartographer: Pinnacle and MapRoutes; Teamster: researched cart-mod targets, no invented names)
6. Package correctness and version synchronization
7. Teamster only: vanilla physics preservation and fail-closed mutation paths

## Versioning

Thunderstore accepts numeric `Major.Minor.Patch` versions. Use namespaced Git tags such as `concerned-cartographer/v0.1.0` and `concerned-teamster/v0.1.0`. Each product versions and releases independently.
