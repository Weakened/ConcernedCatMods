"""Round-trip demonstration edit (addendum section 7). Applies, to the SAVED scene, the kind of edit a person makes in
the Graph Editor and properties panel, then saves:

  * foot arc: every ARC_FOOT location.y key doubled (feet draw back twice as far)
  * hand timing: RH start +0.08 of a cycle
  * body dwell: GAIT dwell_slow 0.55 -> 0.25
  * bend: HINTS knee_side 0.10 -> 0.25

    blender --background src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/demo_edit.py

Restore afterwards with `git checkout -- src/ConcernedLadders/AnimationSources src/ConcernedLadders/Domain/Animation/GeneratedGait.cs`.
"""
import bpy

scene = bpy.context.scene
foot = bpy.data.objects['ARC_FOOT']
for frame in range(0, 101, 10):
    scene.frame_set(frame)
    foot.location.y = foot.location.y * 2.0
    foot.keyframe_insert(data_path='location', index=1, frame=frame)
bpy.data.objects['RH']['start'] = bpy.data.objects['RH']['start'] + 0.08
bpy.data.objects['GAIT']['dwell_slow'] = 0.25
bpy.data.objects['HINTS']['knee_side'] = 0.25
scene.frame_set(0)
bpy.ops.wm.save_mainfile()
print('demo edit saved', bpy.data.filepath)
