# ConcernedCatMods

Valheim mods by **The Concerned Cat**. This repository is a monorepo: shared engineering standards and tooling live at the root, while every mod has its own source project, package metadata, version, changelog, and release tag.

## Mods

| Mod | Status | Purpose |
|---|---|---|
| [Concerned Cartographer](docs/mods/concerned-cartographer/PROJECT.md) | Prototype / alpha | Builds a local, per-world road atlas by detecting player-made dirt and paved terrain and drawing it on Valheim's map. |

## Repository model

Use this monorepo while one small team owns the catalog. A mod may move to its own repository later when it gains independent maintainers, a substantially different toolchain, or a release cadence that makes the shared issue tracker noisy.

Each mod remains independently packaged. Do **not** combine all mods into one DLL or one Thunderstore package.

## Quick start

1. Read [`docs/NAMING_CONVENTIONS.md`](docs/NAMING_CONVENTIONS.md) and [`docs/END_TO_END_GUIDE.md`](docs/END_TO_END_GUIDE.md).
2. Copy `Environment.props.example` to the ignored file `Environment.props` and enter the local Valheim/BepInEx paths.
3. Run `pwsh ./scripts/bootstrap.ps1`.
4. Run `pwsh ./scripts/build.ps1` and `pwsh ./scripts/deploy.ps1`.
5. Launch the dedicated development profile with **Start modded** and inspect `BepInEx/LogOutput.log`.

## Important boundaries

- Never commit Valheim, Unity, BepInEx, or publicized game DLLs.
- Never publish an untested package directly from an AI agent.
- Thunderstore packages are built and versioned independently.
- The first public release is blocked until the manual release checklist passes.

## Release tags

Use namespaced tags so versions from different mods cannot collide:

```text
concerned-cartographer/v0.1.0
another-mod/v0.1.0
```

## License

Source code in this repository is licensed under the MIT License. Valheim and its assets are owned by their respective rights holders and are not distributed here.
