"""Blender writes a GLB's packed textures to disk byte-for-byte (WebP in the Tripo GLBs) whatever the file is named, and
Unity can't import WebP. Re-encode every texture under the given folders into a format Unity reads:

  - JPEG q95, 4:4:4 (no chroma subsampling) when the image has no meaningful alpha: the source is already lossy WebP, so a
    lossless PNG only makes the repo ~4x bigger, and Unity re-compresses to BC formats on import anyway
  - PNG when the alpha channel carries data

The generated `*_urpmask.png` files (ZU.Editor builds them; alpha = smoothness) are left alone.
usage: python tools/blender/fix_textures.py <folder> [<folder> ...]"""
import sys, os
from PIL import Image

done = {'jpg': 0, 'png': 0}
for root_dir in sys.argv[1:]:
    for d, _, files in os.walk(root_dir):
        for f in files:
            low = f.lower()
            if not low.endswith('.png') or low.endswith('_urpmask.png'):
                continue
            p = os.path.join(d, f)
            with open(p, 'rb') as fh:
                head = fh.read(12)
            webp = head[:4] == b'RIFF' and head[8:12] == b'WEBP'
            im = Image.open(p)
            im.load()
            alpha = 'A' in im.getbands() and im.getchannel('A').getextrema()[0] < 250
            if alpha:
                if webp:                                   # keep the name, make the bytes a real PNG
                    im.convert('RGBA').save(p, format='PNG')
                    done['png'] += 1
                continue
            jpg = p[:-4] + '.jpg'
            im.convert('RGB').save(jpg, format='JPEG', quality=95, subsampling=0, optimize=True)
            os.remove(p)
            meta = p + '.meta'
            if os.path.exists(meta):
                os.remove(meta)
            done['jpg'] += 1
print('re-encoded', done['jpg'], 'texture(s) as JPEG q95 and', done['png'], 'with alpha as PNG')
