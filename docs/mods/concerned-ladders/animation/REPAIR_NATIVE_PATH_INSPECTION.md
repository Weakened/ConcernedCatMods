# Installed native repair animation path inspection — 2026-10-26

**Read-only source inspection; repair was not invoked in game by this lane.** This substantiates use of the recorded place/remove hammer action as the representative native repair motion. It does not pass repair gameplay, controls, equipment, stamina or attached-animation tests.

## Reproduction and identity

- Actual installed assembly: `C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll`.
- SHA256: `94998885b103d6e5f5480123eaee0a093e82af2ddeeee37536c15bf555f24f3a`. No assembly or decompiled game source is committed, copied to a package or uploaded.
- Local inspection tool: `C:\Users\erenc\.dotnet\tools\ilspycmd.exe`, version11.0.0.9375.
- Reproduce read-only: `ilspycmd -t Player <installed assembly_valheim.dll>`; inspect `Repair(ItemDrop.ItemData, Piece)`, `RemovePiece()` and `PlacePiece(Piece, Vector3, Quaternion, bool, bool)` by method identity. Line numbers can vary with decompiler version.
- Ignored local inspection output: `artifacts/native-baseline-analysis/Player.inspection.cs`. In this output, tool-trigger expressions are at3018 (remove),3114 (place),3736 (repair); successful repair branch is3716..3750.

## Observed control flow, summarized

Repair first requires place mode, a hovered piece, removal/access checks and successful `WearNTear.Repair()`. Only that successful branch faces the look direction and calls `m_zanim.SetTrigger` with the passed tool item's `m_shared.m_attack.m_attackAnimation`. An undamaged piece reaches the no-repair message and does not play the attack. The passed `repairPiece` parameter does not establish a successful repair by itself.

Place with its attack option enabled, and successful remove with a right-hand item, face the look direction and use that right-hand item's same attack-animation field. Thus, for the same equipped hammer and native controller context, repair selects the same tool animation trigger path represented by the observed `hammer_Swing` place/remove captures. The expression is data-driven; this inspection does not invent a separate literal repair clip/trigger.

## Inference and unavailable checks

The source path supports representative-motion equivalence, independently of Claude's approximate line references. It does not demonstrate a repair input, a genuinely damaged target, observed trigger payload, or exact playback while attached. Those remain real-game tests on the integrated candidate. Other equipped tools/context may select different native animation data.

Both arms and torso can move in the captured representative. Right-hand hammer ownership does not authorize pinning the left arm during its action. The conservative presentation contract releases both arms/upper body until a narrower exact-candidate mask is validated.
