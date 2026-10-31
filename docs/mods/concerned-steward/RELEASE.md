# Concerned Steward 0.1.0 - release notes for the maintainer

Owner decisions (2026-10-27): the first Steward upload is **0.1.0**, and
**Concerned NPC is published with it**, after the owner's smoke test. Nothing in
this document has been run; it describes the order and the checks. Publication
needs the owner's explicit approval (`CLAUDE.md`: never `tcli publish` without it).

## Publish order - Concerned NPC first

Concerned Steward 0.1.0 hard-depends on `TheConcernedCat-ConcernedNPC = 0.5.0`
(`src/ConcernedSteward/Package/thunderstore.toml`) and requires it at load
(`[BepInDependency("com.theconcernedcat.valheim.concernednpc", "0.5.0")]` in
`src/ConcernedSteward/Plugin.cs`). Uploading Steward first would publish a
package whose dependency does not exist on the store, which is uninstallable.

1. **Concerned NPC 0.5.0** (`TheConcernedCat-ConcernedNPC`). Its CHANGELOG 0.5.0
   section covers everything merged into the library since 0.4.0: #443 (worker
   with presentation), #445 (`JobTarget.Carries`), #464 (the one figure dresser),
   #478 (worker-body hair), #484 (the shared keepsake). Concerned Foreman and
   Concerned Teamster also pin `0.5.0` in their manifests (Teamster's load-time
   `BepInDependency` has no minimum version; Steward's and Foreman's require
   0.5.0); neither is published by this release.
2. Wait until the store lists `TheConcernedCat-ConcernedNPC-0.5.0` and it installs
   from a mod manager.
3. **Concerned Steward 0.1.0** (`TheConcernedCat-ConcernedSteward`).

`scripts/publish.ps1` accepts both products (slugs `concernednpc` and `steward`).
`scripts/package.ps1` builds whatever is in the working tree; run it from a clean
checkout of the exact commit, because it does not check.

## Before either upload

- [ ] The owner's smoke test in `PRE_RELEASE_SMOKE_TEST.md` is done on the exact
      candidate DLLs, including section 3b (her figure at rest; item 5, her
      worker body's hair) and 7b (retire and come back). Every row there is
      OWNER GO-AROUND PENDING until then.
- [ ] Both candidates are built by `scripts/package.ps1` from the same exact
      `main` commit, under the build lock, and audited: ZIP and DLL SHA-256,
      `InformationalVersion` = `<version>+<commit>`, entry list (manifest, icon
      256x256, README, CHANGELOG, the product's own DLL only - no game DLLs, no
      publicized assemblies, no PDB), and the manifest dependencies:
      - NPC: BepInExPack, Jotunn.
      - Steward: BepInExPack, Jotunn, `TheConcernedCat-ConcernedNPC-0.5.0`.
- [ ] Each CHANGELOG's top section is the version being uploaded and describes
      what ships.
- [ ] The store's existing versions are checked first: a version number already
      on the store is consumed and cannot be reused.
- [ ] `scripts/publish.ps1` packages again and `tcli publish` builds again, so the
      bytes uploaded are not automatically the audited ZIP: confirm the uploaded
      ZIP's SHA-256 matches the audited candidate, or audit it again.

## After

- Record the published ZIP hashes and store timestamps beside the candidates.
- In-game rows that the owner observed can then be closed on their issues;
  anything not observed stays OWNER GO-AROUND PENDING.
