# NPC_HANDOFF — Concerned Cartographer 1.1.0 candidate (Hulgi)

Epic #264. Leaves #265, #266, #267, #268, #269.
**Candidate only. Nothing has been published, and no version has been tagged.**

---

## The candidate

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
2. Install BepInExPack_Valheim 5.4.2333 and Jötunn 2.29.2, then import
   `TheConcernedCat-ConcernedCartographer-1.1.0.zip`.
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
- **What is left behind:** `<BepInEx>/config/ConcernedCatMods/ConcernedCartographer/`
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
