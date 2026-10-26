# Climbing animation — editable source and guide

Status: 0.2.6 internal owner candidate. Fitted and checked in game (hold, climb up/down, stop/reverse, fast, both
faces, mount, catch, Jump, Drop, top exit, building with the hammer). Owner acceptance pending.

The motion is original. No purchased, stock or extracted game animation, rig or clip is used.

## What to edit

| What | Where |
|---|---|
| Gait: per-limb timing, stride, swing ease, hand/foot arcs, torso dwell, posture, elbow/knee bend | **`LadderGait.blend`** (Blender 5.1) |
| Values measured against the game rig (grip/sole offsets, rails, finish, mount/catch timing, reach limit) | `Runtime/Animation/ClimbTuning.cs` |
| How the values become IK goals each frame | `Runtime/Animation/OriginalClimbPresenter.Pose.cs` |
| The gait model (pure, unit-tested) | `Domain/Animation/LimbGait.cs` |

Generated from the .blend (do not edit by hand): `ladder_gait.json`, `ladder_gait_phases.csv`,
`Domain/Animation/GeneratedGait.cs`. The plugin logs the live source at startup:
`Climbing gait source LadderGait.blend <sha12> (Blender 5.1.2)`.

## The scene

All values are metres, degrees or fractions of a climbing cycle. Curves are keyed on frames 0–100, which always mean
0–1 of the curve's input; scrub the timeline or use the Graph Editor.

- **GAIT** (custom properties): `stride_rungs` (rungs per limb move and per body cycle; 3 = 1 m), `foot_low_rel`
  (rung height under a foot as it leaves, above the feet), `hand_mid_from_shoulder` (middle of a hand's grip range),
  `dwell_slow` (0–0.9: how much the torso slows while a foot recovers).
- **LH, RH, LF, RF**: `start` (when in the cycle the limb lets go) and `length` (how long it is off the rung). Windows
  may overlap; the default follows the mannequin loop: RH 0.00, LF 0.20, LH 0.50, RF 0.70.
- **SWING**: property `progress` keyed 0→1 — the ease of a reach (leave slowly, travel, settle).
- **ARC_HAND / ARC_FOOT**: location Y = clearance away from the rungs, Z = clearance upward, keyed over the swing.
  The foot arc draws back first (knee-led recovery), then rises.
- **BODY**: `pelvis_drop`, `pelvis_toward` (how close the hips hang to the rungs), `lean`, `slide_lean`, `sway_tilt`,
  `sway_shift`.
- **HINTS**: elbow and knee bend directions: `elbow_out/side/down`, `knee_toward/side/down`.

## Edit → game

```powershell
# 1. Edit in Blender and save (or set properties from the command line):
pwsh tools/animation/gait/regenerate.ps1 HINTS.knee_side=-0.03 GAIT.dwell_slow=0.5
#    (with no arguments it only exports the saved scene and regenerates the C#)
# 2. Gate, package, import through the manager, cold start:
pwsh scripts/verify.ps1
pwsh scripts/package.ps1 -Product ConcernedLadders
python tools/animation/gait/compile_gait.py --check   # fails if GeneratedGait.cs is stale
```

`build_gait_blend.py` recreates the scene with the defaults and **overwrites manual edits**; use it only to start over.
The round trip was demonstrated on 2026-10-26 (foot arc, hand timing, dwell and knee bend edited in the .blend, then
measured in the installed game): `artifacts/ladders-0.2.6-dev/BLENDER_ROUND_TRIP_RECEIPT.md`.

## Frame/phase reference

`ladder_gait_phases.csv` lists, per authoring frame of a cycle (60 rows): phase, body travel, torso dwell offset and,
for each limb, its state (planted / release / reach / approach), swing progress, rungs climbed and arc. These are the
authored TARGETS. Post-solver positions come from the in-game per-frame trace and the `Climb fit` log lines (per limb:
passes, unreachable skips, worst/mean fraction of the chain, solve error); a fitted motion has `skip=0`.

## Reference choices (2026-10-26)

- Mannequin loop (ScreenRecording 17-00-36): limb order and staggered hand/foot timing, knee-led foot recovery.
- Real climbers (IMG_1944–1947): quieter torso during foot recovery, the larger rise as the leg extends (`dwell_slow`).
- Mannequin start (17-00-06): staggered contact entry on mount (`ClimbTuning.MountEntry*`).
- Game comparison (16-59-05) and playground (17-01-40): the top exit hands the step to the game's own walk; a deep
  mantle crouch is not used for an ordinary floor.

Cadence: one cycle = `stride_rungs` × 1/3 m of travel. At 0.9 m/s (owner decision) that is 1.11 s; with Run (1.2 m/s)
0.83 s. Climbing speed is gameplay and lives in `Domain/ClimbPolicy.cs`.

## Ownership rules

Movement, collision, stamina, skill and exits belong to the runtime; presentation only writes IK goals, hints and the
body pose inside the player's own IK pass and restores each owned field it still holds. With the hammer out only the
right arm yields to the game's hammer animation. An interact yields both arms, the upper body and the head.

## Known limits

- Fingers keep the native hand pose (no fitted finger curl).
- Other players see the native pose (no observer transport yet).
- The very first frames of a ground mount can be out of reach while contacts fade in (counted, never stretched).
