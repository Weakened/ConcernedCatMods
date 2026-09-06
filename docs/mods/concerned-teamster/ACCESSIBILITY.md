# Accessibility (CT-033)

Scale, contrast, and non-color cues for Concerned Teamster's panels. This
document is the required "cue audit table" and contrast-pass record the
CT-033 acceptance criteria ask for.

## UI scale

`TeamsterSettings.UiScale` (config section `Ui`, key `Scale`) is a single
float, default `1.0`, clamped to `[0.8, 1.5]` by `Domain/Ui/UiScaleOptions`
(both by `AcceptableValueRange` in the config UI and again in code, so no
config edit can escape the range).

The factor is applied once, at panel-build time, to the panel's root
`Transform.localScale` (`Ui/PanelStyle.ApplyScale`) — not to individual
widths, heights, or offsets inside each panel. This is deliberate: uniform
scaling of a parent transform carries every child's relative position and
size along with it, so a panel that does not clip its own content at one
scale cannot newly clip it at another scale. The property is structural
(a Unity transform guarantee), not something each of the six panels has to
re-prove independently. `AccessibilityTests` proves the piece that *is*
pure-domain math: `UiScaleOptions.Clamp` is total (never NaN/throws), and
`MinScale < DefaultScale < MaxScale`.

What this does **not** prove automatically: whether a *larger*-scaled panel
stays clear of a neighboring panel's screen position (each panel's anchor
offset from the screen edge is independent pixel math, not itself scaled),
and whether legacy `Text` components stay crisp when scaled via transform
rather than re-rendered at a larger font size. Both are visual, in-game
checks — see the pending item in `HUMAN_ATTENTION.md`.

Takes effect on the panel's next build (world enter, or first open that
session) — not live on an already-open panel, the same trade-off
`NavigationCatalog` already accepts for label baking (see `ARCHITECTURE.md`).

## Contrast pass

`Domain/Ui/ContrastRatio` implements the public WCAG 2.1 relative-luminance
formula (not a Valheim API). Target: **4.5:1**, the AA threshold for
normal-size text (`ContrastRatio.AaNormalTextMinimum`).

Every panel used the same two colors, copy-pasted six times:

| Name | RGB | Used for |
|---|---|---|
| Header | (0.90, 0.80, 0.60) | Titles, totals, section headers |
| Body | (0.85, 0.85, 0.82) | Data rows |
| HudHint | (1.00, 0.85, 0.50) | HUD warning hint under the Cart button |

CT-033 consolidated these into `Domain/Ui/PanelPalette` (one definition) and
`Ui/PanelStyle` (the Unity `Color` conversion every panel now goes through),
so a future contrast fix applies everywhere at once instead of needing six
matching edits.

**The background problem.** Every panel is Jötunn's wood-panel texture, not
a flat color — Teamster's source cannot read its exact pixel value without a
live game session. `PanelPalette.ApproximateWoodBackground` = (0.30, 0.24,
0.17) is a documented mid-range estimate of that texture's tone, chosen
before running the numbers (not tuned to make the audit pass).

Computed contrast against that estimate:

| Color | Ratio | AA (4.5:1)? |
|---|---|---|
| Header | 6.66:1 | Pass |
| Body | 7.31:1 | Pass |
| HudHint | 7.66:1 | Pass |

**Sensitivity check.** Wood-panel art varies; re-running the same math
against a *lighter* plausible estimate (0.45, 0.35, 0.25) drops Body to
4.56:1 (barely passing) and Header to **4.15:1 — below the AA target**, if
each color were the only cue. This is a real finding, not a hypothetical:
the margin is genuinely thin under a plausible alternate background.

