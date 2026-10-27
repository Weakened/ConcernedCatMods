"""Blender half of the Concerned Ladders animation pipeline: where each number lives in LadderGait.blend.

Every action is keyed on the armature object ``RIG`` (one slot, ``OBRIG``) over frames 0..120:

Climbing loops (``climb_up``, ``climb_down``): frames 0..120 are ONE cycle in time order (the body travels
``stride_rungs`` rungs). The ascent's cycle phase is frame/120; the descent plays the cycle backwards (its body
moves down), so its phase is 1 - frame/120.

  * ``CTL_Hand.L/.R``, ``CTL_Foot.L/.R`` location: the limb's swing arc in metres while it moves between rungs
    (X = toward the limb's own side, mirrored: +X for .L and -X for .R; Y = away from the rungs; Z = up). The
    first and last key of the limb ARE its window: where the block of keys sits on the timeline is when the limb
    lets go and lands; how long it is, is how long the limb is off the rungs (fraction of the cycle).
  * ``CTL_Swing`` ["ease"]: 0..1 over frames 0..120 = how far along the rung-to-rung travel a moving limb is over
    its own move (shared by all four limbs of that loop).
  * ``CTL_Pelvis`` location Z = body offset up, Y = body offset away from the rungs (metres, relative to steady
    travel); rotation X = extra lean toward the ladder (degrees in the UI).

Transitions (``GroundMount``, ``AirCatch``, ``TopExit``, ``SideExit``, ``BottomStepOff``, ``TopEntry``): frames
0..120 are progress 0..1 (41 samples, one every 3 frames).

  * limb controls: location = extra arc as above; ["weight"] 0..1 (how much the authored contact owns the limb,
    0 = the game's own pose); ["reach"] 0..1 (0 = its ladder contact, 1 = its floor contact).
  * ``CTL_Pelvis``: ["weight"] = body weight (1 = climbing posture), location Z / Y and rotation X as above.
  * ``RIG`` ["seconds"]: duration of a mount or a catch (exits and the top entry follow the route's progress).

Static values (never keyed): ``CTL_Gait`` (stance and settling), ``CTL_Pelvis`` (posture), ``CTL_Bend`` (elbow and
knee bend hints). The exporter refuses a scene where any of them is keyed.
"""
import math

try:  # inside the .blend text block the model is already defined above this code
    default_gait  # noqa: F821
except NameError:
    from gait_model import *  # noqa: F401,F403

import bpy

RIG = 'RIG'
PROXY = 'PROXY'
FRAMES = 120
LOOP_STEP = FRAMES // (BODY_SAMPLES - 1)        # 2
ACTION_STEP = FRAMES // (ACTION_SAMPLES - 1)    # 3
LIMB_BONES = ('CTL_Hand.L', 'CTL_Hand.R', 'CTL_Foot.L', 'CTL_Foot.R')
CONTACT_BONES = ('PRV_Contact_Hand.L', 'PRV_Contact_Hand.R', 'PRV_Contact_Foot.L', 'PRV_Contact_Foot.R')
POLE_BONES = ('PRV_Elbow.L', 'PRV_Elbow.R', 'PRV_Knee.L', 'PRV_Knee.R')
PELVIS, SWING, GAIT, BEND = 'CTL_Pelvis', 'CTL_Swing', 'CTL_Gait', 'CTL_Bend'
STATIC = {GAIT: [key for _, key, _ in SHAPE_SCALARS] + ['stride_rungs'],
          PELVIS: [key for _, key, _ in POSTURE_CONSTANTS],
          BEND: [key for _, key, _ in BEND_CONSTANTS]}


def pb_path(bone, prop):
    return 'pose.bones["%s"]%s' % (bone, prop)


def loc(bone):
    return pb_path(bone, '.location')


def rot(bone):
    return pb_path(bone, '.rotation_euler')


def custom(bone, key):
    return pb_path(bone, '["%s"]' % key)


# ------------------------------------------------------------------------------------- channelbags
def channelbag(action, create=False):
    from bpy_extras import anim_utils
    if not action.slots:
        if not create:
            raise SystemExit('action %s has no slot' % action.name)
        action.slots.new(id_type='OBJECT', name=RIG)
    slot = action.slots[0]
    if create:
        return anim_utils.action_ensure_channelbag_for_slot(action, slot)
    bag = anim_utils.action_get_channelbag_for_slot(action, slot)
    if bag is None:
        raise SystemExit('action %s has no keys' % action.name)
    return bag


