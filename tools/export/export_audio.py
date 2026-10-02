"""Bring the TS game's recorded sound bank (public/sfx: bank.json + <id>/<i>.ogg one-shots, lossless .flac loops, and
vo/<voice>/<key>_<i>.ogg voice lines) into Unity's Resources so the runtime can load any sound by id:

  sfx/<id>/<i>.ogg   -> Assets/ZU/Resources/ZUAudio/sfx/<id>/<i>.ogg   (copied)
  sfx/<id>/<i>.flac  -> Assets/ZU/Resources/ZUAudio/sfx/<id>/<i>.ogg   (Unity can't read FLAC: ffmpeg -> Vorbis q7)
  sfx/vo/...         -> Assets/ZU/Resources/ZUAudio/vo/...
  sfx/bank.json      -> Assets/ZU/Resources/ZUData/sfxbank.json

usage: python tools/export/export_audio.py [path/to/zenith-umbra]"""
import json, os, shutil, subprocess, sys

zu = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser('~/Projects/zenith-umbra')
src = os.path.join(zu, 'public', 'sfx')
root = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
out = os.path.join(root, 'Assets', 'ZU', 'Resources', 'ZUAudio')
n = {'copied': 0, 'converted': 0}
for d, _, files in os.walk(src):
    rel = os.path.relpath(d, src)
    for f in files:
        p = os.path.join(d, f)
        if f == 'bank.json':
            continue
        sub = 'vo' if rel.split(os.sep)[0] == 'vo' else 'sfx'
        rest = rel if sub == 'sfx' else os.path.relpath(d, os.path.join(src, 'vo'))
        dst_dir = os.path.join(out, sub, rest) if rest != '.' else os.path.join(out, sub)
        os.makedirs(dst_dir, exist_ok=True)
        stem, ext = os.path.splitext(f)
        dst = os.path.join(dst_dir, stem + '.ogg')
        if ext == '.ogg':
            shutil.copyfile(p, dst); n['copied'] += 1
        elif ext in ('.flac', '.wav'):
            if not os.path.exists(dst) or os.path.getmtime(dst) < os.path.getmtime(p):
                subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', p, '-c:a', 'libvorbis', '-q:a', '7', dst], check=True)
            n['converted'] += 1
shutil.copyfile(os.path.join(src, 'bank.json'), os.path.join(root, 'Assets', 'ZU', 'Resources', 'ZUData', 'sfxbank.json'))
print('audio:', n, '->', out)
