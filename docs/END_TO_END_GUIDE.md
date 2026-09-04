# ConcernedCatMods: end-to-end setup, development, and publishing guide

This is the authoritative Windows workflow for creating the repository, installing the toolchain, running Claude Code and Codex safely, testing Concerned Cartographer, and publishing an independently versioned Thunderstore package. Sections 2-16 were written for the first product, Concerned Cartographer, and remain the reference workflow; section 17 covers the second product, Concerned Teamster, which follows the same workflow with its own identity, profiles, and issue conveyor.

## 1. Repository decision

Use **one monorepo now**: `Weakened/ConcernedCatMods`.

This is the right tradeoff while one owner maintains a small catalog because build scripts, agent rules, release validation, documentation, and issue conventions can be shared. The catalog currently contains **Concerned Cartographer** (public beta) and **Concerned Teamster** (active development). Every mod must still have its own:

- C# project and DLL;
- plugin GUID;
- package directory and icon;
- Thunderstore package name and version;
- changelog;
- release tag.

Split a mod into its own repository later only when it gains independent maintainers, needs a different toolchain, or has a release/issue volume that makes the monorepo difficult to navigate.

## 2. Create the empty GitHub repository

On the GitHub page already open in the browser, use:

```text
Owner:        Weakened
Repository:   ConcernedCatMods
Description:  Valheim mods by The Concerned Cat — shared tooling, independent packages and releases.
Visibility:   Public
README:       Off
.gitignore:   None
License:      None
```

Leave GitHub's README, `.gitignore`, and license generation off because this starter already contains all three. Click **Create repository**.

Do not upload the ZIP through GitHub's web UI. Push it as a normal Git repository so history, branches, and agents work correctly.

## 3. Accounts

### GitHub

The existing `Weakened` account is sufficient. Enable two-factor authentication. Install GitHub CLI and authenticate:

```powershell
gh auth login
gh auth status
```

### Thunderstore

1. Sign in to Thunderstore with GitHub.
2. Open **Settings → Teams**.
3. Create the team `TheConcernedCat` after checking the spelling carefully.
4. Do not create the first package until both the team namespace and package name are final.
5. Later, under the team, create a service account named `ConcernedCartographerPublisher` for TCLI publishing.

Treat `TheConcernedCat-ConcernedCartographer` as a permanent package identity.

### Claude Code and Codex

Use native Windows installations because Valheim, Visual Studio, and the mod-manager profile are Windows-native. WSL is unnecessary for this project and complicates access to Steam and profile paths.

After installing each tool, launch it once and complete the account sign-in:

```powershell
claude
codex
```

## 4. Required software

Install the following before opening the solution:

1. **Valheim through Steam**.
2. **Git for Windows**.
3. **GitHub CLI**.
4. **Visual Studio 2022 Community**.
5. In Visual Studio Installer, add the **.NET desktop development** workload.
6. In Individual components, confirm the **.NET Framework 4.8 SDK and targeting pack** are installed.
7. Install a current .NET SDK so global tools such as TCLI can run.
8. Install PowerShell 7 (`pwsh`).
9. Install r2modman, Gale, or Thunderstore Mod Manager. This guide calls it the “mod manager.”
10. Install TCLI:

```powershell
dotnet tool install -g tcli
# Use this instead when it is already installed:
dotnet tool update -g tcli
```

11. Install Claude Code in PowerShell:

```powershell
irm https://claude.ai/install.ps1 | iex
```

