# side-by-side ult sheets: web Ult Viewer shots (top row) over Unity UltShowcase shots (bottom row), one column per moment
# usage: python ult_sheets.py <webDir> <unityDir> <times.json> <outDir> [hero ...]
import json, os, sys
from PIL import Image, ImageDraw

web, uni, times_p, out = sys.argv[1:5]
heroes = sys.argv[5:]
T = json.load(open(times_p))
os.makedirs(out, exist_ok=True)
W, H = 512, 288
for hero in heroes or T.keys():
    ts = T[hero]
    sheet = Image.new('RGB', (W * len(ts), H * 2 + 28), (12, 14, 20))
    d = ImageDraw.Draw(sheet)
    d.text((6, 6), f'{hero}  -  top: web (Three.js)   bottom: Unity', fill=(240, 240, 240))
    for i, t in enumerate(ts):
        for row, folder in enumerate((web, uni)):
            p = os.path.join(folder, f'{hero}_{i + 1}.png')
            x, y = i * W, 28 + row * H
            if os.path.exists(p):
                sheet.paste(Image.open(p).convert('RGB').resize((W, H)), (x, y))
            else:
                d.rectangle([x, y, x + W - 1, y + H - 1], outline=(200, 60, 60)); d.text((x + 8, y + 8), 'missing', fill=(255, 120, 120))
        d.text((i * W + 6, 28 + 2 * H - 16), f't = {t:.2f} s', fill=(255, 230, 120))
    sheet.save(os.path.join(out, f'{hero}.png'))
    print('sheet', hero)
