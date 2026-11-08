# #356 ordinary resident — owner decision and implementation

The owner chose on 2026-11-01: "Presentation resident; no jobs."
One ordinary resident has a local extracted presentation figure, a saved housing
reservation and a small native-pose daily routine. No job, inventory, custody,
combat, loot, network body, bed claim or worker motor is added. Neither the
reservation nor the figure grants features or worker authority.

## Persistence and housing

The EXISTING SettlementJournal/JournalStore is used in a separate product-owned
`residents` directory. It is not a second writer to the settlement's custody
journal. ResidentRecorded is appended as kind 23 in the existing named-field
lane; old kind numbers and schema behavior are preserved. A resident has no
synthetic material order or worker id. Unknown fields/phases, orphan admission,
changed reservation payload and multiple resident identities fail closed.

Reserved and Admitted are separate durable events. Failed saves discard ONLY
the unsaved tail; a callback reporting failure after actually saving blocks until
reload rather than discarding durable evidence. Repeated confirmation and reload
do not create another identity. This slice admits ONE resident per world/home
scope. Exact bed coordinates identify the slot, not rounded labels or load-local
native object ids. Complete surveys reject ambiguous duplicate addresses.

Native roof/cover/fire/claim facts come from WorldHousing. Reservation and
confirmation take fresh measurements. Routine revalidation runs every 10 seconds
and measures cover only for the reserved bed. Incomplete/unloaded ground pauses;
it does NOT mean homeless. A bed absent from a COMPLETE loaded survey is BedRemoved.
A moved settlement area reports OutsideSettlement, not a fabricated missing bed.
Unsafe housing records Homeless, retaining the resident/figure; it never deletes
one in response to that loss. Repaired housing requires explicit reservation and
confirmation again. A failed invalidation write pauses the routine. A destroyed
reservation before admission can be replaced without first creating a resident.

## Local presentation and routine

ResidentPresentation registers `foreman/ordinary-resident` as ForPresentation
and claims one Concerned NPC body lease. The standard dark extraction removes
native scripts, character, network and physics; only its owned local visual is
stood at a clear supported spot beside the bed. No specialist identity or worker
look is reused. Proposed ordinary clothes are the shared leather-tunic/pants
requests; their runtime resolution remains observable, not an asset guarantee.

The existing ThorsteinPresentation host ticks the independent service BEFORE
Thorstein's fault gate; residents do not require recruiting Thorstein. Its world
unload/disposal hooks remove the figure/UI and release the lease before the
registry world ends. Master/Residents opt-out removes local presentation while
preserving the resident record. Leaving loaded ground or the 64 m view radius
unloads only the local visual; a fresh safe check rebuilds it on return. Unsafe
housing itself retains the current figure.
A homeless reload retains the identity even when no safe figure spot can be found.

Vanilla clock: installed EnvMan.GetDayFraction() was inspected via ilspycmd; it
returns m_smoothDayFraction. Native IsNight uses <=.25 or >=.75. Routine stages:
night ground rest; morning ground sitting (.25-.35); daytime standing/idle;
evening ground sitting (.65-.75). This is a modest home routine, with no work,
resource or native sleep effect. It rests on the clear ground beside the reserved
bed; it DOES NOT claim, attach to, or trigger sleeping in the vanilla bed.

ResidentPose accepts ONLY the extracted NpcPresentationFigure, never an arbitrary
Player/worker/Animator. It checks exact bool parameters `onGround`, `lying_down`,
`emote_sit` and refuses root motion. Those parameters and applyRootMotion=False
were observed in the actual installed player's native baseline:
`C:\code\cc-integration\cl-ladders-review-evidence\native-baseline\animdump.txt`
(game 1.0.20 / Unity 6000.0.75f1). Native transitions blend sitting/standing/rest;
no authored clip, trigger, speed parameter, root travel or extra motor is used.
Metadata is NOT rendered-playback evidence: pose fit and blends still need game checks.

## Operator procedure for the isolated integration candidate

1. Claude integrates this issue branch into the test candidate and runs the
   shared-lock build/verify gate. No profile import/build/game operation is owned
   by this source handoff. Include Concerned NPC at the existing compatible pin.
2. In that candidate's Foreman config, opt into `Settlement/SettlementRuntimeEnabled`
   and `Residents/Enabled` (both false by default). The Residents panel button is
   visible on the host with a local player. Diagnostics need neither switch.
3. Use the existing settlement-area designation. Build a real roofed, at least
   80%-covered, warm bed, unclaimed or owned by you but not your current spawn bed.
4. Stand within four metres. Residents > Reserve nearby bed chooses the nearest
   qualifying measured bed, saves its reservation and shows the result. Confirm
   admission rechecks that exact slot and saves admission separately.
5. Refresh status or Speak to the local Resident to read admission/routine state.
   Record files: `<BepInEx config>/ConcernedCatMods/ConcernedForeman/residents/`
   plus JournalStore's world/settlement key and `.settlement.tsv` suffix. Do not
   edit/copy another world's record to test it.
6. Remove roof/fire or claim/remove the bed; wait for the 10-second recheck.
   Rehome explicitly. Restore config switches to opt out; saved identity remains.

## Required candidate game checks — UNVERIFIED

- Fresh install/UI access; nearest selection with two close beds and upstairs bed.
- Reload between reservation and admission, repeated confirmation, ordinary relog,
  world switch and return: exactly one identity/figure and correct world record.
- Destroy reserved bed before admission; destroy roof/fire/bed after admission;
  player claims the slot; safe rehome; no resident deletion caused by housing loss.
- Travel out of loaded ground, return, and reload homeless: unknown vs removed
  wording, retained record and safe presentation placement.
- Observe morning/day/evening/night poses and transitions on the actual rig,
  leather garment fit, ground-rest clipping, lighting/terrain, other mods' controller.
- Toggle either switch, logout, plugin shutdown, UI fault, source/controller failure:
  no orphan UI/figure/lease and no duplicate after restoration.
- Host with another peer: only host sees the presentation; no NPC ZDO/RPC/native
  save entity, native bed ownership change or inventory/job operation.

These remain actual gameplay checks. Green source tests are not a finished game
build or authorization to publish, tag, merge, install, or close the issue.
