# Bind a Tripo first-person arm (one forearm + hand, palm down) to its own small skeleton and export FBX for Unity.
#   blender -b --factory-startup -P tools/blender/fp_arm_bind.py -- <arm.glb> <fit.json> <out_dir> <id> [--mirror] [--tris 12000]
# fit.json comes from tools/fpfit/fp_arm_fit.py (joints in the GLB's own frame, names as on the hero rigs). Output:
#   <out_dir>/<id>.fbx (+ <id>_tex/), and with --mirror also <id with _R->_L>.fbx - the same arm reflected across the hand's
#   width axis, bones renamed _L. Bones: forearm_S -> hand_S -> {thumb,index,middle,ring,pinky}{1,2,3}_S, each bone's tail on
#   the next joint (the *_tip joints end the fingers). Skin: Blender's bone heat, or - when heat fails on a non-manifold
#   Tripo surface - inverse-distance-to-segment weights over the two nearest bones. Prints "OK <fbx>" / "FAIL <id> <why>".
import bpy, bmesh, json, sys, os, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
glb, fitp, out_dir, ident = argv[:4]
mirror = "--mirror" in argv
tris = int(argv[argv.index("--tris") + 1]) if "--tris" in argv else 12000
fit = json.load(open(fitp))
side = fit["side"]
J = {k: Vector(v) for k, v in fit["joints"].items()}
FING = ["thumb", "index", "middle", "ring", "pinky"]


def gltf_to_blender(v):
    # glTF is +Y up; Blender's importer turns it to +Z up: (x, y, z) -> (x, -z, y)
    return Vector((v.x, -v.z, v.y))


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_arm():
    bpy.ops.import_scene.gltf(filepath=glb)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError("no mesh in " + glb)
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    arm = bpy.context.view_layer.objects.active
    for o in list(bpy.context.scene.objects):
        if o is not arm and o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    arm.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    arm.name = ident
    return arm


def decimate(o):
    n = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if n > tris:
        m = o.modifiers.new("dec", "DECIMATE"); m.ratio = tris / n
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=m.name)
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def chain(names):
    return [(names[i], names[i + 1]) for i in range(len(names) - 1)]


