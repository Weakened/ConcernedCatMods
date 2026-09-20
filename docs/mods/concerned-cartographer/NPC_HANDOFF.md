# NPC_HANDOFF — Concerned Cartographer 1.1.1 candidate (Hulgi)

Epic #264. Leaves #265, #266, #267, #268, #269, #303. Owner reference #298.
**Candidate only. Nothing has been published, and no version has been tagged.**

---

## 1.1.1 — the first build anybody played

1.1.0 below was written, unit-tested and packaged without a single minute of
play. On 2026-09-15 it was played, on Valheim 1.0.12 in an isolated profile,
character and world. Four things it claimed were not true, and the section
after this one preserves the 1.1.0 record exactly as it was written so the
difference stays visible.

| Item | Value |
|---|---|
| Package | `TheConcernedCat-ConcernedCartographer-1.1.1` |
| Path | `artifacts/thunderstore/TheConcernedCat-ConcernedCartographer-1.1.1.zip` |
| ZIP SHA256 | `D2FD728E8E5BB500D154B6BF28593C8D22DFEAA9F1DB36EF67C931A1C15F1706` |
| ZIP bytes | 916,516 |
| DLL SHA256 | `0FC7C1FA44206427DDFAE9BC6AB797BFDCA69EEDD63EA652E141FBF602B73FC5` |
| DLL bytes | 695,808 |
| InformationalVersion | `1.1.1+2e3ce79ca8650f5cbbb49abe92edad6a5293f017` |
| Source | `feat/cc-npc-006-hulgi-appearance` @ `2e3ce79` |
| Checksums | `artifacts/thunderstore/SHA256SUMS-ConcernedCartographer-1.1.1.txt` |

The 1.1.0 ZIP is untouched and still hashes to
`686B0CC749E907338086BBB0FD01D77C7BE567742CFC4432537B2293FD0CC49B`.

### What playing it found

1. **Every `cc_*` console command was dead on Valheim 1.0.x.** Jötunn 2.29.2
   reflects for a twelve-parameter `Terminal.ConsoleCommand` constructor; 1.0.12
   has thirteen. All seven commands logged "No suitable constructor" at startup
   and answered *is not a recognized command* in game — including
   `cc_companion toolsonly on`, which the design relies on as the way to the map
   tools for somebody who never finds the compass. Fixed by registering against
   whatever constructor the build has, matched by shape, with Jötunn as fallback
   and the accepted command list written to the log.
2. **A brand new character in a brand new world was granted access as an
   EXISTING user.** The plugin writes its own starter `survey-rules.tsv` during
   startup and the legacy probe then read that file back as proof of prior use.
   #264's story gate could not engage for anybody, ever. Fixed; a fresh install
   now reports `access pending introduction (NotUnlocked)`.