def write_curve(bag, path, index, points, group):
    fc = bag.fcurves.find(path, index=index)
    if fc is None:
        fc = bag.fcurves.new(path, index=index)
    else:
        fc.keyframe_points.clear()
    grp = bag.groups.get(group) or bag.groups.new(group)
    fc.group = grp
    fc.keyframe_points.add(len(points))
    for key, (frame, value) in zip(fc.keyframe_points, points):
        key.co = (frame, value)
        key.handle_left = (frame, value)
        key.handle_right = (frame, value)
        key.interpolation = 'LINEAR'
    fc.update()
    return fc


def evaluator(action):
    bag = channelbag(action)

    def value(path, index, frame, default):
        fc = bag.fcurves.find(path, index=index)
        return default if fc is None else fc.evaluate(frame)

    def keys(path, index):
        fc = bag.fcurves.find(path, index=index)
        return [] if fc is None else [k.co[0] for k in fc.keyframe_points]

    return bag, value, keys


# ---------------------------------------------------------------------------------- write (seed)
def write_loop(action, data, down):
    bag = channelbag(action, create=True)
    for limb, bone in enumerate(LIMB_BONES):
        start, length = data['start'][limb], data['length'][limb]
        first = ((1.0 - start - length) if down else start) % 1.0
        span = length * FRAMES
        sign = lat_sign(limb)
        rows = []
        for j in range(ARC_SAMPLES):
            frame = first * FRAMES + span * j / (ARC_SAMPLES - 1)
            arc = data['arc'][limb][(ARC_SAMPLES - 1 - j) if down else j]
            rows.append((frame, (arc[0] * sign, arc[1], arc[2])))
        for axis in range(3):
            write_curve(bag, loc(bone), axis, [(f, v[axis]) for f, v in rows], bone)
    ease = []
    for j in range(ARC_SAMPLES):
        frame = FRAMES * j / (ARC_SAMPLES - 1)
        ease.append((frame, (1.0 - data['swing'][ARC_SAMPLES - 1 - j]) if down else data['swing'][j]))
    write_curve(bag, custom(SWING, 'ease'), 0, ease, SWING)
    body = []
    for i in range(BODY_SAMPLES):
        frame = (FRAMES - LOOP_STEP * i) if down else LOOP_STEP * i
        body.append((frame, data['body'][i], data['lean'][i]))
    body.sort(key=lambda row: row[0])
    write_curve(bag, loc(PELVIS), 1, [(f, -b[1]) for f, b, _ in body], PELVIS)
    write_curve(bag, loc(PELVIS), 2, [(f, b[0]) for f, b, _ in body], PELVIS)
    write_curve(bag, rot(PELVIS), 0, [(f, math.radians(l)) for f, _, l in body], PELVIS)


def write_transition(action, data):
    bag = channelbag(action, create=True)
    frames = [ACTION_STEP * i for i in range(ACTION_SAMPLES)]
    for limb, bone in enumerate(LIMB_BONES):
        sign = lat_sign(limb)
        arc = data['arc'][limb]
        write_curve(bag, loc(bone), 0, [(f, arc[i][0] * sign) for i, f in enumerate(frames)], bone)
        write_curve(bag, loc(bone), 1, [(f, arc[i][1]) for i, f in enumerate(frames)], bone)
        write_curve(bag, loc(bone), 2, [(f, arc[i][2]) for i, f in enumerate(frames)], bone)
        write_curve(bag, custom(bone, 'weight'), 0, list(zip(frames, data['weight'][limb])), bone)
        write_curve(bag, custom(bone, 'reach'), 0, list(zip(frames, data['reach'][limb])), bone)
    write_curve(bag, custom(PELVIS, 'weight'), 0, list(zip(frames, data['bodyWeight'])), PELVIS)
    write_curve(bag, loc(PELVIS), 1, [(f, -v) for f, v in zip(frames, data['bodyToward'])], PELVIS)
    write_curve(bag, loc(PELVIS), 2, list(zip(frames, data['bodyUp'])), PELVIS)
    write_curve(bag, rot(PELVIS), 0, [(f, math.radians(v)) for f, v in zip(frames, data['lean'])], PELVIS)
    write_curve(bag, '["seconds"]', 0, [(0.0, data['seconds'])], RIG)


def limb_value(action, bone, axis, frame):
    """A limb control's keyed location at `frame`. In a loop, a window that crosses the end of the cycle keeps
    going past frame 120 (its keys are contiguous); frames before the window's first key read it one cycle later,
    so the arc wraps the way the runtime's phase does."""
    _, value, keys = evaluator(action)
    if action.name in LOOP_NAMES:
        frames = keys(loc(bone), 0) + keys(loc(bone), 1) + keys(loc(bone), 2)
        if frames:
            first = min(frames)
            while frame < first:
                frame += FRAMES
    return value(loc(bone), axis, frame, 0.0)


