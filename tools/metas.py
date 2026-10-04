"""Give every asset under Assets/ a .meta (fresh GUID) if it has none, so every worktree's Editor imports the same GUIDs
instead of minting its own. Folders and scripts get their full default importer block; textures (.png/.jpg/.tga) the full
TextureImporter block of tools/metas_texture.txt (a guid-only texture meta imported as a Cubemap on 2026-10-04, so
MakeMaterial's Texture2D loads came back null): Texture2D, sRGB, except linear for mask maps (_rm, _urpmask, ...) and
linear + normal-map type for _normal; anything else just the GUID - Unity fills in the importer defaults on import and
keeps the GUID. Run before committing new assets made outside the Editor. Usage: python tools/metas.py [--dry]"""
import os, re, sys, uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets'))
TEXTURE = open(os.path.join(os.path.dirname(__file__), 'metas_texture.txt'), encoding='utf-8').read()
LINEAR = ('_rm', '_urpmask', '_metal', '_rough', '_ao', '_mask', '_orm', '_height')


def texture_meta(name):
    """the TextureImporter block for a texture, by the role its file name ends in (glb2fbx names them <base>_<role>)"""
    stem = os.path.splitext(name)[0].lower()
    body = TEXTURE
    if re.search(r'_normal\d*$', stem):
        body = body.replace('    sRGBTexture: 1\n', '    sRGBTexture: 0\n').replace('  textureType: 0\n', '  textureType: 1\n')
    elif any(re.search(k + r'\d*$', stem) for k in LINEAR):
        body = body.replace('    sRGBTexture: 1\n', '    sRGBTexture: 0\n')
    return body


dry = '--dry' in sys.argv
made = []
for dp, dns, fns in os.walk(ROOT):
    dns[:] = [d for d in dns if not d.startswith('.')]
    for name in dns + fns:
        if name.endswith('.meta') or name.startswith('.'):
            continue
        p = os.path.join(dp, name)
        if os.path.exists(p + '.meta'):
            continue
        body = 'fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n'
        if os.path.isdir(p):
            body += 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        elif name.endswith('.cs'):
            body += ('MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n'
                     '  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
        elif name.lower().endswith(('.png', '.jpg', '.jpeg', '.tga')):
            body += texture_meta(name)
        if not dry:
            with open(p + '.meta', 'w', encoding='utf-8', newline='\n') as f:
                f.write(body)
        made.append(os.path.relpath(p, os.path.dirname(ROOT)).replace(os.sep, '/'))
print(len(made), 'metas' + (' (dry run)' if dry else ' written'))
for m in made:
    print(' ', m)
