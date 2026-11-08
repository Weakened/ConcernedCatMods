# Hulgi / Broken Compass: owner test checklist

Internal Concerned Cartographer 1.4.0. #264 remains open until the gameplay acceptance below is observed. This package includes current main-branch features, including Work Areas; it is not a narrow hotfix for published 1.3.x. No game was launched or profile deployed by this task.

## Install and first encounter

Import this ZIP through your mod manager into a separate test profile with the manifest's BepInEx/Jotunn dependencies. Disable any other Cartographer copy. Use a disposable character/world, never your real saves. Preserve your existing profile and data. Record the startup release/source version and check for errors. Installed-game compatibility on your profile remains unverified.

For a genuinely fresh story-mode test, leave CompanionsEnabled on and ToolsOnly off; use new test character/world identifiers. Existing players intentionally retain access and may therefore not exercise the fresh-user gate.

1. Find the recognizable Broken Compass near your respawn anchor. Confirm hovering shows the actual Use binding, including a remapped binding.
2. Examine it. The opening press must leave page one visible. Read all four pages: one Return/arrow/Next/Submit per page. Test Return while Back or Close has focus. The final-page transition must not also select Welcome or Tools-only.
3. Deliberately choose Welcome. Confirm one recruitment, compass retirement after a successful save, and usable map tools.
4. Repeat using controller navigation/Submit/Cancel and mouse. Native controller Cancel routing is implemented but has not been observed in game.
5. Close partway through and re-examine. Replay through map Settings after completion: pages are readable without completion choices or progress changes.

## Required negative and lifecycle checks — ALL UNVERIFIED ON THIS ZIP

- Settings is reachable before meeting Hulgi. Tools-only immediately grants access; turning it off never revokes access. Visibility toggles do not revoke tools or progress.
- Escape, Close and native controller Cancel release input. Death, teleport, player attack, logout and world changes close the story without unintended completion. Incoming danger/loading transitions also need observation; no full combat detector is claimed.
- Repeated/held Use, repeated Submit, simultaneous Escape/Submit, close/reopen and repeated interactions do not duplicate introductions, recruitment or grants.
- Relog preserves recruitment, story preference, compass retirement and existing roads/pins/routes. Test character/world isolation with two disposable characters/worlds.
- Existing-user migration preserves tool access and saved data. In test sidecars only, exercise corrupt/unwritable/newer-version recovery: clear notice, retained original data, no surprise lockout or invented completion.
- Bed claim/replacement/destruction/default-spawn fallback moves residence appropriately; portals/death positions do not become homes. No suitable placement defers safely rather than blocking paths or putting Hulgi in water/fire/geometry.

## Appearance, idle and compatibility — ALL UNVERIFIED ON THIS ZIP

- Hulgi matches the approved Long Braid / Handlebar appearance, colour and clothing; no floating/missing hair, skin or equipment.
- Ground sitting, usable seating, newly built furniture, campfire/roof/bed routines and yielding to occupied seats work without clipping, blocking or claiming furniture.
- Talk gives one varied line without raising the player's arm; hover prompts resolve bindings. Verify dialogue repetition suppression, progression spoiler limits and ambient chatter preferences.
- Companion hide/disable, death, logout and world changes clean up visuals and UI; ordinary controls/map/UI work afterward.
- A real unmodded peer sees no local-only companion; Teamster coexistence and Pinnacle/MapRoutes compatibility remain unverified. No networked NPC or shared-world worker authority is added.
- Smoke-test ordinary atlas, markers, routes, backup/restore and the included unreleased main features.

Report source/version, game/dependency versions, profile, steps, expected/actual behavior, log excerpt and game-only screenshots/video. Automated policy tests do not prove appearance, controller input, seating, persistence under engine failures or complete #264 acceptance.
