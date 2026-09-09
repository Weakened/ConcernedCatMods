# Valheim 1.0.7 compatibility audit

Issue: DEF-teamster-v1.0-002 (#230)  
Audit date: 2026-09-09  
Status: **automated compatibility PASS; owner in-game smoke pending**

Concerned Cartographer exposed a Valheim API break after the game update:
Character.Message gained a fifth bool log parameter. Teamster was audited
independently against the updated local installation. No Teamster code change
was required: its compiled DLL contains no call to Character.Message.

## Audited environment

No game or loader binary is committed or packaged. Hashes identify only the
local inputs used for this audit.

| Input | Identity | SHA-256 |
|---|---|---|
| Valheim | 1.0.7, Steam build 25185596; assembly_valheim.dll 2,566,144 B | A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC |
| Unity | 6000.0.75f1; globalgamemanagers 205,668 B | FB918473A11E666800CA644AEAA6FE6C6A82609CB3E8EEBA14315CB1F6276E10 |
| Publicized game reference | assembly version 0.0.0.0; 2,559,488 B | EC3AF92B8E297DB7461E933719295EC1EA1060D1960D2DA319691A0F92506B88 |
| BepInEx | 5.4.23.3; 130,048 B | E9AC3A950E91E71B13DF5480B36CE06AF27E981A688F0E62125B674D03A0713A |
| Jötunn | 2.29.2.0; 516,096 B | F65751BC15E7AE7466B0F7D3B38C758397741C99A50890DEB19ACF738376BFB1 |

## API findings

The installed game exposes Character.Message with five parameters:
MessageType, string, int, Sprite, and the new bool log parameter. The prior
four-parameter binary signature no longer exists.

Teamster is not affected because its Release DLL has **zero**
Character::Message member references. scripts/audit-teamster-game-api.ps1
makes both facts repeatable: it resolves the installed versions, asserts the
current five-parameter shape, decompiles the built Teamster IL, and fails on
any Character message call.

The rest of the Teamster game boundary was rechecked rather than inferred from
the clean compile:

- Direct Vagon, Player, Character, ZNetView, inventory, terrain, and Unity
  physics bindings compile against the publicized 1.0.7 assemblies.
- The startup cart capability remains all-or-nothing. Missing required members
  produce one actionable warning and disable cart features rather than using
  guessed values.
- GameVersionResolver and GameLocalization remain narrow reflective cosmetic
  adapters with explicit unknown/raw-token fallbacks.
- Teamster has no Harmony patch targets.
- Cartographer integration remains runtime-only, read-only capability
  detection; there is no compile-time Cartographer dependency.
- No startup, recurring cart, UI, persistence, brake, or optional-integration
  path retains the removed four-parameter method.

## Repeatable evidence

Run:

    pwsh ./scripts/audit-teamster-game-api.ps1 -Configuration Release
    dotnet test ./src/ConcernedTeamster.Tests/ConcernedTeamster.Tests.csproj -c Release
    dotnet test ./src/ConcernedCartographer.Tests/ConcernedCartographer.Tests.csproj -c Release
    python ./tools/validate_repo.py
    pwsh ./scripts/package.ps1 -Product ConcernedTeamster -Configuration Release

The audit script requires the licensed local Valheim installation configured
by Environment.props and ilspycmd; it never copies those assemblies.

## Unpublished compatibility candidate

The candidate is intentionally not tagged, released, or published. Exact
source identity, package/DLL hashes, contents, and test totals are recorded
below after the clean package run.

| Item | Evidence |
|---|---|
| Version | 1.0.0 |
| Source commit | _recorded after compatibility-proof commit_ |
| ZIP | artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-1.0.0.zip |
| ZIP SHA-256 | _recorded after package run_ |
| DLL SHA-256 | _recorded after package run_ |
| Package contents | _recorded after package audit_ |
| Teamster tests | _recorded after regression run_ |
| Cartographer regressions | _recorded after regression run_ |
| Repository validator | _recorded after validator run_ |

## Owner-only Valheim 1.0.7 smoke

Manual PASS is not claimed. Before publication, run the existing
PRE_RELEASE_SMOKE_TEST.md on Valheim 1.0.7 and specifically confirm:

1. clean startup and no MissingMethodException;
2. cart discovery, attach/detach, telemetry, cargo, grade, and panels;
3. brake engage/release, authority loss, logout/login, and world switch;
4. trip persistence and cross-world isolation;
5. uninstall leaves vanilla cart behavior and saves untouched;
6. BepInEx log review finds no Teamster errors or repeated warnings.

Publication, tagging, and the final in-game approval remain owner-only.
