# Concerned Teamster

**In development by The Concerned Cat. Not yet released — this package exists for internal validation only.**

Your cart stops being a mystery: Concerned Teamster measures load, grade, traction, and risk so hauling decisions are informed — while vanilla cart physics stay untouched by default.

## Why

Valheim shows no cart mass, no cargo weight total, no hint whether a loaded cart can climb the hill ahead, and no warning before a descent turns into a runaway. Most cart mods answer that pain by deleting it — weightless or physics-free carts. Concerned Teamster keeps the logistics gameplay and explains it instead.

## What it does today (v0.1 — Cart Truth)

* A visible **Cart** button (right screen edge, in-world) opens the **Cart Status panel**: total mass with its base + cargo breakdown, live terrain grade with climbing/descending state, ground surface, attachment/pull state, and data freshness. Stale or unavailable values say so — never wrong numbers.
* Read-only, bounded telemetry with hard performance caps; everything game-facing is verified at startup and fails closed with one actionable log line if a game update changes cart internals.

## What comes next (roadmap)
* **Cargo and Load Planning (v0.2):** a sortable cargo manifest, safe-load estimates, and overload warnings.
* **Descent Safety (v0.3):** runaway-risk warnings and recovery guidance — advice, never teleports or cheats.
* **Road Quality and Trip Profiles (v0.4):** trip recording and road scoring for your hauling routes.
* Later: optional Concerned Cartographer route integration, multiplayer trust, accessibility, and compatibility hardening.

## Principles

* **Vanilla truth first.** Measures and explains the game's real behavior; never silently changes it.
* **No cheats by default.** No zero-weight carts, no teleports, no stamina bypass, no autopilot.
* **World-safe.** Reads game state; writes only its own sidecar files. World saves are never modified.
* **Fail closed.** If a game internal changes, the dependent feature disables itself with one actionable log line.
* **Uninstall-safe.** Removing the mod leaves worlds, characters, and carts exactly as vanilla made them.

## Status

Version 0.1.x is the internal **Cart Truth** line: verified cart telemetry and the Cart Status panel, with vanilla physics untouched. Features land issue by issue on the [GitHub tracker](https://github.com/Weakened/ConcernedCatMods/issues). The first public release will be the v0.9 beta after the full hardening pass.

## Support

* Bugs and feature requests: the [GitHub issue tracker](https://github.com/Weakened/ConcernedCatMods/issues).
* Anything that should not be public: **support@theconcernedcat.com**.
