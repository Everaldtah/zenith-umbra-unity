"""Bring the TS game's environment surfaces into Unity (public/env: CC0 Poly Haven texture sets + per-map HDRIs, see
CREDITS.txt there). Unity can't read WebP, so:

  <map>_<kind>_c.webp  (per-map tinted albedo)   -> Assets/ZU/Art/Env/Albedo/<map>_<kind>.jpg
  <set>_n.webp         (OpenGL normal map)       -> Assets/ZU/Art/Env/Sets/<set>_n.jpg
  <set>_arm.webp       (glTF ARM: R ao, G rough, B metal)
                                                 -> Assets/ZU/Art/Env/Sets/<set>_mask.png  (URP Lit: R metallic, G occlusion,
                                                    A smoothness - one texture serves _MetallicGlossMap and _OcclusionMap)
  hdr_<map>.hdr                                  -> Assets/ZU/Art/Env/Sky/hdr_<map>.hdr (copied; Unity reads Radiance HDR)
  manifest.json                                  -> Assets/ZU/Resources/ZUData/env.json (the sets / maps / hdri tables)

usage: python tools/export/export_env.py [path/to/zenith-umbra]"""
import json, os, shutil, sys
from PIL import Image

zu = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser('~/Projects/zenith-umbra')
src = os.path.join(zu, 'public', 'env', 'pbr')
root = os.path.join(os.path.dirname(__file__), '..', '..')
out = os.path.join(root, 'Assets', 'ZU', 'Art', 'Env')
for d in ('Albedo', 'Sets', 'Sky'):
    os.makedirs(os.path.join(out, d), exist_ok=True)

man = json.load(open(os.path.join(src, 'manifest.json'), encoding='utf-8'))
n = {'albedo': 0, 'normal': 0, 'mask': 0, 'hdr': 0}

def jpg(a, b):
    Image.open(a).convert('RGB').save(b, 'JPEG', quality=95, subsampling=0, optimize=True)

for f in os.listdir(src):
    p = os.path.join(src, f)
    if f.endswith('_c.webp'):
        jpg(p, os.path.join(out, 'Albedo', f[:-len('_c.webp')] + '.jpg')); n['albedo'] += 1
    elif f.endswith('_n.webp'):
        jpg(p, os.path.join(out, 'Sets', f[:-len('.webp')] + '.jpg')); n['normal'] += 1
    elif f.endswith('_arm.webp'):
        r, g, b = Image.open(p).convert('RGB').split()
        smooth = g.point(lambda v: 255 - v)
        Image.merge('RGBA', (b, r, Image.new('L', r.size, 0), smooth)).save(os.path.join(out, 'Sets', f[:-len('_arm.webp')] + '_mask.png'))
        n['mask'] += 1
    elif f.endswith('.hdr'):
        shutil.copyfile(p, os.path.join(out, 'Sky', f)); n['hdr'] += 1
shutil.copyfile(os.path.join(src, 'CREDITS.txt'), os.path.join(out, 'CREDITS.txt'))

