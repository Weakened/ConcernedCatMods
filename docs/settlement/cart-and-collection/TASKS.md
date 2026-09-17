# Cart pulling and resource collection: tasks

Contract revision **C1**. One lead, up to five implementation agents, one independent reviewer. Genuine agents only;
a report that was not produced by the agent that did the work is not a handoff.

## 1. Issues and owners

| Issue | Key | Owner | Requirements | Depends on |
|---|---|---|---|---|
| #312 | CC-SET-001 | **Lead** | contracts C1, seams, rule amendments, CI | — |
| #313 | CT-NPC-002 | **A: cart mechanics** | CART-01–04, CART-06 (execution), AUTH-01 (Teamster) | #312; B's planner through `ICartRoutePlanner` (fakes until B lands) |
| #314 | CT-NPC-003 | **B: cart navigation** | CART-05, CART-06 (progress and recovery) | #312 |
| #315 | CF-NPC-004 | **C: Foreman collection** | GATHER-01–06, ARCH-02 (Foreman) | #312; D's `ICustodyRuntime` (fakes until D lands) |
| #316 | CF-NPC-005 | **D: custody and recovery** | DATA-01–05; #283, #293, #294, #299, #300 | #312 |
| #317 | CC-SET-002 | **E: cooperative orders and UI** | COOP-01–04, ARCH-03 interop | #312; A's `IHaulService`, C's `IWorkerMotion`, D's `ICustodyRuntime` (fakes until each lands) |
| — | — | **R: independent review** | exact-head review of each integrated slice; no edits | each slice |

