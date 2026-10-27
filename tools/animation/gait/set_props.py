"""Set static values on the saved LadderGait.blend from the command line and save (the same as typing them into
Blender: Pose Mode, select the control bone, Bone Properties > Custom Properties).

    blender --background src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/set_props.py -- CTL_Bend.knee_toward=0.3 CTL_Gait.hand_mid_from_shoulder=0.0

Targets are RIG control bones (CTL_Gait, CTL_Pelvis, CTL_Bend) or the RIG object itself (RIG.seconds is keyed per
transition, so it is refused here: change it in the action). Keyed channels are edited in the actions, not here.
"""
import sys

import bpy

STATIC_BONES = ('CTL_Gait', 'CTL_Pelvis', 'CTL_Bend')

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
rig = bpy.data.objects['RIG']
for item in args:
    target, value = item.split('=', 1)
    name, key = target.split('.', 1)
    if name not in STATIC_BONES:
        raise SystemExit('%s is not a static control (use one of %s)' % (name, ', '.join(STATIC_BONES)))
    holder = rig.pose.bones[name]
    if key not in holder.keys() or key == 'weight':
        raise SystemExit('no static property %s on %s' % (key, name))
    holder[key] = type(holder[key])(float(value))
    print('set', name, key, holder[key])
bpy.ops.wm.save_mainfile()
