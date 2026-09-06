# v1.0 final sign-off (CT-049)

Six explicit sign-off lines across every non-code surface, each with
evidence — not a re-derivation of what earlier leaves already proved,
but a fresh rerun/recheck at v1.0 time, with anything found stale fixed
in this same leaf rather than left for a future pass to rediscover.

## 1. Docs truth

**Signed off — 6 stale claims found and fixed, everything else checked
consistent with current source.**

Specific claims cross-checked directly against source, not re-read on
faith: current version (0.9.0, consistent across `README.md`,
`ConcernedTeamster.csproj`, `thunderstore.toml`, `Plugin.cs`,
`CHANGELOG.md`), default config profile (`Standard`, matches
`TeamsterDefaults.DefaultProfile`), every test-class name `GOLDEN_PATH.md`
cites as evidence (all exist), every example string key
`LOCALIZATION.md` cites (all exist verbatim).

Fixed in this leaf:
- `src/ConcernedTeamster/Package/README.md`: localization count "274" →
  **277** (see §2); the "What it does today (v0.7 — ...)" heading
  wrongly scoped an entire feature list spanning v0.2–v0.8 to a single
  version — dropped the version tag since every bullet already self-dates.
- `docs/mods/concerned-teamster/V1_DEFINITION_OF_DONE.md`: "274+ keys" →
  **277**, plus an explicit note that no community translation exists yet.
- `docs/mods/concerned-teamster/FEATURE_FREEZE.md`: "a 280+-key
  localization catalog" — this was not a conservative rounding, it was
  **wrong** (277 < 280) — corrected to 277.
- `docs/mods/concerned-teamster/TEST_PLAN.md` + `COMPATIBILITY.md`:
  compatibility mod versions rechecked live (see §6).

Deliberately **not** changed: `RELEASE_DOSSIER.md`'s **v0.8 RC1** entry
still says "609/609 PASS" — accurate for that exact sealed commit at the
time it was sealed (that entry's own successor, the v0.9.0 entry, already
correctly records the jump to 622/622, attributed to CT-041's
`DefaultFreezeSnapshotTests` + `FeedbackLinksTests`, not to CT-048). A
dated dossier entry is a historical record, not a live claim; rewriting
it to match today's count would make it wrong for the commit it actually
describes. Independent review of this leaf caught this section's own
first draft making exactly that mistake in the other direction — it had
misidentified which dossier entry carries "609/609" and mislabeled the
*current* count as 622, when CT-048's `PerformanceBudgetTests.cs` (7
tests, merged before this leaf branched) had already moved it to
**629** — confirmed by this leaf's own test run. `REGRESSION_REPORT_v1.0.md`
correctly cites 622 because that report ran before CT-048 merged; it is
not stale, just older. `V1_DEFINITION_OF_DONE.md`'s "every merge this
whole conveyor" row is a present-tense claim, so it's updated to 629 in
this same leaf.

## 2. Localization completeness

**Signed off, scoped honestly.**

The English catalog (`Domain/Localization/TeamsterStrings.cs`) has
**277** keys (independently recounted for this leaf, twice, after a
first quick regex undercounted at 256 by missing multi-line-wrapped
values — the exact count matters here specifically because getting it
wrong is the failure mode this leaf exists to catch). `HardcodedStringAuditTests`
(4 cases) and `TeamsterStringsTests` (17 methods, 20 executed cases with
`[Theory]` expansion) — 24 executed cases total — pass on rerun: no
hardcoded English string leaked outside the catalog, no dead
unreferenced key, no re-hardcoded sentence, no leading/trailing
whitespace in a catalog value.

**Scope note, stated explicitly so it isn't misread:** Teamster's
localization architecture (`LOCALIZATION.md`, CT-032) is a single
generic translator-override file (`teamster-strings.tsv`) the player
supplies — not a set of shipped per-locale files. No `.tsv` file exists
anywhere in this repo. "Completeness" therefore means the **English
source-of-truth catalog's own internal consistency**, proven above — it
does not mean any actual non-English translation has been produced or
verified. Zero community translations exist as of this sign-off.

## 3. Controller navigation

**Signed off, one pre-existing gap confirmed still open (not silently
resolved, not newly discovered).**

`ControllerNavigationTests.cs`: 14 `[Fact]` + 1 `[Theory]` (8 cases) =
**22 executed test cases**, rerun green. Covers both halves of
"controller": `NavigationCatalog` focus-ring traversal (wrap-and-visit-
once, previous-is-inverse-of-next, deterministic order, every panel
non-empty/reachable/buttons-first) and the gamepad accelerator layer
(chord normalization, external/internal conflict detection).
`NavigationCatalog.cs` currently registers 7 panels and 26 focus items.

