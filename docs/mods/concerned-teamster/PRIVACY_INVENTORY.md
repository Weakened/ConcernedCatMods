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
breach, and why the suite passed over it. A space is now admitted inside a
segment when the token after it does not begin with a lower-case letter
(`\p{Ll}`, so non-Latin and punctuation-led folder names are covered too), and
in the final component only where the path visibly ends.

**Stated limits**, so they are written down rather than found:

1. A path ending at a **folder** followed by prose cannot be told from a
   folder name with more words in it, so the run is refused and the rest of
   that folder name survives. Refusing is the right way round — the
   alternative deletes the sentence the bundle exists for.
2. A folder name whose post-space token **begins lower case** (`steam games`)
   stops the chain there, and the remainder of the path survives. The user
   name is before that point and still does not.
3. UNC (`\\server\share\...`), relative and `~`-rooted paths are matched by
   neither pattern: `WindowsPath` needs a drive letter and `UnixPath` needs a
   `/`. Pre-existing and unchanged by #410, and tracked as **#408**, which
   covers this sanitizer as well as the sibling product's.

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
