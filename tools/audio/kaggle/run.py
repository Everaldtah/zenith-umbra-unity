#!/usr/bin/env python3
"""Run an audio stage (tools/audio/kaggle/<stage>.py) on Kaggle's free GPUs (2 x T4) as a private headless script kernel,
follow its progress over ntfy, and download what it made - the web game's assetgen/kaggle_run.py pattern, for the Unity
sound bank (the user's rule: heavy generation runs on Kaggle, not on this PC).

    python tools/audio/kaggle/run.py audiosr --dataset zu-audio-vo24 --src <dir>     # upload <dir> as a private dataset first
    python tools/audio/kaggle/run.py sa3 --env SA3_JOBS=@tools/audio/design/jobs.json --secret HF_TOKEN
    python tools/audio/kaggle/run.py <stage> --fetch                                 # just download the last run's output

--env K=V puts V in the kernel's environment (K=@file reads V from a file); --secret K passes this PC's value of K
(for HF_TOKEN: the token `hf auth login` saved) - the kernel is private, and nothing is written to the repo.
Outputs land in tools/audio/out/kaggle/<stage>/.
"""
import argparse, json, os, secrets, shutil, subprocess, sys, time, urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parent / "out" / "kaggle"
USER = "everaldtah"


def kaggle(*a):
    return subprocess.run([sys.executable, "-m", "kaggle", *a], capture_output=True, text=True)


def hf_token():
    t = os.environ.get("HF_TOKEN")
    if t: return t
    p = Path.home() / ".cache" / "huggingface" / "token"
    return p.read_text().strip() if p.exists() else None


def upload(name, src):
    """src -> private dataset everaldtah/<name> (created the first time, versioned after)"""
    src = Path(src)
    meta = src / "dataset-metadata.json"
    if not meta.exists(): meta.write_text(json.dumps({"title": name, "id": f"{USER}/{name}", "licenses": [{"name": "other"}]}))
    r = kaggle("datasets", "status", f"{USER}/{name}")
    if "ready" in (r.stdout + r.stderr).lower():
        r = kaggle("datasets", "version", "-p", str(src), "-m", time.strftime("%Y-%m-%d %H:%M"), "--dir-mode", "zip")
    else:
        r = kaggle("datasets", "create", "-p", str(src), "--dir-mode", "zip")
    print("dataset:", (r.stdout + r.stderr).strip()[-400:])
    # wait until the new version is ready (a kernel pushed too early sees the old one)
    for _ in range(90):
        s = kaggle("datasets", "status", f"{USER}/{name}")
        if "ready" in (s.stdout + s.stderr).lower(): return
        time.sleep(10)
    print("warning: dataset not ready after 15 min")


def push(stage, env, datasets):
    topic = f"zu-audio-{stage}-{secrets.token_hex(4)}"
    build = OUT / "_build" / stage
    shutil.rmtree(build, ignore_errors=True); build.mkdir(parents=True)
    env = {"NTFY_TOPIC": topic, **env}
    head = "import os\n" + "".join(f"os.environ[{k!r}] = {v!r}\n" for k, v in env.items())
    (build / "main.py").write_text(head + (HERE / f"{stage}.py").read_text(encoding="utf-8"), encoding="utf-8")
    meta = {"id": f"{USER}/zu-audio-{stage}", "title": f"zu-audio-{stage}", "code_file": "main.py", "language": "python",
            "kernel_type": "script", "is_private": True, "enable_gpu": True, "enable_tpu": False, "enable_internet": True,
            "machine_shape": "NvidiaTeslaT4", "dataset_sources": [f"{USER}/{d}" for d in datasets], "competition_sources": [], "kernel_sources": []}
    (build / "kernel-metadata.json").write_text(json.dumps(meta))
    r = kaggle("kernels", "push", "-p", str(build))
    print((r.stdout + r.stderr).strip())
    (OUT / f"{stage}.topic").write_text(topic)
    # the kernel's source holds the env (a token too): don't leave a copy on disk
    (build / "main.py").unlink()
    return topic


def watch(stage, topic):
    slug = f"{USER}/zu-audio-{stage}"
    since, last = "all", time.time()
    while True:
        try:
            with urllib.request.urlopen(f"https://ntfy.sh/{topic}/json?poll=1&since={since}", timeout=30) as r:
                for line in r.read().decode().splitlines():
                    m = json.loads(line)
                    if m.get("event") == "message":
                        print(time.strftime("%H:%M:%S"), m.get("message", "")[:300], flush=True); since = m["id"]
        except Exception:
            pass
        if time.time() - last > 60:
            last = time.time()
            s = kaggle("kernels", "status", slug)
            st = (s.stdout + s.stderr).lower()
            if any(k in st for k in ("complete", "error", "cancel")):
                print("kernel:", st.strip()); return "complete" in st
        time.sleep(15)


def fetch(stage):
    dst = OUT / stage
    shutil.rmtree(dst, ignore_errors=True); dst.mkdir(parents=True)
    r = kaggle("kernels", "output", f"{USER}/zu-audio-{stage}", "-p", str(dst))
    print((r.stdout + r.stderr).strip()[-600:])
    print("->", dst)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage")
    ap.add_argument("--env", nargs="*", default=[])
    ap.add_argument("--secret", nargs="*", default=[])
    ap.add_argument("--dataset", nargs="*", default=[], help="private datasets the kernel mounts (names under everaldtah/)")
    ap.add_argument("--src", help="upload this folder as the (first) --dataset before pushing")
    ap.add_argument("--fetch", action="store_true")
    ap.add_argument("--no-wait", action="store_true")
    a = ap.parse_args()
    if a.fetch: return fetch(a.stage)
    env = {}
    for kv in a.env:
        k, v = kv.split("=", 1)
        env[k] = Path(v[1:]).read_text(encoding="utf-8") if v.startswith("@") else v
    for k in a.secret:
        v = hf_token() if k == "HF_TOKEN" else os.environ.get(k)
        if not v: sys.exit(f"no value for secret {k} (for HF_TOKEN run: hf auth login)")
        env[k] = v
    if a.src: upload(a.dataset[0], a.src)
    topic = push(a.stage, env, a.dataset)
    print("ntfy topic:", topic)
    if not a.no_wait and watch(a.stage, topic): fetch(a.stage)


if __name__ == "__main__":
    main()
