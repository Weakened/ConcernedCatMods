# Gunnar presentation (#458): source handoff

Built on #456/#457. GunnarBodyPlan prioritizes every saved worker body over any
figure. A figure otherwise requires a registered role, known world, host, intro
opt-in, reliable zero worker census, rest, verified anchor, nearby player and
IntroPending/Recruited. Unknown facts and tools-only Skipped show nobody. The
visitor does not imply recruitment. MaySurvey denies unopted/non-host work before
any full-world census. The existing census stays in Claude's runtime.

GunnarPresentationRole wraps the existing role BEFORE its sole registration.
It uses ForWorkerWithPresentation without changing teamster/gunnar,
CT_TeamsterWorker, tcc.worker. or the existing data paths. It is not registered
by this branch: the lead replaces the worker-only role at its existing call site.
Do not register two roles or replace the current worker factory/prefab.

GunnarBodyLeases holds only presentation permission in the real ConcernedNPC
arbiter. Worker permissions remain with the existing job holder. HandOverToWorker
removes the local figure, releases presentation and THEN calls the existing
worker claim. A removal failure keeps the lease and cannot claim a worker. The
lead must capture the figure's actual position before handover and use that
position in its existing safe spawn/placement path; this branch never moves a
worker, cart or cargo. Existing despawn/custody rules remain authoritative.

GunnarFigurePresenter delegates extraction, dressing, standing, talk and cleanup
to ConcernedNPC. It adds no direct engine writes/component surgery and creates no
collider, AI, network view or saved entity of its own. The library's existing
figure-parts pins still apply; Teamster-specific validator plants and confinement
must be added/reconciled by the lead after Claude's active #495 validator work.
That required #458 audit row is NOT claimed complete here.

GunnarLook requests Short Curls + Stonedweller. GunnarLookSource.Read reads the
installed ObjectDB/localization label matches and logs actual prefab IDs; no
Hair23/Beard16 guess is compiled as a default. This read has NOT been run in the
game by Codex. Non-English labels may remain unresolved; that is a reported
fallback to the source look, never a matching appearance claim. The library
applies the character palette: skin 0.52, hair tone 0.05, blondness 0.49; explicit
hair/beard overrides survive. Unavailable palette uses approximate brown-gray
hair RGB (0.35,0.32,0.28) and unchanged source skin. Clothes remain the source
model: no costume is approved by the reference. Colour/fit are unobserved.

Lead changes still required: role registration, lifecycle, same-position spawn
handover in ct_haul and #317 panel, appearance row in APPEARANCE_REFERENCE.md,
Teamster figure confinement/plant audit and real installed preset/visual evidence.
Plugin.cs, TeamsterSettings.cs, GunnarNpcRole.cs, all worker files, validator and
shared appearance document are deliberately untouched while Claude owns them.

Automated: every missing fact, saved/unloaded-worker priority, visitor/recruited
eligibility, unchanged durable role contract, real arbiter exclusion, removal
failure, release-before-claim, world-unload lease invalidation and label/palette/
override policy. These tests do not prove the uninstalled native call sites.

OWNER GO-AROUND PENDING: native preset IDs, front/side/daylight match, visitor at
home, no figure beside saved/unloaded worker, spawn/despawn at his prior spot,
peer/dedicated refusal, missing source assets, logout/relog and cleanup.

Additional lifecycle guards: non-finite placement/yaw refuses the figure; an
expired presentation lease removes the old figure before a new lease is taken;
accessory-fit verification waits until a later presentation tick after creation.
