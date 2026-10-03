"""Find the skeleton of a Tripo first-person arm: one forearm + hand laid straight, palm down, fingers spread.

python fp_arm_fit.py <arm.glb> <R|L> [out.json]
Writes <out>.json (canonical frame + joints, metres in the GLB's own units) and <out>.png (top + side view with the
joints drawn) for a visual check. Joint names follow the hero rigs (forearm_S, hand_S, thumb1..3_S, index1..3_S, ...,
plus *_tip), so the Blender binder and Fingers.cs see the same hierarchy as on the heroes.

Method: PCA of the surface -> the long axis (forearm -> fingertips: the end whose far slab splits into the most separate
pieces), the width axis and the thickness axis (palm normal). The distal slab is cut from the fingertips back toward the
wrist; its connected surface pieces are the fingers (the thumb is the piece that splits off furthest back, on the side
the hand's handedness says). Each finger's centre line comes from slicing it along its own principal direction; the
MCP / PIP / DIP joints sit at 0 / 0.45 / 0.72 of its length from where it leaves the palm.
"""
import json, sys
import numpy as np
import trimesh
import networkx as nx


def load(path):
    s = trimesh.load(path, force='scene')
    return s.to_geometry() if isinstance(s, trimesh.Scene) else s


def frame(m):
    v = m.vertices - m.vertices.mean(0)
    w, U = np.linalg.eigh(np.cov(v.T))           # ascending
    A, W, N = U[:, 2], U[:, 1], U[:, 0]          # long, width, thickness
    return A, W, N


def pieces(m, mask_faces):
    """connected components (by shared edges) of the faces in the mask"""
    f = np.nonzero(mask_faces)[0]
    if len(f) == 0:
        return []
    adj = m.face_adjacency
    keep = np.isin(adj[:, 0], f) & np.isin(adj[:, 1], f)
    g = nx.Graph(); g.add_nodes_from(f.tolist()); g.add_edges_from(adj[keep].tolist())
    return [np.array(sorted(c)) for c in nx.connected_components(g) if len(c) > 12]


def distal_split(m, A):
    """the long-axis sign and the slab depth at which the hand end splits into its fingers"""
    c = m.triangles_center @ A
    lo, hi = c.min(), c.max(); L = hi - lo
    best = None
    for sign in (1, -1):
        cc = c * sign; top = cc.max()
        for frac in np.linspace(0.05, 0.4, 15):
            ps = pieces(m, cc > top - frac * L)
            if best is None or len(ps) > best[2] or (len(ps) == best[2] and frac < best[1] and len(ps) >= 4):
                best = (sign, frac, len(ps))
    return best


def centre_line(m, faces, A):
    pts = m.triangles_center[faces]
    d = pts - pts.mean(0)
    _, _, vt = np.linalg.svd(d, full_matrices=False)
    ax = vt[0]
    if ax @ A < 0:
        ax = -ax
    t = d @ ax
    bins = np.linspace(t.min(), t.max(), 12)
    cl = []
    for a, b in zip(bins[:-1], bins[1:]):
        sel = (t >= a) & (t <= b)
        if sel.sum() > 3:
            cl.append(pts[sel].mean(0))
    return np.array(cl), ax


