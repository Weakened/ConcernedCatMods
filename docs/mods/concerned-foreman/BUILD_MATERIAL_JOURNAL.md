# Build material journal (#398)

Thorstein's production material adapter uses the existing `SettlementJournal`,
`CustodyLedger`, `JournalReplay` and `JournalStore` schema v3. There is no new
file, row kind, schema version or world-object metadata.

Each piece cost has its own request and whole reservation. A phase is still
fetched in one visit; insufficient supply or capacity leaves whole piece costs
undrawn. Unrecorded inventory is not build credit, and another piece's
reservation cannot pay for a rebuild. The runtime retains a request through
retries; an explicit later rebuild gets a new request.

A failed intent also retains its original source. Every retry resolves and
checks that source's availability, reach and whole cost before recording intent,
then checks again before withdrawal. Redesignating a reachable chest cannot
authorize a withdrawal from an inaccessible original source.

| Operation | Persist before mutation | Persist after measured completion |
|---|---|---|
| Draw | `OrderTransition/Reserve`, with request, source, epoch and stacks | `Reserved` |
| Place and pay | `CommitStarted`, with the same payload | `CommitFinished` |
| Return | `OrderTransition/Cancel`, with the same payload | `Refunded` |

The existing v3 material-row fields already support request-bearing order
transitions. Legacy transitions with a request but no material payload or
timestamp keep their order-only meaning, including after migration from v1/v2
to v3. Any material field or timestamp requires full intent validation.
The two intent rows close the interruption windows around draws and refunds;
writing only their receipts would permit a repeated inventory mutation after a
crash. Legacy proof records still replay. A production reservation requires its
matching intent, so the old designation cascade cannot manufacture a refund
receipt by clearing a marker. Such a clear refuses until the real holding has
been returned or repaired.

Payload-free legacy receipts remain readable, but cannot settle a production
request established by a full draw intent. A persisted legacy designation refund
refreshes the live ledger before it is read, so reconciliation and the repair
gate agree with replay immediately. This refresh cannot settle production
holdings or uncertainty, and never credits an unsaved refund.

`ICustodyRuntime.ReserveBuild`, `CommitBuild` and `RefundBuild` delegate to the
game-free writer. A completed retry with the same request and payload returns
`AlreadySatisfied` without invoking the mutation callback or writing another
row. A changed order, source, source epoch or cost is rejected. An uncertain
request is rejected until repaired; it is not an already-completed operation.

`WorldPlacementAuthority` and `WorldPiecePlacer` remain the placement boundary.
The gate runs before the commit callback. The callback persists `CommitStarted`,
installs, checks that the piece is standing, removes the measured reserved cost
through the persisted worker inventory, and only then writes `CommitFinished`.
The host call retains `doAttack: false, cheated: false`.

A false result, exception, failed measurement or missing receipt leaves the
request uncertain. This includes a piece created before payment failed, or one
inventory changed before the other. These states cannot be made atomic against
the game: they stop with a named repair, without automatic compensation or a
second mutation. Failed intent persistence moves nothing. A save that reports
failure after its journal replace is still recognized through the existing
saved-row tracking.

`cf_settle reconcile` lists the build order, request, source key and source
epoch, stacks, and held/uncertain state. World-save markers now classify timed
reservation rows too. A rolled-back or ambiguous request remains blocked for
repair, so loading an older world cannot authorize repeating it.

The build marker/order is still not recovered on reload (#285/#380). Held
reservations from an earlier load are visible and block new work; this change
does not guess a new source binding or automatically refund them. There is no
new command to resolve an uncertain build transaction. That remains a named
manual repair, rather than being passed to collection's `source|destination`
resolution vocabulary.

## Evidence and remaining observation

Game-free coverage is in `BuildMaterialJournalTests`, `WorldBuildMaterialsTests`,
`DesignationGuardTests` and the production-composition tests in
`ShelterConstructionRuntimeTests`. It includes disk round trips, intent and
receipt write failures, failures between mutation halves, duplicate commit and
refund callbacks, changed payloads, source changes, failed inventory persistence
and failed post-payment counts. Existing placement-boundary and exact boolean
tests remain in force.

No live game test was performed for #398. The manual Valheim evidence fields
in the PR template (game/BepInEx/Jotunn versions, profile, disposable world,
steps, logs and screenshots) remain **not run**. The owner go-around still needs
to observe a whole shelter, cancellation near the original source, cancellation
when that source is inaccessible, and reload reconciliation of held and
interrupted transactions in a disposable test world. No release readiness is
claimed.