**Fix applied:** every panel text `CreateText` call that previously passed
`outline: false` now passes `outline: true` (a black outline, already
supported by Jötunn's `CreateText` and already used for panel titles). An
outline is contrast-robust independent of the exact background tone — it
does not depend on getting `ApproximateWoodBackground` right. This is a
more resilient fix than tuning fill colors against an unverified estimate.

`AccessibilityTests.PanelTextColor_MeetsAaContrastAgainstApproximateBackground`
pins the three colors against the AA target so a future palette change is
caught if it regresses below 4.5:1 against the documented estimate.

**Pending:** an in-game screenshot with an actual color-pick of the wood
panel texture would let `ApproximateWoodBackground` be replaced with a
measured value — see `HUMAN_ATTENTION.md`. The outline fix means the
practical risk of this being wrong is low (outlined text reads clearly
against both the estimate and the lighter sensitivity case above), but the
exact numbers in this table would need the estimate corrected once measured.

## Non-color cues

Every state that carries a color anywhere in Teamster's design already
carries distinguishing text or a symbol beside it — a deliberate pattern
since CT-009 ("the composed line carries the non-color cues — a symbol AND
the level word") and CT-018 ("Series are labeled 'A #id' / 'B #id' — text,
never color alone"). CT-033's job was to make this an audited, tested
invariant instead of a habit. The audit table:

| Colored state | Enum | Non-color cue | Proving test |
|---|---|---|---|
| Warning: none | `WarningLevel.None` | No line rendered at all | — |
| Warning: caution | `WarningLevel.Caution` | `"[!] CAUTION"` prefix + situation + action text | `WarningSeverityCues_AreDistinctNonEmptyText` |
| Warning: danger | `WarningLevel.Danger` | `"[!!] DANGER"` prefix + situation + action text | `WarningSeverityCues_AreDistinctNonEmptyText` |
| Stuck: overloaded | `CartDiagnosis.ImpossibleLoad` | "overloaded for this grade" label + evidence + action | `StuckDiagnosisLabels_AreDistinctNonEmptyText` |
| Stuck: marginal | `CartDiagnosis.MarginalLoad` | "load is marginal here" label + evidence + action | `StuckDiagnosisLabels_AreDistinctNonEmptyText` |
| Stuck: steep climb | `CartDiagnosis.SteepClimb` | "steep climb" label + evidence + action | `StuckDiagnosisLabels_AreDistinctNonEmptyText` |
| Stuck: obstruction | `CartDiagnosis.Obstruction` | "obstruction or grounded chassis" label + evidence + action | `StuckDiagnosisLabels_AreDistinctNonEmptyText` |
| Stuck: unclear | `CartDiagnosis.Unclear` | "cause unclear" label + evidence + action | `StuckDiagnosisLabels_AreDistinctNonEmptyText` |
| Coop: helping | `CoopEffort.Helping` | "{n} helping" tally + named list | `CooperativeEffortCounts_AreDistinctNonEmptyText` |
| Coop: hindering | `CoopEffort.Hindering` | "{n} hindering" tally + named list | `CooperativeEffortCounts_AreDistinctNonEmptyText` |
| Coop: unclear | `CoopEffort.Unclear` | "{n} unclear" tally + named list | `CooperativeEffortCounts_AreDistinctNonEmptyText` |
| Coop: idle | `CoopEffort.Idle` | Not tallied in crew summaries (a bystander is simply not mentioned) | — |
| Trip series A | (picker/comparison "A" slot) | `"A #{id}: ..."` header, `[A]` row marker | `TripComparisonSeriesHeaders_AreDistinctText` |
| Trip series B | (picker/comparison "B" slot) | `"B #{id}: ..."` header, `[B]` row marker | `TripComparisonSeriesHeaders_AreDistinctText` |
| Unknown/missing weight | (manifest row) | literal `"?"` marker, counted separately in the total line | existing `CargoManifestPresenterTests` |

`RiskLevel` (Safe/Unknown/Caution/Danger, `Domain/Risk`) is not in this
table: it is an internal signal consumed by the warning/diagnostic layer
above, not rendered as its own separate colored UI element — it never
reaches a player except through the warning cue or stuck-diagnosis label
rows already covered.

No panel currently sets a `Text.color` conditionally based on any of these
enums — every row uses the same `PanelPalette` color regardless of state,
so today color carries *zero* information; text alone always has. Adding a
future color accent (e.g. tinting danger rows) would still be safe under
this audit as long as the accompanying text keeps differing, which the
tests above enforce.

## Warning readability review

Reviewed `warn.*`, `diag.*`, and `recovery.*` catalog text (`TeamsterStrings`)
for concise wording and consistent terminology. Finding: the canonical
verdict words (`verdict.tooHeavy` = "TOO HEAVY", `verdict.marginal` =
"MARGINAL", `verdict.ok` = "OK", `verdict.unknown` = "UNKNOWN") are used
consistently everywhere a verdict is quoted; the situational phrasing
around them intentionally varies by context (a warning *situation* sentence
reads differently from a compact diagnostic *label* or an actionable
recovery *title* for the same underlying cause), which is appropriate
register variation, not inconsistency. No wording changes were made.
