# Codex instructions for ConcernedCatMods

## Mission

Ship small, stable, testable Valheim mods. The current priority is **Concerned Cartographer**. Work from an issue and satisfy only that issue's acceptance criteria.

## Read first

Before changing Concerned Cartographer, read:

1. `docs/NAMING_CONVENTIONS.md`
2. `docs/mods/concerned-cartographer/PROJECT.md`
3. `docs/mods/concerned-cartographer/ARCHITECTURE.md`
4. `docs/mods/concerned-cartographer/TEST_PLAN.md`
5. the relevant GitHub issue and its Definition of Done

## Hard rules

- Never commit, copy, upload, or package Valheim/Unity/BepInEx/Jötunn binaries other than the mod's own compiled DLL.
- Never expose or request `TCLI_AUTH_TOKEN` in source, logs, prompts, commits, issues, or PRs.
- Never publish to Thunderstore without explicit human approval after the manual release checklist passes.
- Do not change package namespace, package name, plugin GUID, or assembly name without an explicit migration issue.
- Do not work in unrelated mod folders.
- Do not let two agents edit the same working tree at the same time.
- Preserve client-side behavior until a multiplayer-sync design is approved.
- Treat Valheim internal APIs as unstable. Keep them behind narrow adapters and log actionable failures.

## Required workflow

1. Create or use a dedicated branch: `feat/<issue>-<slug>`, `fix/<issue>-<slug>`, or `chore/<issue>-<slug>`.
2. Make the smallest coherent change.
3. Run `pwsh ./scripts/build.ps1` when the local game dependencies are available.
4. Run `python ./tools/validate_repo.py` on every machine.
5. Record manual game-test evidence in the PR template.
6. Summarize changed files, commands run, results, and unresolved assumptions.

## Review priorities

1. Game startup and world safety
2. Cross-world data isolation
3. Overlay lifecycle across login/logout
4. Performance and allocations in `Update`
5. Compatibility with Pinnacle and MapRoutes
6. Package correctness and version synchronization

## Versioning

Thunderstore accepts numeric `Major.Minor.Patch` versions. Use namespaced Git tags such as `concerned-cartographer/v0.1.0`.
