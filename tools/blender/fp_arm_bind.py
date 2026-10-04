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


def crop(o):
    """keep the hand + ~1 hand length of forearm (fit.json 'crop'): Tripo often models the whole arm, sometimes bent at the
    elbow - the rest would hang off the forearm bone past the hero's elbow"""
    cr = fit.get("crop")
    if not cr:
        return 0
    org = gltf_to_blender(Vector(cr["origin"])); ax = gltf_to_blender(Vector(cr["axis"])).normalized()
    bm = bmesh.new(); bm.from_mesh(o.data)
    gone = []
    for f in bm.faces:
        rel = f.calc_center_median() - org; t = rel.dot(ax)
        if t < cr["keep_from"] or (t < 0 and (rel - ax * t).length > cr["radius"]):
            gone.append(f)
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    # only the piece the hand is on (a crop through a bent arm can leave a sleeve chunk floating): walk the surface out
    # from the face nearest the middle fingertip, drop every face not reached
    tipk = next((k for k in (f"middle_tip_{side}", f"index_tip_{side}") if k in J), None)
    if tipk and bm.faces:
        tip = gltf_to_blender(J[tipk])
        bm.faces.ensure_lookup_table()
        start = min(bm.faces, key=lambda f: (f.calc_center_median() - tip).length_squared)
        seen = {start}; todo = [start]
        while todo:
            f = todo.pop()
            for e in f.edges:
                for g in e.link_faces:
                    if g not in seen:
                        seen.add(g); todo.append(g)
        loose = [f for f in bm.faces if f not in seen]
        if loose and len(seen) > 0.2 * len(bm.faces):
            bmesh.ops.delete(bm, geom=loose, context="FACES"); gone += loose
    bm.to_mesh(o.data); bm.free(); o.data.update()
    return len(gone)


def weld(o):
    """glTF splits vertices along every UV seam; welded back (UVs live on the face corners, so they survive) the surface
    is one sheet: bone heat solves it as a whole and two halves of a seam can't get different weights and tear apart"""
    bm = bmesh.new(); bm.from_mesh(o.data)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(o.data); bm.free(); o.data.update()
    return before - len(o.data.vertices)


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
    # the second influence only from the nearest bone's own chain (its parent or a child): two neighbouring fingers never
    # share a vertex, so a curl can't pull webbing between them
    kin = {}
    for b in rig.data.bones:
        k = set(c.name for c in b.children)
        if b.parent: k.add(b.parent.name)
        kin[b.name] = k
    seg = {n: (a, b) for n, a, b in bones}
    for v in mesh.data.vertices:
        p = mesh.matrix_world @ v.co
        d1, n1 = min((seg_dist(p, a, b), n) for n, a, b in bones)
        cand = [(seg_dist(p, *seg[k]), k) for k in kin[n1]]
        d = [(d1, n1)] + ([min(cand)] if cand else [])
        w = [1.0 / max(x, 1e-4) ** 4 for x, _ in d]; s = sum(w)
        for (x, n), wi in zip(d, w):
            groups[n].add([v.index], wi / s, "REPLACE")


def finger_reach(mesh, rig):
    """a finger bone moves only the surface around that finger: weights from vertices further than ~0.12 hand lengths
    from the finger's own bones are dropped (a cuff, the far side of the palm or a sleeve otherwise follows the curl)"""
    hl = (fit.get("crop") or {}).get("hand_len", 0.2)
    reach = 0.12 * hl
    segs = {}
    for b in rig.data.bones:
        if any(b.name.startswith(f) for f in FING):
            f = next(f for f in FING if b.name.startswith(f))
            segs.setdefault(f, []).append((rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local))
    gi_f = {g.index: next((f for f in FING if g.name.startswith(f)), None) for g in mesh.vertex_groups}
    keep = {g.index: g for g in mesh.vertex_groups if gi_f[g.index] is None}
    fore = mesh.vertex_groups.get(f"forearm_{side}"); hand = mesh.vertex_groups.get(f"hand_{side}")
    n = 0
    for v in mesh.data.vertices:
        p = mesh.matrix_world @ v.co
        ws = [(g.group, g.weight) for g in v.groups]
        drop = [gi for gi, w in ws if gi_f.get(gi) and w > 1e-6 and min(seg_dist(p, a, b) for a, b in segs[gi_f[gi]]) > reach]
        if not drop:
            continue
        n += 1
        for gi in drop:
            mesh.vertex_groups[gi].remove([v.index])
        rest = [(gi, w) for gi, w in ws if gi not in drop and w > 1e-6]
        s = sum(w for _, w in rest)
        if s > 1e-6:
            for gi, w in rest:
                mesh.vertex_groups[gi].add([v.index], w / s, "REPLACE")
        elif hand is not None:
            hand.add([v.index], 1.0, "REPLACE")
        elif fore is not None:
            fore.add([v.index], 1.0, "REPLACE")
    return n


