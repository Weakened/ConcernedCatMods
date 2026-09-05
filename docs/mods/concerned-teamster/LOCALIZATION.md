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
- Escapes: `\t` tab, `\n` newline, `\r` carriage return, `\\` backslash.

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

## Notes

- Console/debug log output stays English by design (it is a developer/support
  surface, not a player-facing one).
- Keys are grouped by surface (`routes.*`, `report.*`, …). The set grows as
  more panels are externalized; the template always lists every current key.
