# Custody and recovery: where Thorstein's material and tools are, and how a crash is answered

Issue: [CF-NPC-005 (#316)](https://github.com/Weakened/ConcernedCatMods/issues/316), with
[#283](https://github.com/Weakened/ConcernedCatMods/issues/283),
[#293](https://github.com/Weakened/ConcernedCatMods/issues/293),
[#294](https://github.com/Weakened/ConcernedCatMods/issues/294),
[#299](https://github.com/Weakened/ConcernedCatMods/issues/299) and
[#300](https://github.com/Weakened/ConcernedCatMods/issues/300).
Contract: `docs/settlement/cart-and-collection/CONTRACTS.md` §5 (read at revision C4), decisions D9 and D14.
Authority decisions this extends: [`SETTLEMENT_AUTHORITY.md`](SETTLEMENT_AUTHORITY.md) §5a.

**Evidence state.** Each claim below is one of:
- **automated-tested**: the game-free ledger, executor, journal schema v3, world-save marker rule, reconciliation
  and tool handover procedure (§15 lists the suites);
- **adapter-audited**: the worker body's persistence, the engine inventory ports, the world-save hook and the death
  drop. Every game member they use was re-read in the installed **Valheim 1.0.14** assembly, and they build through
  the build lock;
- **pending**: every row of the live checklist in §14. **Nothing in this document has been observed in the game
  yet.**

---

## 1. The model in five sentences

1. The settlement journal is the one ledger. The world holds the items, and the journal says which order they belong
   to and where they are.
2. Every unit an order has taken is in exactly one **custody place**, or has a recorded **disposition**. Delivery is
   credited once.
3. Nothing is ever minted, compensated or replayed. When the record and the world might disagree, work stops, the
   evidence is kept, and a person answers with one command.
4. A journal row reaches disk the moment it is written. The world effect it describes reaches disk only at the game's
   next save. **World-save markers** decide which rows the loaded world actually contains.
5. After every load, what the record expects is compared with what can actually be seen. Every order then resumes
   Paused, or NeedsAttention with a reason. Nothing resumes by itself.

---

## 2. Places and dispositions

| Place | What it holds | Compared with the world | Units leave it by |
|---|---|---|---|
| `SourceGround` | drops this order's own pick spawned, identified in the same call | never: a drop's id means nothing after a reload | a take to `Worker` |
| `Worker` | Thorstein's own inventory, persisted in his body's object (D9) | at load: the inventory stored in his body; in session: the live one | a deposit, a hand-over, death, or a person's `lost` answer |
| `Cart` | the assigned cart's container, **above** the baseline recorded when the lease began | in session only, under the provider epoch the baseline was recorded with | an unload to `Destination` |
| `Destination` | the delivery chest | never: the chest is the player's, and later changes to it are theirs | terminal: delivered credit is never reversed |
| `Player` | a hold-for-player hand-over | never | terminal |
| `Lost` | a death place, or a person's `lost` answer | never | terminal |

- Only `SourceGround`, `Worker` and `Cart` can be a transfer's source.
- `cf_settle status` shows each resource's progress in six buckets: requested, carried, in cart, delivered, handed
  over, lost.
- **Pre-existing cart cargo.** When a cart lease begins, every stack already in the cart is recorded
  (`CartBaselineRecorded`), material or not. That cargo is never counted, unloaded or refunded as gathered material.

---

## 3. The records

### 3.1 The settlement journal

- **Where:** `BepInEx/config/ConcernedCatMods/ConcernedForeman/settlements/<world id>.home.settlement.tsv`, beside
  the register `<world id>.home.settlement-register.tsv`. `<world id>` is 16 hex digits.
- **Schema 3.** One line per row:
  - `e`: legacy material rows. v3 adds the container epoch, world time and load epoch.
  - `t`: tool rows. v3 adds world time and load epoch, plus `stage=`, `by=handover` and `note=` flags.
  - `c`: custody rows: `c`, sequence, kind, world time, load epoch, then escaped `key=value` fields. A field this
    build does not know is kept and written back.
- **Kinds.** New kinds are appended after the existing ones: `CollectionAccepted`, `CollectionTransition`,
  `PickupStarted`, `PickupFinished`, `TransferStarted`, `TransferFinished`, `TransferResolved`,
  `CartBaselineRecorded`, `LossRecorded`, `HandoverFinished`, `WorldSaveMarker` and `CollectionRebound`. Schema 1
  and 2 files still load, and are written back as schema 3.
- **The closing line (#293).** Each file ends with `end <rows> <last sequence> <checksum>`. A file cut on a line
  boundary, a row missing from the middle, a changed value that still parses, or anything after the closing line is
  **damage**. The file then loads read-only: every readable line is kept, the file is not rewritten, and no new work
  starts. The register carries the same closing line.
- **Durable writes.** A save writes a temporary file, flushes it to disk, then replaces the record. The fallback,
  used when the replace is refused, copies over the record and is **not** atomic. That fallback is exactly what the
  closing line exists to catch.
- **"Persisted" is decided by the file, not by the report.** A row whose save reported failure but which reached
  disk is treated as written. A row that did not reach disk is removed again. Nothing in memory claims more than
  the file holds.

### 3.2 The worker body (D9)

- **Prefab.** `CF_SettlementWorker` is built from `[Settlement] WorkerBaseCreature` when vanilla prefabs become
  available at plugin start, and is registered for every world. The clone is persistent. Its `CharacterDrop` is
  removed (no loot minted on death), and its default and random items are cleared (no gear granted on spawn).
- **Fields in his own object**, never in a vanilla object:
  - `tcc.worker.key`: `foreman/thorstein`;
  - `tcc.worker.inventory`: vanilla's `Inventory.Save` package, the format a chest stores;
  - `tcc.worker.revision`: a count of writes.
- **Written in the same call as the change.** The body subscribes to its inventory's own `m_onChanged`, the way a
  vanilla `Container` does. A pickup, take, deposit or handover is therefore in the body's object before its
  receipt is recorded. Nothing is written before the stored inventory has loaded, so an empty inventory can never
  overwrite a carried one.
- **A failed body write is uncertainty, not success.** When the body could not store its inventory, a
  `Completed` or `Partial` receipt involving him is recorded as `Uncertain`.
- **Not persisted** (CONTRACTS §8): actor modes, source reservations, cart leases and haul phases. They live in
  memory, and a reload asks for them again.

---

## 4. Moving material

### 4.1 One transfer, in the fixed order

`TransferExecutor` runs these steps in this order and no other (CONTRACTS §5.2):

1. **Check.** Nothing is written or moved until every check passes.
   - **Idempotence first.** A repeated request id answers `AlreadySatisfied`, even with authority gone now. The
     same id with a different payload is `RejectedDifferentPayload`.
   - **Then:** work authority, then a writable record with nothing owed to it (§6), then both inventories
     available.
   - **Then the record:** the order is known; the expected revision is current (otherwise `Stale`); this order holds
     the units at the source; and no transfer of the order awaits an answer.
   - **Then the world:** the source physically holds the units, and the destination can accept at least one.
2. **Persist `TransferStarted`.** On failure the outcome is `Refused`: nothing has moved, and the unsaved row is
   removed.
3. **and 4. Move: add before remove.** An engine port never creates an item: a port has nothing to add, and an item
   made from the prefab would lose what the real one carried. Between two engine inventories there are two paths:
   - a whole stack moves with vanilla's own `MoveItemToThis`, which adds and then removes inside one call;
   - part of a stack is added as a clone of that stack's data, and then exactly the count that arrived is removed.
5. **Classify from both inventories' measured counts**, never from what a port said.
   - Equal deltas give `Completed`, `Partial` (the rest stays at the source), or `Refused` when nothing moved.
   - Anything else, or any exception, gives `Uncertain`, with the evidence written into the row (for example
     `worker Stone: 10 -> 3; chest: 0 -> 10; asked 10 (one engine move)`). Nothing is ever compensated.
6. **Persist `TransferFinished`**, and only then settle the ledger. If that write fails, the row is removed, the
   transfer is uncertain in memory, and the next load decides from the record and the actual inventories.

**Why add before remove.** A crash between the two leaves a duplicate. The persisted intent and the counts expose
it, and a person settles it. The reverse order would destroy real items and leave no evidence.

**Request ids** are minted from the order and the custody revision. The revision counts every custody row, including
voided rows and markers, so an id is never reused across reloads.

### 4.2 Pickups

1. `PickupStarted` is persisted.
2. The game's own pick runs once.
3. The drops it spawned are identified.
4. `PickupFinished` is persisted with those drops.
5. Each drop is then taken `SourceGround → Worker` as a transfer (§4.1).

When the drops cannot be identified, the pickup is uncertain: nothing is granted and the order pauses. A pickup is
only ever settled as `source` ("nothing was granted from it").

### 4.3 What each inventory port refuses

Each refusal comes with a stated reason, and none of them moves anything.

- **Worker.** Refuses without exactly one live body with his key. Accepts no more than the real inventory fits,
  and no more than the carry budget. The budget is **`[Collection] WorkerCarryWeight`**, the same value
  Thorstein's collection loop plans trips with, so the loop and the port share one budget. Issued tools do not
  count toward it.
- **Chest.** Refuses when the chest:
  - is gone or not the same chest;
  - is not owned by this process;
  - is in use, including by being a cart's container: vanilla `m_wagon.InUse()` stays strict here;
  - is denied by a ward or privacy;
  - is out of reach.

  Resolving a chest never refuses for distance, because that is how the walk to it is planned. The executor checks
  reach again at the transfer.
- **Cart.** Refuses when the cart:
  - is gone or not owned;
  - has its container open;
  - is pulled by the local player;
  - is denied by access;
  - is out of reach.

  An **attached** cart counts as not in use only under all of CONTRACTS §5.2's C3 conditions: the joint's connected
  body is not the local player's, nobody has the container open, and the cooperative caller holds an accepted
  `acknowledgeWait Transferring` at the current haul revision. The caller enforces that last condition.

---

## 5. Tools

- **Give:**
  1. write and persist the intention;
  2. add the item to Thorstein;
  3. remove it from you;
  4. write and persist the result.

  **Return** follows the same order the other way. A return writes its intention first too (#300), so an
  interrupted return is in the record and is settled by the same command as an interrupted give.
- **When a step fails:**
  - **The receiving inventory refuses the item.** Nothing moved, and the handover closes its own attempt with a row
    marked `by=handover`. That row is never read as a person's answer. Nobody is asked anything, and the same
    handover can run again.
  - **The item arrived but could not be removed from the giver.** It is now in both inventories. The holding is
    unsettled, and a person answers `cf_settle resolve <request> mine|his`.
  - **The result, or the closing row, could not be written.** You are told so, with the answer to give, and the
    holding is unsettled. That is true in the current session, where `cf_settle resolve` reads the record. A
    reload that rolls the attempt back with the world leaves nothing to answer.
  - **In every case,** the ledger the procedure was given says what a replay of the record says.
- **Handovers wait for the record** exactly as material does (§6). While the record is read-only, owes its load
  restatement, or owes a world-save marker, `cf_settle give` and `takeback` refuse and name the block. A handover row
  written before an owed marker would read as part of a save that does not contain it.
- **Which axe he reaches for** is the one first handed over, in record order. It never depends on ids or on
  dictionary order.
- **Death:** see §10.
- **A tool destroyed with his body stays recorded as held** (see §13 for why that can happen). This build has no
  answer that writes a tool off. `cf_settle reconcile` reports it, and work refuses to use it, because usability is
  asked of the actual item.

---

## 6. World saves: the marker rule

- **The hook.** `ZNet.WorldSaveStarted` is subscribed once, at plugin start. The game invokes it on the main thread,
  just before the save snapshot, both for autosaves and for the save made on logout.
- **Markers.** When the record holds world-effect rows that no save has confirmed yet, the hook appends
  `WorldSaveMarker{generation = top + 1}` at the current net time. An idle settlement does not grow its record with
  every autosave.
- **An owed marker.** When the marker cannot be written:
  - the log says `The world was saved, but the settlement record could not note it…`;
  - new custody work stops (`MarkerOwed`), including handovers;
  - stopping work and a person's answers still go through, because they are record-only rows;
  - the marker is written later, before any new world-effect row, with the failed save's time.
- **World-effect rows** are pickups, `TransferStarted`/`Finished`, cart baselines, hold-for-player hand-overs, tool
  handover and return rows, and an order's transition to `Completed`. **Record-only rows** are never voided:
  acceptances, other transitions, a person's answers, losses, rebinds and markers. Neither are rows written before
  schema 3, because they carry no world time.

**At load**, with `T` the net time the loaded save stored:

1. **The matched save** is the last marker whose time is ≤ `T`. When there is none, the world predates every marker.
2. **A row after the matched save is Voided** (never credited, refunded or replayed) when either:
   - its own time is after `T`; or
   - `T` is within 60 seconds of the matched marker. The loaded save *is* that marker's save, and the row came after
     its snapshot.
3. **Otherwise the row is Ambiguous.** The loaded save is a later one whose marker was never written, and it may or
   may not contain the row. An ambiguous transfer or pickup becomes uncertain, and a person answers.
4. **When two markers share a net time**, rows between them are Ambiguous: either save could be the one on disk. Net
   time advances only while a player is in the world.
5. **The load restatement (C3).** When rows follow the matched save, or the loaded save is older than the record's
   top, the load itself is written down as a marker with the matched generation and time `T`. Every later replay then
   makes the same decision, even after a later session saves. Until that restatement reaches disk, **nothing** is
   written (`RestatementOwed`).

| What happened | Loaded `T` | Verdict |
|---|---|---|
| Saved at 100 (marker 1 @ 100); Thorstein took 7 Stone at 130; the game was killed | 100 | The pickup and take are voided; restatement 1 @ 100. In the world the stones are unpicked again and he carries what he had at 100. |
| Saved at 100; delivered 10 at 130; saved at 160 (marker 2 @ 160); killed at 170 | 160 | Nothing is voided. The delivery is in save 2 and stays credited. |
| Saved at 100; took 7 at 130; the marker for a save at 200 could not be written; killed | 200 | The take is Ambiguous (time 130 ≤ 200, and 200 − 100 > 60). The order is NeedsAttention `TransferUncertain`, and a person answers from the inventories. |
| An older world backup (save 1, time 100) was loaded while the record's top is save 3 | 100 | Every world-effect row after marker 1 with a time after 100 is voided, and a restatement is written. |
| Two markers carry the same net time | equal | Rows between the two markers are Ambiguous. |

**What this cannot see.** A complete older copy of the journal file restored over a newer one carries its own
consistent closing line. Nothing inside a file can detect that. Reconciliation against the real inventories (§8) is
what catches it.

---

## 7. What a world load does, in order

1. **Open the records.** A new load epoch is minted. A damaged file loads read-only (§12).
2. **Open custody at the loaded net time.**
   - The marker rule classifies every row, and the record is replayed. Voided rows are never applied, ambiguous
     rows become uncertain, and intents with no result are uncertain.
   - The restatement is written, if the load needs one.
   - The log reports `Settlement record: N recorded change(s) were after the world save that was loaded and are void;
     M could not be placed.`
3. **Count worker bodies by key**, loaded or not. A body from an older build with no key is reported and left exactly
   as it is.
4. **Reconcile** the record against the inventory **stored in his body's object** (§8). Carts and chests from before
   the load cannot be observed at this point.
5. **Pause every order that is not finished.** Each order becomes either:
   - **NeedsAttention**, with the first reason that applies:
     1. `WorkerBodyDuplicated`;
     2. `WorkerBodyLost`, when no body exists and the order carries material;
     3. reconciliation's reason: `TransferUncertain`, then `PlayerRemovedMaterial` or `ReconciliationMismatch`;
     4. `TransferUncertain`, for an uncertain transfer still in the ledger; or
   - **Paused** as `DestinationStale` (a chest delivery) or `ScopeChanged`. The chest key and the work-area snapshot
     belong to the previous load. The order stays paused until the player confirms them again, and the rebind
     (`CollectionRebound`, C2) must name this load's chest and area with the same source and delivery kinds.
6. **Log every finding** that needs attention.

---

## 8. Reconciliation

Only the two places that can be counted are compared: `Worker`, and `Cart` above its baseline. Nothing here writes or
applies anything.

| Finding | When | Outcome | Answer |
|---|---|---|---|
| Matches | the place holds what the record says | nothing | none |
| EffectVisible | an uncertain transfer, where the source lost the units and the destination gained them | the order is NeedsAttention `TransferUncertain`, and nothing is applied until confirmed | `cf_settle resolve <request> destination` |
| NoEffect | an uncertain transfer, where neither place changed | NeedsAttention `TransferUncertain` | `cf_settle resolve <request> source` |
| Unclear | a place cannot be checked now; two uncertain changes touch one place; or the counts fit neither outcome | NeedsAttention; nothing is credited or given back | look at both places, then answer `source` or `destination`, or check again once the place can be reached |
| BelowExpected | fewer units than recorded | NeedsAttention `PlayerRemovedMaterial` when orders were not running, else `ReconciliationMismatch`; **nothing is refunded or replaced** | `cf_settle resolve <order> lost` |
| AboveExpected | more units than recorded | NeedsAttention; the extra is **never credited** | take back what is yours |

The finding's sentence names the place, the counts, the request and the command. It appears in the log at load and
in `cf_settle reconcile`. `cf_settle reconcile` also lists any tool the record says he holds that is not in his
inventory.

---

## 9. Answering the record

| Command | What it records | Needs |
|---|---|---|
| `cf_settle status` | nothing. It shows the record's state and whether custody is writable (or which block applies), per-order progress, open tool holdings, and repairs waiting | a loaded world |
| `cf_settle reconcile` | nothing: record against the places that can be checked now | a loaded world |
| `cf_settle resolve <request> source` | `TransferResolved` (source): it did not move. For a pickup: nothing was granted | settlement authority **and** work authority |
| `cf_settle resolve <request> destination [count]` | `TransferResolved` (destination): `count` arrived (default: all). `count` may be 1 to the intent's count, and the record must hold that many at the source | as above |
| `cf_settle resolve <order> lost` | `LossRecorded` for each below-expected place the order holds material at, capped at what it holds there. Counted from the live places, so his body must be loaded | as above |
| `cf_settle resolve <request> mine\|his` | a person's answer about an unsettled tool handover: `mine` means you have it, `his` means he does | settlement authority |
| `cf_settle give axe\|hammer` | a tool handover (§5). Hold the tool and stand within 4 m of him | work authority; a writable record |
| `cf_settle takeback [axe\|hammer]` | a return of every tool the record says he holds (of that kind) | as above |
| `cf_settle release` | hands you everything the record says he still carries for orders that have **ended**, as ordinary transfers. Refused while any order is still open, so nothing is taken from under a running job | as above |

- **Settlement authority:** `[Settlement] SettlementRuntimeEnabled = true`, and this peer is the host.
- **Work authority:** additionally a loaded world, not a dedicated server, and **nobody else connected**.
- An answer that could not be written settles nothing and says so. An answer the record does not need is refused
  and writes nothing.

---

## 10. The worker's lifecycle

- **Spawn:** `cf_worker spawn` refuses while a body carrying his key exists anywhere in the world, loaded or not. A
  new body is stamped with his key before its first frame.
- **Re-binding:** whenever his ground loads, the body is found again by key, after it has loaded its stored inventory.
  While his ground is unloaded, work refuses with `ScopeUnloaded`. He is not lost.
- **Despawn** (`cf_worker despawn`) refuses in this order, before anything is touched:
  1. **A job holds him.** The plugin wires `SettlementRuntime.MayRetireBody` to the collection runtime, and the
     answer is `Refused: Thorstein is working. Pause or cancel the order first, then despawn.` A guard that throws
     also refuses.
  2. **He carries anything, or the record says he holds tools or material.** The answer is `Refused: … Release
     everything first (cf_settle takeback, or deliver what he carries), then despawn.`
- **Death:**
  - Every carried item goes on the ground, through vanilla's own drop, in the same frame, before the body is
    destroyed.
  - Each order's carried material is recorded as moved to `Lost` with the place (`died at (x, y, z)`) as its key.
    Pick it up by hand; it is not credited to anyone.
  - Each dropped tool is recorded as a return in flight, with the same place as its note. Once you have picked the
    tool up, answer `cf_settle resolve <request> mine`.
  - Those notes go through the same gates as everything else. When the record cannot take them, the log says so,
    and reconciliation reports the shortfall.
- **Duplicate bodies:** every order is NeedsAttention `WorkerBodyDuplicated`, neither body is used, and **neither is
  removed automatically**.
- **Missing body with material recorded on him:** NeedsAttention `WorkerBodyLost`. Nothing is refunded. To write the
  material off:
  1. `cf_worker spawn` (his new body carries nothing);
  2. `cf_settle reconcile`: the record now shows below expected;
  3. `cf_settle resolve <order> lost`.

---

## 11. A marked chest after a reload (#294)

Chest keys are renumbered on every load, so a chest marked in a previous run of the world is **stale**: its key names
nothing now.

- **Re-marking it** with `cf_settle supply` replaces the stale chest through the undesignation cascade, **in the
  record, first**:
  1. whatever was reserved from the old chest is returned in the record (`Refunded`, carrying the old chest's epoch);
  2. every order that drew from it is cancelled;
  3. then the new chest is marked, and you are told what was returned and cancelled.
- **The new chest is checked before the cascade runs** (settlement, containment, ward), so nothing is returned or
  cancelled for a replacement that would then be refused.
- **Reservations match their chest by key and epoch.** A reservation from another run under the same key belongs to a
  different chest. A reservation written before epochs were recorded still matches by key alone.
- **Without the record,** the stale chest is never overwritten: `StaleContainerNeedsTheRecord`.

---

## 12. A damaged record

When `cf_settle status` says `RECORD IS READ-ONLY` and names the damage (the record ends early, or its closing line
does not match), **nothing is being written and nothing has been deleted.** In order of preference:

1. **Look for a complete copy** beside it, named `<file>.tmp`, left by an interrupted write. Copy the damaged file
   somewhere safe, put the complete copy in its place, and load the world.
2. **Restore the file from a backup** taken at the same world save as the world you load.
3. **Repair it by hand**, and only when you know which lines are missing or wrong and accept the record as it will
   then stand. A hand repair makes the file the truth. Rows lost from the end stay lost, and reconciliation (§8)
   will then report whatever they described.

A hand repair ends by recomputing the closing line. Run this in PowerShell 7 (`pwsh`) with the game closed, after
copying the file:

```powershell
$File = 'C:\path\to\BepInEx\config\ConcernedCatMods\ConcernedForeman\settlements\0123456789abcdef.home.settlement.tsv'
Copy-Item -LiteralPath $File -Destination "$File.before-repair"
# ...edit the file by hand here, touching only the lines you mean to change...
Add-Type -TypeDefinition 'public static class RecordFnv { public static string Of(string[] lines) { ulong h = 14695981039346656037UL; foreach (string line in lines) { foreach (byte b in System.Text.Encoding.UTF8.GetBytes(line + "\n")) { h ^= b; h *= 1099511628211UL; } } return h.ToString("x16"); } }'
$data = @(Get-Content -LiteralPath $File -Encoding utf8 | Where-Object { $_.Trim() -ne '' -and -not $_.StartsWith('#') -and -not $_.StartsWith("end`t") })
$last = -1L; foreach ($row in ($data | Select-Object -Skip 1)) { $n = 0L; if (-not $File.EndsWith('-register.tsv') -and [long]::TryParse(($row -split "`t")[1], [ref]$n) -and $n -gt $last) { $last = $n } }
$kept = @(Get-Content -LiteralPath $File -Encoding utf8 | Where-Object { -not $_.StartsWith("end`t") })
Set-Content -LiteralPath $File -Value ($kept + "end`t$($data.Count - 1)`t$last`t$([RecordFnv]::Of($data))") -Encoding utf8
```

**How the closing line is computed.**
- **Checksum:** FNV-1a 64 over the UTF-8 bytes of every line that is not blank, not a `#` comment and not a closing
  line, each followed by `\n`. The first such line is the `v` header. Line endings do not matter.
- **Rows:** every such line after the header.
- **Last sequence:** the highest non-negative second column, or `-1` for the register.

The script was checked against files this build wrote: journal and register, with non-ASCII and escaped values. It
reproduces their closing lines exactly, and a journal hand-edited and resealed with it loads intact and writable.
`JournalSchemaTests.AHandRepairedRecordWithARecomputedClosingLineLoadsAgain` pins the same rule in code.

---

## 13. Uninstalling Foreman: "Release everything" first

Without Foreman, the game does not know the `CF_SettlementWorker` prefab. When the world loads, the host **deletes his
body, with everything in it** (D9). So before removing the mod, in the world he works in:

1. **Stop every order:** `cf_collect cancel`. Cancelling is not a refund, and what he carries stays with him.
2. **Take your tools back:** stand next to him and run `cf_settle takeback`.
3. **Take the material back:** standing next to him, `cf_settle release`. It hands you everything the record says he
   still carries for those ended orders. If your inventory fills up, make room and run it again.
4. **Check:**
   - `cf_settle status` shows no `tool …` lines, carried and in cart are 0 for every order, and no repairs are waiting;
   - `cf_settle reconcile` says everything matches.
5. **Retire the body:** `cf_worker despawn` answers `Worker despawned.`
6. **Save the world** by logging out, then remove the mod.

If the mod was removed while he still held items, reinstalling it does not bring them back. §10 describes how the
record then writes material off; tools stay recorded as held (§5).

---

## 14. Live checklist (pending)

**Setup for every row.**
- **Profile and world:** the isolated test profile, a disposable world, solo (the host, nobody else connected), and
  `[Settlement] SettlementRuntimeEnabled = true`.
- **Keep:** `BepInEx/LogOutput.log`, the output of every console command below, and a copy of the journal file before
  and after each row. Never copy or open the world save.
- **A known save** means: log out to the main menu and load the world again. Logout saves the world.
- **"Kill"** means `taskkill /IM valheim.exe /F` from a terminal, before the next autosave.
- **Prep P0**, once per world, **before** the known save a row starts from: `cf_settle area 30` standing in camp,
  then `cf_worker spawn`. A body spawned after the save a row rolls back to is rolled back with it.
- **Prep P**: look at a chest and run `cf_collect start 10 0`.

| # | Steps | Expected |
|---|---|---|
| L1 | P0, then P. When `cf_settle status` shows Stone `carried` ≥ 1, note it as N. Make a known save. | The log has no "void" line. The order is `Paused (DestinationStale)` with carried N, and `cf_settle reconcile` says everything matches. He stands where he was saved. |
| L2 | Hold an axe next to him and run `cf_settle give axe`. Note the axe's durability. Make a known save. | `cf_settle status` lists `tool give-…: axe held by him`, and reconcile has no tool line. `cf_settle takeback axe` answers `He hands it back.`, and the axe has the same quality and durability. |
| L3 | As L1 and L2 (carrying N, holding the axe). Run `cf_collect pause`, walk until his ground unloads (about 300 m), run `cf_settle reconcile`, then walk back and run it again. | Away: `…cannot be checked now. Nothing was credited or taken back…`. Back: everything matches, and `takeback` works. Nothing is duplicated or lost. |
| L4 | P0, then a known save. Run P. As soon as carried ≥ 1, kill the game, then load the world. | The log reports K ≥ 1 changes void and 0 unplaced. The picked stones are back in the world, and carried is 0. Reconcile matches. The journal has gained a `c … 20 …` restatement line (kind 20 = `WorldSaveMarker`). |
| L5 | P0, then a known save. Run P with `cf_collect start 3 0`. As soon as `delivered` ≥ 1, kill the game, then load the world. | Void changes are logged, and delivered is back to its value at the save. The chest holds what it held at the save. No stone is both in the chest and on him. |
| L6 | As L5, but make a known save instead of killing. | Delivered is unchanged, the chest holds the units, and nothing is logged as void. |
| L7 | P0, then a known save. Run `cf_settle give axe`, kill, and load. Then make a known save with him holding the axe, run `cf_settle takeback axe`, kill, and load. | First: you have the axe, status lists no tool, and `takeback` says he is not holding any tool you gave him. Second: he holds it again, status lists it as held, and `takeback` returns it. |
| L8 | Make a known save. Run P and let him work for at least two minutes after that save, so the loaded save cannot be mistaken for the last marker's. From a second PowerShell window, lock the journal: `$l = [IO.File]::Open('<journal path>', 'Open', 'ReadWrite', 'None')`. Wait about a minute, then log out. Release the lock (`$l.Close()`) and load the world. | While locked nothing new moves, and at logout the log says `The world was saved, but the settlement record could not note it…`. After the load, the log reports M ≥ 1 changes that could not be placed, and the order is `NeedsAttention (TransferUncertain)`. `cf_settle status` lists a repair line per uncertain pickup and take, each with its `cf_settle resolve …` answer, and `cf_settle reconcile` describes the takes against his actual inventory. Answer each one as the inventories show (his stones arrived: `destination`; a pickup: `source`). Afterwards `cf_settle reconcile` has no transfer finding left. |
| L9 | He carries N and holds the axe. Let him die, for example to a hostile creature; this build has no command for it. | The log says `The worker died at (x, y, z)…`, and N Stone and the axe lie there with no other loot. Status shows lost N and the axe as unsettled. Pick the axe up and run `cf_settle resolve give-… mine`, which answers `Recorded: you have it.` |
| L10 | Run `cf_worker despawn` three times: while an order runs; after `cf_collect cancel` while he still carries or holds a tool; and after `cf_settle takeback` and `cf_settle release`. | `Refused: Thorstein is working…`, then `Refused: he is carrying…`, then `Worker despawned.` |
| L11 | Follow §13 in the disposable world. Remove the DLL, load the world, restore the DLL, and load again. | Without the mod, the world loads cleanly. With it back, status shows no holdings and `cf_worker spawn` works. |
| L12 | Negative, disposable world only: remove the DLL while he carries N, load, restore the DLL, load. Then run `cf_worker spawn`, `cf_settle reconcile` and `cf_settle resolve <order> lost`. | At load the order is `NeedsAttention (WorkerBodyLost)`. After the spawn, reconcile shows below expected, and `lost` records N. Tools stay recorded as held (§5). |

**Kill points and evidence.**
- **Not reachable live:** a kill between an intent and its result, or between an add and its remove. Nobody can time
  one, and the verdict would be the same as L4 and L5, because the world rolls back to its last save. Those points are
  **automated-tested** (§15).
- **Covered live:** L4, L5 and L7 cover the reachable points before and after a delivery and a handover; L8 covers
  the only live path to an ambiguous row.
- **Recording:** each row is recorded in `docs/settlement/cart-and-collection/EVIDENCE.md` by the lead, as observed or
  pending.

---

## 15. Automated evidence

All suites are in `src/Shared.Settlement.Tests`.

| Suite | Pins |
|---|---|
| `CustodyExecutorTests` | the exact step order; one-call moves keep it; every check refuses before anything is written or moved; ids are never reused across reloads |
| `FaultInjectionTests` | a kill **before and after every persistence write and engine mutation**, with the world save rolled back or not: pickup, take, carry to cart, cart to chest, worker to chest, tool handover; failed and half-failed saves; partial acceptance; misreporting ports; cancellation, retries, stale plans, external removal, surplus; no authority |
| `ReconciliationTests` | the marker rule, including restatements, ambiguity, owed markers and owed restatements; the reconciliation matrix |
| `CustodyRecoveryTests` | recovering the worker's active order after a reload; rebinds (C2) |
| `JournalSchemaTests` | every v3 kind round-trips; unknown fields are kept; truncation, missing middle rows, changed values and trailing lines are damage; hand repair; schema 2 upgrade; the register's closing line |
| `ToolReturnTests` | #300: interrupted returns, unwritten results and closes, record order, repairs once per unsettled handover, handover-authored rows; a killed return at every step |
| `DesignationStaleRemarkTests` | #294: the cascade, epoch-aware provenance, no cascade for a refused replacement |

---

## 16. Known limits

- **Tools cannot be written off.** A tool destroyed with his body stays recorded as held (§5, §13).
- **Carts and chests are not observed at load.** Reconciliation at load sees only his stored inventory, so a cart's
  contents are compared in session, under the lease's provider epoch.
- **An older copy of the journal file is not detected** when it is restored over a newer one (§6).
- **One settlement and one worker** per world in this slice (`home`, `foreman/thorstein`).
- **No live evidence yet.** Every §14 row is pending.
