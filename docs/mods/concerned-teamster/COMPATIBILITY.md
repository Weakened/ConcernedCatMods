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
registered GUID and fails if one appears. Live since CT-037 added the first
real entry: a future feature that wants to know "is a specific policy
active" must query the evaluated `ModDetectionResult`s (or a small helper
reading them), never hardcode a GUID or mod name of its own.

## The shipped registry

`Domain/Compatibility/CompatibilityKnownMods.Registry` carries one entry as
of CT-037 (`TastyChickenLegs.BetterCarts` — see the research trail below).
Naming a specific mod's GUID and policy requires first verifying its
actual, current, shipped metadata — inventing one would violate this
repository's "research uncertain … APIs instead of inventing them" rule.
CT-038 (the broader current-mod research pass) grows this list further. The
framework itself is fully proven off-game against fake registries in
`CompatibilityFrameworkTests` (detection, each policy outcome, silence for
unregistered/not-found mods, and the shared-composition guarantee).

## Precedence policy (CT-037)

`Domain/Compatibility/CompatibilityAffectedAspect` names which Teamster
reading a mod's presence calls into question — today just `None` and
`CartMassOrPhysics` (every LoadModel- or RiskModel-derived verdict:
warnings, stuck diagnostics, recovery guidance, route bottlenecks, and
descent risk — all calibrated against vanilla physics).
`Domain/Compatibility/CompatibilityAdvisoryGate.CartMassAdviceReliable`
answers "is that calibration still trustworthy right now" from the
evaluated registry results — generic over the aspect, never a specific
mod's GUID or name, so a future registry entry tagged `CartMassOrPhysics`
gates every consumer automatically with no feature code to touch.

The gate is wired into every consumer that renders a LoadModel- or
RiskModel-derived value to the player, or (for the one consumer with no
player-facing surface today) to a diagnostic log:

