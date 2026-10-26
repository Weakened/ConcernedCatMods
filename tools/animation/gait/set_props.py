"""Set custom properties on the saved gait scene from the command line and save (same effect as typing them into
Blender's Object Properties > Custom Properties panel).

    blender --background src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/set_props.py -- HINTS.knee_side=-0.02 GAIT.hand_mid_from_shoulder=0.1
"""
import sys
import bpy

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
for item in args:
    target, value = item.split('=', 1)
    name, key = target.split('.', 1)
    obj = bpy.data.objects[name]
    if key not in obj.keys():
        raise SystemExit('no property %s on %s' % (key, name))
    obj[key] = type(obj[key])(float(value))
    print('set', name, key, obj[key])
bpy.ops.wm.save_mainfile()
