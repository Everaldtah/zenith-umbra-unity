"""Copy a Kaggle fparms batch's bound arms into the Unity project, the layout zu_import_fp_arms reads.

python tools/fpfit/stage_arms.py staging/kaggle/out/fparms-bN [hero ...]
-> Assets/ZU/Art/FPArms/<hero>/fp_arm_<hero>_<S>.fbx + fp_arm_<hero>_<S>_tex/ (basecolor.jpg q92 - a quarter of the PNG,
   normal.png and metallic_roughness.png kept lossless, gltf_material.json). Fit JSON / previews / Blender logs stay in staging.
Then: python tools/metas.py, commit, and zu_import_fp_arms in an Editor turn.
"""
import hashlib, os, shutil, sys
from pathlib import Path
from PIL import Image


def digest(d):
    """the maps of one _tex folder, hashed (a mirrored side's maps are the other side's, byte for byte)"""
    h = hashlib.sha1()
    for f in sorted(Path(d).iterdir()):
        h.update(f.name.encode()); h.update(f.read_bytes())
    return h.hexdigest()

src = Path(sys.argv[1]); only = set(sys.argv[2:])
dst = Path("Assets/ZU/Art/FPArms")
n = 0
for hd in sorted(p for p in src.iterdir() if p.is_dir() and p.name != "zu"):
    if only and hd.name not in only:
        continue
    for fbx in sorted(hd.glob("fp_arm_*.fbx")):
        out = dst / hd.name; out.mkdir(parents=True, exist_ok=True)
        shutil.copy2(fbx, out / fbx.name)
        tex = hd / (fbx.stem + "_tex")
        other = hd / (fbx.stem[:-1] + ("R" if fbx.stem.endswith("L") else "L") + "_tex")
        if tex.is_dir() and fbx.stem.endswith("_L") and other.is_dir() and digest(tex) == digest(other):
            shutil.rmtree(out / tex.name, ignore_errors=True)       # the mirrored side shares the other side's maps
            print("  (shares", other.name, ")")
        elif tex.is_dir():
            to = out / tex.name; to.mkdir(exist_ok=True)
            # role-suffixed names: tools/metas.py gives *_normal a normal-map importer and *_rm a linear one (a bare name would
            # import sRGB); HeroImport.MakeMaterial finds basecolor / normal / _rm by name
            names = {"basecolor.png": "arm_basecolor.jpg", "normal.png": "arm_normal.png", "metallic_roughness.png": "arm_rm.png"}
            for f in tex.iterdir():
                if f.name == "basecolor.png":
                    Image.open(f).convert("RGB").save(to / names[f.name], quality=92)
                elif f.name in names:
                    shutil.copy2(f, to / names[f.name])
                elif f.suffix == ".json":
                    shutil.copy2(f, to / f.name)
        n += 1
        print("staged", out / fbx.name)
print(n, "arms staged into", dst)
