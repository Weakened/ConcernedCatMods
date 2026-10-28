# Adversarial recovery matrix (#287)

#287 asks that every required failure case has **a test and a stated expected behaviour**, and that no case
resolves by guessing. This file is that list, so the claim can be checked against the tree rather than against
an issue comment. Every test named here was confirmed to exist on `main` when the file was written; a rename
that breaks a row should update the row.

**The invariant every row protects:** container + reservations + placed cost is unchanged across any sequence of
failures. Nothing is conjured and nothing is lost silently. When the runtime cannot prove what happened, it stops
with a repairable journal entry and a readable message instead of guessing.

That invariant is asserted rather than assumed. `CooperationLoopTests` runs an `AssertInvariants` helper after
its steps (including after reloads), and
`CollectionLoopTests.TheConservationCheckFailsWhenTheRecordCreditsWhatWasAskedRatherThanWhatMoved` proves the
check itself can fail. A check that cannot fail would make every row below meaningless.

Test files: `src/Shared.Settlement.Tests/` unless the row says `Foreman.Tests`
(`src/ConcernedForeman.Tests/`).

| Required case | Expected behaviour | Tests | State |
|---|---|---|---|
| Insufficient supplies | A short take or remove is never rounded up. The difference is reported as a reconciliation shortfall against the order; a build waits and names what is missing; placement refuses with the amount. | `CollectionLoopTests.ADropThatCouldNotBeTakenNeedsAttentionAndIsNeverRetaken`, `FaultInjectionTests.ARemoveThatTakesLessThanWasAddedIsUncertainWithTheEvidence`, `FaultInjectionTests.MaterialThePlayerTookIsNeverReplacedAndCanBeRecordedAsLost` (`ShortfallsFor`), Foreman.Tests `ShelterBuildLoopTests.A_chest_that_is_short_waits_and_names_what_is_missing`, `PlacementAuthorityTests.Cost_comes_out_of_the_reservation_and_a_shortfall_refuses_with_the_amount` | Covered |
| Unreachable work | Bounded retries with backoff, then the source or chest is left alone or the order pauses, and the order says why. Nothing is credited. | `CollectionLoopTests.AnUnreachableChestPausesAfterBoundedRetries`, `AnUnreachableSourceIsRetriedWithBackoffThenLeftAlone`, `WhenEverySourceIsUnreachableTheOrderSaysSo` | Covered |
| Cancellation mid-order | Cancel is accepted from every non-final state. Cancel is not a refund: only measured, still-held material is returned, and Gunnar is stopped or parked mid-haul without a refund being invented. | `CollectionLoopTests.CancelIsAllowedFromEveryStateThatIsNotFinal`, `CancelIsNotARefund`, `CooperationLoopTests.CancellingMidHaulStopsOrParksGunnarAndRefundsNothing`; Foreman.Tests `BuildRecoveryTests.Cancel_returns_unspent_custody_even_when_a_previously_paid_piece_is_missing` | Covered |
| Two simultaneous orders | A source held by one order is never claimed by another. One active order per worker is the deliberate first-playable limit, so two orders on one worker cannot arise. | `CollectionLoopTests.ASourceHeldByAnotherOrderIsNeverClaimedTwice` | Covered within the one-order-per-worker limit |
| Disconnect and reload at reserve / carry / commit / cancel | The active order is recovered after a reload. A parked order restates why and keeps the player's own pause. A commit started since the plan is never refunded. An order known only by its reservation is still cancelled and returned. Request ids are never reused across a reload. Rows the world rolled back are voided by the world-save marker rule. | `CustodyRecoveryTests.TheWorkersActiveOrderIsRecoveredAfterAReload`, `ARebindReplacesOnlyTheScopeAndTheDeliveryAndSurvivesTheNextReload`, `AReloadRestatesWhyAnOrderIsParkedAndKeepsThePlayersOwnPause`, `DesignationGuardTests.ACommitStartedSinceThePlanIsNeverRefunded`, `AnOrderKnownOnlyByItsReservationIsStillCancelledAndReturned`, `CustodyExecutorTests.ARequestIdMintedFromTheRevisionIsNeverReusedAcrossAReload` | Covered |
| Reload at place (construction) | Reopening at each material transition (`draw-intent`, `draw-receipt`, `commit-intent`, `commit-receipt`, `refund-intent`, `refund-receipt`) keeps the approval and never repeats an uncertain effect. A paid piece missing from the loaded world never authorises a second payment. Clearing supply returns held build material exactly once. | Foreman.Tests `BuildRecoveryTests.Reopen_at_every_material_transition_keeps_approval_and_never_repeats_uncertain_effects` (six cuts), `A_paid_piece_missing_from_the_loaded_world_never_authorises_a_second_payment`, `Clearing_supply_cancels_held_build_material_then_measured_cleanup_returns_it_exactly_once` | Covered |
| A transfer whose outcome is unknown | It stops without replay or compensation, and the uncertainty is recorded rather than resolved by a guess. | `CooperationLoopTests.AnUncertainTransferStopsWithoutReplayOrCompensation` | Covered |
| No compatible authority | Nothing is marked, written or moved. Losing authority pauses before the next mutation, and clearing is refused outright once authority is lost. | `DesignationTests.WithoutAuthorityNothingIsMarkedAndTheWorldIsNotEvenAsked`, `FaultInjectionTests.WithoutAuthorityNothingIsWrittenOrMoved`, `CollectionLoopTests.LosingAuthorityPausesBeforeTheNextMutation`, `DesignationGuardTests.ClearingIsRefusedOutrightOnceAuthorityIsLost` | Covered |
| Designated container emptied by the player mid-order | Material the player took is never replaced and can be recorded as lost. Re-marking a stale chest returns what was held against it and cancels its order, and the refund keeps the container's epoch. | `FaultInjectionTests.MaterialThePlayerTookIsNeverReplacedAndCanBeRecordedAsLost`, `DesignationStaleRemarkTests.ReMarkingAStaleChestReturnsWhatWasHeldAgainstItAndCancelsItsOrder`, `ARefundKeepsTheContainersEpochInTheRecord` | Covered |
| Harvest area cleared | For collection: an exhausted area ends in attention, kept distinct from an unloaded one, and cancelling refunds nothing. Felling does not exist, so there is no felled area to clear. | `CooperationLoopTests.AnExhaustedAreaEndsInAttentionAndCancellingRefundsNothing` | Covered for collection; felling waits on the owner's #282 decision |
| Settlement area overlapping a ward | Ground inside somebody else's ward is refused when it is marked. A ward check that could not be made refuses. Re-marking stays idempotent once the check starts refusing. | `DesignationTests.GroundInsideSomebodyElsesWardIsRefusedWhenItIsMarked`, `AWardCheckThatCouldNotBeMadeRefuses`, `ReMarkingIsIdempotentEvenWhenTheWardCheckHasStartedRefusing` | Covered |
| Invalid housing | — | none | **Not covered.** There is no resident admission to invalidate until the owner decides what a resident is (#356). |

**Status for #287:** every required case but one has a test and a stated expected behaviour. Invalid housing is the
exception, and it is waiting on an owner decision (#356), not on code. Harvest-area clearing is covered for
collection only, because felling is unauthorised (#282).