def build_armature(S):
    back = gltf_to_blender(Vector(fit["axes"]["back"])).normalized()
    a = bpy.data.armatures.new("fp_arm_" + S); ob = bpy.data.objects.new("fp_arm_" + S, a)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    eb = a.edit_bones
    P = {k: gltf_to_blender(v) for k, v in J.items()}

    def bone(name, head, tail, parent=None):
        b = eb.new(name); b.head = P[head]; b.tail = P[tail]
        if (b.tail - b.head).length < 1e-4:
            b.tail = b.head + Vector((0, 0, 0.01))
        b.align_roll(back)                     # the bone's Z = back of the hand: one curl axis convention for every finger
        if parent:
            b.parent = eb[parent]
        return b

    bone(f"forearm_{side}", f"forearm_{side}", f"hand_{side}")
    mid = f"middle1_{side}" if f"middle1_{side}" in P else f"index1_{side}"
    bone(f"hand_{side}", f"hand_{side}", mid, f"forearm_{side}")
    for f in FING:
        js = [f"{f}{k}_{side}" for k in (1, 2, 3)] + [f"{f}_tip_{side}"]
        if not all(j in P for j in js):
            continue
        for k, (h, t) in enumerate(chain(js)):
            bone(h, h, t, f"hand_{side}" if k == 0 else js[k - 1])
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def distance_weights(mesh, rig):
    mesh.vertex_groups.clear()
    bones = [(b.name, rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones]
    groups = {n: mesh.vertex_groups.new(name=n) for n, _, _ in bones}
    for v in mesh.data.vertices:
        p = mesh.matrix_world @ v.co
        d = sorted(((seg_dist(p, a, b), n) for n, a, b in bones))[:2]
        w = [1.0 / max(x, 1e-4) ** 4 for x, _ in d]; s = sum(w)
        for (x, n), wi in zip(d, w):
            groups[n].add([v.index], wi / s, "REPLACE")


def bind(mesh, rig):
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    weighted = sum(1 for v in mesh.data.vertices if len(v.groups) > 0)
    if weighted < 0.95 * len(mesh.data.vertices):
        print(f"heat weights covered {weighted}/{len(mesh.data.vertices)} vertices: distance weights instead")
        distance_weights(mesh, rig)
    return weighted


def save_textures(tex_dir):
    """the GLB's packed images written out as PNG beside the FBX (the exporter copies files, not packed data)"""
    os.makedirs(tex_dir, exist_ok=True)
    for img in bpy.data.images:
        if img.type != "IMAGE" or not img.has_data and not img.packed_file:
            continue
        name = "".join(c if c.isalnum() or c in "-_" else "_" for c in os.path.splitext(img.name)[0]) + ".png"
        img.filepath_raw = os.path.join(tex_dir, name); img.file_format = "PNG"
        try:
            img.save()
        except Exception as e:
            print("texture not saved", img.name, e)


def export(rig, mesh, path):
    save_textures(os.path.join(os.path.dirname(path), os.path.splitext(os.path.basename(path))[0] + "_tex"))
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = rig
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             bake_anim=False, path_mode="COPY", embed_textures=False, axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE")


def mirror_side(rig, mesh):
    """the same arm reflected across the hand's width axis, renamed to the other side"""
    other = "L" if side == "R" else "R"
    W = gltf_to_blender(Vector(fit["axes"]["across"])).normalized()
    # the reflection across the plane normal to W: I - 2 W W^T
    R = Matrix([[1 - 2 * W.x * W.x, -2 * W.x * W.y, -2 * W.x * W.z],
                [-2 * W.y * W.x, 1 - 2 * W.y * W.y, -2 * W.y * W.z],
                [-2 * W.z * W.x, -2 * W.z * W.y, 1 - 2 * W.z * W.z]])
    bpy.ops.object.select_all(action="DESELECT")
    for o in (rig, mesh):
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.duplicate()
    r2 = bpy.context.view_layer.objects.active; m2 = [o for o in bpy.context.selected_objects if o.type == "MESH"][0]
    M = R.to_4x4()
    for o in (r2, m2):
        o.matrix_world = M @ o.matrix_world
    bpy.ops.object.select_all(action="DESELECT")
    for o in (r2, m2):
        o.select_set(True)
    bpy.context.view_layer.objects.active = r2
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # a reflection flips the faces' winding: put the normals back outward
    bpy.context.view_layer.objects.active = m2
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.mesh.flip_normals(); bpy.ops.object.mode_set(mode="OBJECT")
    for b in r2.data.bones:
        b.name = b.name[:-1] + other
    for g in m2.vertex_groups:
        g.name = g.name[:-1] + other
    r2.name = "fp_arm_" + other; m2.name = ident[:-1] + other
    return r2, m2


try:
    clear()
    mesh = import_arm()
    n = decimate(mesh)
    rig = build_armature(side)
    w = bind(mesh, rig)
    out = os.path.join(out_dir, ident + ".fbx")
    export(rig, mesh, out)
    print("OK", out, json.dumps({"id": ident, "tris": n, "heat_weighted": w, "verts": len(mesh.data.vertices), "bones": len(rig.data.bones)}))
    if mirror and ident.endswith("_" + side):
        r2, m2 = mirror_side(rig, mesh)
        out2 = os.path.join(out_dir, ident[:-1] + ("L" if side == "R" else "R") + ".fbx")
        export(r2, m2, out2)
        print("OK", out2, json.dumps({"id": ident[:-1] + ("L" if side == "R" else "R"), "mirrored": True}))
except Exception as e:
    import traceback; traceback.print_exc()
    print("FAIL", ident, str(e)[:300])
