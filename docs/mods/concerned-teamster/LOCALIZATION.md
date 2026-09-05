# Translating Concerned Teamster (CT-032)

Concerned Teamster's user-facing strings live in a localization catalog with
English as the complete, canonical source. Any language can be added with a
single text file — no build, no code.

## How it works

- Every player-facing string has a stable **key** (e.g. `routes.selected`)
  and an English default. Code resolves strings by key at display time.
- On first run the mod writes a **template** next to its config:
  `BepInEx/config/ConcernedCatMods/ConcernedTeamster/teamster-strings-template.tsv`
- To translate, copy that template to
  `teamster-strings.tsv` (same folder) and fill in the translations. The mod
  loads it on the next launch.
- **Missing keys fall back to English**, so a partial translation is safe and
  never blanks the UI. A key with no English default at all is logged once as
  a programming error (it should never happen in a release).

## File format

Tab-separated, one key per line, `#` starts a comment:

```text
# ConcernedTeamster strings v1 — key<TAB>translation (missing keys fall back to English)
routes.pick	Choisissez un itinéraire à profiler.
routes.selected	Sélectionné : {0}
report.title	Rapport d'itinéraire : {0}
```

- The part **before** the tab is the key — copy it exactly, never translate it.
- The part **after** the tab is your translation.
- Encoding: a tab, newline, carriage return, or percent sign **inside a
  translation** is written percent-encoded — `%09` tab, `%0A` newline, `%0D`
  carriage return, `%25` percent. (Backslash sequences like `\n` are NOT
  interpreted — they would show up literally in game.) A plain `%` that is
  not followed by two hex digits is kept as-is, so ordinary text like
  `100%` works either way.
- Line ends are trimmed when the file loads, so a translation cannot begin
  or end with a space — the English never does either; spacing around
  inserted values lives inside the string, next to its placeholders.

## Placeholders

Some strings contain numbered placeholders like `{0}`, `{1}` that the mod
fills in at runtime (a route name, a number). Your translation **must keep
the same set of placeholders** — you may reorder them, but you may not add or
drop one. A row whose placeholders do not match the English is **skipped**
(and counted in the load log) so a broken format can never crash or garble
the UI. Example:

```text
routes.selected	{0} sélectionné
```

`{0}` is present, so this is accepted; `Sélectionné` (no `{0}`) would be
skipped.

## Submitting a translation

- Open a pull request on the [GitHub repository](https://github.com/Weakened/ConcernedCatMods)
  adding your language, or attach your `teamster-strings.tsv` to a GitHub
  issue.
- Please note the game/mod version you translated against; keys are stable
  across patch releases, and new keys added in a release fall back to English
  until translated.

## Coverage and what stays English

Every player-facing panel string is externalized — Cart Status, Cargo
Manifest, warnings, stuck diagnostics, cooperative-effort lines, recovery
guidance, trip history/comparison/bottlenecks, route picker/profile/report,
load-model verdict sentences, and the controller focus labels. A source-level
audit test (`HardcodedStringAuditTests`) runs in CI and fails the build if a
user-facing English literal is ever added to the presentation layer outside
the catalog, if a catalog key goes dead, or if an externalized sentence is
re-hardcoded.

Deliberately **not** translatable, by design:

- Console/log output, including the fail-closed session-disable lines and
  the descent-risk debug summary (`Domain/Risk` verdict text surfaces only
  in a debug log line today) — logs are a developer/support surface, and
  support needs to read them regardless of the player's language.
- BepInEx configuration entry names and descriptions — they live in the
  user's `.cfg` file, which is a file format, not UI; translating them would
  fork users' config files by language.
- Language-neutral notation: numbers, `%`, `m`-style units inside composed
  patterns the catalog controls, grade-band labels like `<3%`, the `?`/`—`
  unknown markers, and alignment spacing.

## Notes

- Keys are grouped by surface (`status.*`, `manifest.*`, `warn.*`, `diag.*`,
  `coop.*`, `recovery.*`, `load.*`, `trips.*`, `compare.*`, `bottleneck.*`,
  `profile.*`, `report.*`, `routes.*`, `nav.*`, `unit.*`, `verdict.*`,
  `ui.*`). The template always lists every current key.
- The trip-selection markers must stay consistent as a set if you change
  them: `trips.markerA`/`trips.markerB` (row markers), `trips.selectA`/
  `trips.selectB` (row buttons), and the bracketed letters mentioned in
  `compare.*` prompts all refer to the same two slots.
