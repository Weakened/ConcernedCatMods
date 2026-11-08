# Gunnar at home: his presentation body (#458)

Status: **code-complete, never observed in game.** Every in-game row below is
OWNER GO-AROUND PENDING. Parent: #297 ("A Fair Load").

## What a player sees

Once this character has examined the worn harness buckle and the introduction
has begun (`IntroPending`), Gunnar stands beside the respawn anchor (a claimed
bed, else the world start) as a visitor, before he has a cart. He stays there
once recruited. He is shown only while he has **no worker body** anywhere in the
world, only on the host (single player, or the host of a game; never a dedicated
server or a client), only with `Companions/Gunnar` on, and only within 48 m of
home (kept until 64 m). A skipped story shows nobody. The figure is not
interactive yet: talking to him is #459.

`ct_haul spawn` hands him over to his worker body: the figure comes down, its
presentation lease is released, the worker lease is claimed, and the body is
placed **where the figure stood** (on the ground there), facing the player. With
no figure standing, spawn behaves exactly as before. `ct_haul retire` removes the
body as before; once the census agrees he is gone, he is shown at home again.
`ct_haul status` gains one line about his figure and its lease.

The #317 haul panel has no body verb (it assigns, confirms, releases, stops,
detaches and hauls), and every command it sends goes through the same
`GunnarHaulingRuntime.Execute`, so it cannot place a body round the hand-over.

## One body at a time

The rule is CLAUDE.md's: the presentation body and the worker body never
coexist. It holds three ways:

1. **The plan** (`Domain/Companions/GunnarBodyPlan.cs`). A saved worker body
   anywhere outranks everything, loaded or not. Then: registered role, world,
   host, opt-in, an `IntroPending` or `Recruited` story, a census that has
   finished and found none, resting (no job holds him), a known anchor and the
   player near it. Anything unknown means no figure.
2. **The leases** (`Domain/Companions/GunnarBodyLeases.cs`), through Concerned
   NPC's one arbiter, which refuses the second kind while the first is held.
   The role is `NpcBodyContract.ForWorkerWithPresentation` with the same prefab
   name (`CT_TeamsterWorker`) and key prefix (`tcc.worker.`) as before, so no
   saved world migrates (`GunnarBodyContract`, pinned by `GunnarBodyTests`).
   Teamster's jobs claim the worker kind under their own job ids, so a standing
   body lease would refuse every haul. Instead the hand-over holds an **arrival
   guard** (holder `teamster/gunnar-arrival`) from the moment the figure's lease
   is released until the census accounts for the body it placed (or has said
   "no body" for five seconds, which is then not a stale answer). While it is
   held, the arbiter refuses a figure to every runtime in the process.
3. **The presenter** (`Adapters/Companions/GunnarFigurePresenter.cs`) builds the
   figure only after its lease is granted and takes it down before anything that
   could release the lease. A take-down that throws keeps the lease and claims
   nothing.

A world unload takes the figure down before Concerned NPC is told the world has
ended (`GunnarHaulingRuntime.BeforeWorldEnds`); the plugin's teardown takes it
down before the hauling runtime is removed.

## What it reads

