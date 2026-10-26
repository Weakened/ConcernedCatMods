"""Export the saved gait scene to JSON and a frame/phase reference table.

    blender --background src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/export_gait.py

Writes src/ConcernedLadders/AnimationSources/ladder_gait.json (what compile_gait.py turns into C#) and
ladder_gait_phases.csv (one row per authoring frame of a cycle: each limb's state, rung and arc, and the torso
dwell offset; values are the runtime TARGETS, marked 'authored'; in-game solved positions come from the dev trace).
Curves are read by evaluating the scene at each sample, so any interpolation or modifier edited in Blender counts.
"""
import csv
import hashlib
import json
import math
import os
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
SRC = os.path.join(ROOT, 'src', 'ConcernedLadders', 'AnimationSources')
SAMPLES = 33
LIMBS = ('LH', 'RH', 'LF', 'RF')


def obj(name):
    found = bpy.data.objects.get(name)
    if found is None:
        raise SystemExit('missing object ' + name + ' (rebuild with build_gait_blend.py?)')
    return found


def sample_curves():
    scene = bpy.context.scene
    swing, hand, foot = [], {'out': [], 'up': []}, {'out': [], 'up': []}
    for i in range(SAMPLES):
        frame = 100.0 * i / (SAMPLES - 1)
        whole = int(math.floor(frame))
        scene.frame_set(whole, subframe=frame - whole)
        swing.append(float(obj('SWING')['progress']))
        hand['out'].append(float(obj('ARC_HAND').location.y)); hand['up'].append(float(obj('ARC_HAND').location.z))
        foot['out'].append(float(obj('ARC_FOOT').location.y)); foot['up'].append(float(obj('ARC_FOOT').location.z))
    scene.frame_set(0)
    return swing, hand, foot


def props(name, keys):
    o = obj(name)
    return {k: float(o[k]) for k in keys}


def dwell_table(timing, slow, cycle, samples=120):
    def in_window(limb, t):
        d = (t - timing[limb][0]) % 1.0
        return d < timing[limb][1]
    speed = [(1.0 - slow) if (in_window('LF', (i + 0.5) / samples) or in_window('RF', (i + 0.5) / samples)) else 1.0
             for i in range(samples)]
    slow_time = sum(1 for s in speed if s < 1.0) / samples
    fast = 1.0 if slow_time >= 1.0 else 1.0 + slow * slow_time / (1.0 - slow_time)
    offset = [0.0]
    for s in speed:
        offset.append(offset[-1] + ((s if s < 1.0 else fast) - 1.0) * cycle / samples)
    mean = sum(offset[1:]) / samples
    return [o - mean for o in offset]


def state(progress):
    if progress <= 0.0:
        return 'planted'
    if progress < 0.15:
        return 'release'
    if progress < 0.7:
        return 'reach'
    if progress < 1.0:
        return 'approach'
    return 'planted'


def main():
    blend = bpy.data.filepath
    with open(blend, 'rb') as handle:
        blend_hash = hashlib.sha256(handle.read()).hexdigest()
    gait = obj('GAIT')
    stride = int(gait['stride_rungs'])
    timing = {name: (float(obj(name)['start']), float(obj(name)['length'])) for name in LIMBS}
    swing, hand, foot = sample_curves()
    data = {
        'source': os.path.basename(blend),
        'sourceSha256': blend_hash,
        'blender': bpy.app.version_string,
        'strideRungs': stride,
        'footLowRel': float(gait['foot_low_rel']),
        'handMidFromShoulder': float(gait['hand_mid_from_shoulder']),
        'dwellSlow': float(gait['dwell_slow']),
        'start': [timing[n][0] for n in LIMBS],
        'length': [timing[n][1] for n in LIMBS],
        'swing': swing,
        'arcOut': [hand['out'], foot['out']],
        'arcUp': [hand['up'], foot['up']],
        'body': props('BODY', ('pelvis_drop', 'pelvis_toward', 'lean', 'slide_lean', 'sway_tilt', 'sway_shift')),
        'hints': props('HINTS', ('elbow_out', 'elbow_side', 'elbow_down', 'knee_toward', 'knee_side', 'knee_down')),
    }
    with open(os.path.join(SRC, 'ladder_gait.json'), 'w', encoding='utf-8', newline='\n') as handle:
        json.dump(data, handle, indent=1, sort_keys=True)
        handle.write('\n')

    pitch = 1.0 / 3.0
    cycle = stride * pitch
    dwell = dwell_table(timing, data['dwellSlow'], cycle)

    def lookup(table, x):
        position = max(0.0, min(1.0, x)) * (len(table) - 1)
        i = min(len(table) - 2, int(math.floor(position)))
        return table[i] + (table[i + 1] - table[i]) * (position - i)

    with open(os.path.join(SRC, 'ladder_gait_phases.csv'), 'w', encoding='utf-8', newline='') as handle:
        out = csv.writer(handle)
        header = ['frame', 'phase', 'body_travel_m', 'torso_dwell_m']
        for name in LIMBS:
            header += [name + '_state', name + '_swing', name + '_rungs_climbed', name + '_arc_out_m', name + '_arc_up_m']
        header += ['basis']
        out.writerow(header)
        for frame in range(0, 61):
            t = frame / 60.0
            row = [frame, '%.4f' % t, '%.4f' % (t * cycle), '%.4f' % lookup(dwell, t)]
            for index, name in enumerate(LIMBS):
                start, length = timing[name]
                local = (t - start) % 1.0
                progress = min(1.0, local / length)
                moving = 0.0 < progress < 1.0
                climbed = stride * lookup(swing, progress) if moving else (stride if local >= length and t >= start else 0)
                kind = 0 if index < 2 else 1
                arc_out = lookup(data['arcOut'][kind], progress) if moving else 0.0
                arc_up = lookup(data['arcUp'][kind], progress) if moving else 0.0
                row += [state(progress if moving else 0.0), '%.3f' % (progress if moving else 0.0), '%.3f' % climbed,
                        '%.3f' % arc_out, '%.3f' % arc_up]
            row += ['authored target (reference-informed, adapted for Valheim); verify against the in-game trace']
            out.writerow(row)
    print('exported', blend, blend_hash[:12])


main()
