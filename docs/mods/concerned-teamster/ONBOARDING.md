# Onboarding, profiles, discoverability, and config migration (CT-034)

## First-run onboarding

A single clickable hint above the always-visible Cart button, shown only
when a cart is nearby and never once dismissed:

`Domain/Onboarding/OnboardingPresenter.Evaluate(dismissedForever, isNearCart)`
is the entire decision, a pure two-input function:

| `dismissedForever` | `isNearCart` | Result |
|---|---|---|
| true | (either) | Hidden — permanent |
| false | false | Hidden — first launch away from any cart stays silent |
| false | true | Visible |

"Near a cart" reuses existing telemetry (`pump.Telemetry.Count > 0`) — no
new adapter or game read. Dismissal (`TeamsterSettings.OnboardingDismissed`,
default `false`) is set the moment the hint is tapped and is never reset by
the mod; the config file is the single source of truth, so it survives
restarts and world switches by construction.

The hint is also suppressed (not dismissed — a display-only check, re-run
every frame) whenever the Cart Status panel is already open: at that point
the player has plainly already found the button, so a pointer to it
floating above an open panel would be redundant. Closing the panel while
still near a cart and not yet dismissed shows it again.

**Never modal, never blocks input:** the hint is one small button
(`GUIManager.CreateButton`, ~280×44) positioned above the Cart button with
no background dim and no raycast-blocking overlay elsewhere on screen —
every other button and the game world underneath remain fully clickable
while it is showing. This is a construction guarantee (nothing else was
added that could intercept input), not something Unity lets a headless test
observe directly — the actual on-screen placement and wrap behavior of the
hint text are recorded pending in `HUMAN_ATTENTION.md`.

