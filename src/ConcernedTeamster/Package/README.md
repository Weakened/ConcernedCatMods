# Concerned Teamster

**In development by The Concerned Cat. Not yet released — this package exists for internal validation only.**

Your cart stops being a mystery: Concerned Teamster measures load, grade, traction, and risk so hauling decisions are informed — while vanilla cart physics stay untouched by default.

## Why

Valheim shows no cart mass, no cargo weight total, no hint whether a loaded cart can climb the hill ahead, and no warning before a descent turns into a runaway. Most cart mods answer that pain by deleting it — weightless or physics-free carts. Concerned Teamster keeps the logistics gameplay and explains it instead.

## What it does today (v0.5 — Optional Cartographer Integration)

* **Cart Status panel.** A visible **Cart** button (right screen edge, in-world) shows total mass with its base + cargo breakdown, live terrain grade with climbing/descending state, ground surface, attachment/pull state, and data freshness. Stale or unavailable values say so — never wrong numbers.
* **Cargo manifest and load planning (v0.2).** A sortable, filterable manifest of the cart's cargo using the game's own quality-scaled weights, a calibrated safe-load model that answers "uncalibrated" instead of faking precision, and live load/grade warnings with actionable non-color text.
* **Descent safety and recovery (v0.3).** A calibrated descent-risk model with bounded lookahead, an explicit reversible parking brake (never written to saves — a reloaded world is always brake-free), stuck-cause diagnostics, and numbered vanilla-legal recovery steps — advice, never teleports or cheats.
* **Trip recording and road quality (v0.4).** Pulled-cart trips are recorded to Teamster's own per-world sidecar (bounded, capped, atomic writes; world saves untouched); recorded trips score the roads in 8 m segments — roughness, grade, drag proxy — and a Trips panel lists, compares, and analyzes them, locating worst-grade points, roughest segments, and hypothetical-load bottlenecks on your real routes.
* **Optional Cartographer integration (v0.5).** If [Concerned Cartographer](https://thunderstore.io/c/valheim/) is installed (0.10.0+), a **Routes** button lists its drawn routes; pick one and Teamster terrain-profiles it in bounded chunks — distance, surfaces, grade histogram, worst sections, and the safe-load bottleneck for your cart's current mass — then renders a numbered problem report with load advice straight from the calibration model. Unloaded terrain is reported as UNSAMPLED, never guessed. Strictly read-only toward Cartographer (its atlas is never touched), no hard dependency in either direction, and without Cartographer the feature simply does not exist.
* Read-only, bounded telemetry with hard performance caps; everything game-facing is verified at startup and fails closed with one actionable log line if a game update changes cart internals.

## What comes next (roadmap)
* **Multiplayer trust and authority (v0.6):** ownership policy, dedicated-server validation, cooperative diagnostics.
* Later: accessibility and localization, and compatibility hardening.

## Principles

* **Vanilla truth first.** Measures and explains the game's real behavior; never silently changes it.
* **No cheats by default.** No zero-weight carts, no teleports, no stamina bypass, no autopilot.
* **World-safe.** Reads game state; writes only its own sidecar files. World saves are never modified.
* **Fail closed.** If a game internal changes, the dependent feature disables itself with one actionable log line.
* **Uninstall-safe.** Removing the mod leaves worlds, characters, and carts exactly as vanilla made them.

## Status

Version 0.5.x is the internal **Optional Cartographer Integration** line: cart truth, cargo and load planning, descent safety, trip-based road scoring, and capability-detected route profiling, with vanilla physics untouched by default. Features land issue by issue on the [GitHub tracker](https://github.com/Weakened/ConcernedCatMods/issues). The first public release will be the v0.9 beta after the full hardening pass.

## Support

* Bugs and feature requests: the [GitHub issue tracker](https://github.com/Weakened/ConcernedCatMods/issues).
* Anything that should not be public: **support@theconcernedcat.com**.
