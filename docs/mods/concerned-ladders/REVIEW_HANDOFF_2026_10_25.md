# CL-002 independent review and piece handoff

**Historical review of the original candidate.** The source IDs and findings
below describe that earlier scope. For the owner's later air-grab, real-jump,
two-face, stamina/skill and snap revision, use
[OWNER_REVISION_2026_10_25.md](OWNER_REVISION_2026_10_25.md), the current
[TEST_PLAN.md](TEST_PLAN.md) and [STATUS.md](STATUS.md). This earlier review
cannot establish acceptance of those new requirements. Its original evidence
is retained below without being promoted into a current PASS.

Reviewed 2026-10-25. Local work item CL-002 from the owner's handoff; this is not a GitHub issue number.

**Source review complete; gameplay acceptance remains unobserved.** No confirmed P0/P1 was established in this review. One runtime P2 remains for the lead. The actual plugin was not built or run in this lane. No profile was installed or changed, no save was opened, and no screenshots or video were captured.

## Source and ownership

- Supplied baseline: `89ce8b68e05c4494e5d14165482eca8d2486c786`.
- Exact review base: `25d392f415d7163ff1e60915a69914e76f3e0cce`.
- Piece-fix commit: `ba48dae8eb4586cbb367e2f37a47227bdb405324`.
- Branch: `fix/cl-002-piece-review`.
- Worktree: `C:\Users\erenc\.codex\worktrees\ladders-piece-review\ConcernedCatMods`.
- Final handoff head is obtained with `git rev-parse HEAD`; the documentation commit containing this report follows the piece-fix commit without changing its source.
- The candidate identity was verified from the supplied bundle and manifest. All 46 manifest file hashes matched. No separate active integration-lead chat was identified; this does not establish that the lead has no newer candidate. Confirm the live integration head before cherry-picking.
- Only this lane writes this worktree. A bounded subagent performed read-only runtime and final-piece-diff audits. Runtime, solution, CI, pins, profiles, other products and the canonical integration tree were not edited.

Changed files relative to the review base:

1. `src/ConcernedLadders/Pieces/LadderPieceRegistration.cs`.
2. `docs/mods/concerned-ladders/REVIEW_HANDOFF_2026_10_25.md` (this report).

## Findings and fixes

| Priority | Status | Condition and consequence | Correction |
|---|---|---|---|
| P2 | Fixed in source, not observed in game | `RenderManager.Render` can return null without throwing. The old branch silently retained the donor icon and logged ordinary registration success; B02 could appear healthy despite an icon failure. An empty sprite was also accepted. | Validate sprite, texture and rectangle before assignment. Route failure through the existing scrubbed cosmetic diagnostic and retain the donor icon and stable registration. |
| P2 | Reference cleanup completed; no observed gameplay failure claimed | Donor hierarchy removal left serialized snow-renderer references and the non-solid-renderer list untreated. Unity's destroyed-object checks can hide the stale references; this was not evidence of a live crash. | Clear the actual `WearNTear.m_snow`, `m_snowWorn`, `m_snowBroken` fields and replace `m_nonSolidRenderers` with a fresh list on the clone. Preserve structural values, effects and replacement damage-state roots. |
| P2 | Open; runtime belongs to the lead | `Runtime/LadderRuntime.cs:230`: excessive or nonfinite positional drift triggers release, but is omitted from the external-control classification. A still-owned, alive player moved by another controller without a persistent teleport/attach flag then has velocity zeroed and fall altitude rewritten at lines 446-450. A downward correction can inherit the old ladder altitude. | Include drift failure in the external-control release classification. Preserve velocity, fall reference and pose on that path while restoring ladder-owned gravity. |

Minimal proposed runtime diff, deliberately **not applied**:

```diff
--- a/src/ConcernedLadders/Runtime/LadderRuntime.cs
+++ b/src/ConcernedLadders/Runtime/LadderRuntime.cs
@@
-                Release(false, !owned || !free || !worldPresent || player.IsDead());
+                Release(false, !owned || !free || !worldPresent || player.IsDead() ||
+                    !LadderRules.Finite(drift) || drift > 1.5f);
```

Lead validation for the runtime correction: exercise a disposable mid-climb displacement with ownership retained and no persistent native teleport flag. Supply a distinct external velocity/fall reference. Expected: the session relinquishes control without zeroing that velocity or restoring the stale ladder altitude. Observed here: the overwrite follows directly from the current source; no game reproduction was performed. Test the release classification/cleanup contract, not just that `MustRelease` returns true. Repeat L06 and ordinary Jump/fall recovery afterward.

## Exact piece contract reviewed

- Stable IDs remain `tcc_ladder_wood`, `tcc_ladder_copper`, `tcc_ladder_iron`.
- Inactive-parent clones use `woodwall` and `woodiron_pole`; donor prefab instances are not edited.
- 2 m height, 0.8 m width, endpoints `(0,0,0)` / `(0,2,0)`, +Z open face, one-third-metre rung pitch and six direct-child snap points remain unchanged.
- The body collision face remains Z=0.24 m, and `FrontFaceDepth` is derived from that collider. Collision remains outside the three visibility-switched wear roots.
- Costs remain Wood4 / Copper2+Wood1 / Iron2+Wood1; workbench/forge, health 200/400/800, material discovery and native structural/effect components remain as supplied. Both metal variants retain their donor's support class.
- Loaded materials are selected by exact supplied names and copied per damage state. Their actual availability and appearance were not observed in the game.
- Icon errors remain cosmetic; partial prefab registration failure remains terminal for the session. No alternate save IDs or retry were added.

