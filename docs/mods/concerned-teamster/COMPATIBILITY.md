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

## In-game surface

The Cart Status panel's **Compat** button (always visible, unlike the
Cartographer-conditional Routes button) opens a read-only panel listing
every actually-detected known mod and its policy. With today's empty
registry it always shows "No known compatibility concerns detected." —
the honest, correct answer until real entries exist. The startup log
prints the same line (or one line per detected mod) once per session.
