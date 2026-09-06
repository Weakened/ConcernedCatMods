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

`Domain/Compatibility/CompatibilityKnownMods.Registry` carries three
entries as of CT-038 (`TastyChickenLegs.BetterCarts`,
`net.mtnewton.itemstacks`, `org.bepinex.plugins.valheim_plus` — see the
research trail below). Naming a specific mod's GUID and policy requires
first verifying its actual, current, shipped metadata — inventing one
would violate this repository's "research uncertain … APIs instead of
inventing them" rule. Future leaves (a broader Thunderstore sweep, or a
specific mod the owner names) grow this list the same way. The framework
itself is fully proven off-game against fake registries in
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

**The Cargo Manifest panel (issue #153's "manifest" row) also needs no
gate**, for a different reason: it is not a prediction. `CART_INTERNALS.md`
documents that Teamster's mass figures are recomputed live from
`m_baseMass`, `m_itemWeightMassFactor`, and `Inventory.GetTotalWeight()` —
the same fields a manifest listing reads — and BetterCarts' `SetMass`
prefix only rewrites its own local `mass` parameter for the physics engine;
it never writes back to those source fields. So the displayed weight
numbers stay correct even with BetterCarts installed. What breaks is not a
number but a *prediction*: `LoadModel`/`RiskModel`'s calibration rows were
proven against vanilla pulling physics, and once the physics engine sees a
BetterCarts-reduced mass while Teamster keeps computing the vanilla-formula
mass, that calibration no longer reliably predicts real climbing behavior
for the number shown — which is exactly what the gate on those two models,
and only those two, protects against. `Domain/Ui/CargoManifestPresenter.cs`
has no reference to `LoadModel`, `RiskModel`, or `Climbability` (verified),
confirming there is nothing there for the gate to touch.

## Research: registered mods and considered candidates (CT-037/038)

`PROJECT.md`'s market research names "Better Carts" generically ("Better
Carts and similar cart mods change cart physics, weight handling, or
pulling behavior directly") without pinning an exact Thunderstore package.
CT-037 researched that specific reference; CT-038 broadened the pass across
Thunderstore's current Valheim listings for maintained mods touching carts,
cart physics, item weights, or container behavior, selecting the
significant combinations by download count and directness of effect on
Teamster's compatibility surface (cart mass/weight, never player carry
capacity — see "Ruled out" below for why that's a real distinction):

| Candidate | Author | Downloads (retrieved) | What it actually does | GUID | Registered? |
|---|---|---|---|---|---|
| BetterCarts | TastyChickenLegs | 46,000 | Quick attach/detach, up to 4-player push assist, damage removal, network sync. **Also reduces cart mass by a default 20%** — see below. | `TastyChickenLegs.BetterCarts` (verified: `Plugin.cs`'s `ModGUID` constant) | **Yes** — `Adapt`, `AffectedAspect.CartMassOrPhysics` |
| ItemStacks | mtnewton | 306,955 (Thunderstore experimental API, retrieved 2026-09-06) | Increases item stack sizes and **reduces every item's weight by a default 90%**, on by default — see below. | `net.mtnewton.itemstacks` (verified: `ItemStacksPlugin.cs`'s `GUID` constant) | **Yes** — `Adapt`, `AffectedAspect.CartMassOrPhysics` |
| ValheimPlus | (community, `valheimPlus` org) | 186,034 (Thunderstore experimental API, retrieved 2026-09-06) | A large, config-driven overhaul (dozens of independent optional sections). Its Wagon section can change cart mass, but ships **disabled by default**, and disabled reproduces vanilla mass exactly — see below. | `org.bepinex.plugins.valheim_plus` (verified: `ValheimPlus.cs`'s `[BepInPlugin(...)]` attribute) | **Yes** — `Warn`, `AffectedAspect.None` |
| Better Cart | We_Haul | 1,793 (Thunderstore experimental API, retrieved 2026-09-06; CT-037 reported 2,200 from a web search snippet — this leaf's number comes from a direct, timestamped API query, most likely reflecting a version re-upload resetting Thunderstore's counter rather than an error in either research pass) | "Allows for customization of minimum and maximum mass of Carts so that loading a cart doesn't make it impossible to move" — a direct, conceptually strong match for "changes cart physics, weight handling." | **Could not be verified**, in either CT-037 or this leaf's renewed attempt. No linked GitHub/source repository found; Thunderstore's decompiled-source viewer for this package did not yield readable source through available tooling. | **No** |

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

**ItemStacks reduces every item's weight by 90%, on by default.**
`github.com/mtnewton/valheim-mods`, `ItemStacks/ItemStacksPlugin.cs`
installs a Harmony postfix on the game's item-database initialization that
walks every item type and calls a per-item weight setter:

```csharp
weightEnabledConfig = config.Bind(NAME + ".ItemWeight", "enabled", true, ...);
weightMultiplierConfig = config.Bind(NAME + ".ItemMultipliers", "weight_multiplier", .1f, ...);
// ...
if (weightEnabled) tracker.SetWeight(weightMultiplier, item);
```

`ItemStacks/ItemTracker.cs`'s `SetWeight` confirms the effect is not
cosmetic — it overwrites the item's own shared weight field directly:
`item.m_itemData.m_shared.m_weight = value;`, the exact field
`CART_INTERNALS.md` documents as what `Inventory.GetTotalWeight()` sums
over. Both `enabled` (`true`) and the `0.1` multiplier are the shipped
defaults, so a fresh install reduces all cargo weight, and therefore cart
mass, to roughly a tenth of vanilla out of the box — an even larger and
more central effect than BetterCarts', from a mod with over six times the
downloads. Registered `Adapt`/`CartMassOrPhysics` for the same reason.

**ValheimPlus can change cart mass, but only if a player explicitly turns
its Wagon section on.** `github.com/valheimPlus/ValheimPlus`,
`ValheimPlus/GameClasses/Vagon.cs` installs a Harmony prefix on the game's
own cart-mass-recompute method that returns `false` (fully replacing it),
but its disabled branch reproduces vanilla's formula exactly:

Quoted verbatim from `ValheimPlus/GameClasses/Vagon.cs` (this markdown file
is outside the `check_teamster_adapter_isolation` validator's `*.cs` scan,
so the third-party type name below is not a Domain-purity violation — see
`tools/validate_repo.py`):

```csharp
[HarmonyPatch(typeof(Vagon), "UpdateMass")]
public static class ModifyWagonMass
{
    // "Vagon" is from base game
    private static bool Prefix(ref Vagon __instance)
    {
        if (!__instance.m_nview.IsOwner()) return false;
        if (__instance.m_container == null) return false;

        float totalWeight = 0;
        if (Configuration.Current.Wagon.IsEnabled)
            totalWeight = Helper.applyModifierValue(__instance.m_container.GetInventory().GetTotalWeight(), Configuration.Current.Wagon.wagonExtraMassFromItems);
        else
            totalWeight = __instance.m_container.GetInventory().GetTotalWeight();

        if (Configuration.Current.Wagon.IsEnabled)
            __instance.m_baseMass = Configuration.Current.Wagon.wagonBaseMass;
        else
            __instance.m_baseMass = 20;

        float mass = __instance.m_baseMass + totalWeight * __instance.m_itemWeightMassFactor;
        __instance.SetMass(mass);
        return false;
    }
}
```

Three independent source points confirm the Wagon section ships disabled:
`ValheimPlus/Configurations/Sections/WagonConfiguration.cs` defaults
`wagonBaseMass` to `20` and `wagonExtraMassFromItems` to `0` (vanilla-
matching values even if the section *were* on), and the shipped
`valheim_plus.cfg` template itself reads `[Wagon] ... enabled=false` with
an explicit comment that the player must change it to `true` to activate
the section. So a default install computes cart mass identically to
vanilla — confirmed from the patch logic, the section's own defaults, and
the shipped config file, not assumed from any one of them alone.

This is a genuinely different shape of finding from BetterCarts/ItemStacks:
the mod's *presence* doesn't determine whether cart mass is altered — the
player's own, separately-configured choice does, and Teamster's detector
only sees BepInEx plugin presence, never another mod's live config values.
Tagging ValheimPlus `CartMassOrPhysics` would suppress accurate load advice
for the (likely large, given the mod's dozens of unrelated features)
fraction of installs that never touch the Wagon section — a real cost, not
a hypothetical one, given ValheimPlus's popularity. Tagging it `None`
outright would silently under-warn the minority who *did* enable it.
`CompatibilityPolicy.Warn`/`AffectedAspect.None` resolves this: no gate
trip on presence alone (protecting the majority's accurate readings), but
its Compat-panel description explicitly tells the player to check their
own `valheim_plus.cfg` `[Wagon]` section — the first real use of the `Warn`
policy the framework has shipped with a live entry to demonstrate it.
Recorded as a known limitation of presence-only detection in
`HUMAN_ATTENTION.md` rather than silently accepted.

**Ruled out — player carry-capacity mods (different mechanic entirely).**
The research pass also found several current, meaningfully-downloaded mods
that raise how much a *player* can carry before being encumbered
(SkilledCarryWeight, FascinatingCarryWeight, "Skills Give More Carry
Weight", PlecakDlaCweli, InfinityInventory). None of these change an
item's own weight value or a cart's mass computation — they change the
player's encumbrance *threshold* only, a mechanic Teamster's cart-mass
calibration never reads. Considered and not registered, not because
research was incomplete, but because they are outside this compatibility
surface entirely.

**Ruled out — container/chest sizing mods (grid size, not weight).**
CustomContainerSizes (fenrir0054) and Bigger Chests (robclancy) let a
player configure a cart or chest's inventory *grid dimensions* (more
slots), not any item's weight or a cart's mass formula. More slots can let
a player load more total weight than vanilla's cart grid would physically
allow, which could push a real cart above what `LoadModel`'s calibration
table covers — but that surfaces through the table's existing, honest
`Unknown`/uncalibrated answer (the same path an unusually heavy vanilla
load already takes), not a silently wrong one. Not registered.

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

- No in-game coexistence matrix has been run yet, for any of the three
  registered mods (dev machine, `TCT-Compat` profile, per `TEST_PLAN.md`);
  the matrix acceptance criterion is satisfied structurally today (the gate
  and every consumer above are exhaustively unit-tested against both fake
  and every real registered probe) with the real-mod, real-game observation
  pending — never claimed PASS. Tracked in `HUMAN_ATTENTION.md`.
- The We_Haul "Better Cart" GUID remains unverified after two research
  passes (CT-037 and CT-038; see research above) — resolving it needs
  either the owner supplying the real GUID or a future leaf explicitly
  authorized to inspect the compiled binary.
- **Presence-only detection cannot see another mod's live configuration.**
  ValheimPlus's registration reflects its *shipped default* only (Wagon
  section disabled). A player who has both Teamster and ValheimPlus
  installed and has explicitly enabled and reconfigured the Wagon section
  gets a `Warn`-level Compat-panel note, not a suppressed/unavailable
  reading — the framework has no mechanism to read another mod's live
  configuration today, only its presence via `Chainloader.PluginInfos`
  (`Adapters/CompatibilityAdapter.Lookup`). This would be a harder lift than
  it might sound: ValheimPlus's `WagonConfiguration` (verified from source)
  is a plain POCO under the mod's own `ServerSyncConfig<T>` scheme, not a
  standard BepInEx `ConfigEntry<T>` binding — so reading it back would need
  per-mod, format-specific knowledge, not one generic "read any mod's
  config" mechanism mirroring the generic presence probe. A real, buildable
  enhancement in principle, but out of scope for a leaf whose job is
  research and registration, not framework extension — tracked in
  `HUMAN_ATTENTION.md` as a future capability, not a defect in what shipped.

## In-game surface

The Cart Status panel's **Compat** button (always visible, unlike the
Cartographer-conditional Routes button) opens a read-only panel listing
every actually-detected known mod and its policy. Until BetterCarts (or
another registered mod) is actually installed alongside Teamster, it shows
"No known compatibility concerns detected." — the honest, correct answer
when detection finds nothing, regardless of what the registry contains.
The startup log prints the same line (or one line per detected mod) once
per session.
