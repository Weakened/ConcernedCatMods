"""Create the editable Concerned Ladders gait source scene (run once; it OVERWRITES manual edits).

    blender --background --python-exit-code 1 --python tools/animation/gait/build_gait_blend.py

Writes src/ConcernedLadders/AnimationSources/LadderGait.blend with the current default values. After that, open the
.blend in Blender, edit it, save it and run export_gait.py; never rerun this builder over a scene you edited.

Scene layout (all values in metres, cycle fractions or degrees; frames 0..100 always mean 0..1 of the curve's input):
  * GAIT            custom props: stride_rungs, foot_low_rel, hand_mid_from_shoulder, dwell_slow
  * LH RH LF RF     custom props: start, length (release phase and swing length as fractions of a cycle)
  * SWING           custom prop "progress" keyed over frames 0..100 = swing progress in -> out (the ease of a reach)
  * ARC_HAND        location.y = clearance away from the rungs, location.z = clearance upward, keyed over the swing
  * ARC_FOOT        same for feet (knee-led recovery: the foot draws back from the rungs, then rises)
  * BODY            custom props: pelvis_drop, pelvis_toward, lean, slide_lean, sway_tilt, sway_shift
  * HINTS           custom props: elbow_out, elbow_side, elbow_down, knee_toward, knee_side, knee_down
"""
import math
import os
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
OUT = os.path.join(ROOT, 'src', 'ConcernedLadders', 'AnimationSources', 'LadderGait.blend')


def empty(name, location=(0.0, 0.0, 0.0), size=0.1):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = size
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)
    return obj


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * t * (10.0 + t * (-15.0 + 6.0 * t))


def bell(t, height):
    s = math.sin(math.pi * t)
    return height * s * s


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.frame_start = 0
    scene.frame_end = 100
    scene.render.fps = 100

    gait = empty('GAIT', size=0.3)
    gait['stride_rungs'] = 3
    gait['foot_low_rel'] = 0.06
    gait['hand_mid_from_shoulder'] = 0.0
    gait['dwell_slow'] = 0.55

    timing = {'LH': (0.50, 0.30), 'RH': (0.00, 0.30), 'LF': (0.20, 0.28), 'RF': (0.70, 0.28)}
    for index, (name, (start, length)) in enumerate(timing.items()):
        obj = empty(name, location=(index * 0.4, 0.0, 1.0))
        obj['start'] = start
        obj['length'] = length

    swing = empty('SWING', location=(0.0, 1.0, 0.0))
    swing['progress'] = 0.0
    for frame in range(0, 101, 10):
        swing['progress'] = ease(frame / 100.0)
        swing.keyframe_insert(data_path='["progress"]', frame=frame)

    for name, out_height, up_height in (('ARC_HAND', 0.09, 0.02), ('ARC_FOOT', 0.16, 0.05)):
        arc = empty(name, location=(0.0, 0.0, 0.0))
        for frame in range(0, 101, 10):
            t = frame / 100.0
            arc.location = (0.0, bell(t, out_height), bell(t, up_height))
            arc.keyframe_insert(data_path='location', frame=frame)

    body = empty('BODY', location=(0.0, -1.0, 0.0))
    for key, value in (('pelvis_drop', 0.10), ('pelvis_toward', 0.30), ('lean', 10.0), ('slide_lean', 6.0),
                       ('sway_tilt', 3.0), ('sway_shift', 0.03)):
        body[key] = value

    hints = empty('HINTS', location=(0.0, -2.0, 0.0))
    for key, value in (('elbow_out', 0.22), ('elbow_side', 0.2), ('elbow_down', 0.25), ('knee_toward', 0.3),
                       ('knee_side', 0.1), ('knee_down', 0.2)):
        hints[key] = value

    scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=OUT)
    print('wrote', OUT)


main()
