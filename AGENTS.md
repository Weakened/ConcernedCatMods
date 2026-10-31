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
- Teamster: preserve vanilla cart mass and physics by default. No zero-weight defaults, cart teleports, recovery cheats, stamina bypass, pathfinding, world-save mutation, or server-authority takeover. Behavior-mutating features must be explicit, reversible, fail-closed, and authorized by their own issue. Scoped carve-out (#313/#314): the opt-in Gunnar worker runtime may pathfind his own body, rely on host-held cart ownership, and attach/detach a player-assigned cart through vanilla `Vagon.AttachTo`/`Detach`; it never teleports carts, changes cart mass or physics, writes forces or velocities, bypasses stamina, toggles the brake, or writes mod data into a vanilla object. Two further scoped carve-outs (owner decision 2026-09-29, `docs/settlement/cart-and-collection/DECISIONS.md` D15/D16): the collection runtime may move items out of Gunnar's own inventory into a vanilla `Container` the player explicitly marked `Deposit` or `Both`, measured on both sides with the remainder left in him and a fault recorded uncertain; and a **Teamster** worker body (Gunnar) that **genuinely dies** may return what it carries through vanilla's own item drop - on its own death and on no other lifecycle event whatever, never a retirement, a cancellation, a despawn, a zone unload, a logout, a world change or a plugin teardown. Each lives in one validator-pinned file and grants nothing wider. D15 and D16 are implemented and pinned (D16 fails closed, owner confirmation 2026-10-28). D17 (2026-10-28): the own-body pathfinding carve-out also covers his on-foot collection tour (loads of up to 225 units, at most 24 sources a trip; a small load is not refused); a cart-assisted round needs its own grant. **Decided 2026-10-31 by the owner, not yet implemented** (each gets its own pinned file, off-by-default switch and plants when it is built; until then none is authorized in code. Today the validator already refuses Teamster's chest take, any Teamster `.Damage(`, and any Teamster inventory call outside the D15/D16 pins - though D15's deposit can already reach a cart-mounted chest the player marked `Deposit` - but it does **not** yet refuse `SetTrigger` in Teamster, or `.Damage(`/`TreeBase`/`TreeLog` in Foreman; those refusals must be added with or before each implementation): Gunnar may take from a chest the player explicitly marked `Take`/`Both` (measured, the remainder left in the chest, pinned like D15); his collection tour may load his own picked items into his explicitly assigned cart's container (measured); he may play the idle hammer animation through `ZSyncAnimation.SetTrigger` (cosmetic, a test proves no state change); and Thorstein (#282) - the same felling decision also covers Gunnar (#381), whose scope is not yet set - may fell small trees - `Destructible.Damage` on the exact allowlist `Beech_small1`, `Beech_small2`, `FirTree_small`, `FirTree_small_dead`, with the issued axe, inside an explicit work area, at least 15 m from the player and player-built pieces, picking up only the drops his own call produced. `TreeBase.Damage`/`TreeLog.Damage` (full-size trees) were also approved but stay unused and refused until a mature-tree scope is chosen.

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
