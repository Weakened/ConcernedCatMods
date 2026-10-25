# Concerned Ladders — current readiness

**NOT_READY_RUNTIME_REVISION_MISSING**

Updated 2026-10-25 from source audit, not a new game run. The active request is
[PROJECT.md](PROJECT.md) and [OWNER_REVISION_2026_10_25.md](OWNER_REVISION_2026_10_25.md).
The current documents now require that revision; documentation, localization
or provenance changes alone do not implement it. All new game observations
remain NOT RUN until recorded against a new, identified candidate.

## Source and evidence boundary

| Item | Observed or reported basis |
|---|---|
| Repository / PR | [Weakened/ConcernedCatMods #433](https://github.com/Weakened/ConcernedCatMods/pull/433) |
| Audited PR head | `ce6bb7f7ac7b9862b35090baeed61b8ece39a825` |
| Historical tested build | `7e4e7357359ce9329e4274aec186740cddfea87e`, reported version 0.1.1 |
| Difference between those commits | Only STATUS.md; no new gameplay implementation |
| Earlier imports | Historical record describes two different 0.1.1 ZIPs; a filename/version is insufficient identity |
| New feature version | Required >=0.2.0 and newer than reconciled local work; no successful new build claimed here |
| Current installed DLL / ZIP / Downloads copy | NOT VERIFIED by this audit; no current hash or installed-version claim |
| Actual local continuation | Lead must reconcile current branch, dirty edits and profile; do not discard work newer than this audit |
| Historical environment | Reported Valheim 1.0.20, Unity 6000.0.75, BepInExPack 5.4.2351, Jotunn 2.30.2, TCC-Ladders-Dev; re-observe before new tests |
| Existing issue scope | [#430 pieces](https://github.com/Weakened/ConcernedCatMods/issues/430), [#431 traversal](https://github.com/Weakened/ConcernedCatMods/issues/431), [#432 profile](https://github.com/Weakened/ConcernedCatMods/issues/432) |

The previous STATUS body is preserved verbatim after a historical warning in
[STATUS_0_1_1_HISTORICAL.md](STATUS_0_1_1_HISTORICAL.md). Its passes and source
reviews apply to its older contract. They are not imported into the new matrix.
Any local source/ZIP of unknown identity remains unverified, not presumed installed.

## Revision gap at the audited source

| Requirement | Source finding / new game status |
|---|---|
| Reach and midair grab | Old support/fall guards remain; latest photographed approaches not measured. New R09–R11 NOT RUN. |
| Both clear faces | Policy admits negative-plane feet but runtime routes to front across own box. Same-face lane fix missing; R08/R13 NOT RUN. |
| Real Space hop / side platform | Existing release is not proof of upward, camera/movement-directed jump in every attached mode; R05–R07 NOT RUN. |
| Reliable upper-floor access | Historical exit passes do not reproduce the latest complaint; R02–R04 NOT RUN. |
| Run / stamina / native skill / level 20 slide | Requested runtime absent in audited source; R14–R23 NOT RUN. |
| Truthful building-block reason | Required native-Piece classification and terrain/actor distinction not proved; R34 NOT RUN. |
| Title Case names / native snap labels | Current display-source corrections may be integrated separately; all-three hammer/hover/repair and snap observations R24/R33 NOT RUN. |
| Custom animation | Conditional feasibility/attempt and final fallback evidence R25 NOT RUN; walking alone is not a custom animation. |
| New package / live fingerprint / Downloads copy | A11/R30 identity chain and final-version demonstrations NOT RUN. |
| Multiplayer / controller | Historical BLOCKED rows remain historical. Recheck available endpoints/device; do not invent new passes. |

## What must happen next

1. Reconcile actual local source and active profile, adopt the revised contract
   and implement the missing runtime. Preserve existing independent-product,
   collision, ownership, persistence and cleanup protections.
2. Run tests that distinguish new behavior from the old supported-only/front-lane
   candidate, then current independent source/scope review. Validate real native
   APIs against the installed licensed game, not invented stubs.
3. Freeze a new version/source. Package, full verify, compare, manager-import
   that exact mod ZIP, cold-start and complete the active TEST_PLAN. Do not
   reuse the old 0.1.1 package or import the dependency `.r2z` as a mod.
4. Record package/verified/installed/startup/source identity and identical ZIP
   in the actual Windows Downloads Known Folder. The candidate helper's success
   is `IDENTITY_MATCH_ONLY` with `gameplayVerified=false` and
   `binaryProvenanceVerified=false`; controlled build and gameplay proof stay separate.
5. Record R01–R35 and applicable original gates, six latest-note video segments,
   independent verdict and specific remaining blocked/failed rows.

Only observed results can change readiness to READY_FOR_OWNER_SOLO_TEST or
READY_FOR_RELEASE_REVIEW under TEST_PLAN's rules. Custom animation alone may
be conditionally deferred with evidence. Do not use “owner-test candidate” to
hide missing requested mechanics. No publication, release tag or main merge.

## Recording the next evidence

Create a new ignored evidence directory for the new candidate. Record full build
SHA separately from a later documentation SHA; commit the artifact hashes and
receipt summary if useful without rebuilding to chase that docs commit. Keep
complete local logs/captures outside tracked source. Each result identifies the
build and exact bytes it actually tested. Do not overwrite or relabel the old
0.1.1 run as a new pass.
