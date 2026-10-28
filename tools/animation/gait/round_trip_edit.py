"""Round-trip proof edit: applies to the SAVED LadderGait.blend the kind of edit an animator makes in the Graph
Editor, the Dope Sheet and the Bone Properties panel, then saves. Run it only on a scratch copy.

    blender --background <copy>/src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/round_trip_edit.py

  (a) climb_up   CTL_Foot.L location Z keys x1.5         -> Up.Arc[LF].up peak 0.05 -> 0.075 m (higher foot lift)
  (b) climb_down CTL_Hand.L key block moved 6 frames later -> Down.Start[LH] 0.10 -> 0.05 (descent plays phase backwards)
  (c) climb_up   CTL_Pelvis location Z keys x1.5         -> Up.Body[*].up x1.5 (deeper torso dwell, longer catch-up)
  (d) CTL_Bend   knee_toward 0.22 -> 0.30                -> GeneratedGait.KneeToward
  (e) TopExit    CTL_Foot.L location Z keys x1.2         -> TopExit.Arc[LF].up peak 0.22 -> 0.264 m (lead foot over the lip)
"""
import bpy


def curve(action, path, index):
    from bpy_extras import anim_utils
    bag = anim_utils.action_get_channelbag_for_slot(action, action.slots[0])
    fc = bag.fcurves.find(path, index=index)
    if fc is None:
        raise SystemExit('%s has no %s[%d]' % (action.name, path, index))
    return fc


def scale(action, path, index, factor):
    fc = curve(action, path, index)
    for key in fc.keyframe_points:
        key.co.y *= factor
        key.handle_left.y *= factor
        key.handle_right.y *= factor
    fc.update()


def shift(action, path, frames):
    for index in range(3):
        fc = curve(action, path, index)
        for key in fc.keyframe_points:
            key.co.x += frames
            key.handle_left.x += frames
            key.handle_right.x += frames
        fc.update()


actions = bpy.data.actions
scale(actions['climb_up'], 'pose.bones["CTL_Foot.L"].location', 2, 1.5)        # (a)
shift(actions['climb_down'], 'pose.bones["CTL_Hand.L"].location', 6.0)          # (b)
scale(actions['climb_up'], 'pose.bones["CTL_Pelvis"].location', 2, 1.5)         # (c)
bpy.data.objects['RIG'].pose.bones['CTL_Bend']['knee_toward'] = 0.30            # (d)
scale(actions['TopExit'], 'pose.bones["CTL_Foot.L"].location', 2, 1.2)          # (e)
bpy.ops.wm.save_mainfile()
print('round-trip edit saved', bpy.data.filepath)
