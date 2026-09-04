# Claude Code instructions for ConcernedCatMods

You are working in a Valheim mod monorepo with multiple independent products. The active implementation target is **Concerned Teamster** (issue key `CT`, label `mod:teamster`). **Concerned Cartographer** is in public beta; open Cartographer P0/P1 regressions preempt Teamster work, and other Cartographer work proceeds only through its own issues.

## Operating mode

- Begin by reading `docs/NAMING_CONVENTIONS.md`, then the project, architecture, test-plan, and execution documents for the product you are working on under `docs/mods/concerned-teamster/` or `docs/mods/concerned-cartographer/`.
- Teamster conveyor work follows `docs/mods/concerned-teamster/AUTONOMOUS_EXECUTION.md`: lowest-numbered open unblocked CT leaf, one issue per branch/PR, evidence-commented closure, continue immediately.
- Work on exactly one GitHub issue at a time.
- Prefer a vertical slice that can be manually proved in game over a broad speculative implementation.
- Keep changes small enough for an independent Codex review.
- Explain uncertainty around Valheim internals instead of inventing APIs.

## Non-negotiable safety

- Never commit or distribute game DLLs, publicized assemblies, profile folders, saves, world files, or tokens.
- Never invoke `tcli publish` or create a public release without explicit human approval.
- Never modify the user's real world files. Test in a disposable world and a dedicated mod-manager profile.
- Do not silently weaken validation or remove acceptance criteria to make a task appear complete.
- Teamster additionally: preserve vanilla cart mass/physics by default; no zero-weight defaults, cart teleports, recovery cheats, stamina bypass, pathfinding, world-save mutation, or server-authority takeover. Mutating conveniences must be explicit, reversible, fail-closed, and authorized by their own issue. No compile-time dependency between Teamster and Cartographer.

## Local commands

```powershell
pwsh ./scripts/bootstrap.ps1
pwsh ./scripts/build.ps1
pwsh ./scripts/deploy.ps1
python ./tools/validate_repo.py
pwsh ./scripts/package.ps1
```

A build may be impossible on a machine without the user's licensed Valheim installation and the configured BepInEx profile. In that case, complete static checks, clearly report the missing dependency, and do not claim the build passed.

## Completion report

Always report:

- issue addressed;
- files changed;
- build/static checks run and exact outcomes;
- manual test steps still required;
- assumptions or compatibility risks.