## Runtime review scope and limits

Source inspection covered all eight requested boundaries: local-body ownership before motion/pose writes; complete bounded endpoint-chain search; actual capsule shape/stand-off/sweeps; supported Interact exits and deliberate Jump release; processed input and overlay gating; lifecycle/restoration/fall tracking; explicit Foreman conflict detection; scrubbed diagnostics and segment registration lifecycle.

The previously corrected stand-off, safe Interact, supported bottom, cached-ID retry refusal and midspan reversal fixes remain present. They were not reported again as defects. The run searches the complete loaded registry, rejects ambiguous joins/overlaps, and bounds registry/run size. Only the player's own colliders are excluded from collision checks; surrounding walls and actors remain queried.

Native `Player.SetControls` in the installed assembly converts movement using flattened look direction; `TakeInput` checks the named menus, chat, map and UI states. This supports the intended input projection but does not prove controller/remapping or extreme camera-pitch behavior. Actual capsule queries, transfer surfaces, dynamic removals, support, network roles and registry cleanup still require the test matrix.

The walking fallback is the supplied native parameter path. No new animation or IK was authored. Source inspection cannot prove that an animator enters the intended cycle, freezes on hold, reverses coherently, restores equipment, or replicates to an observer. P02-P04 remain unrun. Perfect animation is not made a new blocker; the accepted walking fallback itself must be observed.

## Native API and upstream evidence

Read the installed licensed `assembly_valheim.dll` with `ilspycmd`, without copying or packaging the assembly. Its SHA256 was `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`.

Confirmed the actual public WearNTear snow/list/visual fields used by the change; native Awake constructs its renderer data for new instances, and collider bounds are populated from actual colliders. No guessed private cache fields were added. Also inspected current `Character.UpdateMotion(float)`, `Jump(bool)`, `CustomFixedUpdate`, fall-altitude state, animation parameter writes and `Player.TakeInput` / `SetControls`.

Primary Jotunn 2.30.2 source inspected:

- [CustomPiece: clone/config/validation](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Entities/CustomPiece.cs).
- [PrefabManager: inactive container and cloning](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Managers/PrefabManager.cs).
- [RenderManager: null/empty results and headless handling](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Managers/RenderManager.cs).
- [PieceConfig](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Configs/PieceConfig.cs) and [PieceUsages](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Configs/PieceUsages.cs).

The native API inspection is not a successful semantic plugin compile. No fabricated game stubs were used.

## Checks actually run

| Command/check | Result | Limit |
|---|---|---|
| `git bundle verify <supplied bundle>` | PASS | Baseline prerequisite exists locally. |
| All manifest file SHA256 comparisons | PASS, 46 files, zero mismatches | Pack integrity, not gameplay. |
| `dotnet test src/ConcernedLadders.Tests/ConcernedLadders.Tests.csproj --configuration Release --nologo` | PASS, 31 passed / 0 failed / 0 skipped | Game-free domain suite on the exact imported candidate; subsequent changes affect only Pieces and this report, which that project does not compile. Not product-build evidence. |
| `python -m unittest discover -s tools/tests -p test_ladders_profile.py -v` | PASS with limitation: 6 passed, 1 skipped | Symlink test skipped because this machine cannot create test symlinks. No installed profile touched. |
| `python ./tools/validate_repo.py` | PASS, exit 0 | Includes identity, independence, pins, solution and source audits. |
| `git diff --check` | PASS | Final code diff reviewed independently after fixes. |
| `pwsh ./scripts/build.ps1` / `pwsh ./scripts/verify.ps1` | NOT RUN in this lane | Owner-assigned lead alone runs licensed builds through the existing machine-wide lock. This worktree has no Environment.props; local NuGet cache inspected contained Jotunn 2.29.2, not the target 2.30.2. No verify summary or plugin build is claimed. |
| Full Python mutation suite / package / deploy / game | NOT RUN | Focused checks above do not promote A02/A05-A06/A08-A09 or live acceptance rows. |

## Lead integration and live-test request

Take the piece-fix commit only after checking the actual integration head, resolve the open runtime P2 in its owned lane, and review the resulting final diff. Keep existing profiles and saves intact. The lead configures the isolated dependency references and performs the existing locked build/verify/package/deploy sequence; do not infer readiness from this review.

Run B01-B12 and P01-P04/P07 against the resulting exact source/DLL hash. In a disposable course, explicitly include damage-state transitions, rain/night materials, ten mixed sections, normal rotations/snaps, cold start, menu/world changes, reload and demolition/refund. For cosmetic failure handling, cause a null/empty icon render in a disposable diagnostic run: expected is a donor-icon fallback diagnostic plus all three stable prefabs remaining registered. This injection was not executed here.

Run the full T/L matrix, including controller/remapping, closed top, removed platform, death, ten-section seams and ordinary falling. Record screenshots/video and logs with exact hashes. The review has **no captured screenshot/video references** to offer. Multiplayer/second-client evidence is **BLOCKED in this lane: no second-client session was provisioned or observed**; dedicated-server rows are also unrun. No multiplayer support or release-readiness claim is made.

Once those engineering rows pass, owner visual review can stay short:

1. Wood/copper/iron silhouette and surface finish in daylight and rain/night.
2. Wall stand-off, 2 m seams and placement/snapping feel.
3. Walking fallback during move, hold, reverse and exits.
4. Equipment and ordinary locomotion after leaving the ladder.

No release, publication, tag, push, main merge or integration was performed by this lane.
