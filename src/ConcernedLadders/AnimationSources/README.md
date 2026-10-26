# Original climbing motion — editable source

Status: fitted in game for the 0.2.5 internal owner candidate (hold, interior climb up/down, fast,
both faces, Jump, Drop, top finish, building with the hammer while attached). Owner acceptance pending.

This motion is original and procedural. No purchased, stock or extracted game animation, rig or clip is
used. The editable source is code: change a value, rebuild with `scripts/verify.ps1`, import the package,
and the next attachment uses it.

## Where everything lives

| What | File |
|---|---|
| Gait: which limbs move together, stride, swing timing, stop settling | `Domain/Animation/SameSideGait.cs` (`GaitShape` holds the values) |
| Every pose value (pelvis, lean, grip, sole, rails, bends, finish) | `Runtime/Animation/ClimbTuning.cs` |
| How values become hand/foot/body goals each IK pass | `Runtime/Animation/OriginalClimbPresenter.Pose.cs` |
| Rung surface coordinates on both faces | `Domain/Animation/OriginalRungSurface.cs`, `Domain/ClimbPresentation.cs` (`RungShape`) |
| Per-player rig measurements (chain lengths, pelvis/shoulder) | `Runtime/Animation/OriginalRigBinding.cs` |
| Scoped IK ownership and restore | `Runtime/Animation/NativeIKLease.cs`, `OriginalIKCleanup.cs` |

## The gait (owner reference 2026-10-26)

The right hand and right foot move up together, then the left hand and left foot half a cycle later,
with overlapping timing (`HandSwing`/`FootSwing` above 0.5). Contacts are a pure function of the body's
height on the run: the same height gives the same pose, climbing down retraces climbing up, nothing
drifts and frame rate does not matter. Every planted contact is a real rung. A stop settles each moving
limb onto the nearest rung; resuming rejoins the gait over `RejoinSeconds`.

**Cadence.** Limb cadence = body speed ÷ (`StrideRungs` × pitch). The climbing speeds are gameplay
(1.8 m/s, fast 2.43 m/s) and are not changed by animation. With pitch 1/3 m:

| StrideRungs | Each limb moves | Cycle at 1.8 m/s | Cycle at 2.43 m/s | Reach (measured in game) |
|---|---|---|---|---|
| 2 (0.2.3) | 0.67 m | 0.370 s | 0.274 s | fits |
| **4 (0.2.5)** | 1.33 m | **0.741 s** | **0.549 s** | fits: worst 0.89–0.95 of chain, 0 skips in climb/fast/hold |

The stride must be even so the two sides alternate evenly on real rungs. A slower-looking climb than
0.741 s per cycle is not possible at 1.8 m/s within arm and leg reach; the alternative is a lower
climbing speed, which is a gameplay decision for the owner.

## Adjusting, step by step

1. Change values in `ClimbTuning.cs` / `GaitShape`. Keep `ClimbTuning.ReachLimit` at 1.01: a contact
   needing more of its chain is skipped and counted, never stretched.
2. Rebuild and import (`scripts/verify.ps1`, `scripts/package.ps1`, manager import).
3. Climb in game and read the `Climb fit` lines in `BepInEx/LogOutput.log`. One line per motion (and every
   3 s of a long one) gives, per limb: passes, skips, worst/mean fraction of the measured chain needed,
   where the posed shoulder/hip sat (`up` above the feet, `out` from the rung plane) and the solve error
   (`err`, metres between the solved hand/foot and the goal; about one physics step of body travel while
   moving, 0 when holding). A fitted motion has `skip=0` and `worst` under 0.96.
4. Record normal-speed footage; frame sheets are diagnostics only.

Conventions measured in game (do not re-derive from bones): humanoid IK goals are in the avatar's
normalized space. A foot goal looks along the toes with up as up. A hand goal's forward runs along the
fingers and its up is the back of the hand. The game's own foot IK runs in every animator layer's IK pass;
the presenter re-asserts its goals after it in later layers.

## Ownership rules kept by this source

Movement, collision, stamina, skill and exits belong to the runtime; presentation only writes IK goals,
hints and the body pose inside the player's own IK pass, and restores each owned field it still holds.
With the hammer out, only the right arm yields to the game's own hammer animation. An interact (door,
hatch) yields both arms, the upper body and the head. A top or side exit hands the limbs to the game's
own walk early; the runtime drives that walk from the real exit path speed.

## Known limits

- The first few frames of a mount from the ground can exceed reach while contacts fade in.
- At the very top of a run with no landing, the motor lets the feet reach the top; the hands then have
  nothing within reach and yield to the native pose (a gameplay limit, unchanged).
- Fingers keep the native hand pose; there is no fitted finger curl.
- Other players see the native pose (no observer transport yet).
