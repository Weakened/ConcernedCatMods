# Pre-release smoke test — Concerned Steward (Sunniva, #382 / #340)

The owner's in-game script for the first Concerned Steward candidate. Nothing
here has been run; every row is **OWNER GO-AROUND PENDING** until the owner
records what they saw. Rows marked **BLOCKS** must pass before any upload.
Record PASS / FAIL / note against each numbered row.

## 0. Setup — BLOCKS

1. Disposable world and a dedicated test profile. Never a real save.
2. Install **both** candidate ZIPs named in the lead's handoff: Concerned NPC
   0.4.0 and Concerned Steward. Check the Steward ZIP holds only `manifest.json`,
   `README.md`, `CHANGELOG.md`, `LICENSE`, `icon.png`,
   `plugins/TheConcernedCat.ConcernedSteward.dll` (no Concerned NPC DLL inside).
3. Start once, quit, and edit
   `BepInEx/config/com.theconcernedcat.valheim.concernedsteward.cfg`:
   `StewardRuntimeEnabled = true`, `TendFiresEnabled = false` for now.
   For a quick test of tending, also set `[Upkeep] RefuelBelowSeconds = 100000`
   and `RefuelToSeconds = 100000` so any light that is not full counts as low
   (the shipped 300 s means waiting until a light is nearly out).
4. Load the world. **Expect in `LogOutput.log`:**
   `Concerned Steward 0.1.0 loaded. Release: ConcernedSteward@0.1.0+<sha>` (the
   sha the lead names), `The Steward is registered with Concerned NPC as
   steward/steward.`, and **no** line saying the message display was not
   recognised. If registration is refused, stop: nothing below can work.
5. Negative check (record-and-ship): remove Concerned NPC and start the game.
   Expect BepInEx to report Concerned Steward's missing dependency and the
   Steward not to load; no exception spam. Reinstall it.

## 1. The flint and steel — BLOCKS

1. With an empty world record, pick up resin (chop a pine, or walk over a resin
   drop). **Expect:** the resin enters your inventory exactly as normal, and in
   the middle of the screen: *"A flint and steel are tangled with the resin.
   Runes have been carved into the metal."*, followed in the corner by
   *"Somebody carried these a long way before they ended up in a tree."*
2. **Nothing new in your inventory** besides the resin. (It is a discovery, not
   an item.)
3. Pick up resin several more times. **Nothing is said.**
4. Take resin out of a chest with the chest window open. Nothing is said, before
   or after step 1.
5. Save, quit to the main menu, reload the same world, pick up resin. **Nothing
   is said.** This is the row worth being fussy about: a second flint and steel
   is the defect the quest exists to prevent.
6. Load a *different* disposable world and pick up resin: the discovery happens
   there once (one per world, by design).
7. If it never fires: look for a log line about `[Steward] QuestPickupItem`; the
   resin prefab name `Resin` is assumed, not proven from the assembly.

## 2. The beats and the move-in

1. `cs_steward runes`. **Expect** three lines: the name and rank (Sunniva,
   Flamebearer), the worn steel, the second line cut in the dark.
2. Stand in your base and `cs_steward area 32`. **Expect on screen without being
   asked:** four lines in the corner — she stands at the edge of the firelight,
   priestess with a spear who fell, "Fires go out…", "That one is low."
3. Look at a chest and `cs_steward depot`.
4. Walk **outside** the 32 m area and `cs_steward recruit`. **Expect:** refused
   ("Stand inside your marked settlement…"), nothing agreed, no beat said, no
   body.
