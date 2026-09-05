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
- Parking brake state changes: the **cart id** (`<ownerUserId>:<objectId>`)
  plus a reason string. The owner id here is Valheim's own numeric ZDO owner
  id (a network object id already present in the game's state), not a player
  account, name, or any personal identifier. It appears only in the local
  log and identifies which cart, not who.
- **Player character names are never logged.** Cooperative diagnostics keep
  names in UI text only.

Hostile/oversized network-derived labels are length-capped and
control-character-stripped before they could ever reach a log line
(`NetworkInputGuard.Label`, CT-029), so a crafted name cannot inject newlines
or bloat the log.

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
