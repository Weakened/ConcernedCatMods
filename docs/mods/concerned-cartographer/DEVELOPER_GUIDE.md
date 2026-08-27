# Concerned Cartographer — Developer Setup and Contribution Guide

This document gets a developer from a clean Windows machine to a local build, test run, deployed DLL, and safe pull request.

## 1. Prerequisites

Required:

- Windows 10/11
- Valheim installed through Steam
- Git
- GitHub CLI (`gh`) if contributing through GitHub
- PowerShell 7 (`pwsh`)
- Visual Studio 2022 or compatible MSBuild tooling
- .NET Framework 4.8 targeting pack
- a current .NET SDK capable of running the test project
- Python 3.11+ for repository validation (`tomllib`)
- a Thunderstore-compatible Valheim mod manager
- BepInExPack Valheim in the development profile
- Jötunn in the development profile

For release packaging:

- `tcli`

Do **not** commit Valheim DLLs, BepInEx DLLs or Jötunn DLLs.

## 2. Clone

```powershell
git clone https://github.com/Weakened/ConcernedCatMods.git C:\code\ConcernedCatMods
Set-Location C:\code\ConcernedCatMods
```

Maintainer automation assumes the canonical root above. If you use another path, verify local scripts before running them.

## 3. Create isolated mod profiles

Recommended:

- `TCC-Clean` — no mods; vanilla reference
- `TCC-Dev` — BepInEx, Jötunn and local development DLL
- `TCC-Package` — fresh install of the exact release ZIP
- `TCC-Compat` — compatibility regression
- later multiplayer/NoMap/scale profiles as v1 requires

Never use an important personal world for early development testing.

## 4. Install BepInEx and Jötunn

Use the mod manager for Valheim.

In `TCC-Dev` install:

- BepInExPack Valheim
- Jötunn

Launch the profile modded once so BepInEx creates folders and `LogOutput.log`.

Do not install generic BepInEx manually into the repository.

## 5. Configure machine-local paths

```powershell
Copy-Item .\Environment.props.example .\Environment.props
```

Edit `Environment.props` with actual paths.

Typical shape:

```xml
<Project>
  <PropertyGroup>
    <VALHEIM_INSTALL>C:\Program Files (x86)\Steam\steamapps\common\Valheim</VALHEIM_INSTALL>
    <BEPINEX_PATH>C:\Users\YOU\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\TCC-Dev\BepInEx</BEPINEX_PATH>
    <MOD_DEPLOYPATH>C:\Users\YOU\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\TCC-Dev\BepInEx\plugins</MOD_DEPLOYPATH>
  </PropertyGroup>
</Project>
```

`Environment.props` is machine-specific and **must remain ignored**.

```powershell
git check-ignore -v .\Environment.props
```

## 6. Bootstrap

```powershell
pwsh .\scripts\bootstrap.ps1
```

If bootstrap fails, diagnose the missing path/tool/reference. Do not “fix” it by committing local game assemblies.

## 7. Validate repository metadata

```powershell
python .\tools\validate_repo.py
```

Resolve validator failures before opening a PR.

## 8. Build

```powershell
pwsh .\scripts\build.ps1 -Configuration Debug
```

Direct fallback:

```powershell
dotnet restore .\ConcernedCatMods.sln
dotnet build .\ConcernedCatMods.sln -c Debug
```

Expected plugin assembly:

```text
TheConcernedCat.ConcernedCartographer.dll
```

## 9. Run pure domain tests

```powershell
dotnet test .\src\ConcernedCartographer.Tests\ConcernedCartographer.Tests.csproj
```

The test project compiles pure `Domain/**/*.cs` directly and does not require Valheim assemblies.

When adding deterministic road/pin/search/sync logic, prefer putting it in a pure domain class so it can be tested here.

## 10. Deploy to the development profile

Review `Environment.props`, then:

```powershell
pwsh .\scripts\deploy.ps1 -Configuration Debug
```

Confirm the DLL lands only in `TCC-Dev`.

