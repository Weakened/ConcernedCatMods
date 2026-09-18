# Companion appearance reference

**What this is.** The owner's character references for Hulgi, Thorstein and
Gunnar, transcribed from three Valheim *New Character* screenshots supplied on
2026-09-15. #297 and #298 both treat this table as a contract that supersedes
every earlier hair and beard description, so it lives in the repository rather
than in a handoff directory that one `rm -rf` would take with it. It was copied
here verbatim from
`C:/code/concernedcat-handoffs/2026-09-15-gunnar-companion/APPEARANCE_REFERENCE.md`
by the #298 sweep; nothing below was rewritten.

**What is applied, and what is not** (`main`, 2026-09-18):

| Character | Product | State |
|---|---|---|
| Hulgi | Concerned Cartographer | **Applied.** `AppearancePlan` resolves `Long Braid` and `Handlebar` through the installed game's own localization, not a hardcoded prefab id |
| Thorstein | Concerned Foreman | **Not applied**, and not currently possible: his body is a worker clone of `Dverger`, which has no hair or beard slot |
| Gunnar | Concerned Teamster | **Not applied**, same reason |

Thorstein and Gunnar have no presentation body. Giving them one is the owner
decision written up on #295, and this table is what that decision unblocks.

---

# CC-NPC-006: owner screenshot appearance references - Hulgi, Thorstein, Gunnar

Eren supplied three Valheim New Character screenshots in the September 15 Gunnar message. Readable UI labels and name fields identify each; these are OWNER-DESIGNED character references, not observations of our NPC implementation. New screenshots supersede earlier generic hair/beard prose. Do not keep Thorstein's old 'short beard' or invent mutton chops for Hulgi when the selected presets say otherwise.

| Character | Exact Hair UI label | Exact Beard UI label | Visual direction |
| --- | --- | --- | --- |
| Hulgi | Long Braid | Handlebar | Lighter skin than Thorstein; warm light/blond hair direction, retain strawberry-blond intent while matching the reference |
| Thorstein | Mullet | Mustache | Deeper/tanned complexion, substantially less blond than Hulgi; this REPLACES the earlier short-hair/short-beard placeholder |
| Gunnar | Short Curls | Stonedweller | Substantial full beard, light/gray-brown hair direction in the screenshot; do not reinterpret him as another short-bearded Thorstein |

Approximate normalized SLIDER POSITIONS visually read from the screenshots (not exact game colors, not measured runtime values): Hulgi skin ~0.50, hair tone ~0.94, blondness ~0.74; Thorstein skin ~0.68, hair tone ~0.94, blondness ~0.22; Gunnar skin ~0.52, hair tone ~0.05, blondness ~0.49. These are starting points only, and left/right meanings must come from the game's actual customization conversion, not assumed RGB. Strong campfire/sunset lighting is not a skin/hair color swatch. Compare against the same reference scene AND neutral daylight before claiming a match.
No saved numeric customization data was supplied, and no personal character files were read to derive it. Do not read/modify the owner's real character/save to discover slider values. Visual match can be owner-checked in a disposable character/world.

## Source provenance
The original conversation images are mounted in the assistant's container, NOT BLD. Do not claim they were copied onto BLD; the text/table above is a faithful visible-UI transcription. Filenames/hashes let a future attachment transfer identify the originals without guessing:
- Hulgi: fdab49b8-0f64-47db-a860-d11d518d3efa.png, 2048x1152, SHA256 1075375c086b7108af85b3809d4c9b5eca370562519ea860f975008ebf6cf4b9.
- Thorstein: 1309fc25-95b3-404e-adcb-02c7cd2456cf.png, 2048x1152, SHA256 981a6fe86c398624b51c7424992313109ac51941bc739a78a9b907916a3ca105.
- Gunnar: e6264b89-3ee4-4bc0-86f5-c9211e9220f6.png, 2048x1152, SHA256 ef8fd112949d8654bf68918b828b8002bebf75f57a013a8ab6d14cd02c2f8577.

## Implementation / verification
Use verified stock customization assets at runtime; retain capability/fallback behavior and never create a live Player/AI merely to strip it for personal companions. Jotunn's generated 1.0.7 item list maps Long Braid=Hair11, Handlebar=Beard26, Mullet=Hair32, Mustache=Beard22, Short Curls=Hair23, Stonedweller=Beard16. These are PRIMARY-DATA lookup candidates from https://valheim-modding.github.io/Jotunn/data/objects/item-list.html and https://valheim-modding.github.io/Jotunn/data/localization/translations/English.html ; revalidate actual installed ObjectDB/localization/preset availability before hardcoding runtime defaults. The source is 1.0.7, not proof for the installed game.
Map UI slider settings through the actual game's color conversion; don't directly store the approximate normalized slider tuple as an RGB vector. Document verified preset IDs, safe fallback and approximate tuning separately. Preserve already recruited identities/progress while updating stock default appearance, and do not overwrite an explicitly user-customized appearance. Appearance must not change quest/tool/cart readiness or feature access.
Capture each NPC front/side/neutral lighting in a disposable test setup; compare correct hair silhouette, beard/mustache and relative colors. The user references are not final in-game acceptance captures. Missing required stock preset gets an honest fallback notice/acceptance row, not silently claimed matching. Clothes are not locked by these creator screenshots; no costume/gear assignment was explicitly specified. Keep clothing/equipment safe and modest in actual NPC presentation.
Use each product's own issue/integration boundaries, shared definitions only without compile-time product dependencies. No source edits in parallel with the sole active worker. No DLL/asset redistribution, old-package overwrite or publication. Leave #280 and Evergreen untouched.