Parent issues (#273, #297, #295, #296, #282, #283) are not closed by these. Every PR uses `Refs #`, except where an
issue has no live criteria left (D14.5).

## 2. File allowlists

**Rules:**
- Agents create and modify files only inside their allowlist. A needed change elsewhere goes to the lead in the
  handoff as a precise diff and is not made directly.
- **Contract files are read-only for every agent:** `src/Shared/Workers/**`, `src/Shared/Interop/*.cs`,
  `src/Shared/Interop/Haul/HaulContract.cs`, `src/Shared/Settlement/Collection/{CollectionOrder,SourceObservation,CollectionAttention}.cs`,
  `src/Shared/Settlement/Custody/MaterialTransfer.cs`, and in `src/ConcernedTeamster/Domain/Hauling/`:
  `HaulPhase.cs`, `CartLease.cs`, `HaulReasons.cs`, `CartRoute.cs`, `HaulLimits.cs` and `HaulService.cs`. Also
  `src/ConcernedForeman/Runtime/Work/WorkSeams.cs` and every file in `docs/settlement/cart-and-collection/`.
  - Exception: **B** may change default **values** in `HaulLimits.cs`, never names.

**Shared entrypoints:** `src/ConcernedTeamster/Plugin.cs`, `src/ConcernedForeman/Plugin.cs`, every `.csproj`,
`ConcernedCatMods.sln`, `.github/**`, `CLAUDE.md`, `AGENTS.md` and `docs/NAMING_CONVENTIONS.md`.
- These belong to the **lead**.
- An agent that needs a wiring line adds it as a **separate, minimal commit** titled `wiring(<area>): …`, so the
  lead can take or redo it at integration.

| Agent | May create or modify |
|---|---|
| **A** | `src/ConcernedTeamster/Adapters/Workers/**`; `src/ConcernedTeamster/Domain/Hauling/Execution/**`; `src/ConcernedTeamster/Domain/Authority/{TeamsterFeature,CartAuthorityPolicy}.cs`; `src/ConcernedTeamster/TeamsterSettings.cs` (a new `Workers` section only); `src/ConcernedTeamster/Domain/Config/**` (only if adding settings requires the schema tests to change); `docs/mods/concerned-teamster/AUTHORITY_POLICY.md` (the new row); `docs/mods/concerned-teamster/GUNNAR_HAULING.md` (new); tests `src/ConcernedTeamster.Tests/Hauling*Execution*.cs`, `…/CartAuthorityPolicyTests.cs`; `tools/validate_repo.py` (**only** the Teamster CT-002/CT-026/CT-028 sections: a scoped allowlist for `Adapters/Workers/`) |
| **B** | `src/ConcernedTeamster/Domain/Hauling/Navigation/**`; `src/ConcernedTeamster/Adapters/Navigation/**`; default values in `HaulLimits.cs`; `docs/mods/concerned-teamster/CART_ROUTES.md` (new); `scripts/audit-teamster-navigation-api.ps1` (new); tests `src/ConcernedTeamster.Tests/Hauling*Navigation*.cs` |
| **C** | `src/Shared/Settlement/Collection/Planning/**`; `src/ConcernedForeman/Runtime/Collection/**`; `src/ConcernedForeman/Runtime/Settlement/{ForemanWorkerAI,WorkerSitePolicy}.cs`; `docs/mods/concerned-foreman/COLLECTION.md` (new); `scripts/audit-foreman-collection-api.ps1` (new); tests `src/Shared.Settlement.Tests/Collection*.cs` |
| **D** | `src/Shared/Settlement/Custody/**` except `MaterialTransfer.cs`; `src/Shared/Settlement/{Journal,Storage,Tools,Register}/**`; `src/Shared/Settlement/Designations/DesignationBook.cs`; `src/ConcernedForeman/Runtime/Custody/**`; `src/ConcernedForeman/Runtime/Settlement/{ForemanWorkerPrefab,ToolHandover,SettlementRecords,SettlementRuntime,DesignationTools,SettlementToolsCommand}.cs`; `docs/mods/concerned-foreman/{SETTLEMENT_AUTHORITY,CUSTODY_AND_RECOVERY}.md`; tests `src/Shared.Settlement.Tests/{Custody,Journal,Reconciliation,FaultInjection,Tool,Designation,SettlementContract,AtomicTextFile}*.cs` |
| **E** | `src/Shared/Interop/Haul/**` except `HaulContract.cs`; `src/Shared/Settlement/Collection/Cooperation/**`; `src/ConcernedTeamster/Domain/Hauling/Interop/**`; `src/ConcernedTeamster/Adapters/Interop/**`; `src/ConcernedTeamster/Ui/Hauling/**`; `src/ConcernedTeamster/Domain/Ui/Hauling/**`; `src/ConcernedForeman/Runtime/Interop/**`; `src/ConcernedForeman/Runtime/Cooperation/**`; `src/ConcernedForeman/Ui/**`; `src/ConcernedCartographer/Runtime/Interop/**` (Hulgi presence, read-only); new test project `src/Interop.Tests/**` (the lead adds it to the solution and CI); tests `src/Shared.Settlement.Tests/Cooperation*.cs`, `src/ConcernedTeamster.Tests/Hauling*Interop*.cs`, `…/Hauling*Ui*.cs`; `docs/settlement/cart-and-collection/PLAYER_GUIDE.md` (new; the lead merges it into the docs set); `tools/validate_repo.py` (**only** the cross-product rules section, to allow `src/Interop.Tests`) |

## 3. Seams

| Seam | Contract | Implemented by | Consumed by |
|---|---|---|---|
| Route planning and steering goals | `ICartRoutePlanner`, `CartRoutePlan`, `SteeringGoal` | B | A (the only body mover) |
| Motion judgement | `IHaulMotionMonitor`, `HaulMotion` | B | A |
| Gunnar's runtime | `IHaulService`, `HaulSnapshot`, `HaulLegRequest` | A | E (provider), E (Teamster UI) |
| Haul capability | `concernedcat.haul/1` (`CONTRACTS.md` §3) | E (provider in Teamster) | E (consumer in Foreman) |
| Thorstein's walking | `IWorkerMotion` | C | E |
| Pickup | `ISourcePickupPort`, `PickupResult`, `SpawnedDrop` | C | C (the solo loop) |
| Custody runtime | `ICustodyRuntime`, `ITransferExecutor`, `IInventoryPort`, `IMaterialCustodyView` | D | C, E |
| Cooperative delivery | `ICooperativeDelivery` | E | C (the solo loop hands off) |

**Authority over bodies and inventories:**
- **A** alone moves Gunnar and attaches or detaches carts.
- **C** alone moves Thorstein.
- **D** alone writes inventories and the journal.
- **E** orchestrates through the seams and never mutates an inventory, a body or a cart directly.

Until a producer lands, consumers code against the interface, with fakes in their own tests.

## 4. Worktrees, builds, game

- **Worktrees:** each implementation agent works in its own git worktree and branch, created from `main` after #312
  merges:
  - `feat/ct-npc-002-gunnar-hitch-pull` (A)
  - `feat/ct-npc-003-cart-routes` (B)
  - `feat/cf-npc-004-loose-collection` (C)
  - `feat/cf-npc-005-material-custody` (D)
  - `feat/cc-set-002-cooperative-order` (E)
- **Branches:** agents commit on their branch and **do not push or open PRs**. The lead integrates, pushes and opens
  the PRs.
- **Environment:** `Environment.props` is git-ignored. Copy it from `C:\code\ConcernedCatMods\Environment.props` into
  the worktree root before building anything that references the game.
- **Build lock:** builds that compile a product project against the game run through
  `pwsh C:\code\concernedcat-handoffs\2026-09-17-gunnar-thorstein-work\with-build-lock.ps1 -Command "<command>"`, a
  machine-wide mutex, so publicized-assembly generation never races. Pure test projects (`Shared.Settlement.Tests`,
  `ConcernedTeamster.Tests`, `ConcernedCartographer.Tests`) may run without it.
- **Game and deploys:** no agent launches Valheim, deploys to any profile, touches a save, publishes, tags, or changes
  settings, credentials or billing. Only the lead deploys or operates the game, in the isolated profile with the
  disposable character and world, and only with a game window explicitly granted by the owner.
- **Preserved paths:** protected paths are never modified: `Prepare-TCC-Screenshot-Profile.ps1`, `artifacts/**`, and
  the `#280` branch.

## 5. Handoff format (each agent, at each stopping point)

Write `C:\code\concernedcat-handoffs\2026-09-17-gunnar-thorstein-work\agents\<A-E>\HANDOFF.md` containing:
1. issue and requirement ids; contract revision (C1);
2. base SHA and head SHA of the agent branch; worktree path;
3. changed paths, confirmed inside the allowlist;
4. exact commands run and their exact outcomes (test counts, build result, validator);
5. requested contract changes or out-of-allowlist diffs for the lead, if any;
6. evidence rows for `EVIDENCE.md` (requirement → test or audit → result; live rows stay `pending`);
7. remaining risks and the next step.

## 6. Integration order

1. #312 (C1) → `main`.
2. In parallel: **D** (custody foundation), **B** (navigation), **A** (mechanics), **C** (collection), **E** (interop
   and cooperation core first, UI after the seams land).
3. **Slice 1, Gunnar standalone:** A + B → review R → PR → `main`. The live Gate B proof follows when the owner grants
   a game window.
4. **Slice 2, Thorstein solo:** D + C → review R → PR → `main`. Live Gate C follows.
5. **Slice 3, cooperation and UI:** E → review R → PR → `main`. Live Gate D follows.
6. **Gate E** (full fault-injection review), candidate packages with truthful per-product versions, `EVIDENCE.md`,
   final handoff.
