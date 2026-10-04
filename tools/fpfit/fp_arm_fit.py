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



def load(path):
    s = trimesh.load(path, force='scene')
    m = s.to_geometry() if isinstance(s, trimesh.Scene) else s
    # glTF splits vertices along every UV seam: weld by position so the surface is one connected sheet again (otherwise
    # every UV island is its own "piece" and no finger is ever found)
    m.merge_vertices(merge_tex=True, merge_norm=True)
    return m


def frame(m):
    v = m.vertices - m.vertices.mean(0)
    w, U = np.linalg.eigh(np.cov(v.T))           # ascending
    A, W, N = U[:, 2], U[:, 1], U[:, 0]          # long, width, thickness
    return A, W, N


def pieces(m, mask_faces):
    """connected components (by shared edges) of the faces in the mask (sparse graph: fast on 300k-face HD meshes)"""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    f = np.nonzero(mask_faces)[0]
    if len(f) == 0:
        return []
    adj = m.face_adjacency
    keep = mask_faces[adj[:, 0]] & mask_faces[adj[:, 1]]
    a = adj[keep]
    n = len(m.faces)
    g = coo_matrix((np.ones(len(a)), (a[:, 0], a[:, 1])), shape=(n, n))
    _, lab = connected_components(g, directed=False)
    lab = lab[f]
    out = []
    for u in np.unique(lab):
        c = f[lab == u]
        if len(c) > 12:
            out.append(c)
    return out


def fingerlike(m, ps, L, A=None, top=None):
    """the pieces that look like fingers: elongated (scale-free: a finger is 1.6x+ as long as it is thick, a fingertip cap
    ~1.2x - Tripo sizes every model to ~1 m, so a forearm-only arm has big fingers and a whole arm small ones), not broad
    (sleeve flaps, plates), and - given the axis - with the tip out at the hand's end (cuff trim further back never counts);
    at most the five reaching furthest"""
    out = []
    for p in ps:
        v = m.triangles_center[p]; d = v - v.mean(0)
        s = np.sqrt(np.maximum(np.linalg.eigvalsh(np.cov(d.T)), 0)) * 4          # ~full extents, ascending
        thin = s[2] < 0.3 * L and s[1] < 0.07 * L                       # small next to a whole arm
        long_ = s[2] < 0.35 * L and s[1] < 0.12 * L and s[2] > 1.6 * s[1]  # elongated (big forearm-only hands)
        if not (thin or long_):
            continue
        if A is not None and (v @ A).max() < top - 0.12 * L:
            continue
        out.append(p)
    if A is not None and len(out) > 5:
        out = sorted(out, key=lambda p: -(m.triangles_center[p] @ A).max())[:5]
    return out


def distal_split(m, A):
    """the long-axis sign, the slab depth at which the hand end splits into its fingers, and those pieces: the end and
    depth giving 4-5 finger pieces (the deepest such slab: the most finger length), else the most pieces"""
    c = m.triangles_center @ A
    lo, hi = c.min(), c.max(); L = hi - lo
    best = None
    for sign in (1, -1):
        cc = c * sign; top = cc.max()
        for frac in np.linspace(0.03, 0.35, 17):
            fl = fingerlike(m, pieces(m, cc > top - frac * L), L, A * sign, top)
            n = len(fl); good = 4 <= n <= 5
            key = (good, frac if good else -frac, n) if good else (False, n, -frac)
            if best is None or key > best[3]:
                best = (sign, frac, fl, key)
    return best[0], best[1], best[2]


def finger_axis(m, ps, A):
    """the hand's long axis from the fingers themselves: their principal directions, turned to point at the tips, averaged"""
    ds = []
    for p in ps:
        v = m.triangles_center[p]; d = v - v.mean(0)
        _, _, vt = np.linalg.svd(d, full_matrices=False)
        ds.append(vt[0] * (1 if vt[0] @ A > 0 else -1))
    a = np.mean(ds, 0)
    return a / np.linalg.norm(a)


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


