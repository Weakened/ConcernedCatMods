"""Render the guide's screenshots of the proxy climber (Workbench, 960x540 PNG) from the saved LadderGait.blend.

    blender --background src/ConcernedLadders/AnimationSources/LadderGait.blend --python-exit-code 1 \
        --python tools/animation/gait/render_guide.py [-- <output folder>]

Default output: src/ConcernedLadders/AnimationSources/guide/. The scene is not saved. Each shot poses the proxy the
way the preview script does in an interactive session (the data at that frame of that action).
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402

import gait_blend as gb  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
DEFAULT_OUT = os.path.join(ROOT, 'src', 'ConcernedLadders', 'AnimationSources', 'guide')

SHOTS = (  # file, action, frame, camera
    ('climb_up_f000_side', 'climb_up', 0, 'CAM_Side'),
    ('climb_up_f030_side', 'climb_up', 30, 'CAM_Side'),
    ('climb_up_f060_front', 'climb_up', 60, 'CAM_Front'),
    ('climb_up_f090_side', 'climb_up', 90, 'CAM_Side'),
    ('climb_down_f030_side', 'climb_down', 30, 'CAM_Side'),
    ('climb_down_f090_front', 'climb_down', 90, 'CAM_Front'),
    ('GroundMount_f060_side', 'GroundMount', 60, 'CAM_Side'),
    ('AirCatch_f045_front', 'AirCatch', 45, 'CAM_Front'),
    ('TopExit_f045_side', 'TopExit', 45, 'CAM_Side'),
    ('TopExit_f090_side', 'TopExit', 90, 'CAM_Side'),
    ('SideExit_f060_front', 'SideExit', 60, 'CAM_Front'),
    ('BottomStepOff_f060_side', 'BottomStepOff', 60, 'CAM_Side'),
    ('TopEntry_f045_side', 'TopEntry', 45, 'CAM_Side'),
)


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = os.path.abspath(args[0]) if args else DEFAULT_OUT
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    gb.register_preview()
    # Small files: no dithering noise, 8-bit RGB, maximum PNG compression.
    scene.render.dither_intensity = 0.0
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '8'
    scene.render.image_settings.compression = 100
    for file, action, frame, camera in SHOTS:
        gb.use_action(action)
        scene.camera = bpy.data.objects[camera]
        scene.frame_set(frame)
        scene.frame_set(frame)  # second pass: constraints see the preview bones placed by the first
        scene.render.filepath = os.path.join(out, file + '.png')
        bpy.ops.render.render(write_still=True)
        print('rendered', file)


main()
