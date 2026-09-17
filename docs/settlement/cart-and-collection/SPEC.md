# Cart pulling and resource collection: specification

Contract revision **C1** (2026-09-17). Decisions: `DECISIONS.md`. Contracts: `CONTRACTS.md`. Work breakdown:
`TASKS.md`. Evidence: `EVIDENCE.md`.

## 1. Outcome

Three behaviours, each provable on its own:

| # | Behaviour | Gate |
|---|---|---|
| B1 | **Gunnar** (Concerned Teamster): walks to an explicitly assigned existing cart, hitches himself to it, physically pulls its real cargo to a selected destination, stops and detaches safely. | B |
| B2 | **Thorstein** (Concerned Foreman): walks to eligible natural loose stones and branches, picks up the real Stone and Wood, carries them within capacity and delivers them to a selected container (or holds them for the player). Teamster is not required. | C |
| B3 | **Together**: one cooperative order runs survey → travel → collect → load → haul → unload → repeat as needed → complete. Teamster must be installed and compatible, with an assigned cart ready. | D |

This is a focused slice of #273's settlement program. It is not a new framework.
- Hulgi keeps his accepted appearance, doors, seating, sleeping and blocked-walk behaviour.
- Hulgi helps surveying when available.

## 2. Provenance

- Owner brief, 2026-09-17 (`DECISIONS.md` header). The owner's later explicit requirements supersede older prose.
- #273: settlement authority and conservation gates.
- #297: Gunnar, *A Fair Load*, real cart assignment.
- #295 and #299: Thorstein's real issued tools.
- #296: Work Areas and the default circle.
- #282 and #283: harvest and custody.
- #294, #293 and #300: known custody and record defects.
- Installed game: Valheim 1.0.12 (network 40, build 25253764); `assembly_valheim.dll` SHA-256 `27a766a8…c393a84`;
  Unity 6000.0.75f1. Test profile: BepInEx 5.4.23.5 and Jötunn 2.30.0 (`CART_SEAM_AUDIT.md` §1). The repository
  builds against JotunnLib 2.29.2.

## 3. Scope and non-goals

**In scope:**
- one active collection order per Thorstein and one active haul per Gunnar;
- natural loose stones and branches only, yielding the game's own Stone and Wood;
- single player, or a host with no connected peers;
- loaded ground only;
- work only while the host is running.

**Not in scope** (deferred, never deleted or falsely completed):
- mining, felling trees, chopping brush, food plants, crops, player drops, graves, arbitrary chests;
- fleets, several orders, background or offline gathering, town simulation, construction, combat, portals, sea
  transport;
