# Updated handoff: V1.0 (package 1.0.1)

The owner encountered an already-existing 1.0.0 upload. Thunderstore's package API also reported latest version 1.0.0. This new package remains the V1.0 release, without beta branding, and uses the next patch number 1.0.1.

- Build source: `6ac75c19998945f36b0661bb2cbb60c1f3fffac6`.
- ZIP: `TheConcernedCat-ConcernedCartographer-1.0.1.zip` (384,321 bytes).
- ZIP SHA-256: `9fd64885540e0546a0cb8f57216d63ebc58d92024d40f0b9b5d02413e58d47de`.
- DLL SHA-256: `b0f9d59793087a01cd23de1a05652569298dea94443c55e77ec183d2b118a101`.
- Sealed location: `C:\code\ConcernedCatMods-cc-232\artifacts\cc232\sealed\TheConcernedCat-ConcernedCartographer-1.0.1.zip`.
- Release build, required-binary/version validation, tcli packaging, exact six-entry ZIP audit, README/changelog byte checks, 256x256 icon, and zero direct Character/Player.Message DLL references: PASS.
- Same reviewed runtime implementation and 578-test result as the previous handoff; only release metadata and documentation changed.
- Compared Claude's local commit `fffee51edaaf4b2410543f2db66cd275a54ea3c3`: the underlying HUD compatibility fix is retained, with the reviewed guarded initialization and unsupported-signature checks added here. Claude's checkout remains untouched.
- This is the current upload artifact. The 1.0.0 ZIP below is historical. No automatic publication performed; exact-package manual verification remains as documented below.

---

# Concerned Cartographer 1.0.0 - release handoff (CC-232)

## Artifact identity

- Build source: `4ab68fd7935456b19efe739126fb06381808511c`.
- DLL informational version: `1.0.0+4ab68fd7935456b19efe739126fb06381808511c`.
- ZIP: `TheConcernedCat-ConcernedCartographer-1.0.0.zip` (384,119 bytes).
- Preserved local ZIP: `C:\code\ConcernedCatMods-cc-232\artifacts\cc232\sealed\TheConcernedCat-ConcernedCartographer-1.0.0.zip`.
- ZIP SHA-256: `69d5b4ef872677b03267debd3afe9aa77b7b8956dc0dc0d1e5bef18d6efba45a`.
- DLL SHA-256: `a199db9b1a9a50667ed56947c39bea699e09628e0c67386027f7aa8c9664f13c`.

This exact ZIP is the handoff. Do not rebuild or substitute bytes under its recorded hash. A later documentation-only commit does not change the embedded build-source identity.

## Changes

The owner's existing uncommitted Character.Message fix was copied from the canonical checkout into an isolated release checkout. The original edits remain untouched. All HUD call sites use VanillaMessage; binding setup is guarded atomically and unsupported signatures fail closed. Package, plugin, and assembly are now 1.0.0. Package details identify v1, correct the outdated passive-road-capture claim, and retain the existing gallery. The public changelog explains the API change and repair approach for other mod authors.

## Verified results

- `dotnet test src/ConcernedCartographer.Tests/ConcernedCartographer.Tests.csproj -c Release --nologo`: 578 passed, zero failed/skipped. Includes ten focused compatibility cases against the actual adapter with game-free Harmony/game fixtures.
- `python tools/validate_repo.py`: PASS, including product independence checks.
- `pwsh -NoProfile -File scripts/package.ps1 -Configuration Release -Product ConcernedCartographer`: PASS; performs Release build, required-binary/version validation, and tcli packaging.
- Final DLL metadata audit with Mono.Cecil: assembly version 1.0.0.0; exact informational version above; zero Character/Player.Message member references.
- Installed `assembly_valheim.dll` audit: `void Character.Message(MessageHud.MessageType, string, int amount = 0, UnityEngine.Sprite icon = null, bool log = false)`.
- ZIP CRC, exact six entries, 256x256 icon, manifest 1.0.0, pinned dependencies, README/changelog byte identity, and DLL byte identity: PASS.
- Dependencies unchanged: `denikson-BepInExPack_Valheim-5.4.2333`, `ValheimModding-Jotunn-2.29.2`.
- Independent read-only review: initial adapter initialization issues repaired; re-review found no remaining blockers in scope.
- Existing build warning: Jotunn references unavailable UnityEngine.ProfilerModule; build succeeds with zero errors. Existing test warnings: NameHumanizer CS8600 and xUnit2013 collection-size style.

The compiled-member audit proves removal of the stale binding; it does not prove every Valheim API. Compatibility fixtures substitute Harmony and do not constitute an in-game test. Raw local build/test logs are under `artifacts/cc232/` and are not committed because they include machine paths.

## Exact-package manual check before publication

No new in-game PASS or Thunderstore upload is claimed by this handoff. Use this ZIP in a disposable test profile and record:

1. Startup reports Concerned Cartographer 1.0.0; no MissingMethodException, TypeInitializationException, or repeated mod errors.
2. Open/close the map, build Pathen/Paved roads, and confirm expected road ink and marker behavior.
3. Exercise ordinary HUD notifications (survey observations and explicit sharing when available); no gameplay interruption.
4. Logout/login and verify roads, managed markers, and map controls recover correctly.
5. Review the log and complete any still-applicable rows of PRE_RELEASE_SMOKE_TEST.md.

The package README and CHANGELOG will become the live details when this exact version is published. This preparation does not update the live Thunderstore page, tag a release, or upload to Thunderstore.
