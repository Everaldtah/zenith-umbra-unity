"""Hero photos for the UI and the launcher: every picture must match its GAME MODEL before it ships.

The UI's character pictures (Assets/ZU/UI/Resources/ZUImg/portrait_<id>, key_<id>) are 3D-style pictures made with an
image generator from renders of the game's own models. A generator can drift (wrong hair, wrong armour, wrong face), so
nothing is installed until it has been compared with the model:

  python tools/herophotos/herophotos.py sheets [id ...]
        build one comparison sheet per character and kind:
        [game model render | candidate 0 | candidate 1 | picture now in the game]
        and score each candidate's colours against the model (0 = same palette)
  python tools/herophotos/herophotos.py accept <id> <kind> <n> "<what was compared>"
        record that candidate n was looked at against the model and matches
  python tools/herophotos/herophotos.py reject <id> <kind> "<why>"      a miss: it must be generated again
  python tools/herophotos/herophotos.py fallback <id> <kind> "<why>"    use the game model's own render for this one
  python tools/herophotos/herophotos.py status     what is verified, what is missing
  python tools/herophotos/herophotos.py install    copy the VERIFIED pictures into ZUImg; exit 1 if any is unverified
  python tools/herophotos/herophotos.py gate       exit 1 unless every installed picture is the verified one (before a build)

Work folder (not in the repo): C:/Users/evera/zu-resume/hero_photos  (model/, raw/, sheets/, final/)
The manifest of decisions is in the repo: tools/herophotos/manifest.json.
"""
import hashlib
import json
import os
import sys

from PIL import Image, ImageDraw, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
WORK = os.environ.get('ZU_HERO_PHOTOS', 'C:/Users/evera/zu-resume/hero_photos')
IMG = os.path.join(ROOT, 'Assets', 'ZU', 'UI', 'Resources', 'ZUImg')
MANIFEST = os.path.join(HERE, 'manifest.json')

HEROES = 'enra gantetsu gorgoth haruto hayate hex hibiki kagemaru kaien mirei nocturne qelvaris raijin seiran tenkai tomoe vorn yuzu'.split()
MINIONS = 'minion_bomber minion_lancer minion_sentinel minion_swarmer'.split()
BOSSES = 'boss_genesis boss_ironmaw boss_leviathan boss_phoenix boss_reaper'.split()
# (id, kind) -> the ZUImg file it replaces
TARGETS = {}
for _h in HEROES:
    TARGETS[(_h, 'portrait')] = 'portrait_' + _h
    TARGETS[(_h, 'key')] = 'key_' + _h
for _m in MINIONS:
    TARGETS[(_m, 'portrait')] = 'portrait_' + _m
for _b in BOSSES:
    TARGETS[(_b, 'key')] = 'key_' + _b
SIZE = {'portrait': (512, 512), 'key': (864, 1184)}
MODEL = {'portrait': 'bust', 'key': 'full'}
OK = ('accept', 'fallback')


def load():
    if not os.path.exists(MANIFEST):
        return {}
    with open(MANIFEST, encoding='utf-8') as f:
        return json.load(f)


def save(m):
    with open(MANIFEST, 'w', encoding='utf-8') as f:
        json.dump(m, f, indent=1, sort_keys=True)


