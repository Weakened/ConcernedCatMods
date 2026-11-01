# Gunnar buckle (#457): source handoff

Built on #456. Uses ConcernedNPC NpcKeepsakeSpec / NpcKeepsake; no copied
render implementation, network entity, item or world mutation. The borrowed
BeltStrength look is a runtime lookup candidate, not a verified screenshot or
an original buckle asset. If no renderable mesh is available the library refuses.

GunnarKeepsake.Examine saves Discovered then Collected before returning Collected.
Only this result may remove the buckle, show the inscription or open the intro.
Duplicate input grants nothing. A failed save leaves the presentation eligible.
No recruitment, tool unlock, inventory movement, automatic speech or nag is added.

Lead-owned wiring (not installed by this source branch): construct
Adapters/Companions/GunnarKeepsakePresenter with the #456 adapter, live worker
existence (null = unknown), a player-visible notice sink and an intro callback.
Call Refresh at the presentation cadence with a current verified respawn anchor,
a loaded-ground IPlacementProbe and a COMPLETE snapshot of occupied memento
centres. Null snapshot defers placement. Never substitute an empty list when
other memento positions are unknown. The synthetic compass/flint/buckle test
proves separation for supplied footprints, not discovery of other products.
Actual cross-product footprint discovery remains a lead integration requirement.

Placement uses the shared PlacementPlanner (48 probes maximum), grounded only,
2-4 m from the anchor and 1.25 m from supplied memento centres. Invalid/non-finite
anchors, unloaded ground, unknown occupancy, unknown worker census, a worker
body, opt-out or saved collection all suppress the buckle. Refresh retains an
unchanged object and removes an obsolete one; Forget is required at world exit
and plugin teardown. Worker existence must cover saved bodies, not just nearby
loaded objects. No census or full-world scan is started by this class.

The inscription is exactly: A fair load. A clear road. Everyone home. - G.

OWNED: the new GunnarKeepsake domain policy, GunnarKeepsakePresenter adapter,
GunnarKeepsakeTests and this guide. Plugin.cs, TeamsterSettings.cs, worker code,
shared library, validator, profiles and game remain lead-owned and untouched.

Automated: real temporary sidecar pickup/reopen/idempotence, failure/retry,
opt-out/worker/unknown denial, shared-planner footprint separation, missing anchor,
unknown occupancy, invalid coordinates and all-candidates-occupied deferral.

OWNER GO-AROUND PENDING: actual buckle look/size/hover/use; actual Hulgi/Sunniva
coexistence; saved/unloaded worker suppression; moved/destroyed bed; repeat relog;
write refusal with no inscription/removal; logout and uninstall cleanup.
Do not close #457 or call this an integrated game feature from source tests alone.