| Consumer | Where the gate is checked | What happens when unreliable |
|---|---|---|
| Cart warnings | `Adapters/CartTelemetryPump.TryGetWarning` | Returns a fixed "load advice unavailable" `CartWarning` instead of consulting the tracker. |
| Stuck diagnosis | `Domain/Diagnostics/StuckDetector.Classify` (flag threaded in from `CartTelemetryPump.Update`) | Returns `CartDiagnosis.LoadAdviceUnavailable` instead of ever calling `LoadModel.Query`. |
| Recovery guidance | `Domain/Ui/RecoveryGuidancePresenter.Present` | A dedicated `LoadAdviceUnavailable` switch arm — inherited automatically from the corrected diagnosis above — never calls `AddUnloadStep`'s `RecommendedMaxMass` query. |
| Route profile bottleneck line | `Domain/Routes/RouteLoadBottleneck.Evaluate` (checked at both `Ui/RoutePickerPanel` call sites) | `ProvenMaxMass`/`Verdict` are forced null; `Domain/Ui/RouteProfilePresenter.BottleneckLine` shows the shared unavailable line instead. Terrain-only fields (`HasGradeData`, `BottleneckGradePercent`) are untouched — a route's grade is not a mass fact. |
| Route report (overall + per-section advice) | `Domain/Ui/RouteReportPresenter.Present`/`SectionAdvice` | Overall block shows the shared unavailable line once; per-section advice is suppressed (matching this presenter's existing "sections without a model answer get facts, not advice" rule) rather than repeating the notice after every steep section. |
| Trip-history load binding | `Domain/Ui/RouteBottleneckPresenter.Present`/`DescribeLoadBinding` (checked at `Ui/TripHistoryPanel`) | Shows the shared unavailable line instead of walking the trip's climb points. |
| Descent-risk debug log | `Adapters/CartTelemetryPump.Update`'s debug summary | Appends a flag noting the mod detection instead of printing the risk level as unqualified fact. No player-facing panel renders `DescentRiskInfo.Current`/`.Lookahead` today (only `.CartId`, for correlation), so this opt-in developer log is the only place this model's output is currently materialized as text. |

Every consumer reads the same source of truth,
`Adapters/CompatibilityAdapter.CartMassAdviceReliable` (a small wrapper over
the gate that fails open — `true` — before the first probe runs), so there
is one decision, not several independently-drifting ones.

**The parking brake needs no gate.** `Domain/Brake/BrakeLifecycle`'s
`EvaluateToggle`/`EvaluateTick` and `Domain/Brake/BrakeFacts` (read by
`Adapters/CartBrakeAdapter.ReadFactsCore`) branch only on capability,
world/cart existence, ownership authority, attach state, and a fixed
distance constant — `BrakeFacts` has no mass field, and nothing in the
brake stack calls `LoadModel` or `RiskModel` (verified by reading every
file in `Domain/Brake/` and `Adapters/CartBrakeAdapter.cs`). "Brake policy
under altered physics is explicit and fail-closed" is satisfied
structurally: there is no vanilla-mass assumption for altered physics to
invalidate in the first place.

## Research: identifying "Better Carts" (CT-037)

`PROJECT.md`'s market research names "Better Carts" generically ("Better
Carts and similar cart mods change cart physics, weight handling, or
pulling behavior directly") without pinning an exact Thunderstore package —
CT-038 is the leaf that researches the full current mod landscape.
Identifying which real, currently-published mod this leaf's specific
acceptance criteria refer to required its own research pass:

| Candidate | Author | Downloads | What it actually does | GUID | Registered? |
|---|---|---|---|---|---|
| BetterCarts | TastyChickenLegs | 46,000 | Quick attach/detach, up to 4-player push assist, damage removal, network sync. **Also reduces cart mass by a default 20%** — see below. | `TastyChickenLegs.BetterCarts` (verified: `Plugin.cs`'s `ModGUID` constant) | **Yes** — `Adapt`, `AffectedAspect.CartMassOrPhysics` |
| Better Cart | We_Haul | 2,200 | "Allows for customization of minimum and maximum mass of Carts so that loading a cart doesn't make it impossible to move" — a direct, conceptually strong match for "changes cart physics, weight handling." | **Could not be verified.** No linked GitHub/source repository; Thunderstore's decompiled-source viewer for this package did not yield readable source through available tooling. | **No** |

**BetterCarts does alter cart mass by default — a corrected finding.** The
first research pass read only `Plugin.cs` and the README and concluded no
physics impact; an independent review (re-verified directly against the
live source) found that conclusion was wrong, because the mod's `Patches/`
folder was not inspected. `Patches/CartPatches.cs` installs a Harmony
`Prefix` on `Vagon.SetMass`:

```csharp
[HarmonyPatch(typeof(Vagon), "SetMass")]
private static class SetMass_Patch
{
    private static void Prefix(Vagon __instance, ZNetView ___m_nview, ref float mass)
    {
        if (!BetterCartsMain.modEnabled.Value || !___m_nview.IsOwner()) return;
        if (CartConfigsMain.allowPlayerHelp.Value) { /* per-helper reduction */ }
        else { mass = Mathf.Max(0, mass - mass * CartConfigsMain.cartMassReduction.Value); }
    }
}
```

`Patches/CartConfigs.cs` defaults `cartMassReduction` to `0.2f` ("For
Single Player - fractional weight reduction for the cart") and
`allowPlayerHelp` to `false` — so the solo-mode **20% mass reduction is the
out-of-the-box default**, not an opt-in the player must discover. This is
exactly the scenario the precedence policy exists for: BetterCarts is
registered `Adapt`/`CartMassOrPhysics`, not `Coexist`/`None`, and every
consumer in the table above substitutes its unavailable notice while this
mod is detected. The GUID itself was correctly verified either time; only
the behavioral classification was wrong the first pass.

**Why "Better Cart" (We_Haul) is still not registered:** this repository's
operating rule is to research real mod metadata rather than invent it. A
BepInEx plugin GUID lives inside the compiled DLL, not in Thunderstore's
page metadata or manifest.json, and is not required to follow any naming
convention — guessing one (for example by pattern-matching the
author/package name) risks registering a GUID that either never matches
the real mod (a silently-dead registry entry) or, worse, coincidentally
matches something else. Verifying it would require downloading and
inspecting the mod's compiled binary, which this leaf treats as out of
scope for an autonomous research pass — installing or inspecting
third-party executable content warrants the owner's awareness first. This
is recorded as a pending, non-blocking item (see `HUMAN_ATTENTION.md`)
rather than guessed.

## Known scope limits

- No in-game coexistence matrix has been run yet (dev machine, `TCT-Compat`
  profile, per `TEST_PLAN.md`); the matrix acceptance criterion is
  satisfied structurally today (the gate and every consumer above are
  exhaustively unit-tested against both fake and the real registered
  mass-altering probe) with the real-mod, real-game observation pending —
  never claimed PASS. Tracked in `HUMAN_ATTENTION.md`.
- The We_Haul "Better Cart" GUID remains unverified (see research above);
  CT-038's broader pass may resolve this.

## In-game surface

The Cart Status panel's **Compat** button (always visible, unlike the
Cartographer-conditional Routes button) opens a read-only panel listing
every actually-detected known mod and its policy. Until BetterCarts (or
another registered mod) is actually installed alongside Teamster, it shows
"No known compatibility concerns detected." — the honest, correct answer
when detection finds nothing, regardless of what the registry contains.
The startup log prints the same line (or one line per detected mod) once
per session.
