# Compatibility framework (CT-036)

How Teamster detects other known mods and applies a documented policy,
without ever branching on a specific mod inside feature code.

## Design

`Domain/Compatibility/KnownModProbe` is one registry entry: a mod's BepInEx
plugin GUID, a display name, a `CompatibilityPolicy`, and a description
shown to the player. Three policies:

| Policy | Meaning |
|---|---|
| `Coexist` | No Teamster behavior changes; noted for transparency only. |
| `Adapt` | A specific, documented Teamster behavior changes because this mod is present. |
| `Warn` | No behavior changes, but the player is told about a known limitation so a surprising reading isn't mistaken for a bug. |

`Domain/Compatibility/CompatibilityRegistry.Evaluate` is the whole
mechanism: given a list of probes and a `Func<string, (bool, string?)>`
lookup, it queries the lookup **once per registered probe and nothing
else** — a mod that isn't in the registry is never asked about, which is
what makes "unknown mods produce no warnings" true by construction rather
than a filter that could be implemented wrong. A lookup that throws (a
malformed third-party plugin's metadata, say) fails closed to "not found"
for that one probe only — the rest of the registry still gets evaluated,
unlike an all-or-nothing try/catch that would go dark for every mod over
one bad entry. `Adapters/CompatibilityAdapter`
supplies the real lookup (`Chainloader.PluginInfos`, mirroring
`CartographerCapability`'s CT-021 probe shape) and runs once on the first
Update tick, for the same reason CT-021 waits: BepInEx fills `PluginInfos`
in load order, so probing from `Awake` could misread a not-yet-loaded mod
as absent.

`Domain/Compatibility/CompatibilityStatusPresenter` composes the status
text. Both the startup log banner and the in-game Compat panel call the
*exact same* `ComposeDetectedLines`/`ComposeNoneDetectedLine` methods, so
the panel can never say something different from what was logged — this is
what "the status surface reflects exactly the applied policies" means in
practice: one composition, two displays, not two compositions that could
drift apart.

## Enforcing "no mod-specific branches in feature code"

`CompatibilityFrameworkTests.ShippedRegistry_GuidsNeverAppearOutsideTheCompatibilityDomain`
scans every shipped `.cs` file outside `Domain/Compatibility/` for each
registered GUID and fails if one appears. Currently vacuous (the registry
ships empty — see below), it becomes a real, automatically-enforced
regression guard the moment CT-037/038 add real entries: a future feature
that wants to know "is a specific policy active" must query the evaluated
`ModDetectionResult`s (or a small helper reading them), never hardcode a
GUID or mod name of its own.

## The registry ships empty

`Domain/Compatibility/CompatibilityKnownMods.Registry` is
`Array.Empty<KnownModProbe>()`. Naming a specific mod's GUID and policy
requires first verifying its actual, current, shipped metadata — inventing
one would violate this repository's "research uncertain … APIs instead of
inventing them" rule. CT-037 (Better Carts coexistence and precedence) and
CT-038 (the broader current-mod research pass) populate this list after
that research. The framework itself is fully proven off-game against fake
registries in `CompatibilityFrameworkTests` (detection, each policy
outcome, silence for unregistered/not-found mods, and the shared-composition
guarantee).

## Precedence policy (CT-037)

`Domain/Compatibility/CompatibilityAffectedAspect` names which Teamster
reading a mod's presence calls into question — today just `None` and
`CartMassOrPhysics` (every LoadModel-derived verdict: warnings, stuck
diagnostics, recovery guidance, route bottlenecks — all calibrated against
vanilla physics). `Domain/Compatibility/CompatibilityAdvisoryGate
.CartMassAdviceReliable` answers "is that calibration still trustworthy
right now" from the evaluated registry results — generic over the aspect,
never a specific mod's GUID or name, so a future registry entry tagged
`CartMassOrPhysics` gates every consumer automatically with no feature code
to touch.

The gate is wired into exactly one choke point: `CartTelemetryPump
.TryGetWarning`, which every consumer (the Cart Status panel row and the
optional HUD hint) already calls through. When cart-mass advice is
unreliable, it returns a fixed notice ("A detected mod changes cart mass or
physics. Load advice is unavailable — see the Compat panel for details.")
instead of consulting the normal warning tracker — never a silently-wrong
vanilla-calibrated verdict, per this leaf's acceptance criteria. Diagnostics,
guidance, and route-bottleneck consumers are not yet wired to the same gate;
see "Known scope limits" below.

## Research: identifying "Better Carts" (CT-037)

`PROJECT.md`'s market research names "Better Carts" generically ("Better
Carts and similar cart mods change cart physics, weight handling, or
pulling behavior directly") without pinning an exact Thunderstore package —
CT-038 is the leaf that researches the full current mod landscape.
Identifying which real, currently-published mod this leaf's specific
acceptance criteria refer to required its own research pass:

| Candidate | Author | Downloads | What it actually does | GUID | Registered? |
|---|---|---|---|---|---|
| BetterCarts | TastyChickenLegs | 46,000 | Quick attach/detach, up to 4-player push assist, damage removal, network sync. **Does not touch cart mass, weight, or physics** (confirmed by reading `Plugin.cs` and the README directly from [github.com/TastyChickenLegs/BetterCarts](https://github.com/TastyChickenLegs/BetterCarts)). | `TastyChickenLegs.BetterCarts` (verified: `Plugin.cs`'s `ModGUID` constant) | **Yes** — `Coexist`, `AffectedAspect.None` |
| Better Cart | We_Haul | 2,200 | "Allows for customization of minimum and maximum mass of Carts so that loading a cart doesn't make it impossible to move" — a direct, conceptually strong match for "changes cart physics, weight handling." | **Could not be verified.** No linked GitHub/source repository; Thunderstore's decompiled-source viewer for this package did not yield readable source through available tooling. | **No** |

**Why "Better Cart" (We_Haul) is not registered despite being the better
conceptual fit:** this repository's operating rule is to research real mod
metadata rather than invent it. A BepInEx plugin GUID lives inside the
compiled DLL, not in Thunderstore's page metadata or manifest.json, and is
not required to follow any naming convention — guessing one (for example by
pattern-matching the author/package name) risks registering a GUID that
either never matches the real mod (a silently-dead registry entry) or,
worse, coincidentally matches something else. Verifying it would require
downloading and inspecting the mod's compiled binary, which this leaf
treats as out of scope for an autonomous research pass — installing or
inspecting third-party executable content warrants the owner's awareness
first. This is recorded as a pending, non-blocking item (see
`HUMAN_ATTENTION.md`) rather than guessed.

**BetterCarts (TastyChickenLegs) is registered anyway** because it is a
real, verified, currently-published cart mod a player could plausibly run
alongside Teamster, and the research confirms — rather than assumes — that
it coexists cleanly. Shipping one true, well-researched `Coexist` entry is
more honest than shipping zero entries or a guessed one.

## Known scope limits

- The precedence gate (`CompatibilityAdvisoryGate`) is wired into cart
  warnings only. Stuck diagnostics, recovery guidance, and route-bottleneck
  analysis also derive from `LoadModel` and should eventually consult the
  same gate; deferred rather than touching four more well-tested presenters
  in a leaf whose registry has no mass-altering entry to actually exercise
  the gate against yet. Tracked in `HUMAN_ATTENTION.md`.
- No in-game coexistence matrix has been run (no mass-altering mod is
  registered to test against); the matrix acceptance criterion is satisfied
  structurally (the gate is exhaustively unit-tested against fake
  mass-altering probes) with the real-mod observation pending.

## In-game surface

The Cart Status panel's **Compat** button (always visible, unlike the
Cartographer-conditional Routes button) opens a read-only panel listing
every actually-detected known mod and its policy. With today's empty
registry it always shows "No known compatibility concerns detected." —
the honest, correct answer until real entries exist. The startup log
prints the same line (or one line per detected mod) once per session.
