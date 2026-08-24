# Claude Code instructions for ConcernedCatMods

You are working in a Valheim mod monorepo. The active product is **Concerned Cartographer**.

## Operating mode

- Begin by reading `docs/NAMING_CONVENTIONS.md`, then the project, architecture, and test-plan documents under `docs/mods/concerned-cartographer/`.
- Work on exactly one GitHub issue at a time.
- Prefer a vertical slice that can be manually proved in game over a broad speculative implementation.
- Keep changes small enough for an independent Codex review.
- Explain uncertainty around Valheim internals instead of inventing APIs.

## Non-negotiable safety

- Never commit or distribute game DLLs, publicized assemblies, profile folders, saves, world files, or tokens.
- Never invoke `tcli publish` or create a public release without explicit human approval.
- Never modify the user's real world files. Test in a disposable world and a dedicated mod-manager profile.
- Do not silently weaken validation or remove acceptance criteria to make a task appear complete.

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
