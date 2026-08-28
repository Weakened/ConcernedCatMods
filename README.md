# ConcernedCatMods

Valheim mods by **The Concerned Cat**. This repository is a monorepo: shared engineering standards and tooling live at the root, while every mod has its own source project, package metadata, version, changelog, and release tag.

## Mods

| Mod | Status | Purpose |
|---|---|---|
| [Concerned Cartographer](docs/mods/concerned-cartographer/PROJECT.md) | 1.0 release candidate | A living atlas: self-mapping roads, durable managed pins, search and decluttering, road-aware routes, and explicit collaborative sharing on Valheim's map. |

## Repository model

Use this monorepo while one small team owns the catalog. A mod may move to its own repository later when it gains independent maintainers, a substantially different toolchain, or a release cadence that makes the shared issue tracker noisy.

Each mod remains independently packaged. Do **not** combine all mods into one DLL or one Thunderstore package.

## Quick start

1. Read [`docs/NAMING_CONVENTIONS.md`](docs/NAMING_CONVENTIONS.md) and [`docs/END_TO_END_GUIDE.md`](docs/END_TO_END_GUIDE.md).
2. Copy `Environment.props.example` to the ignored file `Environment.props` and enter the local Valheim/BepInEx paths.
3. Run `pwsh ./scripts/bootstrap.ps1`.
4. Run `pwsh ./scripts/build.ps1` and `pwsh ./scripts/deploy.ps1`.
5. Launch the dedicated development profile with **Start modded** and inspect `BepInEx/LogOutput.log`.

## Support

- Bugs and feature requests: the [GitHub issue tracker](https://github.com/Weakened/ConcernedCatMods/issues).
- Security vulnerabilities, privacy/crash-reporting questions, or anything that should not be public: **support@theconcernedcat.com**.
- Privacy policy (including the optional, opt-in crash reporting): [`PRIVACY.md`](PRIVACY.md).

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

<!-- CC-DEVELOPER-DOCS -->
## Developer documentation

Concerned Cartographer is intentionally documented for outside contributors and future maintainers:

- [Developer setup](docs/mods/concerned-cartographer/DEVELOPER_GUIDE.md)
- [Complete codebase/class map](docs/mods/concerned-cartographer/CODEBASE_GUIDE.md)
- [Architecture](docs/mods/concerned-cartographer/ARCHITECTURE.md)
- [Data formats and migrations](docs/mods/concerned-cartographer/DATA_FORMATS.md)
- [Troubleshooting](docs/mods/concerned-cartographer/TROUBLESHOOTING.md)
- [AI-assisted development and provenance](docs/mods/concerned-cartographer/AI_DEVELOPMENT.md)
- [v1 release/authorship preparation](docs/mods/concerned-cartographer/V1_RELEASE_PREP.md)
- [Contributing](CONTRIBUTING.md)
- [Attribution / project notice](NOTICE.md)

## Original project and attribution

ConcernedCatMods and Concerned Cartographer are created and maintained by **Eren Cansunar / The Concerned Cat**. AI coding agents materially assisted implementation, tests, research and documentation. See [AUTHORS.md](AUTHORS.md), [NOTICE.md](NOTICE.md), and the repository [LICENSE](LICENSE).