# ---- the painted sky panoramas (what the player sees; the HDRIs above only light the scene). The TS wraps
# env/sky_<map>.webp round a cylinder (MapScene: R 420, height circ / 2 / 2.4, bottom at y = -120, mirrored-repeated twice
# round), capped with the ambient sky colour above and the fog colour below. Re-project that into an equirectangular
# image for Unity's panoramic skybox: u = 0.5 - atan2(z, x) / 2pi, v = 0.5 + elevation / pi, as seen from the map centre.
import numpy as np
maps_json = json.load(open(os.path.join(root, 'Assets', 'ZU', 'Resources', 'ZUData', 'maps.json'), encoding='utf-8'))
maps_list = maps_json if isinstance(maps_json, list) else maps_json.get('maps', maps_json)
if isinstance(maps_list, dict): maps_list = list(maps_list.values())
mdef = {m['id']: m for m in maps_list}
def hexrgb(h): h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)
envdir = os.path.join(zu, 'public', 'env')
W, H = 4096, 2048
R, CH = 420.0, 2 * np.pi * 420.0 / 2 / 2.4
y0, eye = -120.0, 2.0
for f in sorted(os.listdir(envdir)):
    if not (f.startswith('sky_') and f.endswith('.webp')):
        continue
    mid = f[4:-5]
    img = np.asarray(Image.open(os.path.join(envdir, f)).convert('RGB'), dtype=np.float32)
    ih, iw, _ = img.shape
    m = mdef.get(mid)
    cap_top = hexrgb(m['ambient'][0]) * 0.8 if m else img[0].mean(axis=0)
    cap_bot = hexrgb(m['fog'][0]) if m else img[-1].mean(axis=0)
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    phi = (0.5 - u) * 2 * np.pi                      # Unity bearing atan2(z, x)
    el = (v - 0.5) * np.pi                           # elevation, rows from the bottom
    # three's cylinder: x = R sin(t), z = R cos(t); Unity mirrors x -> bearing = pi / 2 + t
    t = np.mod(phi - np.pi / 2, 2 * np.pi)
    uc = t / (2 * np.pi) * 2                         # wrapped twice
    uc = np.where(uc > 1, 2 - uc, uc)                # mirrored
    yc = eye + R * np.tan(np.clip(el, -1.55, 1.55))
    vc = (yc - y0) / CH                              # 0 at the cylinder's bottom, 1 at its top
    px = np.clip(uc * (iw - 1), 0, iw - 1)
    out_img = np.zeros((H, W, 3), dtype=np.float32)
    top_edge, bot_edge = img[:8].mean(axis=(0, 1)), img[-8:].mean(axis=(0, 1))
    el_top = np.arctan((y0 + CH - eye) / R); el_bot = np.arctan((y0 - eye) / R)
    def smooth(k): k = np.clip(k, 0, 1); return k * k * (3 - 2 * k)
    for r in range(H):
        vv = vc[r]
        if vv >= 1.0:   # above the painting: its own top colour grading into the TS cap colour at the zenith
            out_img[r] = top_edge + (cap_top - top_edge) * smooth((el[r] - el_top) / (np.pi / 2 - el_top))
            continue
        if vv <= 0.0:
            out_img[r] = bot_edge + (cap_bot - bot_edge) * smooth((el_bot - el[r]) / (np.pi / 2 + el_bot) * 3)
            continue
        py = (1 - vv) * (ih - 1)                     # image rows run top -> bottom
        y_lo = int(np.floor(py)); y_hi = min(ih - 1, y_lo + 1); fy = py - y_lo
        x_lo = np.floor(px).astype(int); x_hi = np.minimum(iw - 1, x_lo + 1); fx = (px - x_lo)[:, None]
        row = (img[y_lo, x_lo] * (1 - fx) + img[y_lo, x_hi] * fx) * (1 - fy) + (img[y_hi, x_lo] * (1 - fx) + img[y_hi, x_hi] * fx) * fy
        # soften the seams into the caps over the last few percent
        if vv > 0.9: row = row + (top_edge - row) * smooth((vv - 0.9) / 0.1)
        if vv < 0.06: row = row + (bot_edge - row) * smooth((0.06 - vv) / 0.06)
        out_img[r] = row
    Image.fromarray(np.clip(out_img[::-1], 0, 255).astype(np.uint8)).save(os.path.join(out, 'Sky', f'skypano_{mid}.jpg'), 'JPEG', quality=93, subsampling=0)
    n['hdr'] += 0
    print('  sky', mid)
data = os.path.join(root, 'Assets', 'ZU', 'Resources', 'ZUData')
json.dump(man, open(os.path.join(data, 'env.json'), 'w', encoding='utf-8'), indent=1)
print('env:', n, '->', os.path.normpath(out))