- multiplayer and mixed clients (these refuse work and leave every existing utility working);
- applying the owner's appearance references to worker bodies (#298 rows stay pending);
- Gunnar's and Thorstein's introduction quests (#297, #295) beyond what readiness needs;
- #280's placement decision (parked). Evergreen stays suspended.

## 4. Requirements

Each requirement has an id, and `EVIDENCE.md` maps every one to a test, an audit or an observation. "Must" is
normative.

### Architecture and authority

- **ARCH-01**:
  - Job behaviour is a capability and state of an identity. It is not a second NPC framework.
  - Each identity has one appropriate visible body. A home-idle body and a working body of the same identity never
    coexist (`ActorModeOwner`, D1, D9).
- **ARCH-02**:
  - One actor-mode owner per identity arbitrates Resting, Surveying, Working, Paused and Recovering.
  - While a job holds the identity, the job's position wins over home.
  - Bed changes, hidden presentation and world unloading never teleport workers, cargo or carts.
- **ARCH-03**:
  - Game-free shared code (`src/Shared/Workers`, `src/Shared/Interop`, `src/Shared/Settlement`) stays separate from
    narrow per-product game adapters.
  - Products stay independently packaged, with no compile-time references between them and no new mandatory DLL.
  - Collaboration uses the versioned runtime capability boundary (D7), tested across separately compiled assemblies.
- **AUTH-01**:
  - Real resource and cart changes happen only under the product's opted-in worker runtime (D3), with the authority
    check repeated before every mutation.
  - Unsupported combinations refuse work with a stated reason and never disable existing utilities.
  - Ward and access checks, player control, safe visual extraction and Hulgi's accepted behaviour are preserved.

### Gunnar: cart pulling (B1)

- **CART-01 Explicit assignment**:
  - **Selecting:** the player selects one real, loaded, accessible, usable cart and confirms "Assign this cart to
    Gunnar". A cart being near is never enough.
  - **What is refused:** in use, braked, not owned here, destroyed, unloaded, stale, ambiguous, not a hand cart, or
    already leased (`CartAssignmentRefusal`).
  - **What is preserved:** the cart's health, cargo and identity.
  - **One lease:** a single lease owns the pairing, and it is revalidated before attaching, moving, loading and
    unloading.
  - **Reloads:** a reload invalidates the lease and needs reselection (D6).
- **CART-02 Prove the seam first**:
  - The attach seam is `Vagon.AttachTo`/`Detach` under the D4 preconditions.
  - The smallest physical proof is observed early: approach, attach, pull an **empty** cart, pull a **legitimately
    loaded** cart, stop, detach.
  - The observation records the joint's connected body, the cart's owner and its velocity, not just a flag or an
    animation.
  - A missing or changed seam fails closed with the exact failed condition (`HitchRefusal.SeamUnavailable`). It blocks
    cart acceptance only; Foreman work is unaffected.
- **CART-03 Approach and hitch**:
  - Gunnar walks to the handle from a usable side. Before attaching once, the runtime checks heading, proximity,
    clearance, cart stability and availability.
  - Repeated requests never create a second joint, lease or puller.
- **CART-04 Physical movement**:
  - Gunnar moves only through the vanilla motor, and the joint moves the cart.
  - Forbidden:
    - transform-following, snapping or kinematic trailer substitutes;
    - zero or changed cart mass, disabled gravity or collision;
    - forced world recovery, hidden teleporting;
    - unlimited force (D5) or stamina.
  - The existing parking brake is respected and never toggled. Default cart physics and player configuration are
    unchanged.
  - The pose and speed shown follow the observed motion.
- **CART-05 Cart-safe routes**:
  - A route is valid only if it fits the loaded cart. The planner checks swept footprint, clearance, turning and
    off-tracking room, grade, footing, supported crossings, door permission and width, and a safe stop point.
  - Global planning stays separate from local steering (`ICartRoutePlanner`, `SteeringGoal`).
  - The numeric budgets, refresh intervals, stall thresholds and retry ceilings are named in `HaulLimits`, configurable
    and tested.
  - No universal safe weight or slope is claimed.
  - Where Gunnar cannot enter, a reachable staging point is chosen instead.
  - A cart is never dragged through walls, vegetation, forbidden doors, water or unsupported gaps.
- **CART-06 Stop, recover, detach**:
  - **Phases:** Unassigned → Ready → Approaching → Hitching → Pulling → Stopping → Waiting/Unloading → Detaching →
    Ready, plus Paused, NeedsAttention and Recovering (`HaulPhases` table).
  - **Asynchronous phases:** every one has success criteria, cancellation, a deadline and bounded retry with backoff.
  - **Progress:** judged from both bodies (`HaulMotion`).
  - **Recovery:** only verified-safe local manoeuvres. Repeated failures are cached so the haul cannot oscillate. When
    blocked, Gunnar stops and reports; he never escalates or retries forever.
  - **Parking:** only on suitable ground, never releasing a loaded cart into a roll.
  - **Loss of control:** player takeover, revocation, destruction, lost authority or unload cancels control through
    the safe lifecycle, and cargo is preserved and reported.

### Thorstein: collection (B2)

- **GATHER-01 Explicit order**:
  - The order sets bounded quantities per resource (1–500 each), a work area, and a selected container or
    hold-for-player mode.
  - Quantities count **newly delivered** units only.
  - Saving an area, installing a mod or recruiting never starts gathering.
- **GATHER-02 Work area**:
  - The order uses the assigned area (the harvest designation, D11).
  - With none assigned, the player sees a preview of a 30 m circle on the latest valid respawn anchor, never the
    moving player.
  - Acceptance captures the scope, world and anchor revision.
  - An assigned area that is invalid, deleted, unloaded or unavailable never silently falls back or expands.
    Revalidation happens at safe checkpoints before a mutation.
- **GATHER-03 Survey and classification**:
  - **With Cartographer:** a cooperative survey with Hulgi. Both are shown and actual observations are used.
  - **Without Cartographer:** a clearly labelled bounded solo survey.
  - **No false credit:** a busy or absent Hulgi is never cloned or credited.
  - **Eligibility:** only sources passing the evidence-based natural-source predicate (CONTRACTS §6). Provenance and
    actual yield are confirmed through current APIs, never names or size.
  - **Survey limits:** only authorized, loaded space is observed. Exhausted, unloaded, inaccessible and unknown are
    kept distinct. Each observation records its identity, revision and reachability, and is rechecked before pickup.
- **GATHER-04 Quantity-aware selection**:
  - **Heuristic:** a bounded, deterministic choice using route cost, pickup effort, carry capacity, cart access and
    return trips. It does not claim to be optimal.
  - **Tracking:** delivered, carried, in-cart, handed-over and on-ground counts are kept separately from estimates.
    Collection stops per resource once met, with no overcollection and no stopping at a merely surveyed quota.
  - **After removal or loss:** the plan is recomputed.
- **GATHER-05 Physical pickup and carry**:
  - **Sequence:** reserve the target, walk into verified interaction range, revalidate, perform the game's own pick
    once, and record the actual items received.
  - **Presentation:** carrying is shown to match real custody.
  - **Forbidden:** destroying a source and granting an estimate; duplicating items a vanilla path routes elsewhere;
    taking any drop not traceably spawned by the order's own pick.
- **GATHER-06 Equipment, capacity, delivery**:
  - **Tools:** the order is accepted only when Thorstein holds a usable issued axe **and** hammer (D12). A later
    missing or broken tool pauses readiness without relocking. Tools are never cloned or reset, and picking wears
    neither.
  - **Capacity:** the worker's real slot and weight capacity, and the destination's real fit (stack limits,
    pre-existing contents), are enforced.
  - **Return space:** before collecting more, the worker makes sure there is reachable space to return to. Overflow is
    never discarded.
  - **Solo delivery:** without Gunnar, Thorstein makes real return trips or holds for handover.
  - **Unavailable destination:** a full or unavailable destination pauses the order with materials retained. No other
    chest is substituted.

### Cooperation (B3)

- **COOP-01**:
  - Thorstein owns the collection order. Gunnar receives leased haul subtasks referencing it and never creates a
    competing order.
  - Standalone Gunnar also hauls a manually loaded, explicitly assigned cart to a selected destination without Foreman
    or Cartographer.
- **COOP-02**: the loop runs in order:
  1. Validate participants, area, destination and cart, then survey and plan.
  2. Travel to a reachable work or staging area.
  3. Gunnar brings the cart to a safe rendezvous while Thorstein picks up and returns to load.
  4. Both wait for proximity, cart stillness and actual transfer results. Nothing moves the cart during a transfer.
  5. At a capacity or quota checkpoint, haul, unload and credit actual accepted units. Repeat only as needed.
  6. Complete when every quantity is delivered, release the leases and rest where reachable.

  Rendezvous and plan updates are versioned. Waits are explicit and acknowledged, and none waits forever: a
  rendezvous deadline ends it. No pipelining and no extra workers.
- **COOP-03**:
  - **Solo mode:** used when Teamster is absent or no ready cart is assigned. The mode is visible.
  - **Cooperative mode:** used when Teamster is present, Gunnar is ready and the cart is usable.
  - **Losing the provider or cart mid-job:** the order pauses, or replans only after custody is reconciled. Cargo is
    never moved into solo inventory by bookkeeping.
- **COOP-04**:
  - **Controls:** quantities, area preview, destination, assigned cart, participants, survey/start, pause/resume/cancel
    and cart release, reusing the existing UI patterns. Console commands are development aids only.
  - **Progress:** requested, reserved-estimate, carried, in-cart and delivered per resource, never double counted.
    Also the current state and one actionable reason. Hold-for-player mode reads HoldingForPlayer, not Delivered.
  - **Notifications:** subject to a cooldown.

### Conservation, persistence, failure

- **DATA-01**: each order, actor mode, cart lease, source reservation and transfer has one authoritative owner.
  Authority, access, source and destination are rechecked at the mutation boundary. Concurrent clicks, retries or
  players never claim one source twice.
- **DATA-02**:
  - **One ledger:** the existing settlement journal is extended; no competing inventory truth is created.
  - **Testable ordering:** mutation order sits behind the ports (`IInventoryPort`, `ISourcePickupPort`,
    `ITransferExecutor`).
  - **Custody:** each accepted unit has exactly one current custody place or recorded disposition, and delivery is
    credited once.
  - **Pre-existing cart cargo:** tracked separately; never counted, unloaded or refunded as gathered material.
  - **Real removals:** a player's actual removal or destruction is never reversed by the ledger.
- **DATA-03**:
  - **Records:** durable request ids, expected revisions and intent/result records for pickup, carry to cart, cart to
    container and tool handover.
  - **Integrity:** item metadata is preserved, and partial transfers credit only accepted units.
  - **Journal versus save:** journal writes are not atomic with the world save. Failures are injected before and after
    every engine mutation and persistence write, and the actual state is reconciled on load.
  - **Uncertain outcomes:** never replayed or compensated. They become NeedsAttention with evidence and an explicit
    resolution control.
- **DATA-04**:
  - Records are scoped by stable world, character, product, worker and order identities.
  - Schemas are versioned and forward data preserved.
  - Cart, container and source identity are revalidated after reload. Coordinates or stale network ids alone never
    authorize a replacement object.
- **DATA-05**:
  - **Releasing control:** pause, cancel, reload, disconnect, area deletion, provider loss and worker or cart
    destruction all release control safely and account for actual contents.
  - **Cancellation:** not a refund.
  - **When progress stops:** no progress while authority is unavailable; loaded-range limits are reported.
  - **Recovery and uninstall:** documented, including "Release everything" before uninstalling Foreman (D9).

## 5. Gates

Each gate row in `EVIDENCE.md` is recorded as one of: implemented, automated-tested, adapter-audited, observed or
pending. A merged PR, a running process or a test count is not gameplay acceptance.

- **Gate A: contract and compatibility.**
  - Separately built products exchange the supported payloads, and missing or mismatched providers fail safely.
  - One owner exists per order, cart and actor, and every legal and illegal transition is tested.
  - Hulgi and the existing Teamster utilities are preserved.
- **Gate B: Gunnar alone.**
  - **Observed happy path:** approach, alignment, attach, empty and loaded movement, turns, stop and detach.
  - **Negative cases:** a cart in use, the brake engaged, an unsafe slope, a narrow passage, an obstruction, a repeated
    hitch, player takeover, authority loss, a destroyed or unloaded cart, and a stale identity after reload.
  - **Captured:** real cart motion and cargo counts.
- **Gate C: Thorstein alone.**
  - **Observed happy path:** mixed collection, bounded carry, multiple trips and correct delivery.
  - **Negative cases:** natural-source exclusions, boundaries, a moving player versus a fixed anchor, an unavailable
    destination, an exhausted versus an unloaded area, source races, and no unused-tool wear.
- **Gate D: cooperation.**
  - **Scenario:** 20 Stone + 30 Wood in a disposable world with documented sources, including Hulgi's joint survey
    when available.
  - **Observed:** cart staging, real pickups and transfers, the loaded haul, unloading and a second trip.
  - **Negative cases:** pre-existing cargo stays accounted; partial collection, provider or cart loss, rendezvous
    timeout and a full destination cause no duplication and no teleport.
- **Gate E: recovery and review.**
  - **Fault injection:** before and after each source mutation, destination mutation and journal write, for pickup,
    load, unload and tool transfers.
  - **Also covered:** failed saves, partial stack acceptance, cancellation, retries, reload and external inventory
    changes.
  - **Adapter ordering:** regression tests.
  - **Checks:** validator, all suites, Release builds, API audits, and an exact-head independent review.

## 6. Defaults and parameters

| Setting or constant | Default | Where |
|---|---|---|
| `Settlement/SettlementRuntimeEnabled` (Foreman) | `false` | existing |
| `Workers/GunnarHaulingEnabled` (Teamster) | `false` | new, agent A |
| `Workers/GunnarPullStrength` | `MatchPlayer` (only value) | new, agent A (D5) |
| Work authority | opted in + world + host + not dedicated + 0 peers | `WorkAuthorityPolicy` |
| Default work circle | 30 m on the latest valid respawn anchor | `WorkScope.DefaultCampRadiusMetres` |
| Scope radius bounds | 4–48 m | `WorkScope` |
| Quota per resource | 1–500 | `CollectedResources.MaxQuota` |
| Haul limits | provisional in C1; agent B records final values and their evidence | `HaulLimits` |
| Notification cooldown | 30 s per reason | `AttentionThrottle`, agent E |
| Survey scan budget, carry reserve, pickup retry | set by agent C with evidence | `CONTRACTS.md` §6 |
