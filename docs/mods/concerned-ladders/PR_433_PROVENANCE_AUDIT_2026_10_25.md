# PR #433: source, package and owner-feedback audit

Audit date: 2026-10-25. This is source/evidence inspection, not an observation
of the owner's currently installed DLL. The source corrections accompanying
this report do not implement the missing gameplay revision.

## Finding

Draft [PR #433](https://github.com/Weakened/ConcernedCatMods/pull/433) contains
the source identified in its final **0.1.1** package record. That source still
implements the earlier feature specification. The later owner requests for
midair grabs, powered directional jumping, two clear climb faces,
sprint/stamina, a Climbing skill/slide and native snap labels are absent.

The repository's old instructions explicitly prohibited several of those
features. Its current-at-audit test record accepted falling-grab refusal and
rear-face refusal as PASS. A green review against that obsolete contract does
not establish acceptance of the owner's later request.

The active replacement contract is now
[OWNER_REVISION_2026_10_25.md](OWNER_REVISION_2026_10_25.md). The original
evidence is retained in [STATUS_0_1_1_HISTORICAL.md](STATUS_0_1_1_HISTORICAL.md).
Neither document claims new runtime behavior has been observed.

## Provenance established from Git

| Item | Value |
|---|---|
| PR | #433, open draft at audit |
| Branch | `feat/cl-001-vertical-ladders` |
| Recorded tested implementation | `7e4e7357359ce9329e4274aec186740cddfea87e` |
| Audited PR head | `ce6bb7f7ac7b9862b35090baeed61b8ece39a825` |
| Difference from tested source to PR head | Only `docs/mods/concerned-ladders/STATUS.md` |
| Reported package | `TheConcernedCat-ConcernedLadders-0.1.1.zip` |
| Reported ZIP SHA256 | `6ab77af980eb29871ca16b2452217d360f999b4d9ddb4d2edfd61295ef419ee9` |
| Reported DLL SHA256 | `82a3e45f8ef6938f65ced6c4756b8b1fb569ed281ce2ba5d791388e9260b3649` |
| Reported startup identity | `Concerned Ladders 0.1.1+7e4e735...` |

The exact comparison was checked with Git locally after fetching the PR, not
inferred from its title:

```text
git diff 7e4e7357359ce9329e4274aec186740cddfea87e ce6bb7f7ac7b9862b35090baeed61b8ece39a825 --name-only
docs/mods/concerned-ladders/STATUS.md
```

[GitHub comparison](https://github.com/Weakened/ConcernedCatMods/compare/7e4e7357359ce9329e4274aec186740cddfea87e...ce6bb7f7ac7b9862b35090baeed61b8ece39a825).

**The hashes above are recorded by the local lead, not independently calculated
from the owner's ZIP in this audit.** No candidate ZIP or installed DLL was
attached for byte inspection. The raw receipts, videos and logs are described
as local ignored artifacts; the audited GitHub workflow runs exposed no
candidate artifacts. The connected-device service remained paused by its
monthly quota and was not retried or bypassed.

There were **two different 0.1.1 packages**, from `4c4892c` and `7e4e735`,
according to the historical status. Thus filename and manager version alone
cannot distinguish them. Even a byte-perfect match to the final hash above
would establish the old candidate, not the requested feature revision.

The local lead must first record the actual selected ZIP hash, installed DLL
hash and fresh startup identity before replacing anything. A stale manual
copy, reused filename or different unpushed work is possible; none is asserted
as the cause without that evidence.

## Source findings at the audited PR head

| Owner request | Observed source | Required next result |
|---|---|---|
| Reasonable grab distance | `LadderRules.MountReach` was widened to 1.6 m in a feet/ladder-plane band. Hover always advertises Climb. Final source commit changes a distant refusal message, not eligibility. | One measured physical reach/selection policy used by both hover and Use; reproduce the first two photos without inferring metres from pixels. |
| Space makes an actual jump to a side platform | `Release(true, ...)` uses `outward * LadderRules.Speed`; outward has zero Y. `BeginAirborne` clears ground/walk state but supplies no upward impulse. | Verified native-strength powered departure, movement/camera-directed horizontal choice, side-platform landing and normal fall behavior. |
| Midair grab | `CanMount` rejects unsupported players, downward speed below -0.5 and an existing fall-height difference. The runtime displays the old prohibition. | Legal rising/falling catch succeeds; failed catch changes no fall state. |
| Title Case | English localization contains `Wood ladder`, `Copper ladder`, `Iron ladder`. | `Wood Ladder`, `Copper Ladder`, `Iron Ladder` in hammer, hover and repair feedback. |
| Both sides | Mounting always targets the authored positive/front lane. | Select and retain the physically approached clear face. |
| Truthful building-obstruction error | A rear approach is routed across the ladder's own solid collider, then receives a generic route-blocked message. | Fix the route, and show a building-blocked error only when another build piece actually obstructs it. Do not hide the failure by ignoring all collision. |
| Native snap text | Six raw `snap_*` transform names remain; no label integration was added. | Native snap display with readable localized labels and remapped keys. |
| Sprint/stamina, skill, slide | Fixed-speed runtime, no Run mode/stamina/skill registration or slide policy. | Complete the earlier owner revision; these requests were not cancelled by the six later notes. |
| Custom animation, if feasible | Walking-parameter fallback only. | Investigate the installed rig, attempt a viable prototype, or record a concrete conditional deferral with tested fallback. |

Primary source locations:

- [Mount rules](https://github.com/Weakened/ConcernedCatMods/blob/ce6bb7f7ac7b9862b35090baeed61b8ece39a825/src/ConcernedLadders/Domain/LadderRules.cs#L72-L88)
- [Single-face mount](https://github.com/Weakened/ConcernedCatMods/blob/ce6bb7f7ac7b9862b35090baeed61b8ece39a825/src/ConcernedLadders/Runtime/LadderRuntime.cs#L195-L224)
- [Jump release](https://github.com/Weakened/ConcernedCatMods/blob/ce6bb7f7ac7b9862b35090baeed61b8ece39a825/src/ConcernedLadders/Runtime/LadderRuntime.cs#L551-L567)
- [Labels](https://github.com/Weakened/ConcernedCatMods/blob/ce6bb7f7ac7b9862b35090baeed61b8ece39a825/src/ConcernedLadders/Pieces/LadderLocalization.cs)
- [Snap names](https://github.com/Weakened/ConcernedCatMods/blob/ce6bb7f7ac7b9862b35090baeed61b8ece39a825/src/ConcernedLadders/Pieces/LadderPieceRegistration.cs#L129-L140)
- [Old acceptance record](https://github.com/Weakened/ConcernedCatMods/blob/ce6bb7f7ac7b9862b35090baeed61b8ece39a825/docs/mods/concerned-ladders/STATUS.md#L120-L137)

The earlier top planner also waits until feet reach the run's top and probes
one inward landing. The owner's latest image shows side platforms, which do
not prove that particular inward destination exists. New acceptance must
separately demonstrate a directed jump onto a side platform and a clear
automatic top exit. The whole query capsule is shifted upward to avoid
ground contact; preserve the actual capsule top when reviewing that tolerance,
so a ground fix does not invent an additional headroom obstruction.

## Corrections prepared by this audit

- Correct the three English display names and generic fallback casing in
  source, without changing saved prefab identifiers or localization keys.
- Replace the stale active specification, architecture, tests and role
  prompts with the owner revision. Preserve the earlier status as history.
- Clarify exact ZIP import as the final profile workflow, with no duplicate
  manual DLL and no `.r2z` masquerading as a mod package.
- Add a read-only candidate identity verifier and focused transport/identity
  regression tests. The verifier can compare package/verified/installed bytes,
  version, the full source claim in a fresh startup log and the Downloads copy.
  Its successful result is **IDENTITY_MATCH_ONLY**, never a gameplay PASS or
  cryptographic proof of how a binary was compiled.

These corrections do **not** implement powered Jump, air grabbing, the second
face, revised top transfer, stamina, skill, slide, snap UI integration or custom
animation. They do not create a replacement game DLL or installable mod ZIP.
The local lead must finish the runtime work and observe the revised tests
before returning a new package. Repackaging this partial source is not completion.

## Completion evidence required from the local agents

Freeze the implementation version and full source SHA. Package, run final
licensed `verify.ps1`, reconcile the package to verified bytes, import that ZIP
through the active manager profile, and cold-start the game. Run the identity
verifier on those exact files and record its output with the behavior evidence.

The final demonstration must visibly include range from the supplied approach
scenarios, Space jumping onto a side platform, a successful airborne catch,
mount/climb/exit from both free faces, refusal caused by an actual wall/floor,
Title Case in the three UI contexts, and every readable snap label. Complete
the earlier sprint/stamina/skill/slide requirements too. Independent review
must compare the current owner contract with both the source and those bytes.

Use a new numeric version beyond all 0.1.1 packages. A changed filename,
increased test count, passing old-scope tests, or an updated PR description
cannot substitute for new behavior. Publish only after the repository's
required release checks and explicit publication approval.
