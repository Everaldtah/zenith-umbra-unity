# Blender batch converter: GLB -> FBX for Unity's ModelImporter (Humanoid avatars + humanoid animation clips).
#   blender -b --factory-startup -P glb2fbx.py -- <jobs.txt>
# jobs.txt: one "<in.glb>|<out.fbx>|<mode>" per line; mode = model (mesh + rig, textures copied beside it)
#           or anim (armature + every action as its own take, no meshes needed by Unity's clip import).
import sys, os, bpy

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def convert(src, dst, mode):
    reset()
    bpy.ops.import_scene.gltf(filepath=src, bone_heuristic='TEMPERANCE', guess_original_bind_pose=False)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
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
        # the materials from them by name)
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
