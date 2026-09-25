# Privacy inventory — Concerned Teamster (CT-029)

Complete enumeration of every piece of data Concerned Teamster stores,
displays, or logs, and where each one goes. The bottom line: **nothing
Teamster produces leaves the local machine.** Teamster sends no network
messages and takes no ownership (validator-audited, CT-026), runs no
analytics or telemetry upload, and includes no crash reporter (crash
reporting is a Concerned *Cartographer* concern, issue #97 — not present in
Teamster). This document is the committed privacy review required by CT-029.

## What Teamster stores on disk

All under the BepInEx config path, on the player's own machine, never a
Valheim save:

| Data | Location | Contents | Leaves machine? |
|---|---|---|---|
| Per-world trip sidecar | `BepInEx/config/ConcernedCatMods/ConcernedTeamster/<worldUID>...` | Recorded trip samples (world X/Z position, grade, speed, cart mass), trip summaries, and 8 m road-quality segment stats; a versioned header carrying the owning world UID | No — local file only |
| Plugin config | BepInEx config (`.cfg`) | Feature toggles and tunables (enable flags, thresholds, retention count) | No — local file only |

- World X/Z positions in trip samples are **the local player's own haul
  route** in their own world; they are never transmitted, and they describe
  terrain, not people.
- The world UID is Valheim's own identifier for the local world, used solely
  to keep one world's trips from loading into another (cross-world
  isolation). It is not account or personal data.

## What Teamster displays (all from local reads)

| Surface | Data shown | Source |
|---|---|---|
| Cart Status / manifest | Cart mass, cargo weight, item names & counts, grade, surface, pull state | The local game's own component reads (the player already sees these items in the container UI) |
| Warnings / descent risk / diagnostics / recovery | Derived advisory text | Domain math over the above |
| Trip history / comparison / route report | Trip stats, road scores, route profiles | The local sidecar + Cartographer's in-memory routes (read-only) |
| Cooperative diagnostics (v0.6) | Nearby players' **in-game character names** and a helping/hindering label | Names the local player already sees rendered above those characters; classification is local math |

Cooperative diagnostics surface only the character name already visible in
the world — no account id, no coordinates, no anything else — and even that
is display-only (not logged, not stored, not sent).

## What Teamster writes to the BepInEx log

Reviewed line by line for sensitive content:

- Environment banner (mod/game/Unity/BepInEx/Jötunn versions) — build
  metadata, no player data.
- Capability probe outcomes, effective config values — no player data.
- Cartographer integration probe (product name, version) — no player data.
- Trip persistence outcomes (counts, file operation results) — no player
  data; never the sample contents.
- Gated telemetry debug summary (only when DebugLogging is on): tracked/
  sampled cart counts plus an advisory risk phrase — counts and advice
  only, no ids, names, or positions.