def find_thumb(m, rest, A, W, tside):
    """the thumb as a separate surface piece beside the index finger: faces between ~0.3 finger lengths in front of the
    knuckles and ~1.1 behind them, lying further out on the thumb side than the index finger's line"""
    flen = np.mean([l['len'] for l in rest]); ka = np.mean([l['base'] @ A for l in rest])
    idx = max(rest, key=lambda l: tside * l['across'])
    sp = abs(rest[0]['across'] - rest[-1]['across']) / max(1, len(rest) - 1)       # finger spacing
    tc = m.triangles_center; ta = tc @ A; tw = (tc @ W) * tside
    best = None
    for out in (0.6, 0.4, 0.8, 0.25):
        mask = (ta > ka - 1.1 * flen) & (ta < ka + 0.3 * flen) & (tw > tside * idx['across'] * tside + out * sp)
        for p in pieces(m, mask):
            v = tc[p]; d = v - v.mean(0)
            s = np.sqrt(np.maximum(np.linalg.eigvalsh(np.cov(d.T)), 0)) * 4
            if s[2] > 0.35 * flen and s[1] < 0.6 * flen and (best is None or len(p) > len(best)):
                best = p
        if best is not None:
            break
    if best is None:
        return None, None
    cl, ax = centre_line(m, best, A)
    # the thumb's tip is its far end from the palm: the end further out on the thumb side / forward
    a_, b_ = cl[0], cl[-1]
    if (a_ @ W) * tside + a_ @ A > (b_ @ W) * tside + b_ @ A:
        a_, b_ = b_, a_
    return dict(base=a_, tip=b_, ax=ax, len=float(np.linalg.norm(b_ - a_)), across=float(cl.mean(0) @ W), back=float(a_ @ A)), best


