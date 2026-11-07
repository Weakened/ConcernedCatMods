# Gunnar buckle (#457): native wiring and evidence

The source now has an installed path: Plugin.Awake installs GunnarKeepsakeRuntime,
Update ticks it and OnDestroy forgets it before the record adapter. This isolated
change is outside Claude's active PR/hammer files. No installed profile, worker
implementation, validator, shared-library or package metadata is modified.

## Implemented production path

- The existing Gunnar census is read through callbacks. NotFound with no live
  body permits a buckle; searching/unspecified/unknown defers; any saved, live,
  unloaded, faulted or duplicate worker suppresses it. No additional census or
  network query is started by presentation. Live-body presence wins immediately.
- Native environment verifies the current bed using the actual profile spawn,
  loaded zone and Bed.IsCurrent; a destroyed loaded bed falls back to the world's
  actual Game.m_StartLocation lookup. Missing/unloaded/invalid anchors defer.
- Ground is sampled with at most 32 solid-surface hits per candidate, nearest to
  the anchor's level (lower floor wins a roof tie). Moving ground, steepness,
  water and real nearby solid obstruction are refused. Missing water/layer
  metadata and full buffers defer, with an actionable once-only limitation.
- Native footprint discovery is bounded to 8 m and 512 colliders on the canonical
  non-solid/item keepsake layers (all layers if neither exists). It reads actual
  BrokenCompassObject and shared NpcKeepsakeBehaviour components without a
  product assembly reference. Other shared keepsakes are reserved; the existing
  CT_GunnarBuckle is excluded from its own snapshot. Saturation is unknown, not
  empty. Sunniva's current flint is remembered quest state, not a placed object;
  a shared physical flint, if later introduced, is recognized by the same reader.
- The shared PlacementPlanner searches 2-4 m around the verified anchor and
  preserves 1.25 m separation from supplied canonical memento centres. Only
  ConcernedNPC builds/removes the local object. No item, saved entity or world
  write is added. BeltStrength remains a runtime LOOK candidate; if its meshes
  cannot be borrowed, placement refuses and reports it once rather than
  substituting an unrelated prop. Actual fitted appearance is unverified.
- Picking up saves Discovered then Collected before removal/inscription. The
  introduction saves IntroPending before its objective is shown. A second-save
  failure leaves Collected durable; quiet retries resume the story after storage
  recovers without recreating a buckle, duplicating pickup or granting a hire.
- A once-per-second physics budget within 30 m limits query work. This is only
  a sampling budget: no timed quest, countdown, reward or per-login nag exists.
  An unchanged object is retained. Real failure/notice logging is suppressed
  after the same failure. World/character changes clear references and objects.

The inscription is exactly: A fair load. A clear road. Everyone home. - G.
The real use callback shows it with the introduction/objective through MessageHud.
Authored lines use the existing Teamster catalog when present and the source
fallback rows otherwise. #459's catalog merge remains separate active-file work.
No cart assignment, worker start, recruitment grant or tutorial cargo is added.

## Verification and limits

The actual GunnarKeepsakeEnvironment source is linked into Teamster.Tests with
small native API test doubles. Tests cover current/destroyed/unloaded/saturated
bed validation, canonical compass/shared keepsake discovery and own-spot exclusion,
saturated footprint/ground buffers, floor-versus-roof choice, moving/absent ground,
water/slope/obstruction and unknown metadata. Existing real-sidecar tests cover
once-only pickup and both save-failure/recovery stages. Product compilation, not
these doubles, proves the actual native signatures.

No game has been operated or fresh profile imported by Codex. OWNER GO-AROUND
PENDING: actual borrowed buckle meshes/fit/hover, native physics/anchor results,
real Hulgi/Sunniva coexistence, saved/unloaded-worker suppression, delayed prefab
availability, moved/destroyed bed, repeated relog, write refusal/recovery and
logout/uninstall cleanup. Do not close #457 or claim game behavior from these
unit/compile results. No blanket solid-piece exclusion was introduced.
