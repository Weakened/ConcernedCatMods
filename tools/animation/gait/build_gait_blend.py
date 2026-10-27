"""Create the editable Concerned Ladders animation source, LadderGait.blend, from the C# defaults.

    blender --background --factory-startup --python-exit-code 1 --python tools/animation/gait/build_gait_blend.py

It OVERWRITES src/ConcernedLadders/AnimationSources/LadderGait.blend: run it only to start over. The scene is
seeded with the values the C# carries today (gait_model.default_gait()), so exporting the fresh scene regenerates
GeneratedGait.cs with the same data (see the README next to the .blend for the layout and the editing guide).

  * Ladder: rails, 15 rungs at the runtime pitch (1/3 m), the ground, an upper floor at the ladder top (5 m) and an
    intermediate floor to the climber's left (2 m).
  * RIG: the control armature. CTL_* bones are what an animator keys; PRV_* bones are placed by the preview
    script from the data (rung contacts, pelvis base, bend hints) and are never keyed.
  * PROXY: a visible humanoid proxy proportioned to the Valheim player (pelvis about 1 m, shoulders 1.45 m), posed
    by IK onto the controls, with weight channels driving the constraint influences.
  * Actions climb_up, climb_down, GroundMount, AirCatch, TopExit, SideExit, BottomStepOff, TopEntry, each also
    an NLA track (muted) so all eight sit side by side.
  * Cameras CAM_Front (behind the climber, facing the ladder) and CAM_Side (from behind the climber's right shoulder, 25 degrees off the side).
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import gait_model as gm  # noqa: E402
import gait_blend as gb  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(ROOT, 'src', 'ConcernedLadders', 'AnimationSources', 'LadderGait.blend')

WOOD = (0.55, 0.38, 0.22, 1.0)
FLOOR = (0.62, 0.62, 0.6, 1.0)
GROUND = (0.42, 0.5, 0.36, 1.0)
SKIN = (0.86, 0.76, 0.66, 1.0)
LEFT = (0.25, 0.48, 0.85, 1.0)
RIGHT = (0.9, 0.45, 0.2, 1.0)
TORSO = (0.75, 0.75, 0.78, 1.0)
CONTROL = (1.0, 0.85, 0.1, 1.0)


def collection(name, hide=False):
    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)
    coll.hide_render = hide
    return coll


def box_mesh(name, minimum, maximum):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector([minimum[i] + (v.co[i] + 0.5) * (maximum[i] - minimum[i]) for i in range(3)])
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def sphere_mesh(name, radius, centre=(0, 0, 0)):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=radius)
    for v in bm.verts:
        v.co += Vector(centre)
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def mesh_object(name, mesh, coll, color, location=(0, 0, 0)):
    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    obj.color = color
    coll.objects.link(obj)
    return obj


def box(name, coll, color, minimum, maximum):
    return mesh_object(name, box_mesh(name, minimum, maximum), coll, color)


def build_ladder():
    coll = collection('Ladder')
    box('GEO_Ground', coll, GROUND, (-3.0, -3.5, -0.1), (3.0, 3.0, 0.0))
    for side, name in ((1, 'L'), (-1, 'R')):
        x = side * gm.RAIL_X
        box('GEO_Rail.' + name, coll, WOOD, (x - 0.04, -0.06, 0.0), (x + 0.04, 0.06, gm.LADDER_TOP))
    for k in range(gm.RUNG_LAST + 1):
        z = gm.rung_height(k)
        box('GEO_Rung.%02d' % k, coll, WOOD, (-gm.RUNG_HALF_WIDTH - 0.05, -gm.RUNG_HALF_DEPTH, z - gm.RUNG_HALF_HEIGHT),
            (gm.RUNG_HALF_WIDTH + 0.05, gm.RUNG_HALF_DEPTH, z + gm.RUNG_HALF_HEIGHT))
    box('GEO_UpperFloor', coll, FLOOR, (-2.0, -3.2, gm.LADDER_TOP - 0.2), (2.0, -0.1, gm.LADDER_TOP))
    box('GEO_SideFloor', coll, FLOOR, (0.5, -0.6, gm.SIDE_FLOOR - 0.2), (2.6, 1.8, gm.SIDE_FLOOR))
    for k, (x, y) in enumerate(((0.5, -0.6), (2.5, -0.6), (0.5, 1.7), (2.5, 1.7))):
        box('GEO_SidePost.%d' % k, coll, WOOD, (x, y, 0.0), (x + 0.1, y + 0.1, gm.SIDE_FLOOR - 0.2))


def widget_meshes(coll):
    shapes = {
        'WGT_Limb': sphere_mesh('WGT_Limb', 0.045),
        'WGT_Pelvis': box_mesh('WGT_Pelvis', (-0.2, -0.12, -0.08), (0.2, 0.12, 0.08)),
        'WGT_Small': sphere_mesh('WGT_Small', 0.02),
        'WGT_Panel': box_mesh('WGT_Panel', (-0.08, -0.08, -0.08), (0.08, 0.08, 0.08)),
    }
    out = {}
    for name, mesh in shapes.items():
        obj = bpy.data.objects.new(name, mesh)
        coll.objects.link(obj)
        out[name] = obj
    return out


def build_rig(coll, widgets):
    arm = bpy.data.armatures.new(gb.RIG)
    obj = bpy.data.objects.new(gb.RIG, arm)
    coll.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.edit_bones

    def bone(name, parent=None, head=(0, 0, 0), direction=(0, 0.08, 0)):
        b = eb.new(name)
        b.head = Vector(head)
        b.tail = Vector(head) + Vector(direction)
        b.roll = 0.0
        b.use_deform = False
        if parent:
            b.parent = eb[parent]
        return b

    bone('PRV_Pelvis')
    bone(gb.PELVIS, 'PRV_Pelvis')
    for contact, ctl in zip(gb.CONTACT_BONES, gb.LIMB_BONES):
        bone(contact)
        bone(ctl, contact)
    # IK goals: the wrist and the ankle relative to the palm / ball-of-foot contact (presenter: GripGoal with the
    # fingers 70 degrees toward the ladder, and FootBallForward / FootAnkleHeight).
    for side in ('L', 'R'):
        bone('IKT_Hand.' + side, 'CTL_Hand.' + side, head=(0, 0.0624, -0.0333))
        bone('IKT_Foot.' + side, 'CTL_Foot.' + side, head=(0, 0.165, 0.146), direction=(0, -0.165, -0.146))
    for pole in gb.POLE_BONES:
        bone(pole)
    for name in (gb.SWING, gb.GAIT, gb.BEND):
        bone(name)
    bpy.ops.object.mode_set(mode='POSE')

    pose = obj.pose.bones
    for name in gb.LIMB_BONES:
        pb = pose[name]
        pb.custom_shape = widgets['WGT_Limb']
        pb['weight'] = 1.0
        pb.id_properties_ui('weight').update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0,
                                             description="Transitions: how much the authored contact owns this limb (0 = the game's own pose)")
        pb['reach'] = 0.0
        pb.id_properties_ui('reach').update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0,
                                            description='Transitions: 0 = the ladder contact, 1 = the floor contact')
        pb.lock_rotation = (True, True, True)
        pb.lock_scale = (True, True, True)
        pb.color.palette = 'THEME01' if name.endswith('.R') else 'THEME04'
    p = pose[gb.PELVIS]
    p.custom_shape = widgets['WGT_Pelvis']
    p.rotation_mode = 'XYZ'
    p.lock_rotation = (False, True, True)
    p.lock_scale = (True, True, True)
    p.lock_location[0] = True
    p['weight'] = 1.0
    p.id_properties_ui('weight').update(min=0.0, max=1.0, description="Transitions: body weight (1 = climbing posture, 0 = the game's own pose)")
    p.color.palette = 'THEME09'
    for name, key, value in gm.POSTURE_CONSTANTS:
        p[key] = float(value)
        p.id_properties_ui(key).update(description='Static (never key): GeneratedGait.%s' % name)
    sw = pose[gb.SWING]
    sw['ease'] = 0.0
    sw.id_properties_ui('ease').update(min=0.0, max=1.0, description="Loops: fraction of the rung-to-rung travel over a limb's move (frame/120 = the move's progress)")
    sw.custom_shape = widgets['WGT_Panel']
    sw.location = (-0.9, 0.6, 1.0)
    g = pose[gb.GAIT]
    g['stride_rungs'] = 3
    g.id_properties_ui('stride_rungs').update(min=1, max=6, description='Rungs per limb move and per body cycle (both loops)')
    for name, key, value in gm.SHAPE_SCALARS:
        g[key] = float(value)
        g.id_properties_ui(key).update(description='Static (never key): GaitShape.%s' % name)
    g.custom_shape = widgets['WGT_Panel']
    g.location = (-0.9, 0.6, 0.7)
    b = pose[gb.BEND]
    for name, key, value in gm.BEND_CONSTANTS:
        b[key] = float(value)
        b.id_properties_ui(key).update(description='Static (never key): GeneratedGait.%s (elbow: out/side/down from the shoulder; knee: toward/side/down from the hip)' % name)
    b.custom_shape = widgets['WGT_Panel']
    b.location = (-0.9, 0.6, 0.4)
    for name in gb.POLE_BONES + ('IKT_Hand.L', 'IKT_Hand.R', 'IKT_Foot.L', 'IKT_Foot.R') + gb.CONTACT_BONES + ('PRV_Pelvis',):
        pose[name].custom_shape = widgets['WGT_Small']
        pose[name].color.palette = 'THEME10'
    obj['seconds'] = 1.0
    obj.id_properties_ui('seconds').update(min=0.06, max=4.9, description='Transitions: duration of a mount or a catch (exits and the top entry follow the route)')
    bpy.ops.object.mode_set(mode='OBJECT')
    obj.show_in_front = True
    arm.display_type = 'WIRE'
    return obj


PROXY_BONES = (  # name, parent, head, tail (standing rest at the feet origin, facing -Y; .L is +X)
    ('hips', None, (0, 0, 0.95), (0, 0.08, 0.95)),
    ('spine', 'hips', (0, 0, 0.95), (0, 0.0, 1.2)),
    ('chest', 'spine', (0, 0, 1.2), (0, 0.0, 1.43)),
    ('neck', 'chest', (0, 0, 1.43), (0, 0.0, 1.53)),
    ('head', 'neck', (0, 0, 1.53), (0, 0.0, 1.78)),
    ('upper_arm.L', 'chest', (0.19, 0, 1.43), (0.21, 0.03, 1.13)),
    ('forearm.L', 'upper_arm.L', (0.21, 0.03, 1.13), (0.21, 0.0, 0.86)),
    ('hand.L', 'forearm.L', (0.21, 0.0, 0.86), (0.21, 0.0, 0.7)),
    ('thigh.L', 'hips', (0.1, 0, 0.92), (0.1, -0.03, 0.53)),
    ('shin.L', 'thigh.L', (0.1, -0.03, 0.53), (0.1, 0.0, 0.146)),
    ('foot.L', 'shin.L', (0.1, 0.0, 0.146), (0.1, -0.165, 0.0)),
)

PROXY_SHAPES = {  # bone kind: (half width X, half depth, colour or None for the side colour), boxes along the bone
    'spine': (0.15, 0.1, TORSO), 'chest': (0.19, 0.11, TORSO), 'neck': (0.05, 0.05, SKIN), 'head': (0.1, 0.11, SKIN),
    'upper_arm': (0.05, 0.05, None), 'forearm': (0.04, 0.04, None), 'hand': (0.035, 0.015, SKIN),
    'thigh': (0.07, 0.07, None), 'shin': (0.055, 0.055, None), 'foot': (0.045, 0.04, None),
}


def mirrored():
    for name, parent, head, tail in PROXY_BONES:
        yield name, parent, head, tail
        if name.endswith('.L'):
            yield (name[:-2] + '.R', parent[:-2] + '.R' if parent and parent.endswith('.L') else parent,
                   (-head[0], head[1], head[2]), (-tail[0], tail[1], tail[2]))


def build_proxy(coll, rig):
    arm = bpy.data.armatures.new(gb.PROXY)
    obj = bpy.data.objects.new(gb.PROXY, arm)
    coll.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    lengths = {}
    for name, parent, head, tail in mirrored():
        b = arm.edit_bones.new(name)
        b.head, b.tail, b.roll = Vector(head), Vector(tail), 0.0
        if parent:
            b.parent = arm.edit_bones[parent]
            b.use_connect = name.split('.')[0] in ('forearm', 'hand', 'shin', 'foot')
        lengths[name] = (Vector(tail) - Vector(head)).length
    bpy.ops.object.mode_set(mode='POSE')
    pb = obj.pose.bones
    c = pb['hips'].constraints.new('COPY_TRANSFORMS')
    c.name, c.target, c.subtarget = 'Body', rig, gb.PELVIS
    for side in ('L', 'R'):
        # Pole angles found by measuring the solved elbow / knee against its hint (roll 0 bones).
        for chain, goal, pole, angle in (('forearm', 'IKT_Hand', 'PRV_Elbow', 90.0), ('shin', 'IKT_Foot', 'PRV_Knee', -90.0)):
            ik = pb['%s.%s' % (chain, side)].constraints.new('IK')
            ik.name, ik.target, ik.subtarget = 'IK', rig, '%s.%s' % (goal, side)
            ik.pole_target, ik.pole_subtarget = rig, '%s.%s' % (pole, side)
            ik.pole_angle = math.radians(angle)
            ik.chain_count = 2
        foot = pb['foot.' + side].constraints.new('COPY_ROTATION')
        foot.name, foot.target, foot.subtarget = 'Foot', rig, 'IKT_Foot.' + side
    bpy.ops.object.mode_set(mode='OBJECT')
    arm.display_type = 'STICK'

    # Meshes, bone-parented (a bone-parented child sits at the bone's tail, Y along the bone).
    for name, _, head, tail in mirrored():
        kind = name.split('.')[0]
        length = lengths[name]
        if kind == 'hips':
            mesh = box_mesh('MSH_' + name, (-0.17, -0.18, -0.12), (0.17, 0.02, 0.06))
            color = TORSO
        else:
            hw, hd, color = PROXY_SHAPES[kind]
            if color is None:
                color = LEFT if name.endswith('.L') else RIGHT
            if kind == 'head':
                mesh = sphere_mesh('MSH_' + name, 0.12, (0, -length * 0.5, 0))
            else:
                mesh = box_mesh('MSH_' + name, (-hw, -length, -hd), (hw, 0.0, hd))
        part = mesh_object('MSH_' + name, mesh, coll, color)
        part.parent = obj
        part.parent_type = 'BONE'
        part.parent_bone = name
        part.hide_select = True
    return obj


def control_markers(coll, rig):
    for name, radius in [(n, 0.035) for n in gb.LIMB_BONES] + [(gb.PELVIS, 0.05)]:
        marker = mesh_object('MRK_' + name, sphere_mesh('MRK_' + name, radius, (0, -0.08, 0)), coll, CONTROL)
        marker.parent = rig
        marker.parent_type = 'BONE'
        marker.parent_bone = name
        marker.hide_select = True


def build_cameras(coll):
    target = bpy.data.objects.new('CAM_Target', None)
    target.empty_display_type = 'SPHERE'
    target.empty_display_size = 0.05
    coll.objects.link(target)
    cams = {}
    for name, location, rotation in (('CAM_Front', (0.0, 6.0, 0.0), (90.0, 0.0, 180.0)),
                                     ('CAM_Side', (-5.44, 2.54, 0.0), (90.0, 0.0, -115.0))):
        data = bpy.data.cameras.new(name)
        data.type = 'ORTHO'
        data.ortho_scale = 3.6
        data.clip_end = 50.0
        cam = bpy.data.objects.new(name, data)
        cam.location = location
        cam.rotation_euler = [math.radians(a) for a in rotation]
        cam.parent = target
        coll.objects.link(cam)
        cams[name] = cam
    bpy.context.scene.camera = cams['CAM_Side']
    return cams


def build_actions(rig):
    data = gm.default_gait()
    rig.animation_data_create()
    for name in gm.LOOP_NAMES:
        action = bpy.data.actions.new(name)
        gb.write_loop(action, data['shape']['up' if name == 'climb_up' else 'down'], name == 'climb_down')
        action.use_cyclic = True
    for t in data['transitions']:
        action = bpy.data.actions.new(t['kind'])
        gb.write_transition(action, t)
    order = gm.LOOP_NAMES + ('GroundMount', 'AirCatch', 'TopEntry', 'TopExit', 'SideExit', 'BottomStepOff')
    for name in reversed(order):
        action = bpy.data.actions[name]
        action.use_fake_user = True
        action.use_frame_range = True
        action.frame_start, action.frame_end = 0, gb.FRAMES
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 0, action)
        try:
            strip.action_slot = action.slots[0]
        except (AttributeError, TypeError):
            pass
        track.mute = True
    gb.use_action('climb_up')


def preview_text():
    parts = []
    for module in ('gait_model.py', 'gait_blend.py'):
        with open(os.path.join(HERE, module), encoding='utf-8') as handle:
            parts.append('# ---- %s (copied by build_gait_blend.py; the tools folder holds the source)\n' % module + handle.read())
    parts.append('\nregister_preview()\n')
    text = bpy.data.texts.new('ladder_preview.py')
    text.from_string('\n'.join(parts))
    text.use_module = True


def setup_render():
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 0, gb.FRAMES
    scene.render.fps = 108  # one ascent cycle (1 m) at the owner's 0.9 m/s climbing speed
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 960, 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'OBJECT'
    shading.show_shadows = False
    shading.show_cavity = True
    shading.show_object_outline = True
    world = bpy.data.worlds.new('World')
    world.color = (0.82, 0.86, 0.9)
    scene.world = world


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    setup_render()
    build_ladder()
    widgets = widget_meshes(collection('Widgets', hide=True))
    bpy.data.collections['Widgets'].hide_viewport = True
    rig_coll = collection('Rig')
    rig = build_rig(rig_coll, widgets)
    control_markers(rig_coll, rig)
    build_proxy(collection('Proxy'), rig)
    build_cameras(collection('Cameras'))
    build_actions(rig)
    preview_text()
    gb.register_preview()
    bpy.context.scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=OUT, compress=True)
    print('wrote', OUT)


main()
