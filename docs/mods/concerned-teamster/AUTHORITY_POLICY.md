# Multiplayer trust and authority policy (CT-026)

This is the written contract behind `Domain/Authority/CartAuthorityPolicy`:
who may read, who may act, and when a reading is remote, for every shipped
Concerned Teamster feature. The policy class is the single source of truth;
this document is written to match it, `tools/validate_repo.py` fails if any
feature enum value is missing here, and the parking brake enforces its right
to act *through* `CartAuthorityPolicy.MayMutate` (test-asserted).

## Foundational invariants

1. **Teamster sends no network messages and takes no ownership.** It reads
   the game's own replicated/local state and writes only its own per-world
   sidecar files. There is no RPC, no `SetOwner`/ownership claim, no
   `ZDO.Set` anywhere in the source — validator-audited (comments that state
   this absence are the only occurrences). Therefore an unmodded peer's
   experience is provably unchanged by Teamster's presence: there is nothing
   for Teamster to alter it *with*.
2. **Exactly one feature mutates cart state** — the parking brake — and only
   under **live local vanilla authority** (`ZNetView.IsValid() &&
   ZNetView.IsOwner()`, the verified surface in `CART_INTERNALS.md`). Every
   authority ambiguity fails closed: `CartAuthority.Unknown` (value 0) denies
   mutation, and an engaged brake releases the instant authority is not
   locally held.
3. **Observation is client-side and read-only.** Any client may read
   replicated or local state. Numbers that the game keeps fresh only on the
   owning client (cart mass, grade, pull state) are **labeled remote** when
   observed without local authority, so an observer is never shown a stale
   value as current truth.

## Authority states

| State | Meaning | Mutation |
|---|---|---|
| `Local` | This client owns the cart under vanilla rules right now | permitted (brake only) |
| `Remote` | Cart is valid but owned by another client | denied |
| `Unknown` | Capability off, invalid view, or probe failure (fail-closed default) | denied |

## Feature matrix

Every value of the `TeamsterFeature` enum appears here. "Class" is the
feature's only relationship to cart state; "Remote-labeled" marks
observations whose values are owner-fresh and must be flagged when observed
without local authority.

| Feature (enum) | Class | Right to act | Remote-labeled |
|---|---|---|---|
| `CartTelemetry` | Observation | — (read-only) | yes |
| `CartStatusPanel` | Observation | — (read-only) | yes |
| `CargoManifest` | Observation | — (read-only) | yes |
| `LoadWarnings` | Observation | — (read-only) | yes |
| `DescentRisk` | Observation | — (read-only) | yes |
| `RecoveryGuidance` | Observation | — (read-only) | no (advisory text over local reads) |
| `TripRecording` | Observation | — (read-only, own sidecar) | no (local history, not owner-fresh cart state) |
| `RouteProfiling` | Observation | — (read-only) | no (route geometry + terrain, not owner-fresh cart state) |
| `ParkingBrake` | **Mutation** | **Local authority only** | no |

## Per-actor summary

- **Local player, owns the cart (`Local`):** full observation (fresh) and the
  only actor who may engage the brake.
- **Local player, cart owned by a peer (`Remote`):** full observation, but
  owner-fresh readouts are labeled remote; brake is unavailable.
- **Other modded peer:** each client runs this same policy independently
  against its own authority; no Teamster-to-Teamster messages exist, so peers
  never coordinate or contend through Teamster.
- **Unmodded peer:** sees pure vanilla behavior; Teamster neither sends them
  anything nor alters any state they replicate.

## Enforcement

- `CartAuthorityPolicy.MayMutate(feature, authority)` — true only for
  `ParkingBrake` under `Local`. `BrakeLifecycle` calls it at engage and on
  every tick; an engaged brake that loses authority releases with one log
  line, and an engage-time refusal surfaces through the toggle's returned
  reason (not the log).
- `CartAuthorityPolicy.RequiresRemoteLabel(feature, authority)` — true for
  owner-fresh observations viewed without local authority.
- `tools/validate_repo.py`: (a) asserts every `TeamsterFeature` value is
  documented here; (b) audits Teamster source for outbound-network and
  ownership-takeover tokens (fails the build if any appear outside comments).
- `ConcernedTeamster.Tests` proves matrix completeness, the mutation truth
  table, fail-closed resolution, and that the brake's authority gate equals
  the policy's.

## Changing this policy

A new cart-touching feature adds a `TeamsterFeature` value, a
`CartAuthorityPolicy` entry, and a row here in the same change — the validator
and the completeness test both fail until all three agree. A new *mutating*
feature must additionally justify its authority gate in its own issue
(mutations are explicit, reversible, fail-closed, separately authorized).