12. Install Codex in PowerShell:

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://chatgpt.com/codex/install.ps1 | iex"
```

Unity is deliberately omitted from the initial setup. Concerned Cartographer v0.1 uses no custom 3D assets or asset bundles.

## 5. Extract and push the starter repository

Extract `ConcernedCatMods-starter.zip` under a normal development folder, for example `C:\src`. Then open PowerShell in the extracted `ConcernedCatMods` folder:

```powershell
git init
git branch -M main
git config user.name "Eren Cansunar"
git config user.email "<your-git-commit-email>"
git add .
git commit -m "chore: bootstrap ConcernedCatMods monorepo"
git remote add origin https://github.com/Weakened/ConcernedCatMods.git
git push -u origin main
```

Then create labels and the initial backlog:

```powershell
pwsh ./scripts/setup-github.ps1 -Repository "Weakened/ConcernedCatMods"
```

The script is idempotent for issue titles and safe to run again.

## 6. Create isolated Valheim test profiles

Create three profiles in the mod manager:

```text
TCC-Clean   — no mods; confirms vanilla behavior
TCC-Dev     — BepInEx, Jötunn, and the local Concerned Cartographer DLL
TCC-Compat  — the development set plus Pinnacle and MapRoutes
```

In `TCC-Dev`, install:

```text
denikson-BepInExPack_Valheim-5.4.2333
ValheimModding-Jotunn-2.29.2
```

Use the manager's **Settings → Browse profile folder** action and write down the profile folder. The project needs the path to that profile's `BepInEx` directory and `BepInEx\plugins` directory.

Create a disposable Valheim world such as `TCC_Mod_Test`. Never use a valuable world for early development.

## 7. Configure local paths

Copy the example file. The resulting `Environment.props` is ignored by Git:

```powershell
Copy-Item ./Environment.props.example ./Environment.props
notepad ./Environment.props
```

Set:

- `VALHEIM_INSTALL` to Steam's base Valheim directory. Steam can reveal it through **Manage → Browse local files**.
- `BEPINEX_PATH` to `TCC-Dev\BepInEx`.
- `MOD_DEPLOYPATH` to `TCC-Dev\BepInEx\plugins`.

Do not point deployment at a clean profile or directly modify the Steam installation when using a profile-based workflow.

## 8. Bootstrap, build, and deploy

From the repository root:

```powershell
pwsh ./scripts/bootstrap.ps1
pwsh ./scripts/build.ps1 -Configuration Debug
pwsh ./scripts/deploy.ps1 -Configuration Debug
```

The first build may take longer because Jötunn's prebuild task creates publicized assemblies from the locally installed game. Those generated assemblies remain inside the local game/tooling environment and must not enter Git.

Launch `TCC-Dev` with **Start modded**. In the BepInEx console and `BepInEx\LogOutput.log`, confirm a line similar to:

```text
Concerned Cartographer 0.1.0 loaded
```

## 9. Prove the first vertical slice

The starter implements a deliberately narrow survey prototype:

1. Enter the disposable world.
2. Create a dirt Pathen strip with the hoe and a paved-road strip with the stonecutter available.
3. Walk along both strips.
4. Open the full map.
5. Confirm two Jötunn map layers are available: dirt paths and paved roads.
6. Confirm the lines also appear on the minimap and remain hidden by unexplored fog.
7. Quit to the menu, reload the same world, and confirm the surveyed roads persist.
8. Enter a different test world and confirm no lines leak between worlds.

The v0.1 survey prototype discovers roads as the local player traverses them. Direct capture of every successful hoe action and loaded-chunk backfill are later issues, not something the first build should pretend to have solved.

## 10. Agent workflow: Claude implements, Codex reviews

Never run both agents against the same working tree simultaneously. Use one branch and one issue per unit of work.

### Claude implementation pass

```powershell
git switch -c feat/cc-001-plugin-load-proof
claude
```

Paste:

```text
Read CLAUDE.md, docs/mods/concerned-cartographer/PROJECT.md,
docs/mods/concerned-cartographer/ARCHITECTURE.md, and TEST_PLAN.md.
Implement only GitHub issue CC-001 and its acceptance criteria. Do not expand scope.
Run python tools/validate_repo.py and, when local game dependencies are available,
pwsh scripts/build.ps1. Do not publish. Report changed files, exact test evidence,
and unresolved assumptions about Valheim APIs.
```

Commit only after reviewing the diff:

```powershell
git status
git diff
git add .
git commit -m "feat(cartographer): prove plugin load and map lifecycle"
```

### Codex review/fix pass

Run Codex after Claude has stopped:

```powershell
codex
```

Paste:

```text
Read AGENTS.md and review this branch against the linked issue's acceptance criteria.
Do not add unrelated features. Focus on startup failures, map overlay lifecycle,
world switching, update-loop allocations, persistence safety, and package validity.
Run every available static check and build. Make only necessary fixes. Do not publish.
Return a concise review with files changed, commands run, results, and remaining manual tests.
```

### Pull request

Push the branch and open a PR even as a solo developer:

```powershell
git push -u origin HEAD
gh pr create --fill
```

Complete the PR's manual-test section. Merge only after the acceptance criteria and Definition of Done are demonstrably true.

Claude and Codex may swap implementation/review roles on later issues; the important rule is independent review and no simultaneous edits.

## 11. Initial issue order

Work in this order:

1. **CC-001** — bootstrap, plugin load, and map lifecycle proof.
2. **CC-002** — terrain paint probe for dirt Pathen and paved terrain.
3. **CC-003** — separate dirt/paved map overlays and incremental line rendering.
4. **CC-004** — per-world local persistence and cross-world isolation.
5. **CC-005** — direct capture of successful terrain-paint actions.
6. **CC-006** — loaded-chunk backfill for pre-existing roads.
7. **CC-007** — compatibility verification with Pinnacle and MapRoutes.
8. **CC-008** — in-place marker editor and expanded legend UX.

Do not start CC-008 merely because it is visually attractive; automatic physical-road cartography is the differentiator.

## 12. Build the Thunderstore package

Before packaging, synchronize the numeric version in:

- `src/ConcernedCartographer/ConcernedCartographer.csproj`;
- `src/ConcernedCartographer/Plugin.cs`;
- `src/ConcernedCartographer/Package/thunderstore.toml`;
- `src/ConcernedCartographer/Package/CHANGELOG.md`.

Then run:

```powershell
pwsh ./scripts/package.ps1 -Configuration Release
```

This performs the C# build, repository validation, and `tcli build`. TCLI packages files already on disk; it does not compile the mod for you.

The generated ZIP is placed under `artifacts\thunderstore`. Import that ZIP into a fresh local mod-manager profile and repeat the release test plan before uploading it.

## 13. Create the Thunderstore publishing token

Under the `TheConcernedCat` team:

1. Open **Service Accounts**.
2. Add `ConcernedCartographerPublisher`.
3. Copy the token once.
4. Put it only in the current PowerShell process:

```powershell
$env:TCLI_AUTH_TOKEN = Read-Host "Paste the Thunderstore service-account token"
```

Pasting at the `Read-Host` prompt avoids embedding the actual token in the command itself. Close the terminal after publishing to remove the process-level variable.

Never paste the token into Claude, Codex, a source file, GitHub issue, PR, screenshot, or chat.

## 14. Publish the first package

Only after the full checklist passes:

```powershell
pwsh ./scripts/publish.ps1 -Version 0.1.0
```

The script validates version synchronization, rebuilds the ZIP, asks for an explicit confirmation, then invokes TCLI. The package is marked as an alpha in its README even though Thunderstore version numbers are numeric only.

After a successful upload:

```powershell
git tag -a concerned-cartographer/v0.1.0 -m "Concerned Cartographer 0.1.0"
git push origin concerned-cartographer/v0.1.0
```

Thunderstore package versions are immutable. Any correction, including README-only changes, requires a higher version such as `0.1.1`.

## 15. Why publishing remains local initially

A normal GitHub-hosted runner does not contain the user's licensed Valheim assemblies. Do not commit or upload those DLLs merely to make cloud CI compile. The included GitHub workflow performs source/package metadata checks only.

Later choices for full automated builds are:

- a self-hosted Windows runner on the development PC with Valheim installed; or
- a carefully maintained legal build environment that acquires dependencies without storing copyrighted game binaries in the repository.

Local build and TCLI publish is the safest first-release workflow.

## 16. Public-alpha Definition of Done

Do not publish until all are true:

- clean `TCC-Dev` launch with no Concerned Cartographer errors;
- dirt and paved terrain are classified correctly;
- surveyed roads render on the full map and minimap;
- both layers can be toggled independently;
- overlays respect unexplored fog;
- data persists after logout/restart;
- road data never leaks into another world;
- disabling/removing the mod does not alter the Valheim world;
- 30-minute traversal produces no obvious frame-time spikes or runaway log spam;
- package ZIP passes validation and installs into a fresh profile;
- compatibility smoke test passes with Pinnacle and MapRoutes;
- README accurately states limitations;
- the Thunderstore **AI Generated** category is included because agents significantly assisted development;
- human review confirms the package contains no secrets, saves, game DLLs, or unrelated files.

## 17. Concerned Teamster

Concerned Teamster is the second independent product in the monorepo: it makes carts understandable, predictable, and safer while preserving vanilla cart mass and physics by default. It has its own project (`src/ConcernedTeamster` plus `src/ConcernedTeamster.Tests`), plugin GUID (`com.theconcernedcat.valheim.concernedteamster`), package (`TheConcernedCat-ConcernedTeamster`), changelog, tags (`concerned-teamster/vX.Y.Z`), and issue key (`CT`). It never references Concerned Cartographer at compile time; the v0.5 integration is runtime capability detection.

Read before working on it:

1. `docs/mods/concerned-teamster/PROJECT.md`
2. `docs/mods/concerned-teamster/ARCHITECTURE.md`
3. `docs/mods/concerned-teamster/TEST_PLAN.md`
4. `docs/mods/concerned-teamster/AUTONOMOUS_EXECUTION.md` (conveyor selection and workflow)
5. `docs/mods/concerned-teamster/HUMAN_ATTENTION.md` (non-blocking owner questions)

Key differences from the Cartographer sections above:

- **Profiles.** Teamster uses its own mod-manager profile family: `TCT-Clean`, `TCT-Dev`, `TCT-Compat`, and later `TCT-Dedicated`, with a disposable world such as `TCT_Mod_Test`. Deployment for Teamster targets the `TCT-Dev` profile's `BepInEx\plugins`.
- **Issues.** Labels and the full v0.1-v1.0 issue graph (ten `SPRINT Teamster vX.Y` controllers plus leaves `CT-001`..`CT-050`) are generated idempotently by:

```powershell
pwsh ./scripts/setup-teamster-github.ps1 -Repository "Weakened/ConcernedCatMods"
```

- **Execution.** Work is selected per `AUTONOMOUS_EXECUTION.md`: the lowest-numbered open unblocked `CT` leaf, one issue per branch and PR, evidence-commented closure. Open Cartographer public-beta P0/P1 regressions preempt Teamster work. Intermediate sprint gates are internal; the v0.9 public beta and v1.0 release are sealed as RCs, and every Thunderstore publication remains owner-only.
- **Safety.** Teamster never ships zero-weight defaults, cart teleports, recovery cheats, stamina bypass, pathfinding, world-save mutation, or server-authority takeover. Behavior-mutating features (the parking brake) are explicit, reversible, fail-closed, and separately authorized by their issues.
