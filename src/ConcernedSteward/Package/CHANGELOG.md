# Changelog

All notable changes to Concerned Steward are recorded here. Versions follow
`Major.Minor.Patch`, and this product versions and releases independently of
every other Concerned Cat mod.

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