def fit(path, side):
    m = load(path)
    A, W, N = frame(m)
    sign, frac, fl0 = distal_split(m, A)
    A = A * sign
    tc = m.triangles_center
    c0 = tc @ A
    L = c0.max() - c0.min()
    # the hand's own frame: Tripo arms come bent at the elbow, turned at the wrist, or with the hand spread as wide as it
    # is long, so neither the whole arm's axes nor a PCA of the hand say where the fingers point. The fingers found at the
    # hand's end do: their mean direction is the long axis, the line through their middles the width, and the palm normal
    # is square to both. (Fallback, when that first pass found too few: a PCA of the region round the fingertip end.)
    P = tc[np.argmax(c0)]
    if len(fl0) >= 4:
        A = finger_axis(m, fl0, A)
        cen = np.array([tc[p].mean(0) for p in fl0]); cd = cen - cen.mean(0); cd = cd - np.outer(cd @ A, A)
        W = np.linalg.svd(cd, full_matrices=False)[2][0]; W = W - A * (W @ A); W /= np.linalg.norm(W)
        N = np.cross(A, W)
        region = np.linalg.norm(tc - P, axis=1) < 0.5 * L
    else:
        region = np.linalg.norm(tc - P, axis=1) < 0.32 * L
        rc = tc[region].mean(0)
        _, U = np.linalg.eigh(np.cov((tc[region] - rc).T))
        A, W, N = U[:, 2], U[:, 1], U[:, 0]
        if A @ (P - rc) < 0:
            A = -A
    c = np.where(region, tc @ A, -1e9)
    top = c[region].max(); Lh = top - (tc[region] @ A).min()
    # widen the slab from the tips (square to the fingers now) until the pieces merge into the palm (the count drops): the
    # fingers, and how far back they stay separate (the knuckle line is just behind that)
    fingers, f_used, three, f3 = [], None, None, None
    # every depth is tried and the DEEPEST slab still showing 4-5 finger pieces wins (the most finger length): fat fingers
    # can merge at the web (count drops) before the slab reaches their knuckles, so the first drop isn't the end
    for f in np.linspace(0.06, 0.7, 26):
        ps = fingerlike(m, pieces(m, c > top - f * Lh), L, A, top)
        if 4 <= len(ps) <= 5:
            fingers, f_used = ps, f
        elif len(ps) == 3:
            three, f3 = ps, f
    if len(fingers) >= 4:
        t_cut = top - f_used * Lh
    elif len(fl0) >= 4:
        fingers = fl0; t_cut = min((tc[p] @ A).min() for p in fl0)
    elif three is not None or len(fl0) == 3:
        # a stubby little finger often stays joined to the ring finger: go with the three, add it after (below)
        if three is not None:
            fingers, t_cut = three, top - f3 * Lh
        else:
            fingers, t_cut = fl0, min((tc[p] @ A).min() for p in fl0)
    else:
        raise SystemExit(f"{path}: only {max(len(fingers), len(fl0))} finger pieces found - fingers not separated in the mesh?")
    lines = []
    for p in fingers:
        cl, ax = centre_line(m, p, A)
        base = cl[0]; tip = cl[-1]
        v = m.triangles_center[p]; dd = v - v.mean(0)
        thick = float(np.sqrt(max(np.linalg.eigvalsh(np.cov(dd.T))[1], 0)) * 4)
        bow = float(((cl[len(cl) // 2] - (base + tip) / 2) @ N)) if len(cl) >= 3 else 0.0
        lines.append(dict(base=base, tip=tip, ax=ax, len=float(np.linalg.norm(tip - base)), across=float(cl.mean(0) @ W), back=float(base @ A),
                          thick=thick, bow=bow))
    # every long finger runs back to one knuckle line, a little behind where the pieces stop being separate (a short
    # pinky's piece starts wherever the slab happened to cut it)
    long_ = sorted(lines, key=lambda l: -l['len'])[:4]
    t_knuck = t_cut - 0.25 * np.mean([l['tip'] @ A - t_cut for l in long_])
    for l in lines:
        ax = l['ax'] / np.linalg.norm(l['ax'])
        if ax @ A < 0.3:
            continue                                         # (a thumb across the palm keeps its own base)
        s = (l['tip'] @ A - t_knuck) / (ax @ A)
        if s > l['len']:
            l['base'] = l['tip'] - ax * s; l['len'] = float(s); l['back'] = float(l['base'] @ A)
        floor = min(3.0 * l.get('thick', 0), 1.8 * l['len'])     # (a piece with palm web in it reads thick)
        if floor > l['len'] and ax @ A >= 0.3:
            l['base'] = l['tip'] - ax * floor; l['len'] = float(floor); l['back'] = float(l['base'] @ A)
    # the thumb: the piece whose base is furthest back along the arm; the rest ordered across the hand from the thumb side
    th = min(range(len(lines)), key=lambda i: lines[i]['back']) if len(lines) == 5 else None
    rest = [l for i, l in enumerate(lines) if i != th]
    thumb = lines[th] if th is not None else None
    thumb_faces = fingers[th] if th is not None else None
    if thumb:
        tside = np.sign(thumb['across'] - np.mean([l['across'] for l in rest])) or 1
    else:
        # no thumb among the fingertip pieces (it joins the palm further back): the pinky is the shorter outer finger, the
        # thumb side is the index's; then look for the thumb as its own piece beside the index, back toward the wrist
        rest.sort(key=lambda l: l['across'])
        tside = 1 if rest[-1]['len'] > rest[0]['len'] else -1
        bow = np.mean([l.get('bow', 0) for l in rest])
        if abs(bow) > 0.002 * L:
            palm = N * np.sign(bow)
            t = np.cross(A, palm) * (1 if side == 'R' else -1)
            tside = 1 if t @ W > 0 else -1
        thumb, thumb_faces = find_thumb(m, rest, A, W, tside)
    rest.sort(key=lambda l: -tside * l['across'])          # index (nearest the thumb) .. pinky
    if len(rest) == 3:
        # the little finger, joined to the ring finger in the mesh: one finger spacing past it, parallel, ~0.78 as long
        r1, r0 = rest[-1], rest[-2]
        step = (r1['base'] - r0['base']); step = step - A * (step @ A)
        ax = r1['ax'] / np.linalg.norm(r1['ax']); ln = 0.78 * r1['len']
        b = r1['base'] + step - A * 0.1 * ln
        rest.append(dict(base=b, tip=b + ax * ln, ax=ax, len=float(ln), across=float((b @ W)), back=float(b @ A)))
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
    # Tripo often gives the whole arm (shoulder, sometimes bent at the elbow): the forearm's own axis from the stretch just
    # behind the wrist (the bend can't skew it), the elbow ~1.3 hand lengths back along it, and a crop that keeps the hand +
    # ~1 hand length of forearm inside a tube round that axis (the hero's own sleeve covers the arm from mid-forearm up)
    mid_tip = next((l['tip'] for l, nm in zip(rest, names) if nm == 'middle'), rest[0]['tip'])
    hl = float(np.linalg.norm(mid_tip - wrist))
    rel = V - wrist; t = rel @ A
    seg = (t > -1.2 * hl) & (t < -0.1 * hl) & (np.linalg.norm(rel - np.outer(t, A), axis=1) < 0.6 * hl)
    Af = A
    if seg.sum() > 50:
        d = V[seg] - V[seg].mean(0); _, _, vt = np.linalg.svd(d, full_matrices=False)
        Af = vt[0] * (1 if vt[0] @ A > 0 else -1)
    J['forearm_' + side] = wrist - Af * 1.3 * hl
    crop = dict(origin=wrist.tolist(), axis=Af.tolist(), keep_from=-1.05 * hl, radius=0.75 * hl, hand_len=hl)
    for nm, l in zip(names, rest):
        b, t = l['base'], l['tip']
        for k, u in ((1, 0.0), (2, 0.45), (3, 0.72)):
            J[f'{nm}{k}_{side}'] = b + (t - b) * u
        J[f'{nm}_tip_{side}'] = t
    if not thumb:
        # not found as its own piece (it lies against the palm): a default chain beside the index knuckle, out and forward,
        # so the thumb's surface still gets its own bones (bone heat picks it up) and can close over a grip
        i1 = J.get(f'index1_{side}', knuck)
        sp = abs(rest[0]['across'] - rest[-1]['across']) / max(1, len(rest) - 1)
        b0 = wrist + (i1 - wrist) * 0.3 + W * tside * 0.9 * sp
        thumb = dict(base=b0, tip=b0 + (A + W * tside * 0.8) / np.linalg.norm(A + W * tside * 0.8) * 0.75 * flen)
    if thumb:
        b, t = thumb['base'], thumb['tip']
        tl = np.linalg.norm(t - b); want = 0.75 * flen
        if tl < want:                                       # its piece was cut short where it joins the palm
            b = t - (t - b) / max(tl, 1e-6) * want
        b = b + (wrist - b) * 0.25                          # the thumb's CMC sits back toward the wrist
        for k, u in ((1, 0.0), (2, 0.45), (3, 0.75)):
            J[f'thumb{k}_{side}'] = b + (t - b) * u
        J[f'thumb_tip_{side}'] = t
    return m, dict(side=side, axes=dict(along=A.tolist(), across=W.tolist(), back=N.tolist()), length=float(L),
                   fingers=len(lines), crop=crop, debug=[dict(len=round(l['len'], 3), thick=round(l.get('thick', 0), 3), bow=round(l.get('bow', 0), 4)) for l in lines], joints={k: v.tolist() for k, v in J.items()})


def draw(m, info, png):
    import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
    A = np.array(info['axes']['along']); W = np.array(info['axes']['across']); N = np.array(info['axes']['back'])
    V = m.vertices
    fig, ax = plt.subplots(1, 2, figsize=(12, 5))
    cr = info.get('crop')
    if cr:
        o = np.array(cr['origin']); ax_ = np.array(cr['axis']); rel = V - o; tt = rel @ ax_
        keep = (tt >= cr['keep_from']) & ((tt >= 0) | (np.linalg.norm(rel - np.outer(tt, ax_), axis=1) < cr['radius']))
    else:
        keep = np.ones(len(V), bool)
    if len(V) > 60000:
        sel = np.random.default_rng(0).choice(len(V), 60000, replace=False); V, keep = V[sel], keep[sel]
    for a, (u, v, t) in zip(ax, ((A, W, 'top (along x across)'), (A, N, 'side (along x back)'))):
        a.scatter(V[~keep] @ u, V[~keep] @ v, s=0.1, c='#ddd'); a.scatter(V[keep] @ u, V[keep] @ v, s=0.1, c='#777')
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
