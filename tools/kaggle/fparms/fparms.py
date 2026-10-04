# ---------------------------------------------------------------- first-person arms on Kaggle (tools/kaggle/kaggle_run.py fparms)
# Every fp_arm_<hero>_<R|L>.glb under /kaggle/input (Tripo forearm + hand, palm down) -> tools/fpfit/fp_arm_fit.py (joints)
# -> tools/blender/fp_arm_bind.py (skinned FBX on forearm/hand/15 finger bones). A hero with only a right arm gets the
# mirrored left as well. Output: /kaggle/working/out.zip = <hero>/fp_arm_<hero>_<S>.fbx + _tex/ + <S>_fit.json/.png and
# manifest.json - laid out like Assets/ZU/Art/FPArms, then zu_import_fp_arms in the Editor.
#   TRIS   triangle budget per arm (default 12000)
import os, sys, json, glob, time, shutil, subprocess, urllib.request, traceback, re

TOPIC = os.environ.get("NTFY_TOPIC", "zu-unity-fparms")
VER = os.environ.get("BLENDER_VER", "5.1.2")
TRIS = os.environ.get("TRIS", "12000")
ZU = "/kaggle/working/zu"
OUT = "/kaggle/working/out"


def publish(phase, **extra):
    print("PHASE", phase, extra, flush=True)
    try:
        body = json.dumps({"topic": TOPIC, "message": json.dumps({"phase": phase, **extra})[:3800]}).encode()
        urllib.request.urlopen(urllib.request.Request("https://ntfy.sh", data=body, headers={"Content-Type": "application/json"}), timeout=15).read()
    except Exception as e:
        print("ntfy failed", e)


def sh(cmd):
    r = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    return r.returncode, (r.stdout + r.stderr)


try:
    t0 = time.time()
    arms = {}
    for p in glob.glob("/kaggle/input/**/fp_arm_*_*.glb", recursive=True):
        m = re.match(r"fp_arm_(.+)_([LR])$", os.path.splitext(os.path.basename(p))[0])
        if m: arms[(m.group(1), m.group(2))] = p
    publish("boot", arms=len(arms), ids=sorted(f"{h}_{s}" for h, s in arms))
    sh(f"{sys.executable} -m pip -q install trimesh networkx matplotlib scipy")
    sh("apt-get -qq update && apt-get -qq install -y libxi6 libxxf86vm1 libxfixes3 libxrender1 libxkbcommon0 libsm6 libgl1 libegl1 > /dev/null")
    major = ".".join(VER.split(".")[:2]); tar = f"blender-{VER}-linux-x64"
    sh(f"cd /tmp && curl -sSfLO https://download.blender.org/release/Blender{major}/{tar}.tar.xz && tar -xf {tar}.tar.xz")
    blender = f"/tmp/{tar}/blender"
    code, ver = sh(f"{blender} -b --factory-startup --version")
    publish("blender", ok=code == 0, version=ver.strip().splitlines()[0] if ver.strip() else "", secs=round(time.time() - t0))
    if code != 0:
        raise RuntimeError("blender does not start: " + ver[-400:])
    results = {}
    for (hero, S), glb in sorted(arms.items()):
        ident = f"fp_arm_{hero}_{S}"; d = f"{OUT}/{hero}"; os.makedirs(d, exist_ok=True)
        fitj = f"{d}/{S}_fit.json"
        code, log = sh(f"{sys.executable} {ZU}/tools/fpfit/fp_arm_fit.py {glb} {S} {fitj}")
        if code != 0:
            results[ident] = {"ok": False, "stage": "fit", "error": log[-500:]}; publish("fit-failed", id=ident, error=log[-300:]); continue
        fit = json.load(open(fitj))
        mirror = "--mirror" if (hero, "L" if S == "R" else "R") not in arms else ""
        code, log = sh(f"{blender} -b --factory-startup -P {ZU}/tools/blender/fp_arm_bind.py -- {glb} {fitj} {d} {ident} {mirror} --tris {TRIS}")
        oks = [l for l in log.splitlines() if l.startswith("OK ") or l.startswith("FAIL ")]
        results[ident] = {"ok": any(l.startswith("OK ") for l in oks), "fingers": fit.get("fingers"), "lines": oks}
        open(f"{d}/{ident}_blender.log", "w").write(log[-20000:])
        publish("bound", id=ident, ok=results[ident]["ok"], fingers=fit.get("fingers"), mirrored=bool(mirror), done=len(results), of=len(arms))
    json.dump(results, open(f"{OUT}/manifest.json", "w"), indent=1)
    shutil.make_archive("/kaggle/working/out", "zip", OUT)
    shutil.rmtree(OUT, ignore_errors=True)
    publish("done", ok=sum(1 for r in results.values() if r["ok"]), of=len(results), secs=round(time.time() - t0))
except Exception as e:
    traceback.print_exc()
    publish("error", error=str(e)[:500])