Nothing for a player who has not met him: the gate (host, opt-in, registration,
world) is checked first, then the story, and only then is the anchor looked up
and a spot probed, through the buckle's bounded read-only queries, twice a
second. The census is the hauling runtime's existing one; the figure adds no
scan of the world (#443's lesson). The spot is a deterministic ring sweep of the
shared placement planner, 1.5-4 m from the anchor, on bare ground only (seats,
beds and fires are #461's camp poses), and 1.25 m clear of another product's
keepsake.

## What it is

The game's own character prefab (`Game.m_playerPrefab`), extracted dark by
Concerned NPC's `NpcPresentationBody`: no network view, no character, no AI, no
collider that blocks anybody, nothing saved, every game script removed before it
was ever enabled. Dressed and stood by Concerned NPC's `NpcFigureDresser`. It
changes nothing in the world, claims no bed and is seen by nobody else.

## The look

`Domain/Companions/GunnarLook.cs`, from `docs/companions/APPEARANCE_REFERENCE.md`:

- Hair **Short Curls**, beard **Stonedweller**, by the customization screen's
  label first, then by token (`$customization_hair23`, `$customization_beard16`),
  then by prefab id (`Hair23`, `Beard16`). The tokens and ids were read
  read-only from the installed game on 2026-11-08: its localization table in
  `valheim_Data/resources.assets` (`"customization_hair23","Short Curls"`,
  `"customization_beard16","Stonedweller"`) and its prefab manifest
  `StreamingAssets/SoftRef/manifest_extended`
  (`customizations/hairs/Hair23.prefab`, `customizations/beards/Beard16.prefab`).
  A build that renumbers them is caught by the label, which outranks both. A
  missing preset is reported as a fallback, never as a match.
- Colours through the character screen's palette at skin 0.52, hair tone 0.05,
  blondness 0.49. Where the palette cannot be read: hair RGB (0.35, 0.32, 0.28)
  and the source model's own skin.
- Clothes, **this build's proposal** from #381's appearance line: leather
  trousers (installed label `Leather Trousers`, token `$item_legs_leather`,
  prefab `ArmorLeatherLegs`) and a bare torso. **Megingjord is not drawn**: the
  shared dresser dresses hair, beard, chest and legs only, and a belt slot in
  Concerned NPC is outside this issue (lead default pending owner confirmation,
  `docs/settlement/cart-and-collection/DECISIONS.md` D18 note). His capacity is
  unaffected.
- His worker body keeps its plain look. D18 allows it to wear his own hair and
  beard behind a setting of its own; that is not built in Teamster yet.

## The audit

`tools/validate_repo.py`'s **`[teamster] #458 figure audit`**, over the whole
product less `obj/` and `bin/` plus the three shared trees it compiles in:

- only `GunnarFigurePresenter.cs` may name the figure library
  (`NpcPresentationBody`, `NpcPresentationFigure`, `NpcFigureDresser`, and the
  library's internal parts), keyed by path so a same-named file elsewhere
  inherits nothing; `NpcWorkerDresser` is named nowhere until the worker look is
  built with its own setting and pin;
- in that file each figure call is pinned by spelling and population (one
  extraction, one dresser, two stands, one dressing, one talk point, one
  take-down);
- that file makes no engine write, component lookup, network-object reference,
  transform access, reflection or interpolated string of its own;
- the rest of `Adapters/Companions` instantiates, adds components to,
  re-parents, activates and poses nothing.

`tools/tests/test_teamster_figure_audit.py` plants each escape; every plant is
refused by the rule and passes the validator without it (origin/main 4457f20),
except the one regression pin that the #313 worker-runtime scope audit still
refuses component surgery in `Adapters/Workers`. A text audit: it does not
follow a delegate to an engine call that spells none of these tokens, and it
does not prove control flow.

## Automated coverage

- `GunnarBodyTests`: the contract's durable facts; a worker-only role is not
  registered for a figure; every missing fact; saved-worker priority;
  visitor/recruited eligibility; hand-over order against the real arbiter; the
  arrival guard refusing a figure to every holder; a job holding him; removal
  failure; world unload; placement band, keepsake clearance and every deferral.
- `GunnarLookTests`: label, token and id; renumbered build; another language;
  honest fallback; palette; clothes; player overrides.
- `GunnarHandOverOrderTests`: the shipped order in the presenter, `ct_haul
  spawn`/`retire`, the world unload, the pass (story before anything physical)
  and the plugin's wiring.

## OWNER GO-AROUND PENDING

- P1 the visitor appears at home after the buckle; not before; not after Skip.
- P2 front, side and neutral daylight against the reference: hair, beard,
  colours, trousers, bare torso; the resolved ids in `ct_haul status`.
- P3 never a figure beside a saved worker body, loaded or not; none during a
  haul or a tour.
- P4 `ct_haul spawn` places the body where the figure stood; `ct_haul retire`
  brings him home again.
- P5 a client of a hosted game and a dedicated server show nobody.
- P6 logout/relog, world change and plugin reload leave no figure behind.
- P7 a missing rig or preset is one log line and every Teamster tool still works.