# ------------------------------------------------------------------------------------- read (export)
def read_loop(action, down, stride):
    bag, value, keys = evaluator(action)
    out = {'name': action.name, 'strideRungs': stride, 'start': [], 'length': [], 'arc': []}
    for limb, bone in enumerate(LIMB_BONES):
        frames = keys(loc(bone), 0) + keys(loc(bone), 1) + keys(loc(bone), 2)
        if len(frames) < 2:
            raise SystemExit('%s: %s has no swing keys (its window is the first..last key of its location)' % (action.name, bone))
        first, last = min(frames), max(frames)
        length = (last - first) / FRAMES
        start = ((1.0 - first / FRAMES - length) if down else first / FRAMES) % 1.0
        if start > 1.0 - 1e-6:
            start = 0.0
        out['start'].append(start)
        out['length'].append(length)
        sign = lat_sign(limb)
        rows = []
        for i in range(ARC_SAMPLES):
            frame = (last - (last - first) * i / (ARC_SAMPLES - 1)) if down else (first + (last - first) * i / (ARC_SAMPLES - 1))
            rows.append([value(loc(bone), 0, frame, 0.0) * sign, value(loc(bone), 1, frame, 0.0), value(loc(bone), 2, frame, 0.0)])
        out['arc'].append(rows)
    swing = []
    for i in range(ARC_SAMPLES):
        if down:
            swing.append(1.0 - value(custom(SWING, 'ease'), 0, FRAMES * (ARC_SAMPLES - 1 - i) / (ARC_SAMPLES - 1), 0.0))
        else:
            swing.append(value(custom(SWING, 'ease'), 0, FRAMES * i / (ARC_SAMPLES - 1), 0.0))
    out['swing'] = swing
    body, lean = [], []
    for i in range(BODY_SAMPLES):
        frame = (FRAMES - LOOP_STEP * i) if down else LOOP_STEP * i
        body.append([value(loc(PELVIS), 2, frame, 0.0), -value(loc(PELVIS), 1, frame, 0.0)])
        lean.append(math.degrees(value(rot(PELVIS), 0, frame, 0.0)))
    out['body'] = body
    out['lean'] = lean
    return out


def read_transition(action):
    bag, value, keys = evaluator(action)
    frames = [ACTION_STEP * i for i in range(ACTION_SAMPLES)]
    out = {'kind': action.name, 'seconds': value('["seconds"]', 0, 0.0, 0.6), 'weight': [], 'reach': [], 'arc': []}
    for limb, bone in enumerate(LIMB_BONES):
        sign = lat_sign(limb)
        out['weight'].append([value(custom(bone, 'weight'), 0, f, 1.0) for f in frames])
        out['reach'].append([value(custom(bone, 'reach'), 0, f, 0.0) for f in frames])
        out['arc'].append([[value(loc(bone), 0, f, 0.0) * sign, value(loc(bone), 1, f, 0.0), value(loc(bone), 2, f, 0.0)] for f in frames])
    out['bodyWeight'] = [value(custom(PELVIS, 'weight'), 0, f, 1.0) for f in frames]
    out['bodyUp'] = [value(loc(PELVIS), 2, f, 0.0) for f in frames]
    out['bodyToward'] = [-value(loc(PELVIS), 1, f, 0.0) for f in frames]
    out['lean'] = [math.degrees(value(rot(PELVIS), 0, f, 0.0)) for f in frames]
    return out


def check_static(action):
    """A static property keyed in an action would make the export depend on the current frame: refuse."""
    bag = channelbag(action)
    for fc in bag.fcurves:
        for bone, keys in STATIC.items():
            for key in keys:
                if fc.data_path == custom(bone, key):
                    raise SystemExit('%s keys %s["%s"], which is a static value: delete that F-curve' % (action.name, bone, key))


def rig():
    obj = bpy.data.objects.get(RIG)
    if obj is None or obj.type != 'ARMATURE':
        raise SystemExit('missing armature RIG (rebuild with build_gait_blend.py?)')
    return obj


def read_static():
    pose = rig().pose.bones
    shape = {key: float(pose[GAIT][key]) for _, key, _ in SHAPE_SCALARS}
    stride = int(pose[GAIT]['stride_rungs'])
    constants = {name: float(pose[PELVIS][key]) for name, key, _ in POSTURE_CONSTANTS}
    constants.update({name: float(pose[BEND][key]) for name, key, _ in BEND_CONSTANTS})
    return shape, stride, constants


