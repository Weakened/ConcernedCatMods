# Gunnar: the #297 coverage matrix (#462)

Status: **automated rows code-complete; every in-game row is OWNER GO-AROUND
PENDING.** #297 ("A Fair Load") is **not** closable from this document: it may
be reported closable only with observed evidence on an identified candidate,
row by row below. No package was built and nothing was published.

## Where each part lives

| Issue | Part | Document |
|---|---|---|
| #456 | Scoped companion record, `Companions/Gunnar` opt-out, display name | `GUNNAR_COMPANION.md` |
| #457 | The worn harness buckle (shared keepsake) | `GUNNAR_KEEPSAKE.md` |
| #458 | His presentation figure at home and the hand-over to his worker body | `GUNNAR_PRESENTATION.md` |
| #459 | Catalogued lines and talking to his figure | `GUNNAR_DIALOGUE.md` |
| #460 | Recruitment against an explicitly chosen real cart | `GUNNAR_RECRUITMENT.md` |
| #461 | Camp idle and poses (Lane H, PR #515 open when this was written) | `GUNNAR_CAMP.md` |
| #313/#314/#317, #381 | The haul, collection tour, deposit and death drop (not redone here) | `GUNNAR_HAULING.md`, `GUNNAR_COLLECTION.md` |

## The #297 matrix

