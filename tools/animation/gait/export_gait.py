"""Export the saved LadderGait.blend to JSON and per-action frame tables.

    blender --background src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/export_gait.py

Writes, next to the .blend:

  * ladder_gait.json: every value GeneratedGait carries (both climbing loops, the six transitions, the stance and
    settling values, the posture and bend constants), sampled from the actions exactly as the C# tables are sampled.
    compile_gait.py turns it into Domain/Animation/GeneratedGait.cs.
  * frames/<action>.csv: one row per frame 0..120 of each action: the keyed control values and the derived channels
    (cycle phase or progress, each limb's swing progress / weight / reach, its rung contact and its final target in
    the reference scene, the body offsets and the pelvis). See the README for the columns.

Curves are read by evaluating the F-curves at each sample, so any interpolation, handle or key timing edited in
Blender counts. Nothing depends on the current frame or on which action is active. The scene is not saved.
"""
import csv
import hashlib
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402

import gait_model as gm  # noqa: E402
import gait_blend as gb  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
SRC = os.path.join(ROOT, 'src', 'ConcernedLadders', 'AnimationSources')


def clean(value):
    """Six decimals (a micrometre / a millionth of a cycle), no negative zero: stable, readable diffs."""
    if isinstance(value, dict):
        return {k: clean(v) for k, v in value.items()}
    if isinstance(value, list):
        return [clean(v) for v in value]
    if isinstance(value, float):
        rounded = round(value, 6)
        return 0.0 if rounded == 0.0 else rounded
    return value


def dumps(value, indent=0):
    """JSON with every innermost list of numbers on one line."""
    pad = ' ' * indent
    if isinstance(value, dict):
        items = ['%s "%s": %s' % (pad, k, dumps(v, indent + 1).lstrip()) for k, v in value.items()]
        return pad + '{\n' + ',\n'.join(items) + '\n' + pad + '}'
    if isinstance(value, list):
        if all(not isinstance(v, (list, dict)) for v in value):
            return pad + '[' + ', '.join(json.dumps(v) for v in value) + ']'
        return pad + '[\n' + ',\n'.join(dumps(v, indent + 1) for v in value) + '\n' + pad + ']'
    return pad + json.dumps(value)


def f4(value):
    text = '%.4f' % value
    return '0.0000' if text == '-0.0000' else text


def write_frames(data):
    folder = os.path.join(SRC, 'frames')
    os.makedirs(folder, exist_ok=True)
    for name in gm.LOOP_NAMES + gm.TRANSITION_KINDS:
        loop = name in gm.LOOP_NAMES
        action = bpy.data.actions[name]
        _, value, _ = gb.evaluator(action)
        header = ['frame', 'time', 'phase' if loop else 'progress', 'feet_x', 'feet_y', 'feet_z']
        for short in gm.LIMBS:
            header += ['%s_key_x' % short, '%s_key_y' % short, '%s_key_z' % short]
            if loop:
                header += ['%s_state' % short, '%s_swing' % short, '%s_rung_from' % short, '%s_rung_to' % short]
            else:
                header += ['%s_weight' % short, '%s_reach' % short]
            header += ['%s_arc_lat' % short, '%s_arc_out' % short, '%s_arc_up' % short,
                       '%s_rung_z' % short, '%s_target_x' % short, '%s_target_y' % short, '%s_target_z' % short]
        header += ['pelvis_key_y', 'pelvis_key_z', 'pelvis_key_lean_deg']
        header += ['swing_ease_key'] if loop else ['body_weight']
        header += ['body_up', 'body_toward', 'body_lean_deg'] + ([] if loop else ['seconds'])
        header += ['pelvis_x', 'pelvis_y', 'pelvis_z', 'pelvis_lean_total_deg']
        rows = [header]
        for frame in range(gb.FRAMES + 1):
            pose = gb.preview_pose(name, frame, data)
            row = [frame, f4(frame / gb.FRAMES), f4(pose['phase'])] + [f4(v) for v in pose['root']]
            for limb, bone in enumerate(gb.LIMB_BONES):
                part = pose['limbs'][limb]
                row += [f4(gb.limb_value(action, bone, axis, frame)) for axis in range(3)]
                if loop:
                    row += [part['state'], f4(part['swing']), part['rungs'][0], part['rungs'][1]]
                else:
                    row += [f4(part['weight']), f4(part['reach'])]
                row += [f4(v) for v in part['arc']] + [f4(part['height'])] + [f4(v) for v in part['target']]
            row += [f4(value(gb.loc(gb.PELVIS), 1, frame, 0.0)), f4(value(gb.loc(gb.PELVIS), 2, frame, 0.0)),
                    f4(math.degrees(value(gb.rot(gb.PELVIS), 0, frame, 0.0)))]
            p = pose['pelvis']
            row += [f4(value(gb.custom(gb.SWING, 'ease'), 0, frame, 0.0))] if loop else [f4(p['weight'])]
            row += [f4(p['up']), f4(p['toward']), f4(p['extra_lean'])]
            if not loop:
                row += [f4(value('["seconds"]', 0, 0.0, 0.0))]
            row += [f4(v) for v in p['climbing']] + [f4(p['lean'])]
            rows.append(row)
        with open(os.path.join(folder, name + '.csv'), 'w', encoding='utf-8', newline='') as handle:
            csv.writer(handle, lineterminator='\n').writerows(rows)


def main():
    blend = bpy.data.filepath
    with open(blend, 'rb') as handle:
        blend_hash = hashlib.sha256(handle.read()).hexdigest()
    data = clean(gb.read_all())
    document = {'format': 2, 'source': os.path.basename(blend), 'sourceSha256': blend_hash,
                'blender': bpy.app.version_string}
    document.update(data)
    with open(os.path.join(SRC, 'ladder_gait.json'), 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(dumps(document) + '\n')
    write_frames(data)
    print('exported', blend, blend_hash[:12])


main()
