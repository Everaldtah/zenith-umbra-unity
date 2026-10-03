# Blender batch converter: GLB -> FBX for Unity's ModelImporter (Humanoid avatars + humanoid animation clips).
#   blender -b --factory-startup -P glb2fbx.py -- <jobs.txt>
# jobs.txt: one "<in.glb>|<out.fbx>|<mode>" per line; mode = model (mesh + rig, textures copied beside it),
#           prop (a map prop: like model, plus one mesh decimated to ~8k tris with _LOD0/_LOD1/_LOD2 levels)
#           or anim (armature + every action as its own take, no meshes needed by Unity's clip import).
import sys, os, bpy
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'export'))
from gltf_factors import write_factors   # the glTF material factors the FBX loses
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
    # only a rig lying down (hips -> head more than ~70 deg off vertical): hunched bosses, serpents and birds lean by design
    if up_world.dot(Vector((0, 0, 1))) > 0.35:
        return False
    arm.rotation_mode = 'QUATERNION'
    arm.rotation_quaternion = up_arm.rotation_difference(Vector((0, 0, 1)))
    print('  upright:', arm.name, 'was up', tuple(round(v, 2) for v in up_world))
    return True

def image_roles():
    """What each image does in the glTF materials, from the node graph: walk forward from every Image Texture node to the
    first shader socket it reaches - basecolor, normal, rm (metallic / roughness), emissive or occlusion."""
    roles = {}
    def walk(sock, depth=0):
        if depth > 6:
            return None
        for link in sock.links:
            n, inp = link.to_node, link.to_socket.name.lower()
            if n.type == 'BSDF_PRINCIPLED':
                if inp == 'base color': return 'basecolor'
                if inp in ('metallic', 'roughness'): return 'rm'
                if inp.startswith('emission'): return 'emissive'
                if inp == 'alpha': return 'basecolor'
                if inp == 'normal': return 'normal'
            if n.type == 'NORMAL_MAP': return 'normal'
            if n.type == 'GROUP' and 'occlusion' in inp: return 'occlusion'
            for out in n.outputs:
                r = walk(out, depth + 1)
                if r: return r
        return None
    for m in bpy.data.materials:
        if not m.use_nodes:
            continue
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image is not None and n.image.name not in roles:
                for out in n.outputs:
                    r = walk(out)
                    if r:
                        roles[n.image.name] = r
                        break
    return roles

PROP_TRIS = 8000
LOD_RATIOS = (0.40, 0.12)   # LOD1 / LOD2 of the LOD0 mesh (the engine's prop LOD convention, ZU.EditorTools.PropImport)

def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

def decimate(o, ratio):
    m = o.modifiers.new('decimate', 'DECIMATE')
    m.decimate_type = 'COLLAPSE'; m.ratio = max(0.001, min(1.0, ratio)); m.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)

def make_lods(base):
    """a map prop: one mesh decimated to ~PROP_TRIS as <base>_LOD0, plus <base>_LOD1 / _LOD2 copies at LOD_RATIOS of it -
    one material on every level (Unity's importer reads the _LODn names; PropImport builds the LODGroup)"""
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        return
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    lod0 = bpy.context.view_layer.objects.active
    n = tri_count(lod0)
    if n > PROP_TRIS:
        decimate(lod0, PROP_TRIS / n)
    lod0.name = lod0.data.name = f'{base}_LOD0'
    counts = [tri_count(lod0)]
    for i, k in enumerate(LOD_RATIOS, 1):
        c = lod0.copy(); c.data = lod0.data.copy()
        for col in lod0.users_collection:
            col.objects.link(c)
        decimate(c, k)
        c.name = c.data.name = f'{base}_LOD{i}'
        counts.append(tri_count(c))
    print('  lods', base, 'from', n, 'tris ->', counts)

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
    if mode in ('model', 'prop'):
        # unpack the GLB's embedded textures beside the FBX first, so the FBX references real files (ZU.Editor builds
        # the materials from them by name). Packed images are written as their original bytes (WebP in Tripo GLBs) whatever
        # the name says, so tools/blender/fix_textures.py re-encodes them as real PNGs afterwards
        tex_dir = os.path.splitext(dst)[0] + '_tex'
        os.makedirs(tex_dir, exist_ok=True)
        write_factors(src, dst)
        roles = image_roles()
        base = os.path.splitext(os.path.basename(dst))[0]
        used = set()
        for img in bpy.data.images:
            if img.packed_file or img.has_data:
                name = bpy.path.clean_name(img.name)
                role = roles.get(img.name)
                if role and role not in name.lower():
                    # generic names (Image_0, Image_1) say nothing; ZU.Editor finds the maps by their role in the name
                    name = f'{base}_{role}'
                    k = 2
                    while name in used:
                        name = f'{base}_{role}{k}'; k += 1
                used.add(name)
                img.filepath_raw = os.path.join(tex_dir, name + '.png')
                img.file_format = 'PNG'
                try:
                    img.save()
                except Exception as e:
                    print('  texture', img.name, 'not saved:', e)
    if mode == 'prop':
        make_lods(os.path.splitext(os.path.basename(dst))[0])
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
