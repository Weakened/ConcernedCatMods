# Original climbing motion source — procedural authoring

Status: source-only internal prototype; live-rig fitting and final animation acceptance pending.

This is original authored contact/body motion. No Meshy purchase, stock exports, game clip/rig extraction or dependency binaries are used. The owner reference plans informed contact order; their numbered phases are not a60FPS clip. Native walking remains only Claude's contingency and is not this motion source.

## Source and reproduction

- `Domain/Animation/OriginalContactTrack.cs`: diagonal support, signed completed-travel strokes, anatomical hold settling, reversal and extension continuity. Two-rung endpoint travel occurs over one rung of completed body travel. Constant travel follows the paired reference order; interrupted strokes use actual support/body position rather than restarting phase0.
- `Domain/Animation/OriginalRungSurface.cs`: real visible front/rear surface coordinates and actor-relative lateral basis.
- `Runtime/Animation/OriginalRigBinding.cs`: per-player measured limb chains, anatomical palm/sole landmarks and controller/avatar identity. Mesh contact offsets and normals are initial fitting values, not measured successful contacts.
- `Runtime/Animation/OriginalClimbPresenter.Pose.cs`: original pelvis lean/support sway, limb clearance, bends, mount/catch/slide/exit choreography and native action handoff.
- `Runtime/Animation/NativeIKLease.cs` and `OriginalIKCleanup.cs`: scoped input ownership/restoration.

Reproduce game-free policy verification from the repository with `dotnet test src/ConcernedLadders.Tests/ConcernedLadders.Tests.csproj`; the test project links the production Domain source directly. It does not reference the game/Unity or compile the presenter. Run `python tools/validate_repo.py`. Claude alone runs the controlled licensed build and imports the resulting package.

The procedural functions are the editable original sources; no export step or proprietary animation asset is required. Evaluate them in the actual humanoid Fixed IK pass. Do not quantize targets to60 reference rows or advance phase from requested speed/frame time. Keep root movement and collision in Claude's motor.

## Why this route is the available first prototype

Read-only tooling checks found no Unity Editor/Hub in PATH or the checked default installation directories. Blender Foundation directories2.93/5.1 exist, but no blender executable was found there. This is a bounded inventory, not proof no authoring tools exist anywhere. No compatible local humanoid-clip import/asset-bundle pipeline has been established.

Unity documents runtime SetCurve support for legacy clips, with non-legacy clip editing restricted to the editor: [Unity6 AnimationClip.SetCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationClip.SetCurve.html). The observed native rig is a valid non-legacy humanoid with an existing IK callback. Original continuous contact/body authoring in that scoped per-player pass therefore gives a reproducible prototype using available tooling. This does not claim custom clips are impossible or rule out later original clip baking.

## Fitting procedure

First demonstrate upward travel, a stable all-contact hold and departure restoring ordinary movement. Use a connected run long enough to inspect an interior loop, a clear side/front view and the installed player rig; no replacement mannequin is acceptance evidence. Compare target vs actual palm/sole positions and all four limbs plus pelvis/torso. Record normal and fast speeds actually completed by the motor.

Measure/fit palm normal, contact-to-wrist offset, sole thickness/forefoot offset, lateral span, lean, pelvis support and hints. Current values are a prototype hypothesis. A reach warning yields that limb to native and is a failed fit to correct, never an accepted hidden substitute. Test rear independently, without negative skeleton scale.

Then compare paired and deliberate candidates at actual normal/fast travel rates and record the chosen final gait. The current source implements the paired candidate; the deliberate actual-rig comparison is outstanding. Source samples alone do not establish fluidity. Do not slow the motor to hide excessive cadence.

Finish up/down/hold/reverse/fast/slide, ground mount, air catch, supported top/side exit, immediate Jump/Drop, native hammer and equipment, lifecycle, scaffold refresh and real observers. The first fitted loop is only an internal checkpoint. Finger articulation currently remains native; original fitted grip closure is an explicit remaining presentation task.
