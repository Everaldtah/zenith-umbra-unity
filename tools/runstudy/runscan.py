# Frame-by-frame run study of a glTF animation pack (Quaternius UAL, Mixamo, Tripo motion): forward kinematics at N phases
# of each locomotion loop; per arm: where the hand is from its shoulder (forward / up / out, in arm lengths), the elbow's
# flexion, the swing's range and its phase against the opposite leg; per body: torso lean, cycle time.
#   python runscan.py <pack.glb> [name filter...] [--frames]      (--frames prints every sampled frame)
import json, struct, sys
import numpy as np

NAMES = {  # canonical -> candidates (lowercase, prefix stripped)
 'hips': ['hips', 'pelvis'], 'chest': ['spine2', 'spine_03', 'spine_02'], 'head': ['head'],
 'ua_l': ['leftarm', 'upperarm_l'], 'fa_l': ['leftforearm', 'lowerarm_l'], 'h_l': ['lefthand', 'hand_l'],
 'ua_r': ['rightarm', 'upperarm_r'], 'fa_r': ['rightforearm', 'lowerarm_r'], 'h_r': ['righthand', 'hand_r'],
 'th_l': ['leftupleg', 'thigh_l'], 'th_r': ['rightupleg', 'thigh_r'], 'ft_l': ['leftfoot', 'foot_l'], 'ft_r': ['rightfoot', 'foot_r'] }

def load(path):
    f = open(path, 'rb'); f.read(12)
    clen, _ = struct.unpack('<I4s', f.read(8)); j = json.loads(f.read(clen))
    blen, _ = struct.unpack('<I4s', f.read(8)); return j, f.read(blen)
def acc(j, b, i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]; n = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
    return np.frombuffer(b, dtype=np.float32, count=a['count'] * n, offset=bv.get('byteOffset', 0) + a.get('byteOffset', 0)).reshape(a['count'], n).astype(float)
def qmat(q):
    x, y, z, w = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
def sample(times, vals, t):
    k = np.searchsorted(times, t)
    if k <= 0: return vals[0]
    if k >= len(times): return vals[-1]
    u = (t - times[k-1]) / max(1e-9, times[k] - times[k-1]); a, c = vals[k-1], vals[k]
    if vals.shape[1] == 4:
        if np.dot(a, c) < 0: c = -c
        v = a + (c - a) * u; return v / np.linalg.norm(v)
    return a + (c - a) * u

args = [a for a in sys.argv[2:] if not a.startswith('--')]; FRAMES = '--frames' in sys.argv
j, b = load(sys.argv[1]); nodes = j['nodes']; parent = {}
for i, n in enumerate(nodes):
    for c in n.get('children', []): parent[c] = i
low = {i: n.get('name', '').split(':')[-1].lower() for i, n in enumerate(nodes)}
B = {}
for k, cands in NAMES.items():
    for c in cands:
        hit = [i for i, n in low.items() if n == c]
        if hit: B[k] = hit[0]; break
def world(i, pose, cache):
    if i in cache: return cache[i]
    n = nodes[i]
    t = pose.get((i, 'translation'), np.array(n.get('translation', [0, 0, 0]), float)); r = pose.get((i, 'rotation'), np.array(n.get('rotation', [0, 0, 0, 1]), float))
    M = np.eye(4); M[:3, :3] = qmat(r) * np.array(n.get('scale', [1, 1, 1]), float); M[:3, 3] = t
    if i in parent: M = world(parent[i], pose, cache) @ M
    cache[i] = M; return M

N = 16
print(f"{'clip':22s} {'cycle':>5s} {'lean':>5s} | per arm: hand fwd mean [min..max] (swing), up, out, elbow flex mean [min..max] deg, phase vs opposite foot")
for an in j['animations']:
    nm = an.get('name', '')
    if args and not any(f.lower() in nm.lower() for f in args): continue
    ch = []; T = 0
    for c in an['channels']:
        s = an['samplers'][c['sampler']]; times = acc(j, b, s['input'])[:, 0]; vals = acc(j, b, s['output'])
        if c['target']['path'] in ('rotation', 'translation'): ch.append((c['target']['node'], c['target']['path'], times, vals)); T = max(T, times[-1])
    rec = {k: [] for k in ('L', 'R')}; lean = []; foot = {k: [] for k in ('L', 'R')}
    for f in range(N):
        pose = {(n, p): sample(tm, v, T * f / N) for n, p, tm, v in ch}
        cache = {}; P = {k: world(i, pose, cache)[:3, 3] for k, i in B.items()}
        up = np.array([0, 1, 0.]); side = P['th_l'] - P['th_r']; side[1] = 0; side /= np.linalg.norm(side); fwd = np.cross(side, up)
        sp = P['chest'] - P['hips']; lean.append(np.degrees(np.arctan2(np.dot(sp, fwd), sp[1])))
        leg = np.linalg.norm(P['ft_l'] - P['th_l'])
        for S, s in (('L', 'l'), ('R', 'r')):
            a, e, h = P['ua_' + s], P['fa_' + s], P['h_' + s]; L = np.linalg.norm(e - a) + np.linalg.norm(h - e); d = h - a
            u, v = e - a, h - e; flex = np.degrees(np.arccos(np.clip(np.dot(u, v) / np.linalg.norm(u) / np.linalg.norm(v), -1, 1)))
            rec[S].append((np.dot(d, fwd) / L, d[1] / L, np.dot(d, side) / L * (1 if S == 'L' else -1), flex, np.dot(u, fwd) / np.linalg.norm(u)))
            foot[S].append(np.dot(P['ft_' + s] - P['hips'], fwd) / leg)
    out = f'{nm:22s} {T:5.2f} {np.mean(lean):+5.1f} |'
    for S, O in (('L', 'R'), ('R', 'L')):
        r = np.array(rec[S]); hf = r[:, 0]; of = np.array(foot[O])
        corr = np.corrcoef(hf, of)[0, 1] if hf.std() > 1e-4 and of.std() > 1e-4 else 0
        out += f' {S}: fwd {hf.mean():+.2f} [{hf.min():+.2f}..{hf.max():+.2f}] ({hf.max()-hf.min():.2f}) up {r[:,1].mean():+.2f} out {r[:,2].mean():+.2f} flex {r[:,3].mean():3.0f} [{r[:,3].min():3.0f}..{r[:,3].max():3.0f}] r={corr:+.2f} |'
    print(out)
    if FRAMES:
        for f in range(N):
            l, r = rec['L'][f], rec['R'][f]
            print(f'    f{f:02d}  L hand fwd {l[0]:+.2f} up {l[1]:+.2f} flex {l[3]:3.0f}  footL {foot["L"][f]:+.2f} | R hand fwd {r[0]:+.2f} up {r[1]:+.2f} flex {r[3]:3.0f}  footR {foot["R"][f]:+.2f} | lean {lean[f]:+5.1f}')