def fit(path, side):
    m = load(path)
    A, W, N = frame(m)
    sign, frac, n = distal_split(m, A)
    A = A * sign
    c = m.triangles_center @ A
    L = c.max() - c.min()
    # widen the slab until the thumb (furthest back) separates too, without letting the palm in: stop when the count drops
    fingers = []
    for f in np.linspace(frac, min(0.62, frac + 0.3), 16):
        ps = pieces(m, c > c.max() - f * L)
        if len(ps) >= len(fingers) and len(ps) <= 5:
            fingers = ps
        elif len(ps) < len(fingers):
            break
    if len(fingers) < 4:
        raise SystemExit(f"{path}: only {len(fingers)} finger pieces found - fingers not separated in the mesh?")
    lines = []
    for p in fingers:
        cl, ax = centre_line(m, p, A)
        base = cl[0]; tip = cl[-1]
        lines.append(dict(base=base, tip=tip, ax=ax, len=float(np.linalg.norm(tip - base)), across=float(cl.mean(0) @ W), back=float(base @ A)))
    # the thumb: the piece whose base is furthest back along the arm; the rest ordered across the hand from the thumb side
    th = min(range(len(lines)), key=lambda i: lines[i]['back']) if len(lines) == 5 else None
    rest = [l for i, l in enumerate(lines) if i != th]
    thumb = lines[th] if th is not None else None
    thumb_faces = fingers[th] if th is not None else None
    tside = np.sign((thumb['across'] if thumb else 0) - np.mean([l['across'] for l in rest])) or 1
    rest.sort(key=lambda l: -tside * l['across'])          # index (nearest the thumb) .. pinky
    names = ['index', 'middle', 'ring', 'pinky'][:len(rest)]
    # palm normal sign: for a palm-down RIGHT hand seen from above (back of the hand up) the thumb is on the left
    # (W x A = up convention); flip N so the back of the hand is +N
    up = np.cross(W * tside, A) * (1 if side == 'R' else -1)
    if up @ N < 0:
        N = -N
    J = {}
    knuck = np.mean([l['base'] for l in rest], 0)
    flen = np.mean([l['len'] for l in rest])
    # the wrist: walking back from the knuckles, where the cross-section's width first drops under 80% of the palm's
    # (the thumb's own vertices excluded), kept to a plausible palm of 0.8-1.3 finger lengths
    V = m.vertices; va = V @ A; vw = V @ W; ka = knuck @ A
    tset = set(m.faces[thumb_faces].ravel().tolist()) if thumb else set()
    keepv = np.array([i not in tset for i in range(len(V))]) if tset else np.ones(len(V), bool)
    def width(a, h=0.004):
        s = keepv & (np.abs(va - a) < h)
        return vw[s].max() - vw[s].min() if s.sum() > 4 else 0
    palm = max(width(ka - flen * f) for f in (0.15, 0.3, 0.45))
    back = flen * 1.05
    for f in np.linspace(0.8, 1.3, 26):
        if width(ka - flen * f) < 0.8 * palm:
            back = flen * f; break
    wrist = knuck - A * back
    J['hand_' + side] = wrist
    lo = m.vertices[np.argmin(m.vertices @ A)]
    J['forearm_' + side] = wrist - A * max(0.05, (wrist - lo) @ A * 0.9)
    for nm, l in zip(names, rest):
        b, t = l['base'], l['tip']
        for k, u in ((1, 0.0), (2, 0.45), (3, 0.72)):
            J[f'{nm}{k}_{side}'] = b + (t - b) * u
        J[f'{nm}_tip_{side}'] = t
    if thumb:
        b, t = thumb['base'], thumb['tip']
        b = b + (wrist - b) * 0.25                          # the thumb's CMC sits back toward the wrist
        for k, u in ((1, 0.0), (2, 0.45), (3, 0.75)):
            J[f'thumb{k}_{side}'] = b + (t - b) * u
        J[f'thumb_tip_{side}'] = t
    return m, dict(side=side, axes=dict(along=A.tolist(), across=W.tolist(), back=N.tolist()), length=float(L),
                   fingers=len(lines), joints={k: v.tolist() for k, v in J.items()})


def draw(m, info, png):
    import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
    A = np.array(info['axes']['along']); W = np.array(info['axes']['across']); N = np.array(info['axes']['back'])
    V = m.vertices
    fig, ax = plt.subplots(1, 2, figsize=(12, 5))
    for a, (u, v, t) in zip(ax, ((A, W, 'top (along x across)'), (A, N, 'side (along x back)'))):
        a.scatter(V @ u, V @ v, s=0.2, c='#999')
        for k, p in info['joints'].items():
            p = np.array(p); a.plot(p @ u, p @ v, 'o', ms=4, c='r' if 'tip' in k else 'b')
        a.set_aspect('equal'); a.set_title(t)
    fig.savefig(png, dpi=90); plt.close(fig)


if __name__ == '__main__':
    path, side = sys.argv[1], sys.argv[2]
    out = sys.argv[3] if len(sys.argv) > 3 else path.rsplit('.', 1)[0] + '_fit.json'
    m, info = fit(path, side)
    json.dump(info, open(out, 'w'), indent=1)
    draw(m, info, out.rsplit('.', 1)[0] + '.png')
    print(out, info['fingers'], 'fingers, length', round(info['length'], 3))
