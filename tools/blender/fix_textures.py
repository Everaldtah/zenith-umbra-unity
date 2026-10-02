"""Blender writes a GLB's packed WebP textures to disk byte-for-byte even when the file is named .png, and Unity can't
import WebP. Re-encode every such file under the given folders as a real PNG (lossless, channels kept exactly).
usage: python tools/blender/fix_textures.py <folder> [<folder> ...]"""
import sys, os
from PIL import Image

fixed = 0
for root_dir in sys.argv[1:]:
    for d, _, files in os.walk(root_dir):
        for f in files:
            if not f.lower().endswith('.png'):
                continue
            p = os.path.join(d, f)
            with open(p, 'rb') as fh:
                head = fh.read(12)
            if head[:4] == b'RIFF' and head[8:12] == b'WEBP':
                im = Image.open(p)
                im.load()
                im = im.convert('RGBA' if 'A' in im.getbands() else 'RGB')
                im.save(p, format='PNG', optimize=False)
                fixed += 1
print('re-encoded', fixed, 'WebP-as-PNG file(s) as real PNG')
