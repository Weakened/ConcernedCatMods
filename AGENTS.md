# Codex instructions for ConcernedCatMods

## Mission

Ship small, stable, testable Valheim mods. The active implementation target is **Concerned Teamster** (issue key `CT`). **Concerned Cartographer** is in public beta: open Cartographer P0/P1 regressions preempt Teamster work; other Cartographer changes happen only through Cartographer issues. Work from an issue and satisfy only that issue's acceptance criteria.

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
- Do not let two agents edit the same working tree at the same time.
- Preserve client-side behavior until a multiplayer-sync design is approved.
- Treat Valheim internal APIs as unstable. Keep them behind narrow adapters and log actionable failures.
- Products never reference each other at compile time; cross-product integration is runtime capability detection only.
- Teamster: preserve vanilla cart mass and physics by default. No zero-weight defaults, cart teleports, recovery cheats, stamina bypass, pathfinding, world-save mutation, or server-authority takeover. Behavior-mutating features must be explicit, reversible, fail-closed, and authorized by their own issue.

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
