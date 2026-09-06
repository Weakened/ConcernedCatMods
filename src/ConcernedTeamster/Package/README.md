# Concerned Teamster

**In development by The Concerned Cat. Not yet released — this package exists for internal validation only.**

Your cart stops being a mystery: Concerned Teamster measures load, grade, traction, and risk so hauling decisions are informed — while vanilla cart physics stay untouched by default.

## Why

Valheim shows no cart mass, no cargo weight total, no hint whether a loaded cart can climb the hill ahead, and no warning before a descent turns into a runaway. Most cart mods answer that pain by deleting it — weightless or physics-free carts. Concerned Teamster keeps the logistics gameplay and explains it instead.

## What it does today (v0.7 — UX, Controller, Accessibility, Localization)

* **Cart Status panel.** A visible **Cart** button (right screen edge, in-world) shows total mass with its base + cargo breakdown, live terrain grade with climbing/descending state, ground surface, attachment/pull state, and data freshness. Stale or unavailable values say so — never wrong numbers.
* **Cargo manifest and load planning (v0.2).** A sortable, filterable manifest of the cart's cargo using the game's own quality-scaled weights, a calibrated safe-load model that answers "uncalibrated" instead of faking precision, and live load/grade warnings with actionable non-color text.
* **Descent safety and recovery (v0.3).** A calibrated descent-risk model with bounded lookahead, an explicit reversible parking brake (never written to saves — a reloaded world is always brake-free), stuck-cause diagnostics, and numbered vanilla-legal recovery steps — advice, never teleports or cheats.
* **Trip recording and road quality (v0.4).** Pulled-cart trips are recorded to Teamster's own per-world sidecar (bounded, capped, atomic writes; world saves untouched); recorded trips score the roads in 8 m segments — roughness, grade, drag proxy — and a Trips panel lists, compares, and analyzes them, locating worst-grade points, roughest segments, and hypothetical-load bottlenecks on your real routes.
* **Optional Cartographer integration (v0.5).** If [Concerned Cartographer](https://thunderstore.io/c/valheim/) is installed (0.10.0+), a **Routes** button lists its drawn routes; pick one and Teamster terrain-profiles it in bounded chunks — distance, surfaces, grade histogram, worst sections, and the safe-load bottleneck for your cart's current mass — then renders a numbered problem report with load advice straight from the calibration model. Unloaded terrain is reported as UNSAMPLED, never guessed. Strictly read-only toward Cartographer (its atlas is never touched), no hard dependency in either direction, and without Cartographer the feature simply does not exist.
* **Multiplayer trust and authority (v0.6).** A written, enforced policy decides who may read, act, and observe each feature: the parking brake — the only feature that changes a cart — works only when you own the cart under vanilla rules, and any doubt fails closed. Crews get cooperative diagnostics (who's helping, hindering, or idle, and why the cart still won't move) with zero added force, owner-fresh readings are labeled when you're only observing, and every network-derived value is bounded as hostile input. Teamster stays client-side: it sends nothing, takes no ownership, and an unmodded peer sees pure vanilla.
* Read-only, bounded telemetry with hard performance caps; everything game-facing is verified at startup and fails closed with one actionable log line if a game update changes cart internals.
* **UX, controller, accessibility, and localization (v0.7).** Every panel scales (0.8–1.3×), meets a WCAG AA contrast target, and never relies on color alone — every warning, diagnosis, and comparison already carries distinguishing text or a symbol. A gamepad focus order and accelerator-conflict checker keep every feature reachable by button first. All 254 user-facing strings resolve through a translator-friendly catalog with English fallback — see the [translator guide](https://github.com/Weakened/ConcernedCatMods/blob/main/docs/mods/concerned-teamster/LOCALIZATION.md) to contribute a language. New players get a short, dismissable pointer to the Cart button, and three documented settings presets (Minimal / Standard / EverythingObservational) cover common preferences without ever auto-enabling the parking brake.

## Compatibility with other mods (v0.8, in progress)

Teamster now detects a small, researched registry of known mods (by GUID,
never guessed) and applies a documented policy: coexist quietly, adapt a
reading, or warn. Three mods are registered so far, chosen from a
Thunderstore-wide research pass by download count and directness of effect
on cart mass or cargo weight. **BetterCarts** (TastyChickenLegs) and
**ItemStacks** (mtnewton, 306,000+ downloads — reduces every item's weight
by 90% by default) both alter cart mass out of the box, verified directly
from their published source, so every load-advice surface (warnings, stuck
diagnosis, recovery guidance, route bottlenecks) shows a plain "load advice
unavailable" notice instead of a vanilla-calibrated number presented as
truth while either is detected. **ValheimPlus** ships an optional Wagon
section that *can* change cart mass, but ships it off — disabled, its
patch reproduces vanilla's mass math exactly — so it's flagged with a
caution note pointing at that setting rather than suppressing advice for
every install. A **Compat** button on the Cart Status panel always shows
what was detected. This registry grows as more mods are researched; an
unrecognized mod is always silent — nothing to configure, no false alarms.

## Recovery, migration, and support bundle (v0.8)

Trip sidecar backups now rotate a bounded set of copies per event instead
of overwriting the same file every time, so a second corruption or
migration event never destroys the evidence of the first. A new
**Support** button on the Cart Status panel exports a single sanitized
diagnostic file — versions, config, compatibility status, sidecar
summaries, this session's recovery events, and recent Teamster-only log
lines — for sharing when reporting a problem. No world names, player
names, or full file paths are included; nothing is sent anywhere by the
mod itself.

## What comes next (roadmap)
* Further compatibility research and recovery hardening (v0.8), then the public beta (v0.9).

## Principles

* **Vanilla truth first.** Measures and explains the game's real behavior; never silently changes it.
* **No cheats by default.** No zero-weight carts, no teleports, no stamina bypass, no autopilot.
* **World-safe.** Reads game state; writes only its own sidecar files. World saves are never modified.
* **Fail closed.** If a game internal changes, the dependent feature disables itself with one actionable log line.
* **Uninstall-safe.** Removing the mod leaves worlds, characters, and carts exactly as vanilla made them.

## Status

Version 0.7.x is the internal **UX, Controller, Accessibility, Localization** line: cart truth, cargo and load planning, descent safety, trip-based road scoring, capability-detected route profiling, an enforced multiplayer read/act/observe policy, and now scalable/contrast-checked/controller-navigable panels with full localization and onboarding, with vanilla physics untouched by default. Features land issue by issue on the [GitHub tracker](https://github.com/Weakened/ConcernedCatMods/issues). The first public release will be the v0.9 beta after the full hardening pass.

## Support

* Bugs and feature requests: the [GitHub issue tracker](https://github.com/Weakened/ConcernedCatMods/issues).
* Anything that should not be public: **support@theconcernedcat.com**.
