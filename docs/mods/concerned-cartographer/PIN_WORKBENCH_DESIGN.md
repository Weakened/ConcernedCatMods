# Pin Workbench design (CC-008)

Approved design for the v0.3 marker-management layer. No production UI code
belongs to this document's issue; implementation is split into CC-019
through CC-024.

## Competitive overlap and differentiation

| Capability | Pinnacle | PinAssistant | Concerned Cartographer |
|---|---|---|---|
| Edit pin name/icon in place | Yes | Yes | Yes (plus stable IDs and revisions) |
| Search/filter pins | Yes | Yes | v0.4 (query language over rich metadata) |
| Pin colors | Yes (rendered) | Yes | Stored metadata in v0.3; rendering deferred |
| Auto-pin from world objects | No | Yes | v0.4 quick context pins (opt-in) |
| Tags/notes/status/scope metadata | No | Partial | Yes, first-class and versioned |
| Durable identity, journal, tombstones | No | No | Yes (collaboration-ready) |
| Road atlas integration | No | No | Yes (the differentiator) |

The workbench does not try to out-feature Pinnacle on rendering flair. Its
value is durable, versioned, collaboration-ready metadata attached to pins
that also participate in the road atlas, with strict interop safety.

## Pin ownership model

Every pin visible on the map belongs to exactly one class:

1. **Managed** — created through Concerned Cartographer. Authoritative
   record lives in the atlas store; the vanilla `PinData` on the map is a
   rendering of it (`m_save = true`, owner 0).
2. **Adopted vanilla** — a former vanilla pin the player explicitly
   adopted. Same authority as managed; `Source = AdoptedVanilla` preserved.
3. **Unadopted vanilla** — the player's ordinary saved pins
   (`m_save = true`, `m_ownerID = 0`, not tracked by us). Read-only to us
   except through explicit adoption.
4. **System/foreign** — everything else: death/bed/shout/boss/player/event
   pins, server shared-map pins (`m_ownerID != 0`), pins with a foreign
   author, and pins other mods manage. **Never adopted, never edited,
   never deleted by any Concerned Cartographer operation, batch ops
   included.**

Rules:

- Adoption is explicit (single pin or reviewed batch), preserves exact
  position, name, icon, and checked state, never duplicates the pin, and is
  cancellable with the source pin untouched.
- Managed pins render as ordinary saved vanilla pins. **Downgrade/uninstall
  safety falls out of this:** removing the mod leaves every managed pin on
  the map as a plain vanilla pin with its name/icon/position/checked state;
  only the extra metadata stops being visible.
- Re-linking after restart matches stored managed pins to saved vanilla
  `PinData` by position (0.5 m) plus name; unmatched store entries re-add
  their pin; unmatched vanilla pins stay untouched. No duplicates by
  construction.
- Visible labels stay clean: metadata is never encoded into pin names.

## Editing model

- Every edit mutates the stored entity in place: stable `cc:pin:<guid>` ID,
  monotonic revision bump, modified timestamp. Delete is a durable
  tombstone; restore undeletes.
- Fields: name, icon (namespaced registry ID), category, color, display
  size, notes, tags, status, checked, scope intent (private/table/server —
  intent only until v0.6 sync), plus read-only owner/source, coordinates,
  created/modified times.
- Cancel restores the pre-edit buffer; nothing touches the store until
  Apply.

## Interaction design

Two equally capable front ends drive one shared controller
(`PinWorkbenchController`, pure and unit-tested):

1. **Map panel** (keyboard/mouse): with the large map open, a hotkey
   (default `P`, rebindable) opens the workbench focused on the pin nearest
   the cursor. Valheim-styled wood panel, fields top-to-bottom (name, icon
   picker, category, color, size, status, checked, tags, notes, scope),
   Apply/Cancel/Delete buttons, foreign/read-only banner when applicable.
   The panel anchors to the screen edge so vanilla map controls (pin bar,
   ping, cartography toggles) stay visible.
2. **Console** (`cc_pins …`): every operation scriptable — the automation
   test surface, the controller-fallback until CC-045, and the NoMap
   bridge later.

Controller readiness (full pass in CC-045/v0.7): the panel is built as a
linear focus chain (top-to-bottom field order, wrap-around), all
interactions are button/toggle/increment based (no drag), and the shared
controller exposes discrete commands so bindings can drive it without UI
rework.

## Implementation split

- CC-019 (#28): pure Atlas Core entities and store.
- CC-020 (#29): IDs, revisions, snapshot+journal persistence, backups,
  migrations.
- CC-021 (#30): adoption + ownership enforcement adapter.
- CC-022 (#31): workbench UI + shared controller.
- CC-023 (#32): operations, batch tools, duplicate merge, undo/restore.
- CC-024 (#33): icon registry, managed renderer, v0.3 RC.