5. Walk back inside and `cs_steward recruit` again. **Expect:** her body appears
   next to you, and the settled beat ("set her pack down by the hearth… not going
   anywhere"; "Keep the wood where I can reach it…").
   If the body appears but the settled beat does not, run `cs_steward recruit`
   once more: the beat is only said once the census sees her body, and is never
   said twice.
6. `cs_steward recruit` once more: no second body. `cs_steward status` shows one
   body.
7. Save, quit, reload: she is still there, in the same place, one of her;
   `cs_steward status` shows the quest as "Sunniva lives here."

## 3. Her look — record what you see

1. Describe her body: model, hair, colouring, clothing. **Expected today:** a
   leather tunic and leather pants on the base creature (`Dverger`).
2. **Expected log line:** "The Steward's base creature is not a player-style
   body…". Decompiled 1.0.14 `VisEquipment` draws model, hair, beard and
   colours only on player-style bodies, so on Dverger she will **not** be fair,
   blonde or female-modelled. That is a known gap and an **owner decision**
   (which body she should have), not a pass/fail of this build.
3. If you know a vanilla dress or robe prefab, set `[Appearance] Garment` and
   confirm she wears it instead of the leather.

## 4. One round, one trip to the chest — BLOCKS

1. Put five campfires or standing torches inside the area, light them, and let
   them burn a little (or use the quick thresholds from §0.3). Put plenty of the
   right fuel (wood for campfires, resin for torches) in the marked chest.
2. Set `TendFiresEnabled = true` (restart if you edited the file outside the
   game).
3. **Watch:** she walks to the chest **once**, takes enough for all of the lights
   of one fuel kind, then walks a circuit of them, feeding each. **The failure to
   watch for is her going back to the chest between lights.**
4. With a mix of wood and resin lights: expect one chest visit per fuel kind,
   never one per light; the kind whose light is closest to going out goes first.
5. Lights that are full are never walked to.
6. Count the chest before and after, and the fuel on each light: what left the
   chest equals what went into the lights plus what she brought back. Nothing
   dropped on the ground, nothing appears from nowhere.
7. `cs_steward status` → "Last round: She tended N of N light(s)…".

## 5. Things changing while she walks — BLOCKS

1. While she is walking the circuit, fill one of the not-yet-reached lights
   yourself. **Expect:** she arrives, does not feed it, carries on; status later
   says one "needed nothing by the time she got there"; she brings that share
   back.
2. Destroy (hammer) a not-yet-reached light mid-circuit. Same: skipped, share
   returned, nothing lost.
3. If you have a second character or a ward you do not own: a ward over a light
   mid-circuit. **Expect:** skipped, the round is **not** called complete, status
   says "she may not touch", and on screen: *"Sunniva: "One light here is burning
   low, and they are not mine to touch…""*.

## 6. No free fuel — BLOCKS

1. Empty the chest of resin, keep wood in it, and have several low torches.
   **Expect on screen:** *"Sunniva: "I need <n> Resin. <k> lights are burning
   down, and the chest I may take from does not hold enough.""* She does not
   walk to the chest for it. It is not repeated more than once every five
   minutes while it stays true.
2. Put in some but not enough resin: she tends the most urgent torches she can
   fill completely, says what she still needs, and does not loop back and forth.

## 7. Interruption, reload, custody — BLOCKS

1. While she carries fuel mid-circuit, set `TendFiresEnabled = false` (or open a
   second session/peer): she stops, keeps what she carries. Turn it back on: she
   returns the carried fuel to the chest before anything new.
2. While she carries fuel, save and quit; reload. **Expect:** nothing replayed,
   no light fed twice; she puts back exactly what she was carrying, then plans
   a fresh round for what is still low.
3. Make the chest full while she carries surplus: she keeps holding it (never
   drops it), says why, and puts it back once there is room.
4. Kill her while she carries fuel (if you are willing): items drop where she
   fell, she stops, the screen says she stopped and needs you; `cs_steward
   resolve` after you have looked lets her work again. Nothing is recreated.

## 8. Settings

1. `[Steward] SpeakOnScreen = false`: nothing on screen, everything still in the
   log.
2. `RefuelToSeconds` lower than `RefuelBelowSeconds`: one log line refusing it;
   shipped thresholds apply.

## 9. Uninstall (record-and-ship)

Remove Concerned Steward only and load the world: no error loop. Vanilla deletes
her saved body as an unknown prefab, and with it anything she was carrying; her
world record stays on disk under the mod's data folder. Recommend players run a
final round (empty hands) before uninstalling.