"Automated" names the tests that fail when the protected property is removed.
The xUnit tests and `tools/validate_repo.py` run in `scripts/verify.ps1` (the
gate); the `tools/tests` plant suites (the figure audit's plants) run in CI,
one job per class (`repo-checks.yml`), and were run locally before each push.
"Owner" is the in-game row and its state.

| Row | Requirement | Automated | Owner |
|---|---|---|---|
| G01 | Fresh introduction: buckle, examine, story, objective | `GunnarIntroductionFlowTests` scenario 0; `GunnarKeepsakeTests` | OWNER GO-AROUND PENDING (E1) |
| G02 | Legacy 1.0.x save keeps every tool before meeting him | `GunnarNeverMetUpgradeTests` (a never-met character gets no figure, no hire and writes nothing; no Teamster file outside a Companions folder names any companion type, bar two pinned ports); `GunnarCompanionRecordTests.ExistingFeatureGatesHaveNoCompanionDependency` (every gate, and Plugin.cs's hooks one by one); flow scenario 1 keeps a synthetic legacy file byte for byte | OWNER GO-AROUND PENDING (E1) |
| G03 | Advanced introduction: an existing cart counts, only when chosen | `GunnarCompanionRecordTests.AlreadyOwningACartIsNotAnAcceptedAssignment`; `GunnarRecruitmentCartPortTests.AnExistingParkedAssignmentOfThisSameCartCanCount`; `GunnarRecruitmentTests` explicit confirmation | OWNER GO-AROUND PENDING (E3) |
| G04 | Early no-cart state: visitor at home, "Bring Gunnar a usable cart" | `GunnarIntroductionFlowTests` (no work lease is created by the story: the lease book's revision stays 0 and no lease is active); `GunnarBodyTests.VisitorAndRecruitedCanPresentAtRest`; `GunnarTalkTests.IntroductionAndToolsOnlyDoNotPretendHeWasRecruited` | OWNER GO-AROUND PENDING (E1, E2) |
| G05 | Existing cart accepted | `GunnarRecruitmentTests`; `GunnarRecruitmentCartPortTests` | OWNER GO-AROUND PENDING (E3) |
| G06 | Wrong, in-use or unauthorized cart refused | `GunnarRecruitmentTests` (every `GunnarCartCheck`, post-prompt recheck, changed identity) | OWNER GO-AROUND PENDING (E3) |
| G07 | Simultaneous assignment | `GunnarRecruitmentTests` second prepare is busy; `HaulingExecutionAssignmentTests` | OWNER GO-AROUND PENDING (E3) |
| G08 | Stale identity after relog | `GunnarRecruitmentTests` epoch/character; `GunnarCompanionRecordTests` scope; `GunnarBodyTests.WorldUnloadInvalidatesBothLeasesAndForgetDropsThem` | OWNER GO-AROUND PENDING (E3) |
| G09 | Cart destroyed or unloaded | `GunnarIntroductionFlowTests` lease invalidation; `HaulingExecutionLeaseTests`, `HaulingExecutionSignalTests` | OWNER GO-AROUND PENDING (E4) |
| G10 | Bed moved during a haul | `GunnarBodyTests.SavedWorkerAnywhereOutranksFigure` (no figure while a body exists, wherever home is); `GunnarCampTests.ChangedBedRepositionsOnlyThePresentationAndWorkerSuppressesIt` | OWNER GO-AROUND PENDING (E4) |
| G11 | Player takeover | `HaulingExecutionSignalTests`, `HaulingExecutionTeardownTests` (`PlayerTookOver`) | OWNER GO-AROUND PENDING (E4) |
| G12 | Detach / cancel | `HaulingExecutionLifecycleTests`, `HaulingExecutionTeardownTests`, `CollectionLifecycleTests` | OWNER GO-AROUND PENDING (E4) |
| G13 | Missing optional mods | `GunnarTalkTests` (no Hulgi/Thorstein/progression hint); `HaulingInteropProviderTests`; `CartographerGateTests` | OWNER GO-AROUND PENDING (E5) |
| G14 | Full destination | `DepositOrderGateTests` (a full or unavailable chest leaves the remainder in him) | OWNER GO-AROUND PENDING (E4) |
| G15 | Conserved contents | `GunnarCargoLedgerTests`; `GunnarTourPlanKillTests`; `WorkerRetirementTests`; `WorkerDeathDropTests`; `PickAccountingTests` | OWNER GO-AROUND PENDING (E4) |
| G16 | One body at a time; ordered hand-over | `GunnarBodyTests` (real arbiter, arrival guard, removal failure, second hand-over); `GunnarHandOverOrderTests`; validator `[teamster] #458 figure audit` + `tools/tests/test_teamster_figure_audit.py` | OWNER GO-AROUND PENDING (E2) |
| G17 | Appearance | `GunnarLookTests` (label, token and id read from the installed game, renumbering, language, fallback, palette, clothes, overrides) | OWNER GO-AROUND PENDING (E2) |
| G18 | Dialogue is talk-only and catalogued | `GunnarTalkTests`; `GunnarDialogueCatalogTests`; `HardcodedStringAuditTests` | OWNER GO-AROUND PENDING (E1) |
| G19 | Camp idle and range retention (#461) | `GunnarCampTests` (the domain routine, on main); the public camp facade and the sitting pose are PR #515's and pending until it merges | OWNER GO-AROUND PENDING once #515 is in |
| G20 | World/character/logout cleanup | `GunnarCompanionRecordTests` scope; `GunnarBodyTests` world unload; `GunnarHandOverOrderTests.AWorldUnloadTakesTheFigureDownBeforeTheSharedRuntimeEndsTheWorld` | OWNER GO-AROUND PENDING (E1) |
| G21 | The buckle is picked up once; a refused write never strands the story | `GunnarKeepsakeTests` (real sidecar, reopen, retry, second-write failure) | OWNER GO-AROUND PENDING (E1) |
| G22 | No memento collision with another product's keepsake | `GunnarKeepsakeTests`, `GunnarNativeEnvironmentTests` (other keepsakes reserved, own spot excluded, full buffers defer); `GunnarBodyTests.HeKeepsClearOfAnotherProductsKeepsake` | OWNER GO-AROUND PENDING (E1) |

## Owner evidence rows

Separate captures, on one identified candidate (source SHA, ZIP and DLL hashes
recorded by the lead), in a disposable world and a dedicated profile. None is
satisfied by unit tests, by Hulgi's or Thorstein's footage, or by a historical
worker capture.

| Row | What to capture | State |
|---|---|---|
| E1 Progression / introduction | Never-met 1.0.x save: every utility works (G02). New character: buckle, examine, inscription and introduction, the objective, talking to the visitor (G01, G04, G18, G21, G22); relog and world change leave nothing behind (G20) | OWNER GO-AROUND PENDING |
| E2 Appearance and one body | Gunnar at home front, side and neutral daylight against the reference; the resolved ids in `ct_haul status`; `ct_haul spawn` places him where he stood and never beside a figure; `ct_haul retire` brings him home (G16, G17) | OWNER GO-AROUND PENDING |
| E3 Cart assignment | An existing cart chosen and confirmed; wrong, in-use and unauthorized carts refused; a second assignment busy; a relog invalidates a pending prompt (G03, G05-G08) | OWNER GO-AROUND PENDING |
| E4 The laden haul | A loaded haul to a destination; the bed moved mid-haul; the player taking the cart; detach and cancel; a destroyed or unloaded cart; a full destination; contents counted before and after (G09-G12, G14, G15) | OWNER GO-AROUND PENDING |
| E5 Missing optional mods | Teamster alone, without Cartographer or Foreman: start-up clean, no Hulgi/Thorstein lines (G13) | OWNER GO-AROUND PENDING |

## Out of scope (per #297)

#280's construction clearance, fleets, base-wide chest collection, cross-world
routing and any new carve-out.

## Closure

#297 is reported closable only when E1-E5 are observed and every row above is
green on the same candidate. Until then it stays open.
