# Concerned Ladders: isolated profile and deployment

**Work item:** CL-003. **Product:** Concerned Ladders 0.1.1.
**Profile:** `TCC-Ladders-Dev`. **Plugin:** `com.theconcernedcat.valheim.concernedladders`.

## What is prepared, and what is still local work

`tools/prepare_ladders_profile.py` prepares a dependency import file. It does not create a live profile or
install the unpublished ladder DLL. `scripts/deploy-ladders-profile.ps1` installs only our locally built DLL
after the local lead supplies exact build evidence. Neither tool launches the game or claims a play test.

The cloud development session could not operate BLD because Desktop Commander paused device access at its
monthly usage allowance. Do not retry or reconnect it as a workaround. The local agent can perform the
steps below on the authorized machine. Actual manager import, game build, launch, and play results must be
recorded as unperformed until observed there.

## Dependency contract

The generator reads `src/ConcernedLadders/Package/thunderstore.toml`; this is the single source of pins.
It refuses a manifest for another product, a floating version, or any additional dependency.

| Package | Initial pin | Purpose |
|---|---:|---|
| `denikson-BepInExPack_Valheim` | `5.4.2351` | Valheim loader |
| `ValheimModding-Jotunn` | `2.30.2` | Piece registration, localization, compatibility |

These package versions were verified as available on their publisher pages on 2026-10-24; availability is
not runtime compatibility evidence for the installed BLD game. Record the actual Valheim build, manager
version, loader package version, Jotunn version, and mod source commit after import. Existing products keep
their own dependency pins. Do not add ConcernedNPC, ConcernedForeman, Teamster, another ladder mod,
configuration managers, or convenience mods to this clean profile.

Publisher sources:

- [Valheim BepInEx pack](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)

## 1. Prepare and import the new profile

From the isolated ConcernedCatMods checkout, using Python 3.11 or newer:

```powershell
python ./tools/prepare_ladders_profile.py
```

This creates `artifacts/profiles/TCC-Ladders-Dev.r2z` and prints its SHA256 and state
`DEPENDENCY_IMPORT_PREPARED`. Repeating it with identical contents leaves the artifact unchanged. If the
pins change, pass a new `--output` filename; differing existing artifacts are preserved.

In Thunderstore Mod Manager, select **Valheim**, go to profile selection, choose **Import/Update → Import
new profile → From file** (wording may vary by manager version), and select that `.r2z`. Import as a new
`TCC-Ladders-Dev` profile, never as an update to an existing gameplay or NPC profile. If that name already
exists, inspect it before doing anything: do not delete, overwrite, or silently reuse it. A profile known
to be an earlier run of this ladder work can be reused only after its contents match this clean contract.

Let the manager download both dependencies. Verify the Installed view shows the intended versions,
enabled, without unrelated packages. Open **Settings → Browse profile folder** and copy the actual path.
Default locations are only discovery hints, not deployment destinations to guess:

```text
%APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\profiles\TCC-Ladders-Dev
%APPDATA%\r2modmanPlus-local\Valheim\profiles\TCC-Ladders-Dev
```

Custom data locations are valid. The deploy script requires an explicit real path ending in
`Valheim\profiles\TCC-Ladders-Dev`; it does not scan and choose a profile for you.

### Why the ladder DLL is installed separately

An `.r2z` contains `export.r2x`, a version-pinned package list, and optionally configuration. The current
r2modman importer rejects DLLs and scripts while extracting a profile. An unpublished local package also
cannot be downloaded from Thunderstore just because its name appears in that list. Our export therefore
contains only the two published dependencies, and never a fake `ConcernedLadders` online entry.

