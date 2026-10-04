# Bakes the costume palettes the skins remap (TS CharacterView analysePalette) for the hero base-colour textures, the
# way the web game measures them (each model's LOD1 texture = the web model's, given to all of that model's base maps): the full-size image drawn into a 96 x 96 canvas (Chrome's default drawImage = bilinear
# at the pixel centres, no mipmaps), then the same 36-bin saturated-hue histogram. The player can't repeat that: it
# only has the imported texture (2048 in a Windows build) and a GPU blit samples its mipmaps, which averages the costume
# texels down - the hues come out the same but the mean value (zuSrc.y, the recolour's brightness normaliser) about
# 40 % low, so a skin drew its main colour up to 1.6 x brighter than the TS. HeroLook.Palette uses this table first.
#   python tools/looks/bake_palettes.py        -> Assets/ZU/Game/Looks/Resources/ZULooks/palettes.txt
# Checked against the TS Hero Viewer's own zuSrc1/zuSrc2 (evera-11): gantetsu 0.04/0.48 0.54/0.50 and tomoe 0.54/0.71
# 0.04/0.27 come out the same here (tomoe 0.72: a JPEG decoder's rounding).
import os, re, sys
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
HEROES = os.path.join(ROOT, 'Assets', 'ZU', 'Art', 'Heroes')
OUT = os.path.join(ROOT, 'Assets', 'ZU', 'Game', 'Looks', 'Resources', 'ZULooks', 'palettes.txt')
N, B = 96, 36


def canvas96(path):
    """the image drawn into a 96 x 96 canvas: bilinear at the pixel centres, rounded to bytes (RGBA)"""
    a = np.asarray(Image.open(path).convert('RGBA'))          # (bytes: only the sampled rows become floats)
    h, w = a.shape[:2]
    def axis(size):
        c = (np.arange(N) + 0.5) * size / N - 0.5
        i0 = np.clip(np.floor(c).astype(int), 0, size - 1)
        return i0, np.clip(i0 + 1, 0, size - 1), c - np.floor(c)
    y0, y1, fy = axis(h); x0, x1, fx = axis(w)
    rows = a[y0].astype(np.float64) * (1 - fy)[:, None, None] + a[y1].astype(np.float64) * fy[:, None, None]
    px = rows[:, x0] * (1 - fx)[None, :, None] + rows[:, x1] * fx[None, :, None]
    return np.round(px).reshape(-1, 4)


def analyse(px):
    """analysePalette's histogram half, in the TS's order (Float32Array sums)"""
    wsum = np.zeros(B, np.float32); vsum = np.zeros(B, np.float32)
    for r8, g8, b8, a in px:
        if a < 128: continue
        r, g, b = r8 / 255, g8 / 255, b8 / 255
        mx, mn = max(r, g, b), min(r, g, b); d = mx - mn; s = d / mx if mx > 0 else 0
        if s < 0.22 or mx < 0.12: continue
        hh = ((g - b) / d + 6) % 6 if mx == r else (b - r) / d + 2 if mx == g else (r - g) / d + 4
        hh /= 6
        if 0 < hh < 0.11 and s < 0.6 and mx > 0.3: continue      # skin tones
        k = min(B - 1, int(np.floor(hh * B))); w = s * mx
        wsum[k] = np.float32(float(wsum[k]) + w); vsum[k] = np.float32(float(vsum[k]) + w * mx ** 2.2)
    sm = lambda k: wsum[(k + B - 1) % B] * 0.5 + wsum[k] + wsum[(k + 1) % B] * 0.5
    b1 = 0
    for k in range(1, B):
        if sm(k) > sm(b1): b1 = k
    b2 = -1
    for k in range(B):
        dd = min(abs(k - b1), B - abs(k - b1))
        if dd >= 4 and (b2 < 0 or sm(k) > sm(b2)): b2 = k
    if not wsum[b1]: return None
    hv = lambda k: ((k + 0.5) / B, float(vsum[k] / wsum[k]) if wsum[k] else 0.5)
    h1 = hv(b1); h2 = hv(b2) if b2 >= 0 and wsum[b2] > wsum[b1] * 0.08 else hv((b1 + B // 2) % B)
    return h1 + h2


def main():
    guids = {}
    for d, _, fs in os.walk(HEROES):
        for f in fs:
            if f.endswith('.meta'):
                m = re.search(r'^guid: ([0-9a-f]{32})', open(os.path.join(d, f), encoding='utf8').read(), re.M)
                if m: guids[m.group(1)] = os.path.join(d, f[:-5])
    # base maps per model folder (Art/Heroes/<model>/...)
    maps = {}
    for d, _, fs in os.walk(HEROES):
        for f in fs:
            if f.endswith('.mat'):
                m = re.search(r'- _BaseMap:\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})', open(os.path.join(d, f), encoding='utf8').read())
                if m and m.group(1) in guids:
                    model = os.path.relpath(d, HEROES).split(os.sep)[0]
                    maps.setdefault(model, set()).add(guids[m.group(1)])
    rows, seen = [], {}
    for model in sorted(maps):
        # the web game's model is the LOD1 (rig) one: its texture is the one the TS measures, so every base map of the
        # model (the HD LOD0 one too, whichever renderer HeroLook meets first) gets that palette
        ps = sorted(maps[model]); src = next((p for p in ps if f'{os.sep}{model}_lod1_tex{os.sep}' in p), ps[0])
        pal = analyse(canvas96(src))
        if pal is None: print('  no saturated texels:', os.path.relpath(src, ROOT)); continue
        print(f'{os.path.relpath(src, HEROES):70s} src1 {pal[0]:.2f}/{pal[1]:.2f} src2 {pal[2]:.2f}/{pal[3]:.2f}  ({len(ps)} map(s))')
        for p in ps:
            name = os.path.splitext(os.path.basename(p))[0]
            if name in seen:
                if any(abs(a - b) > 1e-4 for a, b in zip(seen[name], pal)): print('  WARNING: two textures named', name, '- kept the first')
                continue
            seen[name] = pal
            rows.append(f'{name} {pal[0]:.6f} {pal[1]:.6f} {pal[2]:.6f} {pal[3]:.6f}')
    with open(OUT, 'w', encoding='utf8', newline='\n') as f:
        f.write('# texture name, then zuSrc1 (hue, value) and zuSrc2 (hue, value) as the web game measures them - made by\n'
                '# tools/looks/bake_palettes.py from the full-size images; read by Looks/HeroLook.Palette (re-run after a hero import)\n')
        f.write('\n'.join(rows) + '\n')
    print(len(rows), 'palettes ->', os.path.relpath(OUT, ROOT))


if __name__ == '__main__':
    sys.exit(main())
