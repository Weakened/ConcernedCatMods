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

### Adding a project to the solution

`ConcernedCatMods.sln` belongs to the lead (`docs/settlement/cart-and-collection/TASKS.md` §2).
Add a project **by hand**: a `Project`/`EndProject` pair, its twelve
`ProjectConfigurationPlatforms` rows, and one `NestedProjects` row putting it under
the `src` folder.

**Do not use `dotnet sln add`.** It also creates solution folders mirroring the
directory layout, and a folder whose name matches a project makes MSBuild refuse
the whole solution with MSB5004 — which is exactly what broke `main` in #341.
It exits 0 and warns about none of it.

Two rules the validator enforces, because neither is visible from a green CI run:

- every `src/**/*.csproj` is listed in the solution — a project nobody's solution
  mentions still restores and still never runs;
- no two entries share a name **within the same solution folder**, which is
  MSBuild's own uniqueness key. Two projects called `Provider` under different
  folders are fine.

CI additionally runs `dotnet restore ConcernedCatMods.sln`, so MSBuild's own
loader is the authority on whether the file is well formed.

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

**Concerned Foreman** (added by CF-SET-002) follows every row of the table above:
`Concerned Foreman` / `src/ConcernedForeman` / `ConcernedForeman.csproj` /
`ConcernedForeman.Tests` / `TheConcernedCat.ConcernedForeman` /
`TheConcernedCat.ConcernedForeman.dll` /
`com.theconcernedcat.valheim.concernedforeman` / `TheConcernedCat` /
`ConcernedForeman` / `docs/mods/concerned-foreman` / `concerned-foreman/v0.1.0` /
issue keys `CF-###` (diagnostics) and `CF-SET-###` (settlement runtime) /
profiles `TCF-Clean/Dev/Compat`.

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

## Shared source libraries

Some code is useful to more than one product. It is shared as **source**, never
as an assembly: each product compiles its own copy into its own DLL, so
packaging never gains a dependency and the "one mod DLL per package" contract
holds. Products still never reference each other.

| Item | Convention | Example |
|---|---|---|
| Shared source root | `src/Shared/<Area>` in PascalCase | `src/Shared/Companions` |
| Namespace | `TheConcernedCat.<Area>` plus a folder segment | `TheConcernedCat.Companions.Quest` |
| Visibility | every type `internal` | `internal sealed class CompanionRegistry` |
| Adoption | one `Compile` item in the consuming `.csproj` | `<Compile Include="..\Shared\Companions\**\*.cs" LinkBase="Companions" />` |

Rules for anything under `src/Shared`:

- No Unity, BepInEx, or Jötunn types. Shared code must compile into a test
  assembly and run without the game installed.
- No product namespace may appear in it. A shared type that needs to know
  something product specific takes it as a parameter or an interface.
- A shared area is not a framework and is not mandatory. A product that does not
  want it simply does not add the `Compile` item.

Existing areas:

| Area | Purpose | Consumers |
|---|---|---|
| `src/Shared/Companions` | Concerned Companions: identity, quest state, sidecar persistence, unlock decisions, placement planning, dialogue rotation | `ConcernedCartographer` |
| `src/Shared/Settlement` | Settlement runtime: identity, work orders, collection orders, custody ledger and transfers, replayable journal, worker movement planning | `ConcernedForeman` |
| `src/Shared/Workers` | Worker identity, work authority, the single actor-mode owner, bounded retries and deadlines | `ConcernedForeman`, `ConcernedTeamster` |
| `src/Shared/Interop` | Cross-product runtime capability contracts: a BCL-only capability map and versioned contracts such as `concernedcat.haul/1` | `ConcernedForeman`, `ConcernedTeamster` |
| `src/Shared/Ladders` | Ladder geometry, mounting, climb motion, exits and climb safety, game-free (`docs/mods/concerned-foreman/LADDERS.md`) | `ConcernedForeman` |

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