def wrist_clean(mesh):
    """nothing behind the wrist follows a finger: a wide sleeve or puffed cuff sits far from the forearm bone and can pick
    up finger weights (it then shatters when the hand closes) - those weights go, the rest is renormalized, and a vertex
    left with none goes to the forearm"""
    cr = fit.get("crop"); hk = f"hand_{side}"
    if not cr or hk not in J:
        return 0
    wrist = gltf_to_blender(J[hk]); ax = gltf_to_blender(Vector(cr["axis"])).normalized()
    hl = cr.get("hand_len", 0.1)
    fing = {g.index for g in mesh.vertex_groups if any(g.name.startswith(f) for f in FING)}
    keep = {g.index: g for g in mesh.vertex_groups if g.index not in fing}
    fore = mesh.vertex_groups.get(f"forearm_{side}")
    n = 0
    for v in mesh.data.vertices:
        if (mesh.matrix_world @ v.co - wrist).dot(ax) > -0.02 * hl:
            continue
        ws = [(g.group, g.weight) for g in v.groups]
        if not any(gi in fing and w > 1e-4 for gi, w in ws):
            continue
        n += 1
        for gi, _ in ws:
            if gi in fing:
                mesh.vertex_groups[gi].remove([v.index])
        rest = [(gi, w) for gi, w in ws if gi not in fing and w > 1e-6]
        s = sum(w for _, w in rest)
        if s > 1e-6:
            for gi, w in rest:
                keep[gi].add([v.index], w / s, "REPLACE")
        elif fore is not None:
            fore.add([v.index], 1.0, "REPLACE")
    return n


def bind(mesh, rig):
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    weighted = sum(1 for v in mesh.data.vertices if len(v.groups) > 0)
    if weighted < 0.95 * len(mesh.data.vertices):
        print(f"heat weights covered {weighted}/{len(mesh.data.vertices)} vertices: distance weights instead")
        distance_weights(mesh, rig)
    print("wrist clean:", wrist_clean(mesh), "vertices behind the wrist lost their finger weights")
    print("finger reach:", finger_reach(mesh, rig), "vertices too far from a finger lost its weight")
    return weighted


def role_of(img):
    """basecolor / normal / metallic_roughness by what the image feeds in the glTF importer's Principled BSDF (HeroImport's
    MakeMaterial picks the maps by these names); None for anything else"""
    for m in bpy.data.materials:
        if not m.use_nodes:
            continue
        for n in m.node_tree.nodes:
            if n.type != "TEX_IMAGE" or n.image != img:
                continue
            for l in n.outputs["Color"].links:
                to = l.to_node; sock = l.to_socket.name
                if to.type == "BSDF_PRINCIPLED" and sock == "Base Color":
                    return "basecolor"
                if to.type == "NORMAL_MAP":
                    return "normal"
                if to.type in ("SEPARATE_COLOR", "SEPRGB", "SEPARATE_RGB"):
                    return "metallic_roughness"
    return None


