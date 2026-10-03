#!/usr/bin/env python3
"""Run a tools/kaggle/<stage>/<stage>.py stage on Kaggle as a headless GPU (T4 x2) script kernel - the user's rule for the
Unity sessions is to offload batch work that doesn't need Windows, Unity or Chrome (~/zu-resume/KAGGLE.md). Port of the web
repo's assetgen/kaggle_run.py, plus input upload and repo-file shipping:

    python tools/kaggle/kaggle_run.py blender --tag props1 --data staging/props_src --env JOBS='{"prop_gulch_spire": "prop:8000"}'
    python tools/kaggle/kaggle_run.py watch blender-props1          # re-attach to a running kernel

  --data <dir>   uploaded as the PRIVATE dataset everaldtah/zu-unity-<stage>-<tag> (created the first time, then a new
                 version) and mounted under /kaggle/input; the kernel itself is zu-unity-run-<stage>-<tag>. Never put licence-restricted files in it (Mixamo/Kevin).
  --env K=V      environment for the stage (os.environ in the kernel).
  files.txt      beside the stage: repo files the stage needs (e.g. tools/blender/glb2fbx.py), shipped inside main.py and
                 written back to /kaggle/working/zu/<same path> before the stage runs.
Progress streams over ntfy (a random topic per run). When the kernel ends, its /kaggle/working files land in
staging/kaggle/out/<stage>-<tag>/ (out.zip is unpacked there). staging/ is gitignored.
"""
import argparse, functools, json, subprocess, sys, time, urllib.request, secrets, shutil, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
WORK = REPO / "staging" / "kaggle"
USER = "everaldtah"
print = functools.partial(print, flush=True)   # progress shows up when run in the background


def kaggle(*a):
    return subprocess.run([sys.executable, "-m", "kaggle", *a], capture_output=True, text=True)


def upload(src, slug):
    """make <src> the latest version of the private dataset <slug> and wait until Kaggle has processed it"""
    src = Path(src).resolve()
    (src / "dataset-metadata.json").write_text(json.dumps({"title": slug.split("/")[1], "id": slug, "licenses": [{"name": "other"}]}))
    exists = slug in kaggle("datasets", "list", "--mine", "-s", slug.split("/")[1], "--csv").stdout
    r = kaggle("datasets", "version", "-p", str(src), "-m", time.strftime("%Y-%m-%d %H:%M"), "-r", "zip") if exists \
        else kaggle("datasets", "create", "-p", str(src), "-r", "zip")
    print(r.stdout.strip()[-300:], r.stderr.strip()[-300:])
    for _ in range(120):
        st = kaggle("datasets", "status", slug).stdout.strip()
        if "ready" in st: return
        if "error" in st.lower(): sys.exit("dataset failed: " + st)
        time.sleep(10)
    sys.exit("dataset not ready after 20 min: " + slug)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage")
    ap.add_argument("--tag", default="run")
    ap.add_argument("--env", nargs="*", default=[])
    ap.add_argument("--data", help="folder to upload as this run's private input dataset")
    ap.add_argument("--datasets", nargs="*", default=[], help="extra existing datasets to mount (owner/slug)")
    ap.add_argument("--no-wait", action="store_true")
    a = ap.parse_args()
    name = f"{a.stage}-{a.tag}"
    datasets = list(a.datasets)
    if a.data:
        slug = f"{USER}/zu-unity-{name}"
        upload(a.data, slug)
        datasets.append(slug)
    topic = f"zu-unity-{name}-{secrets.token_hex(4)}"
    build = WORK / "build" / name
    shutil.rmtree(build, ignore_errors=True); build.mkdir(parents=True)
    env = {"NTFY_TOPIC": topic, **dict(kv.split("=", 1) for kv in a.env)}
    listed = HERE / a.stage / "files.txt"
    files = {p: (REPO / p).read_text(encoding="utf-8") for p in
             (l.strip() for l in (listed.read_text().splitlines() if listed.exists() else [])) if p and not p.startswith("#")}
    head = ("import os\n" + "".join(f"os.environ[{k!r}] = {v!r}\n" for k, v in env.items())
            + f"ZU_FILES = {files!r}\n"
            + "for _p, _s in ZU_FILES.items():\n"
              "    _f = os.path.join('/kaggle/working/zu', _p); os.makedirs(os.path.dirname(_f), exist_ok=True)\n"
              "    open(_f, 'w', encoding='utf-8').write(_s)\n\n")
    (build / "main.py").write_text(head + (HERE / a.stage / f"{a.stage}.py").read_text(encoding="utf-8"), encoding="utf-8")
    meta = {"id": f"{USER}/zu-unity-run-{name}", "title": f"zu-unity-run-{name}", "code_file": "main.py", "language": "python",
            "kernel_type": "script", "is_private": True, "enable_gpu": True, "enable_tpu": False, "enable_internet": True,
            "machine_shape": "NvidiaTeslaT4", "dataset_sources": datasets, "competition_sources": [], "kernel_sources": []}
    (build / "kernel-metadata.json").write_text(json.dumps(meta), encoding="utf-8")
    r = kaggle("kernels", "push", "-p", str(build))
    print(r.stdout.strip(), r.stderr.strip())
    if r.returncode != 0 or "error" in (r.stdout + r.stderr).lower():
        sys.exit("kernel push failed")
    print("ntfy topic:", topic)
    (WORK / "build" / f"{name}.topic").write_text(topic)
    if not a.no_wait:
        watch(name, topic)


def watch(name, topic):
    seen, slug = set(), f"{USER}/zu-unity-run-{name}"   # kernel; its input dataset is zu-unity-<name> (one slug can't be both)
    while True:
        try:
            with urllib.request.urlopen(f"https://ntfy.sh/{topic}/json?poll=1&since=all", timeout=20) as r:
                for line in r.read().decode().splitlines():
                    e = json.loads(line)
                    if e.get("event") != "message" or e["id"] in seen: continue
                    seen.add(e["id"]); print(time.strftime("[%H:%M:%S]"), e.get("message", "")[:1500], flush=True)
        except Exception as ex:
            print("poll error", ex)
        st = kaggle("kernels", "status", slug).stdout
        if any(s in st for s in ("COMPLETE", "ERROR", "CANCEL")):
            print(st.strip())
            out = WORK / "out" / name
            shutil.rmtree(out, ignore_errors=True); out.mkdir(parents=True)
            print(kaggle("kernels", "output", slug, "-p", str(out), "--page-size", "200").stdout[-400:])
            if (out / "out.zip").exists():
                with zipfile.ZipFile(out / "out.zip") as z: z.extractall(out)
                (out / "out.zip").unlink()
            print("outputs in", out)
            return
        time.sleep(30)


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "watch":
        watch(sys.argv[2], (WORK / "build" / f"{sys.argv[2]}.topic").read_text().strip())
    else:
        main()