def sha(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()[:16]


def subject_hist(im):
    """hue / value histogram of the character only: pixels that differ from the picture's own background (the median
    of its border), so a different backdrop does not count as a different character"""
    im = im.convert('RGB').resize((160, 160))
    px = im.load()
    border = [px[x, y] for x in range(160) for y in (0, 1, 158, 159)] + [px[x, y] for y in range(160) for x in (0, 1, 158, 159)]
    bg = tuple(sorted(c[i] for c in border)[len(border) // 2] for i in range(3))
    hsv = im.convert('HSV').load()
    h = [0.0] * 30          # 12 hues x 2 value bands for coloured pixels, then 6 grey levels
    n = 0
    for y in range(160):
        for x in range(160):
            r, g, b = px[x, y]
            if abs(r - bg[0]) + abs(g - bg[1]) + abs(b - bg[2]) < 60:
                continue
            hh, s, v = hsv[x, y]
            if s < 50:
                h[24 + min(5, v * 6 // 256)] += 1
            else:
                h[(hh * 12 // 256) * 2 + (1 if v > 140 else 0)] += 1
            n += 1
    return [v / n for v in h] if n else h


def distance(a, b):
    return round(sum(abs(x - y) for x, y in zip(a, b)) / 2, 3)      # 0 = same palette, 1 = nothing in common


def cands(hid, kind):
    out = []
    for n in range(6):
        p = os.path.join(WORK, 'raw', '%s_%s_%d.png' % (hid, kind, n))
        if os.path.exists(p):
            out.append((n, p))
    return out


def sheets(only=None):
    os.makedirs(os.path.join(WORK, 'sheets'), exist_ok=True)
    m = load()
    for (hid, kind), target in sorted(TARGETS.items()):
        if only and hid not in only:
            continue
        model = os.path.join(WORK, 'model', '%s_%s.png' % (hid, MODEL[kind]))
        if not os.path.exists(model):
            print('no model render:', hid, kind)
            continue
        cs = cands(hid, kind)
        if not cs:
            continue
        mh = subject_hist(Image.open(model))
        cell = (384, 384) if kind == 'portrait' else (336, 460)
        tiles = [('GAME MODEL', Image.open(model))]
        scores = {}
        for n, p in cs:
            im = Image.open(p)
            scores[str(n)] = distance(mh, subject_hist(im))
            tiles.append(('candidate %d  palette %s' % (n, scores[str(n)]), im))
        old = None
        for ext in ('.jpg', '.png'):
            if os.path.exists(os.path.join(IMG, target + ext)):
                old = os.path.join(IMG, target + ext)
        if old:
            tiles.append(('in the game now', Image.open(old)))
        sheet = Image.new('RGB', (cell[0] * len(tiles), cell[1] + 22), (14, 16, 22))
        d = ImageDraw.Draw(sheet)
        for i, (label, im) in enumerate(tiles):
            sheet.paste(ImageOps.fit(im.convert('RGB'), cell, Image.LANCZOS, centering=(0.5, 0.2)), (cell[0] * i, 22))
            d.text((cell[0] * i + 6, 5), label, fill=(235, 235, 235))
        sheet.save(os.path.join(WORK, 'sheets', '%s_%s.png' % (hid, kind)))
        m.setdefault(hid + '/' + kind, {})['scores'] = scores
        print('%-16s %-8s palette distance to the model: %s' % (hid, kind, scores))
    save(m)


def decide(hid, kind, state, n, note):
    if (hid, kind) not in TARGETS:
        sys.exit('unknown picture %s %s' % (hid, kind))
    m = load()
    e = m.setdefault(hid + '/' + kind, {})
    src = None
    if state == 'accept':
        src = os.path.join(WORK, 'raw', '%s_%s_%d.png' % (hid, kind, n))
    elif state == 'fallback':
        src = os.path.join(WORK, 'model', '%s_%s.png' % (hid, MODEL[kind]))
    if src and not os.path.exists(src):
        sys.exit('missing ' + src)
    e['state'] = state
    e['note'] = note
    for k in ('source', 'sha', 'installed'):
        e.pop(k, None)
    if src:
        e['source'] = os.path.relpath(src, WORK).replace('\\', '/')
        e['sha'] = sha(src)
    save(m)
    print(hid, kind, state, e.get('source', ''))


def status():
    m = load()
    bad = 0
    for (hid, kind) in sorted(TARGETS):
        e = m.get(hid + '/' + kind, {})
        ok = e.get('state') in OK
        bad += 0 if ok else 1
        print('%s %-16s %-8s %-11s %-28s %s' % ('ok ' if ok else '-- ', hid, kind, e.get('state', 'not checked'), e.get('source', ''), e.get('note', '')[:70]))
    print('%d of %d verified' % (len(TARGETS) - bad, len(TARGETS)))
    return bad


def install():
    if status():
        sys.exit('NOT INSTALLED: every picture needs an accept or a fallback first')
    m = load()
    os.makedirs(os.path.join(WORK, 'final'), exist_ok=True)
    for (hid, kind), target in sorted(TARGETS.items()):
        e = m[hid + '/' + kind]
        src = os.path.join(WORK, e['source'])
        if sha(src) != e['sha']:
            sys.exit(src + ' changed after it was verified')
        im = Image.open(src).convert('RGB')
        im.save(os.path.join(WORK, 'final', target + '.png'))                      # full resolution (the launcher's avatars)
        out = ImageOps.fit(im, SIZE[kind], Image.LANCZOS, centering=(0.5, 0.2))
        dst = os.path.join(IMG, target + '.jpg')
        if not os.path.exists(dst + '.meta'):
            sys.exit('no such UI picture: ' + dst)
        out.save(dst, quality=93, subsampling=0)
        e['installed'] = sha(dst)
    save(m)
    print('installed %d pictures into %s' % (len(TARGETS), IMG))


def gate():
    m = load()
    bad = []
    for (hid, kind), target in sorted(TARGETS.items()):
        e = m.get(hid + '/' + kind, {})
        dst = os.path.join(IMG, target + '.jpg')
        if e.get('state') not in OK or not e.get('installed') or sha(dst) != e['installed']:
            bad.append(hid + '/' + kind)
    if bad:
        sys.exit('HERO PHOTO GATE FAILED (unverified, or not the verified file): ' + ', '.join(bad))
    print('hero photo gate: %d pictures verified against their game models' % len(TARGETS))


if __name__ == '__main__':
    a = sys.argv[1:]
    cmd = a[0] if a else 'status'
    if cmd == 'sheets':
        sheets(a[1:] or None)
    elif cmd == 'accept':
        decide(a[1], a[2], 'accept', int(a[3]), a[4] if len(a) > 4 else '')
    elif cmd == 'reject':
        decide(a[1], a[2], 'reject', None, a[3] if len(a) > 3 else '')
    elif cmd == 'fallback':
        decide(a[1], a[2], 'fallback', None, a[3] if len(a) > 3 else '')
    elif cmd == 'status':
        sys.exit(1 if status() else 0)
    elif cmd == 'install':
        install()
    elif cmd == 'gate':
        gate()
    else:
        sys.exit(__doc__)