def save_textures(tex_dir):
    """the GLB's packed images written out as PNG beside the FBX (the exporter copies files, not packed data), named by
    role, plus gltf_material.json with the material factors (MakeMaterial applies them: the FBX doesn't carry them)"""
    os.makedirs(tex_dir, exist_ok=True)
    for img in bpy.data.images:
        if img.type != "IMAGE" or not img.has_data and not img.packed_file:
            continue
        role = role_of(img)
        name = role or "".join(c if c.isalnum() or c in "-_" else "_" for c in os.path.splitext(img.name)[0])
        w, h = img.size
        if max(w, h) > 2048:                                 # 2K is plenty a forearm's length from the lens (and keeps the repo light)
            k = 2048 / max(w, h); img.scale(max(1, int(w * k)), max(1, int(h * k)))
        img.filepath_raw = os.path.join(tex_dir, name + ".png"); img.file_format = "PNG"
        try:
            img.save()
        except Exception as e:
            print("texture not saved", img.name, e)
    for m in bpy.data.materials:
        if m.use_nodes:
            p = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if p is not None:
                bc = list(p.inputs["Base Color"].default_value)[:3]
                json.dump({"metallicFactor": p.inputs["Metallic"].default_value, "roughnessFactor": p.inputs["Roughness"].default_value,
                           "baseColorFactor": bc if not p.inputs["Base Color"].links else [1, 1, 1]}, open(os.path.join(tex_dir, "gltf_material.json"), "w"))
                break


def export(rig, mesh, path):
    save_textures(os.path.join(os.path.dirname(path), os.path.splitext(os.path.basename(path))[0] + "_tex"))
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = rig
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             bake_anim=False, path_mode="STRIP", embed_textures=False, axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE")


def preview(rig, mesh, png):
    """a check render: the arm at rest and with every finger curled into a fist (the skinning's worst case), side by side
    in one image - Cycles on the CPU, low samples (headless-safe)"""
    import numpy as np
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = 8
    sc.render.resolution_x, sc.render.resolution_y = 480, 360
    if not sc.world:
        sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.35, 0.35, 0.38)
    bb = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
    ctr = sum(bb, Vector()) / 8; size = max((b - ctr).length for b in bb)
    back = gltf_to_blender(Vector(fit["axes"]["back"])).normalized()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = size * 2.2
    side = gltf_to_blender(Vector(fit["axes"]["across"])).normalized()
    cam.location = ctr + (back * 0.8 + side * 0.6).normalized() * size * 4
    cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler(); sc.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = cam.rotation_euler; sun.data.energy = 3
    outs = []
    for k, curl in (("rest", 0.0), ("fist", 1.0)):
        for pb in rig.pose.bones:
            pb.rotation_mode = "XYZ"
            n = pb.name
            if any(n.startswith(f) for f in FING):
                pb.rotation_euler = (-1.25 * curl if not n.startswith("thumb") else -0.6 * curl, 0, 0)
        bpy.context.view_layer.update()
        p = png.replace(".png", f"_{k}.png"); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); outs.append(p)
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    return outs


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
    cut = crop(mesh)
    welded = weld(mesh)
    n = decimate(mesh)
    rig = build_armature(side)
    w = bind(mesh, rig)
    out = os.path.join(out_dir, ident + ".fbx")
    export(rig, mesh, out)
    if "--preview" in argv:
        try:
            preview(rig, mesh, os.path.join(out_dir, ident + "_check.png"))
        except Exception as e:
            print("preview failed", e)
    print("OK", out, json.dumps({"id": ident, "cropped_faces": cut, "welded": welded, "tris": n, "heat_weighted": w, "verts": len(mesh.data.vertices), "bones": len(rig.data.bones)}))
    if mirror and ident.endswith("_" + side):
        r2, m2 = mirror_side(rig, mesh)
        out2 = os.path.join(out_dir, ident[:-1] + ("L" if side == "R" else "R") + ".fbx")
        export(r2, m2, out2)
        print("OK", out2, json.dumps({"id": ident[:-1] + ("L" if side == "R" else "R"), "mirrored": True}))
except Exception as e:
    import traceback; traceback.print_exc()
    print("FAIL", ident, str(e)[:300])
