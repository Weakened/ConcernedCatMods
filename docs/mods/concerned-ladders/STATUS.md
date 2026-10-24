# Concerned Ladders — observed status

Updated 2026-10-24. **Development source candidate. Not built against Valheim,
not installed, and not ready to run.** This file records evidence, not intended
behavior. `PROJECT.md` defines the intended behavior.

## Source and recovery

- Repository: `Weakened/ConcernedCatMods`.
- Observed source baseline: `89ce8b68e05c4494e5d14165482eca8d2486c786`.
- Local integration branch: `feat/cl-001-vertical-ladders`.
- The final handoff's `MANIFEST.json` records its full candidate commit, base,
  file hashes and bundle prerequisite. The source commit is not duplicated here
  because committing this status changes that commit.
- GitHub issue creation and feature-branch creation both returned HTTP 403,
  `Resource not accessible by integration`. No remote feature branch, issue or
  PR was created. CL-001–003 are local work-item keys.
- A recoverable Git bundle and binary-safe patch are supplied with the handoff.
  They contain new work relative to the baseline, not licensed game files.

## Implemented in source

| Area | Current implementation |
|---|---|
| Product | Independent Concerned Ladders 0.1.0, DLL/GUID/package, solution and CI entries |
| Pieces | Wood, copper and iron; original 2 m rails/rungs; normal hammer recipes; six snap transforms |
| Persistence | Three stable custom prefab IDs; ordinary native build-piece components; registration remains when climbing is disabled |
| Traversal | Explicit supported mount, connected stack search, climb/hold/reverse, checked landing transfer, deliberate Jump release |
| Collision | Actual capsule dimensions and front-face offset, swept paths and footing checks, colliders remain enabled |
| Lifecycle | Local-body ownership checks, bounded hook adapter, gravity restoration, invalid-world/body/ladder cleanup |
| Animation | Native walking parameters as the accepted fallback; no authored climbing clip or IK |
| Compatibility | Same mod/version required on participants; Foreman ladder-motor conflict refuses mounting |
| Profile tooling | Deterministic dependency-only import; exact-profile deployment preflight and receipt; no launch claim |
| Handoff | Full specification, architecture, acceptance matrix, Claude/Codex prompts, profile instructions |

This table describes code that exists. It is not evidence that these behaviors
work in an actual Valheim session.

## Observed checks

| Check | Result | Scope and limitation |
|---|---|---|
| Repository validator | PASS | Full `python tools/validate_repo.py`, including new product, all existing products, solution and independence audits |
| New C# policy tests | PASS | 31 passed, 0 failed, 0 skipped in Release; BCL-only production rules, not the Unity/plugin assembly |
| New profile tests | PASS | 7 passed; deterministic archive and invalid destination/version/overwrite protections |
| C# syntax audit | PASS | Official Roslyn parser, C# 10, 10 product source files, 0 syntax errors; no semantic compilation or game stubs |
| PowerShell parser | PASS | Deployment script has 0 syntax errors |
| Deployment refusal probes | PASS | Wrong profile is refused; correctly named path on non-Windows is refused; no deployment occurs |
| Independent source review | COMPLETED | Traversal and registration corrections reviewed; live-game API uncertainty remains |
| Independent workflow review | COMPLETED | Dependency-before-build and package-before-final-verify ordering corrected |
| Full Python mutation suite | NOT RUN | Must run in an isolated tree because existing tests temporarily edit product files |
| Full solution restore | NOT PASSED | Attempt exited 1 without useful diagnostics in this environment; local game/reference setup required before retry |
| Mandatory `scripts/verify.ps1` | BLOCKED | Exited 1 before building: `Environment.props is missing. Copy Environment.props.example to Environment.props and edit the paths.` |
| Valheim plugin DLL/package | NOT PRODUCED | No licensed game-reference build was completed |
| Profile installation | NOT RUN | Import artifact prepared; manager/device access unavailable |
| In-game tests | NOT RUN | All building/traversal/lifecycle/visual/controller/multiplayer rows remain unobserved |

The narrow successful checks do not satisfy the mandatory full product gate.
No `verify: PASSED` summary, real plugin binary, game screenshot or gameplay
recording is claimed.

## Review corrections already applied

The initial review caught and corrected the capsule-to-ladder stand-off, unsafe
Interact departure without footing, reversal during an intermediate transfer,
owner loss before animation writes, current capsule-shape invariants, and a
partial registration retry that would reuse Jotunn's already cached clone.
Piece readiness now gates climbing; icon failure retains registration with a
diagnostic; removed donor visuals do not retain a wet-visual reference. The
runtime uses the repository's scrubbed exception reporter.

These fixes were checked in source and in applicable pure tests. Collision,
animation and registration still require actual game observations.

## Prepared profile

The generated import is `TCC-Ladders-Dev.r2z`, containing only these published
dependency references:

| Package | Pin |
|---|---|
| denikson-BepInExPack_Valheim | 5.4.2351 |
| ValheimModding-Jotunn | 2.30.2 |

Artifact SHA256:
`e7acb27a9844b3579ea154ef533fda4d66742ec5db4d3cff09d014bbbb2c310f`.
State: `DEPENDENCY_IMPORT_PREPARED`; `profileInstalled=false`;
`gameLaunched=false`. The import contains no Concerned Ladders DLL, game files,
scripts, credentials, character or world data.

Publisher availability was checked on 2026-10-24. Compatibility with the owner's
installed game has not been observed. See `ARCHITECTURE.md` and
`PROFILE_SETUP.md` for primary references and the exact local sequence.

## Actual blockers and next execution

The connected BLD device was identified, but Desktop Commander reported that
its monthly usage allowance was exhausted and instructed not to retry. No
local profile, game installation or save was changed through that service.
The cloud checkout has no licensed Valheim installation or private
`Environment.props`. GitHub write actions were separately refused as above.

Run the supplied `CLAUDE_PROMPT.md` on BLD as integration lead and use
`CODEX_PROMPT.md` for the disjoint review/presentation lane. Import the preserved
source in an isolated worktree, install the profile dependencies, inspect the
real APIs, build with the existing machine-wide lock, and complete the test
matrix on disposable saves. Before final deployment, package first, run the
full verify afterward, compare the package/DLL hashes and deploy those bytes.
Launch via the profile's supported Start modded flow and record actual results.

Do not open the owner's real saves, change existing profiles, publish, tag or
merge to main as a substitute for this missing evidence. The intended next
handoff is a visibly working candidate with a precise receipt and only a few
optional visual preference checks left for the owner.
