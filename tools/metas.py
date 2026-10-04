"""Give every asset under Assets/ a .meta (fresh GUID) if it has none, so every worktree's Editor imports the same GUIDs
instead of minting its own. Folders and scripts get their full default importer block; anything else just the GUID -
Unity fills in the importer defaults on import and keeps the GUID. Run before committing new assets made outside the
Editor. Usage: python tools/metas.py [--dry]"""
import os, sys, uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets'))
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
        if not dry:
            with open(p + '.meta', 'w', encoding='utf-8', newline='\n') as f:
                f.write(body)
        made.append(os.path.relpath(p, os.path.dirname(ROOT)).replace(os.sep, '/'))
print(len(made), 'metas' + (' (dry run)' if dry else ' written'))
for m in made:
    print(' ', m)
