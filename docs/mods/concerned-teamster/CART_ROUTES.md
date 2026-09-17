# Cart routes for Gunnar (CT-NPC-003, #314)

How Concerned Teamster decides whether a **loaded cart** fits a route, where it may stop, how Gunnar is steered along
it, how progress is judged, and how a stuck haul recovers without escalating. Requirements: CART-05 and CART-06
(progress and recovery) in `docs/settlement/cart-and-collection/SPEC.md`; seam: `CONTRACTS.md` §2.4 (contract
revision C2). Agent B implements navigation; agent A's executor is the only thing that moves Gunnar or the cart.

**Status:** implemented, automated-tested and adapter-audited. Every in-game row is **pending** until the lead observes
it (see [Live observation](#live-observation-pending)).

## Game build and evidence

| Item | Value |
|---|---|
| Valheim | **1.0.14** (network 40), Steam build **25364265** |
| `assembly_valheim.dll` | 2,569,728 B, SHA-256 `F64998168A0DD37EC774816808F914ED68376BE1B9670CD05A6C2F27C8017FB6` |
| `UnityEngine.PhysicsModule.dll` | SHA-256 `98C812A75597BA922BB668FC3CE3104742B1C617ADCF06267399CA3F35032B3E` |
| Main prefab bundle `SoftRef/Bundles/c4210710` | SHA-256 `0E95FE17A7FC0498DF51BD175E23F89E17AD8BE4DC387B738AADB05B224C3255` |
| Previous build read by the audits | 1.0.12, build 25253764, `assembly_valheim.dll` `27A766A8…C393A84` |

The game updated on 2026-09-17 during this work. Every type the navigation adapters use (`Pathfinding`,
`ZoneSystem`, `Heightmap`, `Floating`, `Door`, `Vagon`, `BaseAI`) decompiles **identically** on both builds; `Piece`
differs only in achievement code. The cart prefab and the TagManager layer table read identically on both.
`scripts/audit-teamster-navigation-api.ps1` re-verifies all of it against whatever is installed (below).

### The vanilla cart, measured

Read from `Assets/GameElements/Cart/Cart.prefab` in the installed bundle (read-only bundle reader, type trees from the
bundle itself). Cart-local metres, the handle along +Z:

| Part | Value |
|---|---|
| Wheels | two bodies at x = ±0.80, y 0.518, z −0.131; convex disc colliders of radius 0.509, 0.06 thick |
| Attach point (`m_attachPoint`) | (0, 0.583, 2.079); handle rails reach z ≈ 2.20 at x = ±0.60 |
| Body | floor 1.5 × 2.0 m at y ≈ 0.49; side boards at x = ±0.726; tail board at z ≈ −1.05 |
| Container collider (layer `item`) | top at y ≈ 1.385 |
| Layers | body, wheels and load visuals on `vehicle` (28); container on `item` (12) |
| **Footprint used** | width **1.72 m** (outer wheel faces), length **3.25 m** (handle tip to tail), hitch **2.21 m** (attach point to axle), height **1.4 m** |

`Vagon` fields as serialized on the prefab, which **differ from the code initialisers** that
`CART_INTERNALS.md` and `CALIBRATION_PROTOCOL.md` quote:

| Field | Prefab | Code initialiser |
|---|---|---|
| `m_detachDistance` | **1.0** | 2 |
| `m_breakForce` | **100000** | 10000 |
| `m_baseMass` | **50** | 20 |
| `m_itemWeightMassFactor` | **0.1** | 1 |
| `m_attachOffset` | (0, 0.8, 0) | (0, 0.8, 0) |
| `m_playerExtraPullMass` | 0 | 0 |
| `m_spring` / `m_springDamping` | 5000 / 1000 | 5000 / 1000 |

The cart root also carries `Floating` (it floats: `m_waterLevelOffset` 0.5, `m_force` 1.3) and `ImpactEffect`
(`m_minVelocity` 3, damages itself). So the calibration protocol's cargo-set masses (20 + weight) and the
`DerivedConstant` rows (break force 10000) do not describe this cart. The lead is tracking that as a separate Teamster
calibration issue. It is why no limit below is derived from those rows.

### What the 0.6 m hitch reach means for approach

`HitchReachFraction` 0.6 × the measured `m_detachDistance` 1.0 = **0.6 m**. D4 precondition 8 measures the way
`CanAttach` does: the 3D distance from Gunnar's pivot + `m_attachOffset` (0.8 m up) to `m_attachPoint.position`.

- **On level ground**, the attach point is 0.583 m above the cart root and Gunnar's pivot + 0.8 m is 0.8 m above his
  feet, so they are 0.217 m apart vertically. That leaves **0.56 m** of flat tolerance for hitching, and 0.98 m before
  vanilla's continuous `CanAttach` detaches.
- **On a slope**, the ground height between Gunnar's feet and the cart root adds to that vertical offset. At the 12 %
  grade limit over the ~2.1 m from root to handle, that is up to 0.25 m, which leaves about **0.38 m** of flat
  tolerance for the hitch.
- **Walking pace:** vanilla path following stops walking within 0.5 m of its target (`BaseAI.MoveTo`). So the approach
  must aim at the handle's own position, between the rails, not at a point near it. Only a few centimetres of margin
  remain on level ground, and the hitch can fail on a slope unless Gunnar closes the last distance deliberately.
- **Once hitched**, the rigid hitch keeps him within reach. A cart that is held while he walks on is detached by
  vanilla at 1.0 m and reported as a broken joint.

## Design

### Request semantics

`CartRouteRequest.From` is **where the cart stands** and `To` where Gunnar should bring it, which is how agent A's
executor calls the planner.

- The navmesh path runs from the cart.
- Gunnar's line starts at the handle:
  - by default, one hitch length along the route, taking the cart to face along it;
  - or at his real position through `CartRoutePlanner.Plan(request, pullerPosition, now)`, which also predicts the
    first turn from the cart's actual heading. Integration should use this overload once Gunnar is hitched.
- Every waypoint, steering goal and the stop point are for **Gunnar**. The cart stands about one hitch length behind
  him.
- A target within one hitch length of the cart is refused (`TargetWithinHitch`): there is nothing to pull.

### Global planning (`CartRoutePlanner.Plan`)

1. **Cheap refusals first**, before any navmesh query:
   - an invalid request;
   - a leg longer than `MaxLegMetres` in a straight line;
   - either end on unloaded ground;
   - a target that failed lately (failure cache);
   - a spent query budget (`PathQueriesPerMinute`, a sliding sixty-second window across every haul).
2. **Navmesh path.** `Pathfinding.GetPath(cart, target, …, HorseSize, requireFullPath: true)`.
   - **Why `HorseSize`:** no agent models a cart. `HorseSize` (radius 0.8 m, height 2.5 m, step 0.3 m) is the closest
     body: its radius is within 6 cm of the cart's measured half width, its height clears a cart with Gunnar, and its
     step is a walker's. The wider agents (TrollSize 1.0 m, Abomination 1.5 m) are 5–7 m tall and step 0.6 m, so they
     would shut the cart out of every roofed yard, for a margin the route checks already enforce.
   - **Snapped ends:** `GetPath` may snap either end up to 12 m onto the navmesh. When it moves an end by more than the
     arrival radius, the exact cart position and target are joined to the path by straight stretches, which the checks
     below verify like any other stretch.
3. **Failure cache on the whole line** (the same place passed the same way).
4. **Evaluation** (`CartRouteEvaluator`), with a fresh allowance of `ClearanceProbesPerPlan` probes.

### Route evaluation (`CartRouteEvaluator`)

**Sampling and prediction.** Gunnar's line is sampled every `SampleSpacingMetres`, corners included. At each sample,
the cart's pose is predicted by `CartTrackPredictor`, the classic trailer curve:
- vanilla's hitch holds the handle at Gunnar's body and lets it swivel, and the wheels roll but do not slide;
- so the axle stays exactly one hitch length behind the handle and only moves towards it;
- on a straight pull the cart follows Gunnar's line, and in a turn it **cuts inside** — about 1.0 m at a right-angle
  corner for the vanilla cart's 2.21 m hitch (tested).

**Checks at each pose, in order.** The first problem along the route decides:

| # | Check | Refusal |
|---|---|---|
| 1 | Ground loaded under Gunnar, the axle and both wheel tracks | OutsideLoadedArea / `Unloaded` |
| 2 | Ground readable | NoPath / `GroundUnreadable` |
| 3 | No lava, no water or tar over the ground | Water / `Lava`, `Water` |
| 4 | Something solid under Gunnar and the axle; under both wheels | UnsupportedGap / `NoSurface`, `WheelUnsupported` |
| 5 | A wheel no further below or above the axle than a step plus the grade across half the track | UnsupportedGap / `WheelUnsupported`; TooSteep / `CrossSlopeTooSteep` |
| 6 | Tilt across the cart ≤ `MaxGradeRatio` | TooSteep / `CrossSlopeTooSteep` |
| 7 | No drop or rise between samples sharper than a step plus the grade over the run, for the axle and for Gunnar | UnsupportedGap / `Drop`; TooSteep / `LedgeUp` |
| 8 | Running grade ≤ `MaxGradeRatio`, **uphill and downhill**, over an exact 3 m run of the cart's own track | TooSteep / `RunningGradeTooSteep` |
| 9 | When calibration models are given: the climb model does not say this load stalls; the descent model says neither Caution nor Danger | TooSteep / `CalibratedClimbRefused`, `CalibratedDescentRefused` |
| 10 | No doorway crossed by Gunnar's stretch or the cart's box | ForbiddenDoor / `Doorway` |
| 11 | The cart's heading within 90° of Gunnar's walk | TooNarrow / `TurnTooSharp` |
| 12 | A swept box — cart width + 2 × `SideClearanceMetres`, cart length, from 0.3 m to the cart's top — clear from the previous pose | TooNarrow (see repairs) |

**How the box is swept.** It moves in a straight line between poses. Where the cart turns, the stretch is split so the
box never turns more than 10° between sweeps, and each sweep is widened by how far the box's ends swing, so it stays
conservative. The very first sweep ignores what the cart is already touching where it stands.

**Repairs.** A blocked sweep is not always the end. The navmesh keeps a walker only 0.8 m from walls, and the cart's
corridor is wider.
- **Measuring:** the free room either side of the blocked box is measured with two thin slab sweeps.
- **Refusing:**
  - room narrower than the box: `MeasuredTooNarrow`;
  - room either side, so the obstacle is ahead: `Obstructed`, or `InsideCornerClipped` when the cart was turning.
- **Moving the line:** otherwise Gunnar's line is moved sideways to centre the box in that room. The move applies fully
  from three hitch lengths and a cart length before the contact (so the trailing cart has settled) to a cart length
  after it, easing in and out over a hitch length. Everything is then checked again from the first moved sample.
- **Bounds:** the route's ends never move. There is at most one repair per cart-and-hitch length of route (plus one),
  then `RepairsExhausted`, and every repair spends probes.

**Stop point.** The last sample whose running grade and cross slope are both within `MaxParkingGradeRatio`.
- **When the route's end is not level enough:** the plan is still suitable, but stops earlier, and
  `CartRouteAssessment.StopShortfallMetres` says by how much. A caller whose target has an arrival radius should compare
  the two.
- **When no sample qualifies:** UnsafeStop / `NoLevelStop`.

**Plan fields.**
- `Waypoints` is Gunnar's verified line, simplified to the points that bend it plus the stop.
- `NarrowestClearanceMetres` is the verified corridor width (the cart's width plus both clearances): sweeps are
  pass/fail, so that is what every stretch is proven to have.
- `SteepestGradeRatio` is the steepest measured running grade.

**Faults.** Any exception from a probe or the navmesh becomes a refusal (`ProbeFaulted`, `PathSourceUnavailable`) and
never escapes into the game's update loop. `CartRoutePlanner.LastAssessment.Describe()` gives one log line with the
finding, the place, the obstacle and the costs.

### Local steering (`NextGoal`, `Follow`)

- **One goal at a time:** the next waypoint not yet reached, with an arrival radius of 0.5 m (vanilla's walking corner
  radius). Only the stop is `IsFinalStop`.
- **Order:** progress is kept per plan and **never goes backwards**. A waypoint counts as passed when Gunnar is within
  the radius or is nearer the stretch after it (only the current and next stretches are considered, so a route that
  bends back near itself cannot skip ahead).
- **Corridor:** `null` (with `LeftCorridor` from `Follow`) when Gunnar is farther than half the verified corridor from
  his stretch, or the cart is that far from its predicted track: the plan says nothing about where they are, so plan
  again from here.
- **Finished:** `null` once the stop is reached.

**Refresh.** `NeedsRefresh(plan, now)` becomes true every `PlanRefreshSeconds`. `Refresh(plan, puller, cart, now)`
re-verifies what is left of the route from where both bodies actually are, without a navmesh query, and returns a new
plan to follow (its waypoints start at Gunnar) or a refusal. That is how an obstacle placed after planning, a door, or
ground that unloaded is caught before the cart reaches it.

### Staging and rendezvous (`CartStagingSelector`)

For when the point asked for is not a place a loaded cart can reach or stand: the nearest spot to it that is.
- **Candidates:** the desired point first, then rings one cart length apart out to the requested radius (at most
  `MaxLegMetres`). Each ring starts on the side facing the cart and alternates outwards, so the choice depends only on
  the request and the world.
- **Cheap checks first:**
  - loaded;
  - not a recent failure;
  - dry, supported and within `MaxParkingGradeRatio` under Gunnar, the axle and both wheels;
  - one box check for room.
- **Then a full plan,** which must be suitable **and stop at the candidate**.
- **Bounds:** at most `ClearanceProbesPerPlan` candidates are looked at, and every plan spends the shared query budget
  (`BudgetExhausted` means ask later).
- **What `Spot` means:** where Gunnar stops. The cart stands behind him.

### Progress (`HaulMotionMonitor`)

Progress is judged from **both** bodies over `StallWindowSeconds`. Until a whole commanded window has passed, the pull
gets the benefit of the doubt (Progressing). After that:

| Evidence over the window | Motion |
|---|---|
| The cart's net flat displacement ≥ `StallCartDisplacementMetres` | Progressing |
| The cart did not move that far, but Gunnar got that far from where the window began (net or at any sample) | Wedged |
| Neither | Stalled |
| The motor is not commanded | Idle, and the window starts over |

- **Net displacement, not path length:** a cart rocking back and forth is not progress.
- **Stalled versus Wedged:** vanilla's hitch is rigid along the cart, so a held cart usually holds Gunnar too and shows
  as Stalled. Recovery treats both alike.
- **Bad samples:** non-finite samples are ignored, and a clock that runs backwards starts over.
- **Cost:** memory is fixed (64 samples) at any sampling rate.

### Failure cache (`CartRouteFailureCache`)

The companions' walk-setback idea, copied into Teamster.
- **What is remembered:** a leg that stuck is remembered as its target, the place where the cart was held, and the
  heading.
- **What is refused:** a route passing that place **the same way** (within 60°, within the corridor's half width, at the
  same level), and a target within the corridor's half width of a failed one. Passing another way, or round it by a
  repaired line, is allowed.
- **Pauses:** the same trouble again is avoided for longer: 60 s doubling to 300 s, recalled for 600 s, at most 8
  entries. These are the companions' in-game-proven numbers (see the risks below).

### Recovery (`HaulRecoveryPolicy`)

- **What each stall does:** each Stalled or Wedged judgement records the place in the failure cache and spends one
  attempt.
- **What an attempt is:** at most one of:
  - a **back-off** one sample spacing straight behind the cart, offered only when the probe proved the ground (loaded,
    dry, supported, within the grade limit), the doors and the whole box behind are clear, and never twice at the same
    place;
  - a **re-plan** from where the cart is. The cache keeps it off the stuck way.
- **Waiting:** before each manoeuvre Gunnar holds still for `RecoveryBackoffSeconds`, doubling to
  `RecoveryBackoffMaxSeconds`.
- **Giving up:** after `MaxRecoveryAttempts` the next failure gives up with Wedged (or the refused re-plan's reason).
- **Per leg:** the ceiling is not refilled by progress in between, so nothing oscillates.
- **Budget:** a re-plan refused only for the query budget waits for the budget and spends no attempt.
- **Never escalates:** it never pushes harder or retries forever.

## Limits and their rationale

Every tunable bound is a `HaulLimits` value (`src/ConcernedTeamster/Domain/Hauling/HaulLimits.cs`). None is a claim
about what is safe in general. The values agent B owns are set from the evidence named; the others belong to agent A,
agent E or contract C2 and are listed here unchanged, so the table is complete.
`HaulingNavigationLimitsTests` pins this table to the code.

| Limit | Default | Owner | Rationale |
|---|---|---|---|
| `MaxGradeRatio` | 0.12 | B | **Moderate band, not a measured limit.** No measured calibration row exists. Teamster's calibration protocol classes its test ramps as moderate (10 %, accepted 8–12 %) and steep (25 %), and Teamster's own stuck diagnosis already blames a stalled pull on the grade itself from 15 %. So 0.12 keeps a loaded haul inside the protocol's moderate band and below the grade Teamster treats as able to stop a pulled cart. It applies uphill, downhill (a loaded cart is as dangerous going down) and across the cart. The calibration's `DerivedConstant` rows cannot justify a limit: they assume a break force of 10000, and the prefab's is 100000. Provisional until protocol runs. |
| `SideClearanceMetres` | 0.5 | B | Vanilla path following accepts a corner once within 0.5 m while walking (`BaseAI.MoveTo`), so Gunnar's real line, and the cart behind him, can be that far off the plan near any corner. With the measured 1.72 m cart the verified corridor is 2.72 m. |
| `PathQueriesPerMinute` | 12 | B | `GetPath` pokes a 3 × 3 block of 32 m navmesh tiles around both ends, and the game rebuilds a poked tile only after `m_updateInterval` (5 s). Asking more often than once per 5 s on average cannot see a newer navmesh. The sliding window also stops a burst of re-plans. |
| `ClearanceProbesPerPlan` | 128 | B | A full 64 m leg at 1.5 m spacing needs about 42 straight sweeps. A right-angle corner adds up to 9 sub-sweeps where the cart turns. A repair costs 2 side probes plus re-sweeping up to about 17 m (about 12 sweeps). 128 covers a full leg with two corners and a few repairs. The worst case, 12 plans a minute, is 1536 box casts a minute (about 26 a second); the real frame cost is pending a live measurement (`PERFORMANCE_BUDGETS.md` pending rows). |
| `SampleSpacingMetres` | 1.5 | B | Equal to `TerrainAdapter.HalfRunMeters`, so a two-sample run (3 m) is the run Teamster's Cart Status grade readout uses and a player can check a refusal with it. It is shorter than the cart body (2.06 m), and sweeps are continuous between samples anyway. |
| `MaxLegMetres` | 64 | B | `GetPath` pokes tiles within ±48 m of each end. With ends at most 64 m apart, the pokes of both ends cover the straight corridor between them, so a leg never depends on navmesh tiles nobody asked for. It is also one zone width. |
| `PlanRefreshSeconds` | 5 | B | The same 5 s as `Pathfinding.m_updateInterval` and the cart's own `UpdateMass` cadence: a sooner recheck can see neither a rebuilt navmesh nor an updated mass. |
| `ApproachTimeoutSeconds` | 60 | A | Agent A's; unchanged. |
| `HitchReachFraction` | 0.6 | A | Agent A's (D4); unchanged. With the measured `m_detachDistance` 1.0 it is 0.6 m (see [approach](#what-the-06-m-hitch-reach-means-for-approach)). |
| `MaxHitchAttempts` | 3 | A | Agent A's; unchanged. |
| `MassSettleSeconds` | 6 | A | Agent A's; unchanged (the cart's `UpdateMass` runs every 5 s on its owner). |
| `MinUprightDot` | 0.5 | A | Agent A's (D4); unchanged. |
| `MaxParkingGradeRatio` | 0.05 | C2 | Added by contract C2 (provisional). The route's stop point and the staging spots use it: the steepest running grade and cross slope a loaded cart is left standing on. |
| `StallWindowSeconds` | 2.5 | B | Teamster's `StuckDetector` calls a player's pulled cart stuck after 2.5 s below 0.3 m/s. Gunnar's pull is judged over the same window. |
| `StallCartDisplacementMetres` | 0.75 | B | That same stuck rule as a distance: 0.3 m/s × 2.5 s. A cart Teamster would call stuck for a player is never judged Progressing for Gunnar. |
| `MaxRecoveryAttempts` | 2 | B | Recovery has exactly two distinct verified manoeuvres, one back-off and one re-plan, and repeats neither at the same place. |
| `RecoveryBackoffSeconds` | 5 | B | A re-plan sooner than `Pathfinding.m_updateInterval` (5 s) cannot see a rebuilt navmesh tile. |
| `RecoveryBackoffMaxSeconds` | 20 | B | Unchanged. It leaves room for two more doublings if the attempts are ever raised; with 2 attempts the waits are 5 s and 10 s. |
| `StillSpeedMetresPerSecond` | 0.15 | A | Agent A's; unchanged (half the 0.3 m/s below which Teamster calls a pulled cart stopped). |
| `StillForSeconds` | 1 | A | Agent A's; unchanged. |
| `RendezvousTimeoutSeconds` | 180 | E | Agent E's; unchanged. |
| `PollIntervalSeconds` | 0.5 | E | Agent E's; unchanged. |

### Fixed geometry (not tunable limits)

These come from the game or from Teamster's own definitions (`CartRouteGeometry`, adapters), so changing one would
make the planner disagree with its source:

| Constant | Value | Source |
|---|---|---|
| Waypoint arrival radius | 0.5 m | `BaseAI.MoveTo` walking corner radius (Gunnar never runs, D5) |
| Step | 0.3 m | `HorseSize` agent climb; also the clearance box's lowest height |
| Largest articulation | 90° | beyond it the hitch pushes the cart sideways or back instead of pulling |
| Sweep turn per stretch | 10° | geometry tolerance; the widening keeps the sweep conservative |
| Prediction step | 0.1 m | accuracy of the trailer curve |
| Doorway height band | 1.2 m | the companions' doorway geometry |
| Door half width | 1.0 m | a wood door's leaf is 1.39 m, a wood gate's 1.68 m |
| Ground search window | 1 m up, 3 m down | the navmesh agent's 2.5 m headroom keeps roofs above 1 m; deeper than 3 m is a gap |
| Failure cache pauses | 60 → 300 s, recall 600 s, 8 entries | the companions' walk setbacks, proven in game |

## Doors

Under contract C1 and C2, **any doorway crossing refuses the route as `ForbiddenDoor`**, whether the door is open or
shut:
1. **Width.** A vanilla door's opening (1.39 m leaf; a gate's 1.68 m) is narrower than the measured 1.72 m cart, before
   any side clearance.
2. **Permission.** The companions' door permission is Cartographer's local presentation setting. Teamster cannot read
   it (no compile-time reference, and no capability contract for it), and Teamster has no door permission of its own
   to honour.
3. **Opening.** Opening a door is its `UseDoor` RPC. Teamster's validator forbids RPC calls, and the Gunnar carve-out
   does not authorise one.

`CONTRACTS.md` §2.4 says the planner "reuses the shared `RouteDoors` and `DoorAccessBook` semantics and adapts them to
cart width". Under the reasons above, that adaptation is "no doorway", and the §2.4 sentence should be read that way,
or amended at the next contract revision. Doorways are found from the game's own list of loaded pieces, not a physics
query. A doorway is a plane across the door's facing axis, crossed within its half width widened by the cart's
corridor, at its level.

## Verdicts and attention reasons

`CartRouteFindings.TryGetAttention` maps a refused verdict to the attention reason a haul stops with. Player sentences
for attention reasons belong to agents A and E (`CONTRACTS.md` §2.3); suggested wording for the route reasons:

| Verdict | Attention reason | Suggested sentence |
|---|---|---|
| NoPath | NoRoute | "Gunnar can't find a way there that a loaded cart can take." |
| TooSteep | TooSteep | "That way is too steep for a loaded cart." |
| TooNarrow | TooNarrow | "The cart won't fit through there." |
| ForbiddenDoor | ForbiddenDoor | "Gunnar won't take a cart through a doorway." |
| Water | Water | "That way crosses water." |
| UnsupportedGap | UnsupportedGap | "There's no solid ground for the cart's wheels on that way." |
| OutsideLoadedArea | OutsideLoadedArea | "That way leaves the area around you that's loaded. Stay closer." |
| UnsafeStop | UnsafeParking | "There's nowhere level to leave the loaded cart on that way." |
| BudgetExhausted | (none: ask again later) | — |

## Game surface

| Adapter | Game members (verified by the audit script) |
|---|---|
| `NavmeshCartPathSource` | `Pathfinding.instance`, `Pathfinding.GetPath(Vector3, Vector3, List<Vector3>, AgentType, bool, bool, bool)`, `Pathfinding.AgentType.HorseSize` |
| `GameCartTerrainProbe` | `ZoneSystem.instance`, `ZoneSystem.IsZoneLoaded(Vector3)`, `ZoneSystem.m_waterLevel`, `Heightmap.FindHeightmap(Vector3)`, `Heightmap.IsLava(Vector3, float)`, `Floating.GetLiquidLevel(Vector3, float, LiquidType)`, `Piece.GetAllPiecesInRadius(Vector3, float, List<Piece>)`, `Door`; Unity `Physics.RaycastNonAlloc`, `BoxCastNonAlloc`, `OverlapBoxNonAlloc`, `LayerMask.GetMask`/`LayerToName` |
| `CartFootprintReader` | `Vagon.m_attachPoint`, `Vagon.m_wheels`; Unity colliders and transforms |
| `NavigationCapability` | probes all of the above once, lazily; anything missing turns navigation off (fail closed) |

**Layers.** Ground is `Default`, `static_solid`, `Default_small`, `piece` and `terrain`, which is `ZoneSystem`'s own
solid mask, ignoring anything on a moving body. Obstacles are the same set without terrain, plus `vehicle` (other
carts, ships). These never block:
- characters;
- items;
- non-solid pieces;
- triggers;
- the leased cart and Gunnar (`GameCartTerrainProbe.UseBodies`).

Everything is read-only: no write to the world, no RPC, no force, velocity or transform write (validator CT-026 and
CT-028 stay at 0 violations).

**Audit.**
```powershell
pwsh C:\code\concernedcat-handoffs\2026-09-17-gunnar-thorstein-work\with-build-lock.ps1 -Command "pwsh ./scripts/audit-teamster-navigation-api.ps1 -Configuration Release"
```
It checks 28 contracts:
- every member above, against the decompiled installed game;
- the `HorseSize` agent's size;
- the partial-path refusal;
- the 12 m end snap;
- the game's own solid and blocking layer masks;
- the 0.5 m walking corner radius;
- the Unity physics signatures;
- the TagManager layer names.

It also disassembles the built Teamster DLL and fails if the navigation adapters reference any game type or member
outside the audited list. Result on 2026-09-17: **PASS**, Valheim 1.0.14, build 25364265.

## Integration notes

- **Assembly.** `CartNavigationKit.Create(limits, climbModel?, descentModel?)` builds the planner
  (`ICartRoutePlanner`), the monitor (`IHaulMotionMonitor`), the recovery policy, the failure cache and the staging
  selector over the game adapters. Call `UseCart(cart, gunnarBody)` when a cart is leased: it measures the live
  footprint and keeps the cart and Gunnar out of their own clearance probes. Call `ReleaseCart()` when the lease ends.
  The models come from `LoadCalibrationSource`/`DescentCalibrationSource`.
- **Planning once hitched.** Use `Plan(request, body.Position, now)`, so the first turn uses the cart's real heading.
  A cart facing away from its target then refuses as `TurnTooSharp` instead of being caught at run time.
- **Refresh.** Call `NeedsRefresh`/`Refresh` while pulling. Without it, an obstacle placed after planning is found only
  when the monitor judges the pull stuck.
- **Recovery.** `HaulRecoveryPolicy` is optional for an executor with its own. The failure cache still needs a
  `Remember` for each stuck place, or re-plans may repeat the same way.
- **Logging.** Log `planner.LastAssessment.Describe()` on every refusal and at each leg's acceptance.

## Known limits and risks

- **No turn-around manoeuvre.** A cart facing away from its target cannot be planned into a reversal. It refuses, and
  the player turns the cart.
- **First query in a new place.** The first navmesh query for the `HorseSize` agent in an area can come back empty
  while the game builds the tiles (`PathNotFound`). The query budget and recovery backoff pace asking again.
- **Parking is judged from the axle's track** (running grade over 3 m, cross slope at the axle), not from a survey of
  the whole box.
- **Low obstacles.** Obstacles lower than 0.3 m are left to the ground checks, so small debris is not "clearance".
  Items and characters never block.
- **Box sweeps are straight per stretch,** widened in turns. Unity reports an obstacle the box starts inside with no
  contact point, so only the first sweep of a route ignores such overlaps.
- **Lava** uses the game's own threshold (0.6). Tar counts as water.
- **The failure cache's pauses are not `HaulLimits` values.** They are fixed constants (the companions' proven numbers).
  Making them configurable needs contract names (next revision).
- **Frame cost unmeasured.** The frame cost of a plan (up to 128 box casts, about 4 ground raycasts per sample) is not
  measured in game.
- **Other mods.** The planner cannot know about mods that change cart colliders or mass after the footprint is read.
  `UseCart` should be repeated when the load changes a lot.

## Live observation (pending)

Use the lead's disposable world and the dedicated profile, with Gunnar hauling enabled, a cart assigned and hitched,
and navigation log lines visible. Record for each: the build (Valheim 1.0.14, build 25364265), the Teamster DLL
SHA-256, the scenario, the log line from `LastAssessment.Describe()`, and the cart's cargo count before and after.

1. **Loaded route around an obstacle.**
   - **Setup:** on flat meadow, load the cart with 50 Stone. Put a target 25 m ahead, and between them, about 10 m from
     the cart, a 2 × 2 m block of wood wall pieces (or a large rock).
   - **Action:** request the leg.
   - **Expect:**
     - the plan is Suitable, its line bending around the block (`Repairs` ≥ 1 in the log when the navmesh hugged it);
     - Gunnar walks around it, and the cart never touches the block: no impact effect, cart health unchanged;
     - no vegetation is flattened and no water is crossed;
     - Gunnar stops at the stop point with the cart behind him on level ground;
     - the cargo count is unchanged.
2. **Refused narrow passage.**
   - **Setup:** enclose the target with wood walls, leaving one 2.2 m gap as the only way in. That is narrower than the
     2.72 m corridor.
   - **Action:** request the leg.
   - **Expect:**
     - refused TooNarrow (`MeasuredTooNarrow`);
     - Gunnar does not move and the haul does not start;
     - the stated reason is shown.
   - **Then:** widen the gap to 3.2 m and request again: Suitable, and the cart passes without touching the walls.
3. **Refused slope.**
   - **Setup:** raise a 25 % ramp (the calibration protocol's steep ramp) between the cart and a target on top. Check
     the grade with the Cart Status panel at the ramp.
   - **Action:** request the leg.
   - **Expect:** refused TooSteep (`RunningGradeTooSteep`), with Gunnar not moving.
   - **Then:** repeat from the top down to a target at the bottom: also refused. Then a 10 % ramp: Suitable.
4. **Wedged cart stops and reports.**
   - **Setup:** during a pull on flat ground, place a stone wall piece directly across the cart's path, just behind
     Gunnar, so the cart is held while he pulls.
   - **Expect:**
     - within about 2.5 s the pull is judged Stalled or Wedged, and Gunnar stops walking;
     - the executor holds still for its backoff (5 s, then 10 s) and recovers at most `MaxRecoveryAttempts` (2) times;
     - with `HaulRecoveryPolicy` or failure-cache `Remember` wired, those are at most one back-off (only if the space
       behind is clear) and one re-plan that refuses to go the same way (`RecentFailure`);
     - the haul ends NeedsAttention (Wedged) with Gunnar still;
     - no joint snap, no growing force, and cart health and cargo unchanged.
   - **Then (failure cache wired):** request the same leg immediately: refused (`RecentFailure`) with no navmesh query in
     the log. After removing the wall and waiting out the pause (60 s), it plans again.

The other rows, also pending:
- **Water:** a stream across the only way. Expect Water.
- **Doorway:** a door on the only way, open or shut. Expect ForbiddenDoor.
- **Unsafe stop:** a target on a 10 % slope. Expect the stop earlier on level ground, with the shortfall logged.
- **Unloaded:** a target beyond the loaded area. Expect OutsideLoadedArea.
- **Refresh:** place a wall across the route while pulling. The next refresh refuses before the cart reaches it.
- **Staging (with agent E):** a rendezvous inside a narrow patch. Expect the nearest level spot outside it.
