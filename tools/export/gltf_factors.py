"""The glTF material factors a GLB's textures are multiplied by (three.js applies them; Blender's FBX export drops them):
metallicFactor, roughnessFactor, baseColorFactor, emissiveFactor of its first material, written beside the converted
textures as <tex_dir>/gltf_material.json - ZU.Editor (HeroImport.MakeMaterial) folds them into the URP material. Without
it, a Tripo prop whose metal-roughness map is all-metal but whose metallicFactor is 0 renders as black chrome.

usage: python tools/export/gltf_factors.py <jobs.txt> [...]     (the same "<in.glb>|<out.fbx>|<mode>" job files glb2fbx.py
       reads; model jobs only) - glb2fbx.py also calls write_factors() itself for new conversions"""
import json, os, struct, sys

def glb_json(path):
    with open(path, 'rb') as f:
        magic, version, length = struct.unpack('<4sII', f.read(12))
        if magic != b'glTF':
            raise ValueError('not a GLB: ' + path)
        clen, ctype = struct.unpack('<I4s', f.read(8))
        if ctype != b'JSON':
            raise ValueError('first chunk is not JSON: ' + path)
        return json.loads(f.read(clen).decode('utf-8'))

def factors(path):
    g = glb_json(path)
    mats = g.get('materials') or [{}]
    m = mats[0]
    pbr = m.get('pbrMetallicRoughness', {})
    return {
        'metallicFactor': pbr.get('metallicFactor', 1.0),
        'roughnessFactor': pbr.get('roughnessFactor', 1.0),
        'baseColorFactor': pbr.get('baseColorFactor', [1, 1, 1, 1]),
        'emissiveFactor': m.get('emissiveFactor', [0, 0, 0]),
        'hasMetalRoughTexture': 'metallicRoughnessTexture' in pbr,
        'materials': len(g.get('materials') or []),
    }

def write_factors(src, dst):
    tex_dir = os.path.splitext(dst)[0] + '_tex'
    os.makedirs(tex_dir, exist_ok=True)
    f = factors(src)
    with open(os.path.join(tex_dir, 'gltf_material.json'), 'w', encoding='utf-8') as fh:
        json.dump(f, fh, indent=1)
    return f

if __name__ == '__main__':
    n = 0
    for jobs in sys.argv[1:]:
        for line in open(jobs, encoding='utf-8'):
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            src, dst, mode = line.split('|')
            if mode != 'model':
                continue
            f = write_factors(src, dst)
            n += 1
            if f['metallicFactor'] < 0.99 or f['roughnessFactor'] < 0.99:
                print(f"  {os.path.basename(src)}: metal x{f['metallicFactor']:.2f} rough x{f['roughnessFactor']:.2f}")
    print('wrote', n, 'gltf_material.json')