## 11. Launch and inspect logs

Start `TCC-Dev` in modded mode.

Primary log:

```text
<TCC-Dev>\BepInEx\LogOutput.log
```

Search for:

```text
Concerned Cartographer
```

A useful test record includes:

- Valheim version
- Concerned Cartographer version
- BepInEx version
- Jötunn version
- profile
- disposable world
- reproduction steps
- relevant log excerpt

## 12. Console commands

Concerned Cartographer exposes scriptable diagnostics/operations:

- `cc_roads`
- `cc_pins`
- `cc_atlas` (including `compat`, `backup`/`backups`/`restore`, `support`)
- `cc_survey`
- `cc_routes`
- `cc_sync`

Use these for reproducible tests; UI remains the primary user experience.

## 13. Build a release ZIP locally

```powershell
pwsh .\scripts\package.ps1 -Configuration Release
```

Output belongs under:

```text
artifacts\thunderstore\
```

Inspect the ZIP before publishing. Do not publish from a dirty tree.

## 14. Code-layer boundaries

### Pure domain layer

`src/ConcernedCartographer/Domain`

Good fits:

- deterministic data models
- geometry
- codecs
- search/filtering
- merge/conflict logic
- undo/redo
- state machines

Avoid:

- Unity
- BepInEx
- Jötunn
- `Minimap`
- `Player`
- filesystem IO

### Game adapters

`Roads`, selected `Map`, and selected `Runtime` classes.

Their job is to convert fragile game state into stable domain values and fail closed when game internals drift.

### Persistence

`Persistence`

Filesystem IO only. Parsing/business invariants belong in codecs/domain.

### UI

`Map/*Panel.cs`

Presentation/callback wiring. Do not make UI objects authoritative storage.

## 15. Contribution workflow

1. Choose/create issue.
2. Branch from current `main`.
3. Keep scope narrow.
4. Add tests.
5. Run validator/build/tests.
6. Run required game test.
7. Update docs/changelog when behavior changes.
8. Inspect `git diff`.
9. Push branch/open PR.
10. Include exact manual evidence.

Branch examples:

```text
feat/cc-###-short-description
fix/cc-###-short-description
docs/cc-###-short-description
```

Commit examples:

```text
feat(cartographer): add route model
fix(cartographer): preserve tombstone revision
docs(cartographer): document pin persistence
```

## 16. Pull-request evidence

A strong PR includes:

- problem/issue;
- design;
- files changed;
- data/schema changes;
- compatibility risk;
- tests run;
- Valheim/BepInEx/Jötunn versions;
- screenshots/video for visible behavior;
- migration/uninstall impact;
- known limitations.

Never write “tested” when you only compiled.

## 17. AI-assisted contributions

Material AI use is allowed but must be disclosed in the PR.

You remain responsible for reviewing/validating what you submit.

Before merging AI-generated code:

- understand what each changed class does;
- inspect every changed file;
- run tests yourself;
- validate game-specific assumptions;
- check for invented APIs;
- check license/provenance of assets/code;
- ensure no secrets/private data were introduced.

See `AI_DEVELOPMENT.md`.

## 18. Never commit

- `Environment.props`
- Valheim saves/worlds/characters
- `Assembly-CSharp.dll`
- Unity game assemblies
- BepInEx binaries
- Jötunn binaries
- generated publicized assemblies
- `bin/`
- `obj/`
- `artifacts/`
- Thunderstore/API tokens
- private unrelated logs

## 19. When Valheim updates

Treat every game update as an integration change.

At minimum:

1. update/test BepInEx/Jötunn compatibility;
2. rebuild supported references;
3. test `GroundPaintProbe`;
4. test `ConstructionCapture` Harmony target;
5. test chunk-recovery reflection;
6. test private `Minimap` access;
7. test map overlay alignment;
8. test pin adoption/reconciliation;
9. test world switch/uninstall safety;
10. update compatibility docs.

Do not publish “compatible” solely because the project compiles.