**Confirmed still open** (grepped `NavigationCatalog.cs` directly for
this leaf — not fixed here, not this leaf's scope to fix): the
`CompatibilityPanel`'s own sub-panel Close button is still not
registered in `NavigationCatalog`, matching `HUMAN_ATTENTION.md`'s
existing CT-031-era entry and `V1_DEFINITION_OF_DONE.md`'s existing row.
Cosmetic — Escape and mouse/click both still close it — carried forward
as-is rather than expanded into this leaf's scope.

## 4. Accessibility

**Signed off — fresh baseline recorded, nothing pre-existing to
reconcile against.**

`AccessibilityTests.cs`: 9 `[Fact]` + 3 `[Theory]` (12 cases) = **21
executed test cases**, rerun green. Covers `UiScaleOptions.Clamp`
NaN/±Infinity fallback and range clamping (0.8–1.3×), a geometric
no-clip guarantee at max scale (tallest panel 760 units × 1.3 = 988,
under the 1080 reference canvas), a WCAG AA contrast sweep
(`ContrastRatio.AaNormalTextMinimum` = 4.5:1) against every panel
palette color, non-color-cue distinctness for every warning/diagnosis/
comparison label, and a source-scan regression guard that fails if any
panel ever disables the contrast outline. `ACCESSIBILITY.md`'s cited
numbers cross-checked against these live source constants — consistent.

No prior doc cited a specific test count for this suite, so there was
nothing stale to find here — 21/12 is simply the confirmed current state.

## 5. Migration chain

**Signed off, characterized accurately rather than overclaimed.**

There is no long v0.1→v0.9 fixture ladder to rerun, because Teamster's
on-disk formats have only changed shape **twice** in its whole history —
this section says so plainly rather than implying a chain that doesn't
exist:

- **Config schema** (`ConfigSchemaVersion`: `PreVersioning=0` →
  `Current=1`, introduced v0.8/CT-039): `ConfigSchemaMigrationTests.cs`,
  4 `[Fact]`s — pre-versioning migrates without touching other values,
  already-current is a no-op, never migrates backward. Rerun green.
- **Trip sidecar** (`TripSidecar.FormatVersion`: 1 → 2, CT-017 added
  road-quality segment rows): a **real, shape-changing** format bump,
  not just a forward-compatibility promise. `RoadQualityTests.cs`'s
  `V1File_ParsesTripsAndFlagsMigration_RecomputeMatches` composes a
  v2-format sidecar with no segment rows (a faithful stand-in for a real
  pre-CT-017 v1 file, which likewise never has any), textually rewrites
  its header back to `format-version: 1` (a genuine fixture technique,
  not a synthetic stub), reparses it, and proves the migration recompute
  matches scoring the same trips fresh.
  `TripPersistenceTests.cs` additionally proves an unrecognized *future*
  version is refused rather than guessed at, and that a backup is taken
  before migration; `TripPersistPlanTests.cs` proves the backup mandate
  and the refused-takes-precedence-over-migration-flag ordering. All
  rerun green (this leaf reran `RoadQualityTests`,
  `ConfigSchemaMigrationTests`, `TripPersistenceTests`,
  `TripPersistPlanTests` together: 111 total cases across all nine
  CT-049-relevant suites, zero failures).

## 6. Compatibility statement

**Signed off, versions rechecked live — one real version bump found (not
re-decompiled, flagged honestly), one mod newly deprecated upstream
(flagged for owner awareness), no registry classification changed.**

Full detail and the exact Thunderstore citations are in
`COMPATIBILITY.md`'s new "Version recheck (CT-049)" section; summary:

| Mod | Cited (CT-037/038) | Now (CT-049, live-checked 2026-09-06) |
|---|---|---|
| BetterCarts | 1.0.6 | **1.1.0** — real bump, not re-decompiled |
| ItemStacks | 1.2.0 | **1.2.0** — unchanged |
| ValheimPlus | "0.9.9.11" | **9.9.11** (format-corrected) — **now Deprecated on Thunderstore** |

None of this changes any registry entry's policy: detection is
GUID-based and presence-only, so it never depended on a version number
in the first place, and no evidence surfaced that any mod's core
mass/weight behavior changed. `CompatibilityFrameworkTests.cs` (7 tests)
and `CompatibilityAdvisoryGateTests.cs` (11 tests) — 18 total — rerun
green. Real in-game "load together" verification for all three mods
remains **pending**, exactly as CT-038 originally left it — no new claim
of an in-game result is made here.

## Rerun summary

All nine CT-049-relevant test classes reran together in one pass, this
leaf, on this machine: `ControllerNavigationTests`, `AccessibilityTests`,
`ConfigSchemaMigrationTests`, `RoadQualityTests`,
`CompatibilityFrameworkTests`, `CompatibilityAdvisoryGateTests`,
`HardcodedStringAuditTests`, `TeamsterStringsTests`,
`TripPersistPlanTests` — **111/111 PASS**, zero failures, zero new
defects filed. The full suite (`dotnet test ConcernedTeamster.Tests -c
Release`) is currently **629/629 PASS** — 622 at the v0.9 seal (CT-041)
plus CT-048's 7 `PerformanceBudgetTests.cs` cases; Cartographer's 568/568
is untouched. No production code changed by this leaf; every fix here is
a documentation correction to match already-correct code and
already-passing tests.
