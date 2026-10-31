# Changelog

All notable changes to Concerned Steward are recorded here. Versions follow
`Major.Minor.Patch`, and this product versions and releases independently of
every other Concerned Cat mod.

## 0.1.0

The first release (#340, #382). Sunniva keeps your settlement's fires burning.
Requires **Concerned NPC 0.5.0** (`TheConcernedCat-ConcernedNPC`), which ships as
its own package and must be installed with it; without it BepInEx will not load
the mod and says why.

### Added

- **Sunniva.** A settlement worker with a real body, a stable identity
  (`steward/steward`, prefab `CS_Steward`, keys `tcc.steward.`) and her own
  saved inventory in her own network object. Recruited once and remembered
  across reloads; one body per identity, enforced by Concerned NPC.
- **An introduction with an object in it.** The first time an eligible player
  picks up resin, they also find a flint and steel with runes carved into the
  metal - one for the life of that world. It is a discovery the mod remembers,
  never an item put into an inventory, and the resin pickup is untouched. Her
  introduction is told in beats on screen; recruited from inside the settlement,
  she moves in.
- **Explicit scope.** She acts only inside a settlement area you mark and takes
  only from the supply chest you mark. There is no nearest-chest search, and a
  stale mark refuses and asks to be marked again.
- **A planned maintenance round.** Every fuel-burning light in the settlement
  is surveyed once, the whole round's need is totalled once, and the supply is
  drawn once - five low torches are one trip to the chest, not five. Urgency is
  how long a light has left at its own burn rate; lights are topped up to a
  configurable burning time, not to full. Urgency decides which lights are in
  the round and which trip each goes on; she walks each trip as a short circuit
  from the chest, and only a light that might go out before the trip is over is
  visited first. At each stop
  she checks the light
  again (filled by you or destroyed: she moves on), walks there on her own feet,
  and feeds real fuel one piece at a time through vanilla's own
  `Fireplace.UseItem`. Surplus goes back to the supply chest you marked; if it
  has no room, she keeps holding it and tries again.
- **No free fuel.** Every piece she burns came out of your chest and was
  measured. A round she cannot pay for is refused, naming what is missing ("I
  need 40 Resin"); a round she can half pay for tends the most urgent lights in
  full and counts the rest.
- **Conservation and recovery.** Every unit she withdraws is in exactly one
  place at every moment: the chest, her hands, a fire, or recorded as
  unaccounted. Intent is written before anything moves; after a crash or reload
  an unfinished step is reconciled against what she actually carries, never
  replayed or compensated, and an uncertain one is left for you to resolve.
- **The round's books are closed.** Each planned light ends tended, skipped,
  refused (a ward that is not yours), failed or left for another round, and
  `cs_steward status` shows it.
- **She says it where you can see it**, on screen through vanilla's own message
  display as well as in the log, each subject at most once every five minutes.
  `[Steward] SpeakOnScreen` turns it off.
- **Her look.** At rest, with no worker body in the world, she stands beside
  the supply chest as a player-style figure: female, Matronly hair, fair, light
  blonde, in a leather tunic and leather pants. It is the game's own character
  model, extracted and dressed by Concerned NPC (never a live player stripped
  afterwards), seen only on the host's machine, and it changes nothing in the
  world; the figure and her worker body never exist at the same time. While she
  works, her worker body wears the leather and, locally, her own Matronly hair.
  A dress or robe can be named in the config and is tried first; nothing from
  another mod is included or depended on.
- **`cs_steward retire`** (owner decision S1). Takes her worker body out of the
  world, and only when she holds nothing, no round or plan is open and nothing
  she took is unaccounted for; it never
  removes anything else and never destroys carried fuel. `cs_steward recruit`
  brings her back.
- **Console:** `cs_steward area <radius> | depot | clear area|depot | runes |
  recruit | dismiss | resolve | retire | status`, and `cs_fires` (every light she
  looked at, and her verdict on each).
- **Settings:** `[Steward] StewardRuntimeEnabled`, `TendFiresEnabled`,
  `BaseCreature`, `QuestPickupItem`, `SpeakOnScreen`; `[Upkeep]
  RefuelBelowSeconds`, `RefuelToSeconds`, `MostUnitsPerLight`, `CarryPerTrip`;
  `[Appearance] ModelIndex`, `Garment`, `GarmentLegs`, `HairColour`, `HairStyle`,
  `ShowAtHome`, `WorkerLook`; `[Diagnostics] DebugLogging`.

### Notes

- **Off by default.** `[Steward] StewardRuntimeEnabled` and, inside it,
  `[Steward] TendFiresEnabled`. With both off the mod changes nothing.
- **Single player only for now:** host, not a dedicated server, nobody else
  connected. Every other case refuses and explains.
- **One fuel kind per trip:** a settlement with wood and resin lights makes one
  trip for each kind.
- **Her worker body is a Dverger.** Vanilla draws the female model, skin and hair
  colour only on a player-style body, so while she works only her clothes and her
  hair piece show; at rest the player-style figure shows all of it. `[Appearance]
  ModelIndex` and `HairColour` apply to the worker body only, so on the default
  Dverger neither has a visible effect; the figure uses its own fair, light-blonde
  colouring.
- **Nothing here has been observed in a running game yet**; the owner's
  pre-release smoke test is pending.
