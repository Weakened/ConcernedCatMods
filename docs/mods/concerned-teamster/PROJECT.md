# Project: Concerned Teamster

## Product identity

```text
Creator:          The Concerned Cat
Mod:              Concerned Teamster
Thunderstore ID:  TheConcernedCat-ConcernedTeamster
Plugin GUID:      com.theconcernedcat.valheim.concernedteamster
Assembly:         TheConcernedCat.ConcernedTeamster
Issue key:        CT
Git tags:         concerned-teamster/vX.Y.Z
Initial version:  0.1.0 (internal until the v0.9 public beta)
```

Concerned Teamster is an independent product inside the `Weakened/ConcernedCatMods`
monorepo. It shares tooling and conventions with Concerned Cartographer but has its
own project, DLL, plugin GUID, configuration, data files, package, changelog, and
release lifecycle. It must never take a compile-time dependency on Concerned
Cartographer; any integration is capability-detected at runtime and optional.

## One-sentence promise

**Your cart stops being a mystery: Concerned Teamster measures load, grade, traction, and risk so hauling decisions are informed — while vanilla cart physics stay untouched by default.**

## Problem

Carts are one of Valheim's least understandable systems. The game shows no cart
mass, no cargo weight total, no indication of whether a loaded cart can climb the
hill ahead, and no warning before a descent turns into a runaway. Players discover
the limits by losing ore down a mountainside, snapping a cart on a root, or getting
silently stuck on an invisible lip. Existing cart mods mostly answer this pain by
deleting it — making carts weightless or physics-free — which discards the
logistics gameplay instead of explaining it.

## Product stance

Concerned Teamster makes carts **understandable, predictable, and safer** while
**preserving vanilla cart mass and physics by default**. Default behavior is
observational and advisory: measure, explain, and warn. Any convenience that
mutates behavior (for example the parking brake) must be explicit, reversible,
fail-closed, and separately authorized by its own issue.

## Market/overlap research

This project must not pretend the cart-mod space is empty:

- **Better Carts** and similar cart mods change cart physics, weight handling, or
  pulling behavior directly. Teamster's differentiator is measurement and advice on
  top of vanilla behavior, not physics replacement. Coexistence and precedence with
  Better Carts is an explicit deliverable (CT-037).
- The exact set of currently maintained cart/physics/inventory mods changes over
  time. CT-038 researches the real, current targets before compatibility work;
  inventing mod names in documentation or code is prohibited.
- Concerned Cartographer already models roads, routes, and per-world sidecar
  persistence. Teamster may later consume its route data through an optional,
  capability-detected adapter (v0.5) — never a hard reference.

## Target users

- Haulers moving ore, stone, and wood between mines, ports, and bases.
- Builders planning cart-friendly roads and judging which grades are safe.
- Cooperative crews sharing carts and wanting predictable, explainable behavior.
- Vanilla-plus players who refuse cheat-style cart mods but want the game to stop
  hiding the numbers.

## Product principles

1. **Vanilla truth first.** Measure and explain the game's real behavior; never
   silently change it.
2. **World-safe.** Read game state; write only Teamster's own sidecar files. Never
   mutate world saves, terrain, or inventories.
3. **Fail closed.** If a Valheim internal is missing or changed, the dependent
   feature disables itself with one actionable log line; nothing guesses.
4. **Client-side first.** Multiplayer features arrive only with explicit trust and
   authority design (v0.6) and must coexist with unmodded peers.
5. **Buttons first.** Every feature is discoverable through visible buttons and
   panels; keyboard shortcuts are accelerators only, never the only path.
6. **No cheats by default.** No zero-weight defaults, no teleporting carts, no
   recovery cheats, no stamina bypass, no pathfinding autopilot, no
   server-authority takeover.
7. **Honest evidence.** A claim is PASS only when automation or recorded in-game
   evidence proves it; manual-only claims stay pending and enter the final smoke
   checklist.

## MVP user stories (v0.1 Cart Truth)

- As a hauler, I can open a Cart Status panel and see my cart's total mass, cargo
  weight, and attachment state.
- As a hauler, I can see the grade of the terrain under my cart and whether I am
  climbing, descending, or level.
- As a hauler, I can see at a glance whether the cart is currently being pulled and
  by whom (locally).
- As a player, the panel appears through a visible button, updates smoothly, and
  never spams logs or stutters the game.
- As a player, disabling the mod changes nothing about my world or my carts.

## Later capabilities (ordered roadmap)

| Version | Theme | Leaves |
|---|---|---|
| v0.1 | Cart Truth — telemetry, grade math, Cart Status panel | CT-001..CT-005 |
| v0.2 | Cargo and Load Planning — manifest, safe-load model, warnings | CT-006..CT-010 |
| v0.3 | Descent Safety and Recovery Guidance — risk model, parking brake, diagnostics | CT-011..CT-015 |
| v0.4 | Road Quality and Trip Profiles — trip sampling, road scoring, history | CT-016..CT-020 |
| v0.5 | Optional Cartographer Integration — capability adapter, route profiling | CT-021..CT-025 |
| v0.6 | Multiplayer Trust and Authority — ownership policy, dedicated validation | CT-026..CT-030 |
| v0.7 | UX, Controller, Accessibility, Localization | CT-031..CT-035 |
| v0.8 | Compatibility, Recovery, Scale | CT-036..CT-040 |
| v0.9 | Public Beta Hardening | CT-041..CT-045 |
| v1.0 | Stable Teamster | CT-046..CT-050 |

Each sprint ends in an internal validation/package release candidate. Only the
v0.9 public beta and the final v1.0 release involve the human owner, and
Thunderstore publication is always owner-only.

## Non-goals

- No zero-weight or reduced-weight defaults.
- No cart teleportation or remote summoning.
- No "unstuck" cheats that move the cart through geometry.
- No stamina bypass while pulling.
- No pathfinding or autopilot.
- No world-save mutation of any kind.
- No server-authority takeover; dedicated servers are validated, not replaced.
- No compile-time dependency on Concerned Cartographer or any other mod.

## Success criteria

- A player can answer "can my cart make it up this hill?" before losing the cargo.
- Panels explain every number they show; warnings state what to do, not just red text.
- Removing the mod leaves worlds, characters, and carts exactly as vanilla made them.
- The mod ships v1.0 with the same evidence discipline Concerned Cartographer used:
  every automated claim reproducible, every manual claim honestly listed in the
  owner smoke checklist.
