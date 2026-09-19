# Changelog

All notable changes to Concerned Steward are recorded here. Versions follow
`Major.Minor.Patch`, and this product versions and releases independently of
every other Concerned Cat mod.

## Unreleased

Sunniva (CNPC-R3 / #382). Not released, not tagged, and the version has
deliberately not moved: `0.1.0` is still unpublished, so there is nothing for a
bump to protect.

### Added

- **She has a name.** The Steward is Sunniva. Only the display name moved — the
  identity `steward/steward`, the prefab name `CS_Steward` and the key prefix
  `tcc.steward.` are all exactly what they were, so every saved body and every
  record written by the previous build is found by the same values. That was the
  point of keeping the name out of the identity in the first place.
- **An introduction with an object in it.** The first time an eligible player
  picks up resin, they also find a flint and steel with runes carved into them,
  and there is one of it for the life of that world. It is a discovery the mod
  remembers and never an item put into an inventory: nothing is minted.
- **A look.** A female model, light hair, and a leather tunic and leather pants,
  applied to her own body through vanilla's own `VisEquipment`. A dress or robe
  can be named in the config and is tried first; nothing from another mod is
  included or depended on.
- **A planned maintenance round.** Every fuel-burning light in the settlement is
  surveyed once, the whole round's requirement is totalled once, and the supply
  is drawn once — so five low torches are one trip to the chest rather than five.
  Urgency is measured in how long a light has left at its own burn rate rather
  than in its fuel level, and lights are topped up to a configurable amount of
  burning time rather than to full.
- **No free fuel.** A round she cannot pay for is refused before a step, naming
  what is missing. A round she can half pay for services the most urgent lights
  it can pay for in full and counts the rest, so she never reports a settlement
  looked after when it is not. Surplus goes back to a container approved for
  deposits, and never to any other.

### Changed

- Concerned Steward now requires **Concerned NPC**, which ships as its own
  package. The Steward's identity and the one-body-per-identity rule come from
  it; without it the mod will say so at load rather than fail later.

### Unchanged on purpose

- No durable key, row tag, schema number, prefab name or file name moved. The
  record gains one new row tag under the same format version `1`, so a record
  written by the previous build loads unchanged and simply has no quest row.

## 0.1.0

The first slice (CS-001 / #340). Not released.

### Added

- **The Steward.** A persistent settlement worker with a stable identity, a
  real body built from an inactive prefab clone, and his own saved inventory in
  his own network object. He is recruited once and remembered across reloads.
- **Explicit scope.** He acts only inside a settlement area you marked and
  takes only from a supply depot you marked. There is no nearest-chest search,
  no radius that follows the player, and no fallback when a marked thing has
  gone stale — a stale designation refuses and says to mark it again.
- **Fire tending.** One bounded, deterministic upkeep loop: choose the hearth
  closest to going out, reserve it, walk to the depot, withdraw real wood,
  walk to the hearth, revalidate it, add fuel one unit at a time through
  vanilla's own `Fireplace.UseItem`, measure what each unit was actually
  worth, and carry the remainder back.
- **Conservation.** Every unit of wood he withdraws is in exactly one place at
  every moment — the depot, his hands, a fire, or explicitly recorded as
  unaccounted. Intent is written down before anything moves and the receipt
  records the measured result, never the intended one.
- **Recovery.** After a crash or a reload, an unfinished withdrawal or feed is
  reconciled against what he is actually carrying. Nothing is replayed and
  nothing is compensated; an uncertain step is reported for a person to
  resolve.
- **`cs_steward`** console command: `area`, `depot`, `recruit`, `dismiss`,
  `tend on|off`, `status`, `forget`.

### Notes

- The runtime is **off by default** (`[Steward] StewardRuntimeEnabled`) and
  fire tending is off inside it (`[Steward] TendFiresEnabled`).
- Single player only for now: host, not dedicated, nobody else connected. Every
  other case refuses and explains.
- His appearance uses vanilla assets and is provisional.
- He has no proper name yet, by design.