Tests: `OnboardingAndProfilesTests` — shows when near a cart and never
dismissed, stays hidden away from any cart, and stays hidden forever once
dismissed regardless of later proximity (a `[Theory]` covering both
proximity states after dismissal, so "forever" is not just "immediately
after").

## Config profiles

Three documented presets (`Domain/Profiles/ConfigProfile`), resolved by the
pure `ConfigProfileCatalog.Resolve`:

| Profile | Panel warnings | HUD hint | Trips | Risk lookahead | Brake |
|---|---|---|---|---|---|
| Minimal | off | off | off | 0 (disabled) | **off** |
| Standard (default) | on | off | on | 3 (default) | **off** |
| EverythingObservational | on | on | on | 5 (max) | **off** |

Standard's values are exactly Teamster's pre-CT-034 shipped defaults
(`Resolve_StandardMatchesShippedDefaults`), so an existing installation that
never touches the new `General.Profile` key is byte-for-byte unaffected.

**The parking brake is never touched by any profile.** It is the one
mutating feature Teamster ships; keeping it out of every preset — including
the "everything on" one — is a direct, tested application of the product's
"no cheats by default, mutating conveniences stay explicit" principle
(`Resolve_BrakeStaysOptInInEveryProfile`, parameterized over all three
profiles).

**Switching is explicit and reversible.** The player edits
`TeamsterSettings.ActiveProfile` (a plain BepInEx enum config entry, section
`General`, key `Profile`). `Plugin.ApplyProfileIfChanged` compares it
against `LastAppliedProfile` (internal bookkeeping, not meant for direct
editing) on every plugin load via `ProfileTransition.ShouldApply` — a
preset's values are written to the individual settings only when the active
profile actually changed since the last load. This is what makes "applying
a profile twice equals applying it once" true operationally, not just at
the pure-function level: the *second* load with no `Profile` edit is a
complete no-op, so a player's own manual tweaks to individual settings are
never silently overwritten by a restart. Picking a *different* profile
overwrites the curated settings again — explicit and reversible, never
sticky beyond the player's own choice.

`Resolve` is total over the whole `int` domain behind the enum, not just
its three named members — an unrecognized raw value (see "Config migration
safety" below) fails closed to Standard's values rather than throwing,
matching the fail-closed shape `LoadModel`/`RiskModel` already use for their
own Unknown verdicts.

Tests: `OnboardingAndProfilesTests` — idempotence (resolving the same
profile twice yields value-equal results), brake opt-in across all three
profiles, Standard matches shipped defaults, an out-of-range raw profile
value fails closed to Standard, and the apply-if-changed transition logic
(including a same-active-twice-in-a-row scenario modeling the actual
restart-with-no-edit case).

## Discoverability audit

Every button across all six panels, audited for label wording and
placement consistency:

| Panel | Buttons | Consistency notes |
|---|---|---|
| Cart Status | Cart (toggle), Trips, Routes, Manifest, Guidance, Engage/Release brake, Close | Destination buttons use noun labels (Trips, Routes, Manifest, Guidance); the one action button uses a verb phrase (Engage/Release brake) — a deliberate distinction between "go to a panel" and "do a thing," not an inconsistency. |
| Cargo Manifest | 4 sort-column headers, Close | Close at `(0, 26)`, size `110×28` |
| Recovery Guidance | Close only | Close at `(0, 26)`, size `110×28` |
| Trip History | 5 sort headers, per-row A/B/X, Close | Close at `(0, 26)`, size `110×28` |
| Route Picker | route rows, Clear, Report, Close | 3-button row (Clear/Report/Close); Close is the rightmost of a group, not the "standalone" pattern below |
| Route Report | Close only | Close at `(0, 26)`, size `110×28` — **was `(0, 28)`, `96×30`; aligned to match the other three standalone panels in this leaf** |

Every panel that offers *only* a Close button as its dismissal control
(Cargo Manifest, Recovery Guidance, Trip History, Route Report) now places
it at the identical position and size. Cart Status and Route Picker's Close
sits inside a multi-button row instead, where its position is relative to
its row siblings rather than the "standalone" pattern — not a target for
this alignment.

Buttons-first (every panel reachable and offering at least one button, no
accelerator-only path) was already audited and tested by CT-031 —
`ControllerNavigationTests.EveryPanel_IsNonEmptyEveryElementReachableAndButtonsFirst`
— and needed no changes here; cited rather than re-proven.

## Config migration safety

Teamster has not had a public release yet (every version to date ships
"Internal — unreleased"), so there is no external installation with an old
`.cfg` file to migrate from today. What already exists and what CT-034 adds:

- **Unknown/orphaned keys are preserved verbatim.** This is BepInEx's own
  `ConfigFile` behavior — any key present in a player's file that no bound
  `Config.Bind` call claims this session is kept and re-written unchanged on
  save. Teamster does not implement or need its own preservation logic for
  this; it is a property of the framework every config entry already relies
  on, not something specific to CT-034.
- **Out-of-range values clamp, never crash.** Every ranged Teamster setting
  is bound with an `AcceptableValueRange`, enforced by BepInEx on load — a
  value from an older, wider-range version clamps into the current range
  rather than throwing. Also pre-existing framework behavior.
- **Teamster's own policy going forward: a key's meaning is never repurposed
  in place.** If a future version needs to change what a key means (not just
  its default or range), it ships under a new key name rather than silently
  reinterpreting old data — undocumented here as aspirational, but as the
  concrete rule CT-034's own new keys (`Onboarding.Dismissed`,
  `General.Profile`, `General.LastAppliedProfile`) follow: none of them
  reuse or reinterpret an existing key.
- **The one genuinely new migration-shaped risk this leaf introduces** is
  `ConfigProfileCatalog.Resolve` receiving a profile value that does not
  name a current profile — modeling an "old-version fixture" in the only
  form that is actually testable without a BepInEx-dependent test project
  (see below): a stale raw value, as if a future release removed a profile
  a player's file still names. `Resolve_UnrecognizedProfileValue_FailsClosedToStandard`
  proves this cannot throw and falls back to Standard's values.

**Why this is not deeper migration/versioning infrastructure:** that is
explicitly CT-039's scope ("config/data migration, backup/recovery, and the
sanitized support bundle," v0.8) — building a dedicated migration framework
here would duplicate work planned for its own issue. CT-034's job was
verifying the *new* surface it adds is safe, not building the general
mechanism.

**Why config migration isn't tested against real BepInEx fixtures:**
`ConcernedTeamster.Tests` compiles only `Domain/**` sources directly (see
`ConcernedTeamster.Tests.csproj`) and has no reference to
`BepInEx.Configuration` or the compiled Teamster assembly — `TeamsterSettings`
itself is therefore not constructible in a test. This is the same Domain/
Adapter boundary every other Teamster feature already tests across: the
pure decision logic (`ConfigProfileCatalog`, `ProfileTransition`) is fully
tested; the `ConfigEntry`/`ConfigFile` wiring in `TeamsterSettings.cs` and
`Plugin.ApplyProfileIfChanged` is adapter glue, verified by inspection and
the in-game pending check below, consistent with how `Adapters/*.cs` has
never been unit-tested directly anywhere in this codebase.
