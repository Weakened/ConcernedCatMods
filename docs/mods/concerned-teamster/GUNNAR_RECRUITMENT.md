# Gunnar real-cart recruitment (#460): native implementation

Owner decision 2026-11-01: **Require hauling opt-in and host authority.**
GunnarRecruitmentRuntime is installed/ticked/forgotten from the existing Codex
companion lifecycle in Teamster Plugin. The visible Gunnar Introduction panel
shows the objective from IntroPending (or a later welcome after Skipped), names
an explicitly selected cart, and confirms separately. Opening the panel does not
select anything. No nearest-cart inference exists.

## Native selection and authority

Choose Pointed Cart reads Player.GetHoverObject and that object's Vagon parent
once. GunnarRecruitmentCartPort reads THIS exact live object: its native view,
current load key, localized name/location label, supported hand-cart kind,
ownership, attachment/occupancy/container-in-use/chair flags and three-dimensional
reach (the existing HaulExecutionLimits.SelectionReachMetres default). It never
searches Vagon.m_instances, reaches an inventory, calls an engine setter, claims
ownership, or modifies a cart. A different active Gunnar cart lease refuses the
choice; the same explicitly assigned, parked, usable cart can count.

Both selection and confirmation ask the REAL GunnarHaulService.Authority, which
reads the current Teamster master/hauling switches, Match Player pull strength,
loaded host/non-dedicated status and no-peers rule. Companion visibility is not
that verdict. The existing service supplies its actual WorldLoadEpoch; no fallback
identity is minted by this UI. Unknown/unavailable means refusal.

The in-memory prompt contains a single-use token, exact CartKey, companion
world/character scope and displayed label. Confirm re-reads the retained object,
not the current hover. Changed scope/epoch/key/label requires a new selection.
A moved cart whose displayed location changed therefore needs a fresh named
confirmation. Wrong, occupied, unavailable, out-of-reach, unauthorized or conflicting
lease facts refuse. Two pending selections cannot replace each other's prompt.
Cancel, opt-out, world/character change and teardown forget all native references
and the token. No cart/ZDO id is stored in the companion sidecar.

## Permanent recruitment, separate work readiness

Only a durable TryRecruitAfterAcceptedCartAssignment success shows gunnar.complete.
Save failure consumes the prompt and grants nothing; select again when writable.
The UI's permanent recruitment state is read from the record. Cart loss, disabled
hauling or a trip needing attention never re-locks it or repeats the quest.
Readiness uses the existing service's authority, phase, attention detail, body
availability and lease; all jobs retain their original gates.

The optional First Delivery button points the player to the existing hauling
panel to choose/plan a trip using their own cargo. It starts NOTHING, creates no
items, makes no lease and touches no cargo. No unverified player-owned-cargo
signal is used to manufacture a tutorial transaction. Existing cart assignment
and job controls remain in the #317 runtime. No worker/figure binding is changed.

## Exact remaining integration gate

The new panel has two honest UnityEngine.Object.Destroy calls in Dispose, for
its OWN panel/button returned by GUIManager. No borrowed object is accepted by
its public/internal interface. The four cleanup audit tests prove actual GUI
assignment/receivers and plant a body destroy, substituted receiver and immediate
destroy; they state their text-audit limits and do not claim control-flow proof.

The current validator correctly refuses these UNPINNED new call sites. Claude's
active tools/validate_repo.py is untouched. The integration handoff supplies the
one-line `TEAMSTER_DESTRUCTION_SITES` entry:

`"Ui/Companions/GunnarRecruitmentPanel.cs": 2`

This is GUI population only, NOT retirement or body-removal authority. Claude must
review/apply the exact pin, run its existing plants plus the new cleanup tests,
then re-run the controlled full gate on the INTEGRATED source. No DLL/ZIP from
this pre-pin source is approved for installation or release.

An earlier automatic review rejected Plugin wiring by interpreting its presence
in the retained ownership JSON as a Claude claim. Fresh PR/hammer file lists and
the JSON's actual `changedFiles`/`overlap: []` keys proved it was prior Codex work;
the evidence-backed retry was approved. Only this isolated worktree was changed.
No Claude checkout, validator, runtime, settings, catalog, profile or game session
was edited. All new player copy resolves through GunnarRecruitmentStrings source rows,
with translation and formatting tests. The active localization lane must merge
those rows into the shared TeamsterStrings catalog for native override/template
registration; the existing dialogue key fallback is reused.

## Operator checks on the final integrated candidate — NOT RUN

1. Use the isolated Gunnar profile and disposable character/world. Log exact
   Teamster/NPC versions, source SHA and package/DLL hashes; no source-only pass.
2. Pick up/read the buckle. The objective appears in Gunnar Introduction without
   disabling any pre-existing tool. Enable existing hauling switches on the host,
   Match Player strength, with no peers; no body/job is started by the quest.
3. Aim at an EXISTING cart within reach. Choose Pointed Cart displays that cart's
   localized label/location. Aim at a second cart; confirm still checks the first.
4. Move/rename/destroy/unload/occupy the selected cart; change authority or scope;
   confirmation must refuse/reselect appropriately. Test seat/container occupancy,
   siege machines, two close carts, two selections, a different active lease, and
   a parked same assigned cart. No cart state or cargo changes from either button.
5. Confirm a usable cart; completion follows the save. Relog/world switch/back:
   permanent recruitment remains, pending selection never does. Loss of the cart
   pauses existing jobs with their normal attention reason, never the quest.
6. Refuse a save, then restore writable storage: prompt is consumed, no completion
   is shown, new selection/confirmation succeeds once. No repeated recruitment.
7. Optional first delivery only opens instructions; use the existing hauling panel
   yourself with your own cargo. No tutorial cargo, automatic assignment or trip.
8. Close/cancel, disable Companions, logout/world change/plugin teardown and UI
   failure: no orphan GUI or retained cart reference. Current localization overrides
   and controller navigation remain owner observation checks.

After merge, transfer observation-only rows to current #470 with the exact
candidate identity and steps. The audit PIN is remaining CODE/integration work;
it must not be reclassified as merely an owner observation.
