# Naming conventions

These names are repository contracts. Change them only through an explicit migration issue.

## Repository root

The only local repository root is:

```text
C:\code\ConcernedCatMods
```

The solution file must be directly beneath that root:

```text
C:\code\ConcernedCatMods\ConcernedCatMods.sln
```

Never create any of these duplicate nesting patterns:

```text
C:\code\ConcernedCatMods\ConcernedCatMods\...
C:\code\ConcernedCatMods\ConcernedCatMods-starter\...
C:\code\ConcernedCatMods\src\ConcernedCatMods\...
```

Before any repository-changing operation, verify:

```powershell
git rev-parse --show-toplevel
```

It must return `C:/code/ConcernedCatMods` (path separators may differ).

## Repository and solution

| Item | Convention | Example |
|---|---|---|
| GitHub repository | PascalCase | `ConcernedCatMods` |
| Local root folder | exactly the repository name | `C:\code\ConcernedCatMods` |
| Solution | PascalCase | `ConcernedCatMods.sln` |
| Root documentation | UPPER_SNAKE_CASE where conventional | `README.md`, `AGENTS.md`, `CLAUDE.md` |

## Mods

Each mod is an independently versioned product.

| Item | Convention | Concerned Cartographer | Concerned Teamster |
|---|---|---|---|
| Product/display name | Title Case | `Concerned Cartographer` | `Concerned Teamster` |
| Project folder | PascalCase | `src/ConcernedCartographer` | `src/ConcernedTeamster` |
| C# project | PascalCase | `ConcernedCartographer.csproj` | `ConcernedTeamster.csproj` |
| Test project | PascalCase plus `.Tests` | `ConcernedCartographer.Tests` | `ConcernedTeamster.Tests` |
| Root namespace | PascalCase segments | `TheConcernedCat.ConcernedCartographer` | `TheConcernedCat.ConcernedTeamster` |
| Assembly | PascalCase segments | `TheConcernedCat.ConcernedCartographer.dll` | `TheConcernedCat.ConcernedTeamster.dll` |
| BepInEx plugin GUID | lowercase reverse domain | `com.theconcernedcat.valheim.concernedcartographer` | `com.theconcernedcat.valheim.concernedteamster` |
| Thunderstore namespace | PascalCase | `TheConcernedCat` | `TheConcernedCat` |
| Thunderstore package name | PascalCase, no spaces | `ConcernedCartographer` | `ConcernedTeamster` |
| Documentation folder | lowercase kebab-case | `docs/mods/concerned-cartographer` | `docs/mods/concerned-teamster` |
| Git tag | lowercase package slug plus semantic version | `concerned-cartographer/v0.1.0` | `concerned-teamster/v0.1.0` |
| Issue key | uppercase short product code | `CC-001` | `CT-001` |
| Sprint label | `sprint:` plus product slug where needed | `sprint:v0.3` (legacy, Cartographer) | `sprint:teamster-v0.3` |
| Mod-manager profiles | product-prefixed | `TCC-Clean/Dev/Compat` | `TCT-Clean/Dev/Compat/Dedicated` |

Each product is fully independent: its own DLL, plugin GUID, package, changelog, versions, tags, and release lifecycle. Products never reference each other at compile time.

Do not use `ConcernedCat` and `TheConcernedCat` interchangeably in identifiers. Use:

- `The Concerned Cat` for the public creator name;
- `TheConcernedCat` for namespaces and Thunderstore ownership;
- `ConcernedCatMods` for the repository and solution.

## Source code

- C# types and source filenames: `PascalCase`.
- C# methods, properties, and public members: `PascalCase`.
- Parameters and local variables: `camelCase`.
- Private fields: `_camelCase`.
- Interfaces: `I` plus PascalCase.
- Async methods: suffix with `Async`.
- One primary public type per file.
- Folder names under a C# project: PascalCase, such as `Map`, `Roads`, `Runtime`, and `Persistence`.

## Git

Branches (issue key `cc-###` for Cartographer, `ct-###` for Teamster):

```text
feat/cc-###-short-description
fix/cc-###-short-description
chore/cc-###-short-description
docs/cc-###-short-description
feat/ct-###-short-description
fix/ct-###-short-description
chore/ct-###-short-description
docs/ct-###-short-description
```

Sprint integration branches, when needed: `sprint/concerned-cartographer-vX.Y`, `sprint/concerned-teamster-vX.Y`.

Commits (scope is the product slug, or `repo` for shared tooling):

```text
feat(cartographer): add dirt road overlay
fix(cartographer): isolate atlas data by world UID
feat(teamster): read cart mass and pull-state telemetry
fix(teamster): reset telemetry on world switch
chore(repo): add repository validation
docs(teamster): document the cart internals spike
```

## Scripts and documentation

- PowerShell scripts: lowercase kebab-case, such as `setup-github.ps1`.
- PowerShell modules: PascalCase, such as `RepoTools.psm1`.
- General documentation: uppercase snake case when it is a repository-level contract, such as `DEVELOPMENT.md`.
- Product documentation folders: lowercase kebab-case.
- Generated build artifacts belong only under `artifacts/` and are never committed.

## Archive rule

Starter archives must contain repository files directly at ZIP root. The ZIP root should contain `ConcernedCatMods.sln`, `README.md`, `src/`, `docs/`, and `.github/`. It must not contain an outer `ConcernedCatMods/` wrapper directory.
