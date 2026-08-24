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

| Item | Convention | Concerned Cartographer example |
|---|---|---|
| Product/display name | Title Case | `Concerned Cartographer` |
| Project folder | PascalCase | `src/ConcernedCartographer` |
| C# project | PascalCase | `ConcernedCartographer.csproj` |
| Root namespace | PascalCase segments | `TheConcernedCat.ConcernedCartographer` |
| Assembly | PascalCase segments | `TheConcernedCat.ConcernedCartographer.dll` |
| BepInEx plugin GUID | lowercase reverse domain | `com.theconcernedcat.valheim.concernedcartographer` |
| Thunderstore namespace | PascalCase | `TheConcernedCat` |
| Thunderstore package name | PascalCase, no spaces | `ConcernedCartographer` |
| Documentation folder | lowercase kebab-case | `docs/mods/concerned-cartographer` |
| Git tag | lowercase package slug plus semantic version | `concerned-cartographer/v0.1.0` |
| Issue key | uppercase short product code | `CC-001` |

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

Branches:

```text
feat/cc-###-short-description
fix/cc-###-short-description
chore/cc-###-short-description
docs/cc-###-short-description
```

Commits:

```text
feat(cartographer): add dirt road overlay
fix(cartographer): isolate atlas data by world UID
chore(repo): add repository validation
docs(cartographer): explain road-survey limitations
```

## Scripts and documentation

- PowerShell scripts: lowercase kebab-case, such as `setup-github.ps1`.
- PowerShell modules: PascalCase, such as `RepoTools.psm1`.
- General documentation: uppercase snake case when it is a repository-level contract, such as `DEVELOPMENT.md`.
- Product documentation folders: lowercase kebab-case.
- Generated build artifacts belong only under `artifacts/` and are never committed.

## Archive rule

Starter archives must contain repository files directly at ZIP root. The ZIP root should contain `ConcernedCatMods.sln`, `README.md`, `src/`, `docs/`, and `.github/`. It must not contain an outer `ConcernedCatMods/` wrapper directory.