- Parking brake state changes: the **cart id** (`<ownerUserId>:<objectId>`)
  plus a reason string. The owner id here is Valheim's own numeric ZDO owner
  id (a network object id already present in the game's state), not a player
  account, name, or any personal identifier. It appears only in the local
  log and identifies which cart, not who.
- **Player character names are never logged.** Cooperative diagnostics keep
  names in UI text only.

Player character names are **never logged at all** — that is the primary
guarantee. Where a network-derived name is *displayed* (cooperative
diagnostics), it is length-capped and control-character-stripped first
(`NetworkInputGuard.Label`, wired in `CooperativeEffortClassifier`), so a
crafted name cannot inject newlines or bloat a panel line either.

## What a support bundle carries, and what is scrubbed out of it

A support bundle is the one file here whose purpose is to be handed to
somebody else, and the one input this product cannot review line by line in
advance: `LogTailRecorder` gathers Teamster's own recent log lines, and the
lines above are written for a developer reading BepInEx's log, not for a file
a player shares. Several of them embed a full path. Every line therefore goes
through `SupportBundleSanitizer`, which masks URLs, coordinate pairs, paths,
Valheim save-file names, IPs, secret-shaped blobs and long digit runs, and
caps each line's length.

Unlike Concerned Cartographer's sanitizer, a matched path is replaced by
`<path>` with **no terminal segment kept**. Keeping a file name is safe there
because that composer only ever builds strings from fixed components; here the
input is arbitrary free text, and a leaf file name can itself be the
identifying content.

**Paths containing spaces (#410).** Until #410 both path patterns forbade
whitespace inside a segment, so the match stopped at the first space in a
mod-manager path — `...\Thunderstore Mod Manager\DataFolder\...` — and
everything from `Mod` onwards travelled verbatim: the profile name, the folder
layout, and whatever the player's own folders are called. The user name sits
before that point and always went, which is why this was a leak rather than a
breach, and why the suite passed over it.

A space is now admitted inside a segment, and the discriminator between "this
folder name has more words in it" and "the path ended and a sentence began" is
**count, not case**: at most two space-joined tokens per segment. Two covers
every real multi-word folder in the paths this sanitizer sees —
`Thunderstore Mod Manager`, `Documents and Settings`, `Program Files (x86)`,
`Application Support`, `Eren cansunar` — and refuses the longer runs prose
produces. In the final component three further guards apply: no run at all
after a recognised file extension, no dot/comma/semicolon inside a run token,
and the run taken only where the path visibly ends.

An earlier version of this rule tested case instead, and it was wrong in both
directions: a lower-case second word refused the run, so a **user name with a
space in it** (`C:\Users\Eren cansunar\...`) kept the surname, the folder
layout and the profile name; and a capitalised run was admitted without limit,
so `wrote C:\a\b OK See BepInEx/LogOutput.log` collapsed to `wrote <path>` and
took the pointer to the log file with it.

**Stated limits**, written down rather than found, and each asserted in
`SupportBundleTests.Sanitizer_TheseAreTheStatedLimits`:

1. A path ending at a **folder** followed by at most two capitalised words and
   then a line end: the run fires and those words go.
   `...\Thunderstore Mod Manager\config Access Denied` becomes `<path>`. Erring
   this way keeps the folder name from surviving; the cost is a short reason.
2. A folder name of **four or more words** (`My Very Long Folder`) exceeds the
   cap, so the run is refused and the rest of the path survives. This is what
   buying limit 1's direction and the user-name fix cost.
3. An **extension of more than eight characters**, or one ending in a
   non-alphanumeric (`notes.configuration`, `b.cfg~`), is not recognised as an
   extension, so a run may still follow it and take the sentence.
4. A **world or character name containing spaces** loses only its last word to
   `<save>`: that pattern forbids spaces, so `Erens New World.db` becomes
   `<path> New <save>.db` and a middle word survives.
5. **UNC (`\\server\share\...`) and relative paths** are matched by neither
   pattern: `WindowsPath` needs a drive letter and `UnixPath` needs a `/`. A
   `~`-rooted path **is** matched once it has two separators
   (`~/Library/Application Support/...` → `~<path>`); only a single-separator
   `~/x.cfg` escapes. Pre-existing and unchanged by #410, and tracked as
   **#408**, which covers this sanitizer as well as the sibling product's.

Because of limits 1 and 5, `SupportBundleComposer.Header`'s "no full paths" is
a statement about the ordinary case, not a guarantee. The header says so.

## Data flow summary

- **In:** local game state (read-only), the local sidecar file, Cartographer's
  in-memory routes (read-only).
- **Out:** nothing over any network. The only writes are to Teamster's own
  sidecar file and the BepInEx log, both local.
- **Enforcement:** the CT-026 validator audit fails the build on any
  outbound-network or ownership token; the CT-028 audit fails on any force or
  teleport; the CT-024 audit keeps the Cartographer integration read-only.

## Review conclusion

No Teamster feature transmits data off the machine, and nothing sensitive
(account identity, personal data, precise real-world information) is stored,
displayed, or logged. The only identifiers that appear anywhere are Valheim's
own world UID (local isolation) and ZDO cart/owner ids (local log, which cart)
— both game-internal, neither personal. This inventory is re-checked at each
release gate and whenever a feature adds a new stored/displayed/logged field.
