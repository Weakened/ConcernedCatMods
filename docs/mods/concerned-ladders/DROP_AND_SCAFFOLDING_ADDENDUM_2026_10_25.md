# Owner additions: Crouch/Drop and ladder scaffolding

These requirements supplement the active owner revision. They do not replace
any earlier mechanic or represent implemented/tested runtime behavior. The
existing local lead owns implementation, build, import and game operation;
the independent reviewer checks the new source and exact installed candidate.
Reconcile these docs into ongoing local work without resetting or overwriting it.

## N07 — separate Jump and Drop

| Action while attached | Result |
|---|---|
| Jump | Existing requested upward, directional hop when affordable; exhausted deliberate release remains available |
| Fresh native Crouch press | Immediate deliberate Drop, with no launch impulse or landing requirement |
| Use, when no native hammer action owns the binding | Existing safe exit to a valid landing, otherwise remain attached |

Use the native Crouch action and current keyboard/controller mappings. Inspect
the installed game's actual action and input routing; do not guess a raw key
or treat the persistent native crouching state as a button edge. A pre-held or
already-toggled Crouch must not drop a new mount. Re-arm press detection on mount
and after overlay focus; no buffered drop when a menu closes.

Drop works during mounting, hold, normal/fast climb, sliding, transfer, and
hammer use whenever gameplay input is available. It is free, works at zero
stamina, and does not require support below or a clear top landing. The preceding
eligible climb step still settles its earned stamina/progress once; releasing
must not avoid a charge already earned, nor be blocked by the remaining balance.

Relinquish the ladder motor/pose at the current safe body position and restore
ordinary gravity/collision/fall behavior. No Jump call, upward impulse, outward
kick, teleport or movement to a distant safe landing. Remove only motion the
ladder motor commanded, including horizontal mount/exit motion, without erasing
independently applied motion. Never write after ownership loss or overwrite an
external controller's takeover. Ordinary falling starts from the actual release
height after an accepted catch; no fall immunity. Falling earns no Climbing XP.

With gameplay focus, resolve eligible ladder actions Jump > Drop > Use > travel,
once. Consume competing release edges too: Jump+Crouch must not also toggle
sneak; Drop+Use must not trigger a world interaction after detachment. Outside
the session native crouch remains untouched, including pre-existing state.
Use the existing 0.4-second plus fresh Use guard across all these ladders after
Drop as after Jump; a press during the cooldown must not queue an automatic grab.
Show localized, current-binding Jump and Drop hints.

## N08 — native building while attached

A player can stop on a ladder, equip the hammer, aim around the anchor, open the
build menu, choose/rotate/snap/place pieces, repair and remove, then continue
climbing or deliberately Jump/Drop. The hammer itself does not forbid mounting
or climbing. Stationary hold, focused menus and native building actions retain
the anchor without passive climb stamina or XP. Preserve any native action
cost. Resume travel deliberately after the action/menu; do not replay queued
movement or departure presses. Do not move the capsule with a hammer animation.

Keep native build range, resource costs, stations, wards, placement validity,
structural support, collision and refunds. A mounted player's real collider
remains enabled; placement cannot gain permission to intersect the player or
ignore blocked geometry. No fake grounding, creative-mode flag, enlarged build
reach, synthetic resource/refund, unauthorized ownership claim or direct custom
world mutation. Execute the native building action once and respect its result.

Resolve input ownership before the ladder's fallback Use-to-dismount. If native
hammer snap cycling or another build action shares that binding, the hammer
action wins. Do not consume it wholesale. Jump and fresh Crouch/Drop remain
explicit departures with gameplay focus; focused build/menu UI owns its input.
Allow only verified native hammer placement/repair/removal through action/busy
gates, not all InAttack states or general weapon combat. Death, stagger,
teleport, lost ownership and external-control cleanup remain effective.

Allow the hammer's required arm/action animation alongside the ladder hold.
An authored full-body climbing clip or procedural IK must not pin the arms
against a native hammer action or silently cancel attachment. Keep equipment
correct and restore normal presentation after building, climbing, Jump, Drop,
death, disabled climbing and logout. Prove observer presentation separately.

### Build the next section and keep climbing

Placing an aligned ladder extension must update the attached session's usable
run without a dismount/remount. Refresh the bounded connected graph on relevant
registry changes or before crossing an endpoint; preserve face, current anchor
and height. Respect mixed materials and both clear faces. Reject actual gaps,
ambiguous joins, incompatible rotations and parallel-shaft shortcuts. Do not
scan all world objects every frame or teleport onto the new section.

Removing the actively supporting ladder ends attachment and restores ordinary
falling/control. An unrelated piece removal must not detach the player merely
because a hammer action occurred. Changed support/geometry is revalidated;
cached removed ladder sections cannot remain usable as invisible scaffolds.

### Audited old-source blockers to inspect in the current local implementation

The cloud audit at `8231d25446b1c27e51f171acecc3833fd7c0947f` found:

- `LadderRuntime.Free()` rejects `InPlaceMode()`, and both Update and Drive use
  that gate. Mount also separately refuses the building tool.
- Its blanket `InAttack()` refusal needs comparison with actual native hammer
  action states; removing the whole attack guard would be too broad.
- The fallback Use path can request dismount when no Interactable is hovered;
  native hammer action ownership must be resolved first.
- `TryRun` runs at mount and the connected run is cached for the session.
- The motor fixes the anchor while the pose path writes locomotion parameters;
  construction needs separate physical anchoring and visual action ownership.

These are source findings, not observations of the local agents' newer edits.
Inspect current code first and preserve already-correct work.

## Acceptance and handoff

Implement R36–R43 in TEST_PLAN and update relevant input/lifecycle/animation/
network rows. Add meaningful production-policy regressions for action routing,
fresh press handling, free Drop with preceding settlement, allowed hammer
context and bounded run refresh. Do not fabricate native engine stubs.

Record a new Drop demonstration: upward hop versus straight release, all
attached modes, zero stamina, both faces, remaps/controller, ordinary fall,
cooldown/fresh regrab and normal crouch afterward. Record a scaffolding loop:
mount with/equip hammer, menu/snap/place/repair/remove, build an extension, climb
into it without remounting, and remove current support with clean release.
Use normal costs/collision and a disposable course; show no passive climb drain
or XP, no duplicate native actions, and correct animation/equipment restoration.

Use the established exact-source build/verify/ZIP-import/fresh-start/identity
workflow. Neither docs nor identity-only success is gameplay proof. Give the
owner the final tested mod ZIP; this addendum is not an installable package.