The generated `export.r2x` uses JSON, which the manager's YAML parser accepts as a YAML 1.2 subset. The
version field is an object with `major`, `minor`, and `patch`; it is not a version string. Schema and
executable rejection were checked against
[r2modman's importer at commit 9776867](https://github.com/ebkr/r2modmanPlus/blob/9776867fff5411048a17bca3e9e7e34779737e56/src/utils/ProfileUtils.ts)
and its [export model](https://github.com/ebkr/r2modmanPlus/blob/9776867fff5411048a17bca3e9e7e34779737e56/src/model/exports/ExportMod.ts).

## 2. Build the exact candidate locally

The integration lead owns builds, deployment, and the game. Code workers use separate worktrees and hand
back commits. Preserve `Prepare-TCC-Screenshot-Profile.ps1`, existing profiles, real saves, and unrelated
repository work. Do not call the old generic deploy script with an unsupported product argument.

1. Commit and independently review the integrated candidate. Record `git rev-parse HEAD`; the source
   checkout must be clean, including untracked source files.
2. Configure the isolated checkout's ignored `Environment.props` to use the installed licensed Valheim
   assemblies and the correct dependency profile. Do not rewrite another checkout's machine settings.
3. Run `scripts/verify.ps1` in Release through the existing BLD build lock documented in
   `docs/settlement/cart-and-collection/TASKS.md`. The known wrapper is
   `C:\code\concernedcat-handoffs\2026-09-17-gunnar-thorstein-work\with-build-lock.ps1`.
   Discover and inspect that wrapper locally; never create a parallel substitute lock or bypass it.
4. Capture the complete verify output in `artifacts/ladders-verify.log`, propagating a nonzero exit code.
   Verify must build the products, run tests against those binaries, and validate the repository. A bare
   `dotnet test`, a stub build, or `verify.ps1 -SkipTests` is insufficient.
5. After a successful run, calculate the SHA256 of
   `src/ConcernedLadders/bin/Release/net48/TheConcernedCat.ConcernedLadders.dll`. Do not copy dependency or
   game assemblies into a package or another profile.

## 3. Inspect the deployment, then install the candidate

Exit any running Valheim game or dedicated test server normally. The deploy tool refuses a running
process; it never kills one. Set these variables to the actual observed paths and results:

```powershell
$ladderProfile = 'C:\ACTUAL_MANAGER_DATA\Valheim\profiles\TCC-Ladders-Dev'
$ladderCommit = (git rev-parse HEAD).Trim()
$ladderDllHash = (Get-FileHash './src/ConcernedLadders/bin/Release/net48/TheConcernedCat.ConcernedLadders.dll' -Algorithm SHA256).Hash

pwsh ./scripts/deploy-ladders-profile.ps1 -ProfilePath $ladderProfile `
    -VerifyLog './artifacts/ladders-verify.log' -ExpectedSourceCommit $ladderCommit `
    -ExpectedDllSha256 $ladderDllHash -WhatIf
```

The preflight checks source cleanliness, the full commit, DLL identity/version/hash, the successful verify
summary, dependencies, exact profile naming, links/junctions, unrelated plugin DLLs, duplicate ladder
DLLs, and running game processes. `-WhatIf` writes no file. Review its `PLAN_ONLY` receipt, then repeat the
same command without `-WhatIf` to deploy this authorized candidate.

The single DLL destination is:

```text
TCC-Ladders-Dev\BepInEx\plugins\ConcernedLadders\TheConcernedCat.ConcernedLadders.dll
```

If a different ladder candidate is already there, default behavior is refusal. `-ReplaceExisting` permits
an update of this product only and first preserves the previous DLL under
`%LOCALAPPDATA%\TheConcernedCat\ConcernedLadders\profile-backups\<old-sha256>\`, outside every checkout:
`validate_repo.py` refuses a DLL anywhere in the tree outside `bin/obj`, so a backup under `artifacts/`
would fail the next `verify.ps1` of the same checkout. A duplicate in another plugin folder, any unknown
receipt, or an unrelated plugin remains untouched and requires explicit local inspection.

`ConcernedLadders.deployment.json` records the full source SHA, candidate hash, manifest hash, verification
log hash, observed assembly versions, and `DEPLOYED_NOT_LAUNCHED`. Loader package and assembly versions
are different identifiers: confirm the BepInExPack package pin in the manager rather than inferring it
from `BepInEx.dll`. If the process stops after copying but before writing its receipt, inspect both
before retrying; an incomplete run must not be described as a clean deployment.

This development copy does not fabricate a manager package entry. It is discoverable by BepInEx in the
profile's plugin directory; the manager may not list it in Installed. For distributable ZIP testing,
use the manager's supported **Import local mod** separately, and first resolve any duplicate development
DLL. Never leave both copies enabled.

## 4. Launch and prove readiness

Select **TCC-Ladders-Dev → Start modded** in the manager. Use its own shortcut generation if a shortcut is
needed. Do not hardcode a direct game launcher: Doorstop v3 and v4 use different argument names, which
the manager [selects from the installed loader](https://github.com/ebkr/r2modmanPlus/blob/9776867fff5411048a17bca3e9e7e34779737e56/src/r2mm/launching/instructions/instructions/loader/BepInExGameInstructions.ts).

A separate mod profile does **not** isolate Valheim saves. Use a newly created disposable test character
and test world. Configure a dedicated test save directory through the manager if the installed client
supports it, then verify the actual save location and Steam Cloud behavior before relying on it. The
[official server guide](https://www.valheim.com/support/a-guide-to-dedicated-servers/) documents `-savedir`,
but that alone does not establish isolation for the current desktop client. Never load a real world as a
smoke test or edit native save files to construct the fixture.

Capture the following evidence before calling this profile ready for owner testing:

- Actual manager version and installed Valheim build; installed dependency package versions; full source
  SHA and installed DLL SHA256 matching the receipt.
- Startup log showing one instance of Concerned Ladders and successful registration, with no missing
  dependencies, duplicate plugin GUIDs, Harmony failures, or missing-prefab errors.
- The hammer contains all intended ladder variants. Normal placement, stacking, removal/refund, and a
  one-piece mount/climb/exit work in the disposable world.
- Save/reload returns the test pieces, followed by a second clean mount and dismount.
- The full runtime test matrix is recorded separately, including blocked exits and multiplayer. A
  successful menu launch or smoke run never counts as completion of those rows.

Use statuses literally: `DEPENDENCY_IMPORT_PREPARED`, `PROFILE_IMPORTED`, `BUILD_VERIFIED`,
`DEPLOYED_NOT_LAUNCHED`, `MENU_OBSERVED`, `SOLO_SMOKE_OBSERVED`. Record failures and unperformed rows.
Only observed results can promote a status; the preparation and deployment tools stop before gameplay.
