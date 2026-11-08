# Gunnar dialogue (#459)

Status: **code-complete, never observed in game.** In-game rows are OWNER
GO-AROUND PENDING.

## What a player sees

Gunnar's figure at home (#458) shows the game's own hover prompt, `[Use] Talk`,
with his display name. Pressing the use key on him is the **only** way he
speaks: there is no timer, no update-loop speech and no ambient line. One line
appears top-left, as `Gunnar: "..."`, and in the log.

- While the introduction is open (`IntroPending`) he says the objective: *Bring
  Gunnar a usable cart*.
- Once recruited he says the next line of a rotation of 32 authored remarks
  (camp, travel, load, weather, and two of #297's voice seeds), with an
  eight-line repetition window, through the shared
  `DialogueCatalog`/`DialogueRotation`. A fresh rotation starts per character
  and world, at an offset of its own (`GunnarTalk.StartFor`), so two characters
  do not hear the same order and one save's recent lines never suppress
  another's.
- If the haul service still reports that a haul needs attention when you talk
  to him, he says *We need to stop:* followed by the **same catalog row the haul
  panel shows for that reason** (`HaulPanelKeys.ForReason`); `GunnarTalkTests`
  walks every `HaulAttentionReason`. **This is narrow on purpose and stated:**
  he can only be talked to at home, and he is at home only while he has no
  worker body and no job holds him. During a running haul he is his worker
  body, which has no talk point, so the attention line is heard only in the
  cases where the service keeps a reason with no body (for example after his
  body was lost). The haul panel stays the place to read a running haul's
  reason. Giving the worker body a talk point is not part of this issue.
- A skipped story or an unmet character gets no line.

The buckle's introduction and the recruitment panel's completion line use the
same rows (`gunnar.intro`, `gunnar.complete`).

## Localization

Every Gunnar row - the 36 dialogue and introduction rows, the hover verb
(`gunnar.talk.verb`), the line format (`gunnar.says`, `{0}: "{1}"`) and the 37
recruitment-panel rows - is registered in `Domain/Localization/TeamsterStrings.cs`,
the one English catalog. So the translator template written at start-up offers
them, a `teamster-strings.tsv` row overrides any of them (placeholders checked as
for every other key), and a missing translation falls back to English.
`GunnarDialogueStrings` and `GunnarRecruitmentStrings` hold no text of their own
any more; they are views of the catalog's `gunnar.*` rows and resolvers over it.
The CT-032 audits apply unchanged: every key is referenced by shipped code, and
no catalog sentence is re-hardcoded anywhere else.

His name on the hover prompt and in the line is `Companions/GunnarDisplayName`
(default `Gunnar`), the player's own setting; it is not a catalog row, so a
translator file does not rename him.

## What he never says

No invented load limits or numbers, no biome or boss spoilers, no lore claims,
and nothing inferred from skin or hair. The Hulgi and Thorstein lines and the
"metal out of the forest" hint are **ineligible and absent**: no read-only,
local signal that *this character* met those companions, or found that metal,
exists in the current capability contracts. A plugin being installed does not
prove a meeting, and global boss keys or another player's discoveries are not
this player's evidence. Teamster takes no compile reference to another product.

## Automated coverage

- `GunnarTalkTests`: catalog size and uniqueness, rotation with recency
  suppression reaching every line, every attention reason against the panel's
  key, objective and tools-only behaviour, translation use, withheld hints.
- `GunnarDialogueCatalogTests`: every line he can say is a catalog row; the
  translator template offers them; a translator file changes what he says and
  clearing it restores English; a translation that drops a placeholder is
  refused; the two Gunnar string files hold no English; the figure speaks only
  through its talk callback, which is the only caller of the rotation and of
  the message screen; the rotation is per character and world, at a stable
  offset, and forgotten with the world.
- `HardcodedStringAuditTests` (existing): liveness and no re-hardcoding.

## OWNER GO-AROUND PENDING

- D1 the hover prompt and a line on the use key, visitor and recruited.
- D2 at home, after a haul ended needing attention with no body (for example
  his body lost), the attention line names the panel's reason.
- D3 repetition feel; no ambient speech across a relog.
- D4 a translator file's override shows in game.
