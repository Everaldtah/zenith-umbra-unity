# ---------------------------------------------------------------- Blender batch on Kaggle (tools/kaggle/kaggle_run.py blender)
# The Tripo GLB -> Unity FBX conversion without the user's PC: every *.glb under /kaggle/input goes through
# tools/blender/glb2fbx.py (Linux Blender BLENDER_VER, same as the local scoop install) and tools/blender/fix_textures.py.
#   JOBS     JSON {"<id>": "<mode>"}: per-file glb2fbx mode, e.g. "prop:20000" (building), "prop:3000" (ground dressing),
#            "prop:200" (instanced); ids not listed use MODE (default "prop" = 8k-tri LOD0). Only listed ids run when
#            ONLY=1.
#   WORKERS  parallel Blender processes (default 3; the T4 box has 4 vCPUs / 30 GB).
# Output: /kaggle/working/out.zip = <id>/<id>.fbx + <id>/<id>_tex/ + manifest.json ({id: {mode, tris per LOD, src_tris}}),
# laid out like Assets/ZU/Art/Props so it copies straight in (then zu_import_props in the Editor).
import os, sys, json, glob, time, shutil, subprocess, threading, urllib.request, traceback

TOPIC = os.environ.get("NTFY_TOPIC", "zu-unity-blender")
VER = os.environ.get("BLENDER_VER", "5.1.2")
JOBS = json.loads(os.environ.get("JOBS", "{}"))
MODE = os.environ.get("MODE", "prop")
ONLY = os.environ.get("ONLY", "0") == "1"
WORKERS = int(os.environ.get("WORKERS", "3"))
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
    return r.returncode, (r.stdout + r.stderr)[-800:]


try:
    t0 = time.time()
    glbs = {os.path.splitext(os.path.basename(p))[0]: p for p in glob.glob("/kaggle/input/**/*.glb", recursive=True)}
    todo = {i: p for i, p in sorted(glbs.items()) if not ONLY or i in JOBS}
    publish("boot", glbs=len(glbs), jobs=len(todo), missing=[i for i in JOBS if i not in glbs])
    # headless Blender on the Kaggle image still links a few X / GL libraries
    sh("apt-get -qq update && apt-get -qq install -y libxi6 libxxf86vm1 libxfixes3 libxrender1 libxkbcommon0 libsm6 libgl1 libegl1 > /dev/null")
    major = ".".join(VER.split(".")[:2])
    tar = f"blender-{VER}-linux-x64"
    code, tail = sh(f"cd /tmp && curl -sSfLO https://download.blender.org/release/Blender{major}/{tar}.tar.xz && tar -xf {tar}.tar.xz")
    blender = f"/tmp/{tar}/blender"
    code, ver = sh(f"{blender} -b --factory-startup --version")
    publish("blender", ok=code == 0, version=ver.strip().splitlines()[0] if ver.strip() else tail, secs=round(time.time() - t0))
    if code != 0:
        raise RuntimeError("blender does not start: " + ver)

    os.makedirs(OUT, exist_ok=True)
    lines = [f"{p}|{OUT}/{i}/{i}.fbx|{JOBS.get(i, MODE)}" for i, p in todo.items()]
    parts = [lines[k::WORKERS] for k in range(WORKERS) if lines[k::WORKERS]]
    results, logs = {}, {}

    def worker(k, part):
        jobs = f"/tmp/jobs{k}.txt"
        open(jobs, "w").write("\n".join(part) + "\n")
        r = subprocess.run([blender, "-b", "--factory-startup", "-P", f"{ZU}/tools/blender/glb2fbx.py", "--", jobs], capture_output=True, text=True)
        logs[k] = r.stdout + r.stderr
        for line in r.stdout.splitlines():
            if line.startswith("LODS "):
                d = json.loads(line[5:]); results.setdefault(d.pop("id"), {}).update(d)
            elif line.startswith("OK ") or line.startswith("FAIL "):
                i = os.path.splitext(os.path.basename(line.split()[1]))[0]
                results.setdefault(i, {})["ok"] = line.startswith("OK ")
                if line.startswith("FAIL "): results[i]["error"] = line[:400]
                publish("converted", id=i, ok=results[i]["ok"], done=len(results), of=len(lines))

    threads = [threading.Thread(target=worker, args=(k, p)) for k, p in enumerate(parts)]
    for t in threads: t.start()
    for t in threads: t.join()
    code, tail = sh(f"{sys.executable} {ZU}/tools/blender/fix_textures.py {OUT}")
    publish("textures", ok=code == 0, tail=tail[-300:])
    for i in todo:
        results.setdefault(i, {"ok": False, "error": "no result line"})["mode"] = JOBS.get(i, MODE)
    json.dump(results, open(f"{OUT}/manifest.json", "w"), indent=1)
    for k, text in logs.items():
        open(f"{OUT}/blender_{k}.log", "w").write(text)
    shutil.make_archive("/kaggle/working/out", "zip", OUT)
    shutil.rmtree(OUT)
    shutil.rmtree(ZU, ignore_errors=True)
    bad = [i for i, r in results.items() if not r.get("ok")]
    publish("done", ok=len(results) - len(bad), failed=bad, secs=round(time.time() - t0))
except Exception:
    publish("error", trace=traceback.format_exc()[-1500:])
    raise