def read_all():
    """Everything GeneratedGait carries, read from the saved scene (no frame or active-action dependence)."""
    shape, stride, constants = read_static()
    for name in LOOP_NAMES + TRANSITION_KINDS:
        action = bpy.data.actions.get(name)
        if action is None:
            raise SystemExit('missing action %s (rebuild with build_gait_blend.py?)' % name)
        check_static(action)
    shape['up'] = read_loop(bpy.data.actions['climb_up'], False, stride)
    shape['down'] = read_loop(bpy.data.actions['climb_down'], True, stride)
    transitions = [read_transition(bpy.data.actions[kind]) for kind in TRANSITION_KINDS]
    return {'shape': shape, 'transitions': transitions, 'constants': constants}


# ------------------------------------------------------------------------------------------- preview
def preview_pose(name, frame, data=None):
    """The model pose of action `name` at `frame`, from freshly read data unless given."""
    data = data or read_all()
    time = max(0.0, min(1.0, frame / FRAMES))
    if name in LOOP_NAMES:
        return loop_frame(data['shape'], data['constants'], name == 'climb_down', time)
    action = next(t for t in data['transitions'] if t['kind'] == name)
    return transition_frame(data['shape'], data['constants'], action, time)


def apply_preview(scene, frame=None):
    obj = bpy.data.objects.get(RIG)
    proxy = bpy.data.objects.get(PROXY)
    if obj is None or proxy is None or obj.animation_data is None or obj.animation_data.action is None:
        return
    name = obj.animation_data.action.name
    if name not in LOOP_NAMES + TRANSITION_KINDS:
        return
    try:
        data = read_all()
    except SystemExit as problem:
        print('ladder preview:', problem)
        return
    frame = scene.frame_current + scene.frame_subframe if frame is None else frame
    pose = preview_pose(name, frame, data)
    bones = obj.pose.bones
    action = obj.animation_data.action
    _, value, _ = evaluator(action)
    for limb in range(4):
        contact = list(pose['limbs'][limb]['contact'])
        # Blender holds a control at its first key before its window opens; a loop window that wraps past frame 120
        # is really still moving there, so the contact carries the difference (the control shows the true arc).
        for axis in range(3):
            contact[axis] += limb_value(action, LIMB_BONES[limb], axis, frame) - value(loc(LIMB_BONES[limb]), axis, frame, 0.0)
        bones[CONTACT_BONES[limb]].location = contact
    pelvis = pose['pelvis']
    # The keyed CTL_Pelvis offset (up, away from the rungs, extra lean) rides on PRV_Pelvis: remove it from the base.
    base = [pose['root'][0], pose['root'][1] - data['constants']['PelvisToward'],
            pose['root'][2] + PELVIS_HEIGHT - data['constants']['PelvisDrop']]
    bones['PRV_Pelvis'].location = base
    bones['PRV_Pelvis'].rotation_euler = (math.radians(data['constants']['Lean']), 0.0, 0.0)
    c = data['constants']
    climbing = [pelvis['climbing'][0], pelvis['climbing'][1], pelvis['climbing'][2]]
    for limb in range(4):
        hand = limb < 2
        side = lat_sign(limb)
        if hand:
            joint = [climbing[0] + side * PROXY_SHOULDER_HALF, climbing[1], climbing[2] + PROXY_SHOULDER_ABOVE_HIPS]
            hint = [joint[0] + side * c['ElbowSide'], joint[1] + c['ElbowOut'], joint[2] - c['ElbowDown']]
        else:
            joint = [climbing[0] + side * PROXY_HIP_HALF, climbing[1], climbing[2] - 0.03]
            hint = [joint[0] + side * c['KneeSide'], joint[1] - c['KneeToward'], joint[2] - c['KneeDown']]
        bones[POLE_BONES[limb]].location = hint
    proxy.location = pose['root']
    pb = proxy.pose.bones
    pb['hips'].constraints['Body'].influence = max(0.0, min(1.0, pelvis['weight']))
    for limb, (chain, end) in enumerate((('forearm.L', None), ('forearm.R', None), ('shin.L', 'foot.L'), ('shin.R', 'foot.R'))):
        weight = max(0.0, min(1.0, pose['limbs'][limb]['weight']))
        pb[chain].constraints['IK'].influence = weight
        if end:
            pb[end].constraints['Foot'].influence = weight
    target = bpy.data.objects.get('CAM_Target')
    if target is not None:
        target.location = (pose['root'][0], pose['root'][1], pose['root'][2] + 0.95)


def register_preview():
    handlers = bpy.app.handlers.frame_change_pre
    for existing in [h for h in handlers if getattr(h, '__name__', '') == 'ladder_preview_handler']:
        handlers.remove(existing)

    def ladder_preview_handler(scene, depsgraph=None):
        apply_preview(scene)

    handlers.append(ladder_preview_handler)


def use_action(name):
    obj = rig()
    action = bpy.data.actions[name]
    obj.animation_data.action = action
    obj.animation_data.action_slot = action.slots[0]
