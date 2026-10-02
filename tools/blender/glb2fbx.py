# Blender batch converter: GLB -> FBX for Unity's ModelImporter (Humanoid avatars + humanoid animation clips).
#   blender -b --factory-startup -P glb2fbx.py -- <jobs.txt>
# jobs.txt: one "<in.glb>|<out.fbx>|<mode>" per line; mode = model (mesh + rig, textures copied beside it)
#           or anim (armature + every action as its own take, no meshes needed by Unity's clip import).
import sys, os, bpy
from mathutils import Vector

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def upright(arm):
    """Stand a rig up in Blender's world (Z up). Some GLBs carry a skeleton that is already Z-up in armature space under an
    extra +90 deg X object rotation (kevin.glb), so the rest pose and every clip lie on their back; Unity then measures the
    avatar's hip height as ~0 and humanoid clips put the body tens of metres away. Rotate the object so hips -> head
    points up; the object scale and the actions are left alone (pose channels are relative to the rest)."""
    bones = arm.data.bones
    def find(*keys):
        for k in keys:
            for b in bones:
                if k in b.name.lower():
                    return b
        return None
    hips, head = find('hips', 'pelvis', 'hip'), find('head')
    if hips is None or head is None:
        return False
    up_arm = (head.head_local - hips.head_local).normalized()
    up_world = (arm.matrix_world.to_3x3().normalized() @ up_arm).normalized()
    if up_world.dot(Vector((0, 0, 1))) > 0.9:
        return False
    arm.rotation_mode = 'QUATERNION'
    arm.rotation_quaternion = up_arm.rotation_difference(Vector((0, 0, 1)))
    print('  upright:', arm.name, 'was up', tuple(round(v, 2) for v in up_world))
    return True

def convert(src, dst, mode):
    reset()
    bpy.ops.import_scene.gltf(filepath=src, bone_heuristic='TEMPERANCE', guess_original_bind_pose=False)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    for a in arms:
        if a.parent is None:
            upright(a)
    if mode == 'anim':
        # Unity only needs the skeleton for clips; dropping the meshes keeps the files small
        for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
            bpy.data.objects.remove(o, do_unlink=True)
        for a in arms:
            if a.animation_data is None:
                a.animation_data_create()
            a.animation_data.action = None
            for t in list(a.animation_data.nla_tracks):
                a.animation_data.nla_tracks.remove(t)
    if mode == 'model':
        # unpack the GLB's embedded textures beside the FBX first, so the FBX references real files (ZU.Editor builds
        # the materials from them by name). Packed images are written as their original bytes (WebP in Tripo GLBs) whatever
        # the name says, so tools/blender/fix_textures.py re-encodes them as real PNGs afterwards
        tex_dir = os.path.splitext(dst)[0] + '_tex'
        os.makedirs(tex_dir, exist_ok=True)
        for img in bpy.data.images:
            if img.packed_file or img.has_data:
                name = bpy.path.clean_name(img.name)
                img.filepath_raw = os.path.join(tex_dir, name + '.png')
                img.file_format = 'PNG'
                try:
                    img.save()
                except Exception as e:
                    print('  texture', img.name, 'not saved:', e)
    bpy.ops.export_scene.fbx(
        filepath=dst, use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
        bake_anim=(mode == 'anim'), bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode='RELATIVE', embed_textures=False, mesh_smooth_type='FACE')
    n_act = len(bpy.data.actions)
    print('OK', os.path.basename(src), '->', dst, mode, 'actions', n_act, 'armatures', len(arms))

def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    jobs = [l.strip().split('|') for l in open(argv[0], encoding='utf-8') if l.strip() and not l.startswith('#')]
    failed = 0
    for src, dst, mode in jobs:
        try:
            convert(src, dst, mode)
        except Exception as e:
            failed += 1
            print('FAIL', src, e)
    print('DONE', len(jobs) - failed, 'ok,', failed, 'failed')

main()