3. **He never sat down.** Sitting in Valheim is an animator *parameter*
   (`emote_sit`, or the chair's own `m_attachAnimation`), and the code was
   searching for animator *states* with those names. Both the ground idle and
   furniture were affected. Fixed, and seating now uses the seat's own
   attachment point, heading and animation.
4. **The reference colour could never be read.** It converts through four
   inspector colours on `PlayerCustomizaton`, which lives in the start scene and
   is gone before any world exists. Captured at the menu now and cached.

### Live evidence, 1.1.1

Valheim 1.0.12, Unity 6000.0.75f1, BepInEx 5.4.23.3, Jötunn 2.29.2. Isolated
profile `TCC-HulgiSmoke`, disposable character HULGISMOKE, disposable world
HulgiSmokeWorld. No owner save was used and none was written.

- `Console commands registered: cc_roads, cc_pins, cc_atlas, cc_survey, cc_routes, cc_sync, cc_companion`
- `Read the game's customization palette: hair ramp (1, 0.931, 0.706) to (1, 0.488, 0.279), level 0.1 to 1, skin ramp (1, 1, 1) to (0.3, 0.3, 0.3)`
- `hair -> Long Braid (Hair11) [DisplayName]  MATCHES REFERENCE`
- `beard -> Handlebar (Beard26) [DisplayName]  MATCHES REFERENCE`
- `hair colour -> (0.766, 0.394, 0.234) from the live customization palette (tone 0.94, level 0.74)`
- Compass found by proximity, four-page story read, **Welcome aboard, Hulgi.**,
  `Hulgi has joined your camp. Concerned Cartographer is ready.`
- `pose=SitOnGround state=emote_sit` — the ground idle, on the game's own parameter.
- Spoken dialogue and ambient lines seen on screen.
- Relog: `quest Recruited, access granted (QuestCompleted)`, compass retired,
  actor rebuilt with the same appearance.
- The character-creation screen's three sliders are labelled **Skin Tone**,
  **Hair Tone**, **Blondness**, which settles #298's open question: Blondness is
  `m_hairLevel`.

### Open on 1.1.1, and not claimed

- **#305 — Hulgi has no hair.** Long Braid renders 0.99 m from his head on this
  build, so the fit check removes it with a notice rather than leaving a braid
  floating beside him. The beard is correct. Appearance is therefore *not*
  finished.
- **#306 — furniture seating has not been seen working.** The mechanism is in
  and unit-tested, but no chair fell inside his placement band during testing,
  and a seat built after he settles is not noticed until something else moves
  him.
- **The wardrobe has not been seen on him.** The owner's September 15 note asks
  for a rag tunic and leather pants; both are implemented, unit-tested and in
  this package, and the in-game pass for them was stopped when the owner
  returned to the machine rather than run to a conclusion.

---

## The 1.1.0 candidate (superseded, preserved)

| Item | Value |
|---|---|
| Package | `TheConcernedCat-ConcernedCartographer-1.1.0` |
| Path | `artifacts/thunderstore/TheConcernedCat-ConcernedCartographer-1.1.0.zip` |
| ZIP SHA256 | `686B0CC749E907338086BBB0FD01D77C7BE567742CFC4432537B2293FD0CC49B` |
| ZIP bytes | 872,408 |
| DLL SHA256 | `AE69C3BA735FB1CBB7BA86BEE4B65A8A0C38CD07F3210FF678C4C8176E0A5131` |
| DLL bytes | 655,872 |
| InformationalVersion | `1.1.0+830193bc6edb45f7ef8a33d6bd4c5c6442f96c19` |
| Checksums file | `artifacts/thunderstore/SHA256SUMS-ConcernedCartographer-1.1.0.txt` |

`artifacts/` is git-ignored, so the ZIP lives on disk beside every previous
release and is not in the repository.

### ZIP audit

Six entries, exactly: `manifest.json`, `README.md`, `CHANGELOG.md`, `LICENSE`,
`icon.png` (256×256), `plugins/TheConcernedCat.ConcernedCartographer.dll`.

- Own DLL only — no dependency DLLs, no Jötunn, no BepInEx.
- No `.pdb`, no saves, no profiles, no logs.
- No machine paths. The only `.pdb` string is the relative build path
  `…/obj/Release/net48/TheConcernedCat.ConcernedCartographer.pdb`, byte-identical
  to the one in the shipped 1.0.4 DLL.
- Version agreement across `csproj`, `Plugin.cs`, `thunderstore.toml`,
  `manifest.json` and the `## 1.1.0` CHANGELOG section — enforced by
  `tools/validate_repo.py --expected-version`.
- URLs in the DLL: the GitHub repository, `PRIVACY.md`, and the crash-report
  ingestion DSN. **The DSN is not new** — it is the owner-provided public
  event-ingestion key embedded for #97 and it is byte-present in the shipped
  1.0.3 and 1.0.4 DLLs too. (An earlier read of this suggested it was new to
  1.1.0; that was a UTF-16 alignment artifact in the extraction, corrected by a
  byte-level check.)

### Preserved, verified unchanged

| File | SHA256 | Bytes |
|---|---|---|
| `TheConcernedCat-ConcernedCartographer-1.0.4.zip` | `7D8384D504522FEFE6F57A4EDF257E0A5053227A15933188A39647FAB398631F` | 729,763 |
| `TheConcernedCat-ConcernedTeamster-1.0.4.zip` | `C9E2512F9DA46C051BA24E8B34139CB32432904F1C51C05817AC2891E751F0D6` | 445,261 |
| `TheConcernedCat-ConcernedCartographer-1.0.3.zip` | `E3EC4A968D87D3BE790DF03DF9715F9DF1118D4248A565E7695EE70FA578CB71` | 711,142 |

Concerned Teamster is untouched and stays at 1.0.4. Only Cartographer's version
moved.

---

## Automatic checks — exact commands and outcomes

| Command | Outcome |
|---|---|
| `dotnet test ConcernedCatMods.sln -c Release` | **1023 CC passed / 0 failed**, **647 CT passed / 0 failed** |
| `python ./tools/validate_repo.py` | `Repository validation passed.` |
| `pwsh ./scripts/build.ps1 -Configuration Release` | `Build succeeded.`, 0 errors, no new warnings |
| `pwsh ./scripts/package.ps1 -Product ConcernedCartographer` | `Successfully built TheConcernedCat-ConcernedCartographer-1.1.0` |

Baseline for comparison: 756 CC tests before the companion epic began; 947 after
the shared foundation; 1023 now.

---

## Implemented / automatically tested / live-observed / pending

### Implemented and automatically tested

- Shared companion layer: identity, quest state machine, atomic scope-addressed
  persistence with recovery, unlock policy, placement planner, dialogue rotation.
- Cartographer's product registration, legacy-evidence rule, feature gate,
  story reader, proximity rule, residency rule, appearance plan.
- The Broken Compass object, story panel, companion actor extraction, bed
  validity, placement probing, known-biome reading, `cc_companion`.
- Hulgi's 36-line catalogue, its spoiler gate, and its rotation.

### Live-observed

**Nothing.** No part of this epic has been seen running in Valheim. Every claim
above is a static check, a unit test, or a metadata audit.

### Pending live evidence

Run these in a **disposable profile and world**, on a profile refreshed against
Valheim 1.0.12. The compatibility note warns that `TCC-Dev`, `TCC-Compat`,
`TCC-v1-Smoke` and `TCC-v1-Smoke-RC2` all still log `0.221.12` from August, and
only `TCC-Package` has launched against 1.0.12. **Testing on a stale profile
will mislead.**

| # | What to observe | Consumer |
|---|---|---|
| 1 | The compass is visible and examinable near the spawn anchor | #267 |
| 2 | Story pages read correctly on keyboard and on controller | #267 |
| 3 | Tools-only is reachable without ever meeting Hulgi | #267 |
| 4 | An existing save keeps its atlas, pins, routes and tool access on upgrade | #267 |
| 5 | Hulgi appears, and looks like a person rather than an error | #268 |
| 6 | The real hair/beard preset names (`cc_companion appearance`) | #268 |
| 7 | Whether the strawberry-blond colour vector reads correctly on screen | #268 |
| 8 | Which animator state was used for the idle (`cc_companion status`) | #268 |
| 9 | Whether vanilla hovering reaches the compass and Hulgi at all | #267/#268 |
| 10 | Bed claim → replacement → destruction → fallback behaviour | #268 |
| 11 | An unmodded peer sees nothing | #268 |
| 12 | The real `m_knownBiome` spellings (`cc_companion status`) | #269 |
| 13 | Furniture seating — **detected but deliberately unused** until observed | #268 |
| 14 | Teamster coexistence unaffected | #269 |

`cc_companion status` and `cc_companion appearance` were built specifically to
answer rows 6, 8, 9, 12 and 13 by observation rather than assumption.

---

## First play — exact steps

1. Create a **fresh mod-manager profile** against Valheim 1.0.12. Do not reuse
   `TCC-Dev`, `TCC-Compat` or either `TCC-v1-Smoke*`.
2. Install BepInExPack_Valheim 5.4.2333 and Jötunn 2.29.2, then import a
   package of the version this repository is on, which is **1.2.2**
   (`PluginVersion` in `src/ConcernedCartographer/Plugin.cs`, and `<Version>` in
   the csproj beside it). Not 1.1.0 and not 1.1.1: both are behind the code the
   rest of this document describes. Build the package rather than hunting for a
   file: `pwsh ./scripts/package.ps1 -Product ConcernedCartographer` writes
   `TheConcernedCat-ConcernedCartographer-<version>.zip` into
   `artifacts/thunderstore/`, named from the version it just built, and that is
   the file to import. `artifacts/` is git-ignored, so what is already sitting
   there is whatever this machine last built and is only the right package if
   its version matches.
3. Create a **disposable world and a new character**. Never the owner's saves.
4. Wake up at the start temple. Look around within about ten metres for a small
   object on the ground; walk to it and press the Use key.
5. Read the four pages (arrows page, Enter advances, Escape leaves without
   deciding). On the last page choose **Welcome aboard, Hulgi.**
6. Open the large map. The Atlas, Markers, Routes, Survey and Share buttons
   should now work. Before step 5 they should have refused with a sentence
   naming both ways out.
7. Find Hulgi near the start temple. Press Use on him for a line; do it several
   times and confirm the lines vary and do not repeat immediately.
8. Run `cc_companion status` and `cc_companion appearance` in the console and
   **save the output** — that is the evidence for rows 6, 8, 9, 12 and 13.
9. Claim a bed elsewhere, relog, and confirm Hulgi has moved to it. Destroy the
   bed, return to the area, and confirm he goes back to the start temple.
10. Relog and confirm the tools stay unlocked and Hulgi is still there.

### Negative cases worth five more minutes

- Set `Companions/CompanionsEnabled = false` — the compass and Hulgi should
  disappear and every map tool should stay available.
- Set `Companions/ToolsOnly = true` on a fresh character — tools available
  immediately, no compass.
- Delete the world's `*.companions.tsv` while unlocked, relog — access must
  survive, because the grant was written to the file… which is gone. It survives
  because Cartographer's own road/pin data still counts as prior use.
- Corrupt the `*.companions.tsv` (write junk into it) and relog — expect a
  notice, a `.corrupt` file kept beside it, and **no** loss of access.

---

## Rollback and uninstall

- **Rollback:** re-import `TheConcernedCat-ConcernedCartographer-1.0.4.zip` into
  the profile. 1.0.4 has no companion code; it ignores the
  `*.companions.tsv` sidecar entirely and every atlas feature behaves as before.
  Nothing in the companion sidecar is needed by any other file.
- **Uninstall:** removing the plugin removes every companion object with it.
  Nothing was written to your world save, so a vanilla world opens clean.
- **What is left behind:** `<BepInEx>/data/ConcernedCatMods/ConcernedCartographer/`
  keeps the `*.companions.tsv` sidecars and your ordinary atlas data. Delete the
  `*.companions.tsv` files to reset the introduction; leave them to keep it.
- **Known limitation of rollback:** running 1.0.4 after 1.1.0 and then returning
  to 1.1.0 is safe, but the `Companions` config section written by 1.1.0 stays in
  the config file while 1.0.4 is installed and is simply unused.

---

## Not done, and not claimed

- **No publishing.** No `tcli publish`, no release, no tag, no storefront
  interaction of any kind.
- **No in-game run.** See above.
- The dialogue's biome gate depends on the spellings this build puts in
  `Player.m_knownBiome`. If they do not match, **no** biome line is eligible and
  Hulgi talks about other things — the failure is silent by design, because the
  alternative failure is a spoiler. Row 12 closes it.
- Furniture seating is detected and reported, not used.
- The multiplayer claim ("an unmodded peer sees nothing") is a design argument
  plus the absence of any `ZNetView`, and can only be closed by a two-client
  test.
