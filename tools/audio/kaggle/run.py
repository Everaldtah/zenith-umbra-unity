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
    # utf-8: the CLI prints progress bars and names Windows' cp1252 can't decode (a failed decode leaves stdout None)
    return subprocess.run([sys.executable, "-m", "kaggle", *a], capture_output=True, text=True, encoding="utf-8", errors="replace")


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


def push(stage, env, datasets, tag=""):
    name = stage + tag
    topic = f"zu-audio-{name}-{secrets.token_hex(4)}"
    build = OUT / "_build" / name
    shutil.rmtree(build, ignore_errors=True); build.mkdir(parents=True)
    env = {"NTFY_TOPIC": topic, **env}
    head = "import os\n" + "".join(f"os.environ[{k!r}] = {v!r}\n" for k, v in env.items())
    (build / "main.py").write_text(head + (HERE / f"{stage}.py").read_text(encoding="utf-8"), encoding="utf-8")
    meta = {"id": f"{USER}/zu-audio-{name}", "title": f"zu-audio-{name}", "code_file": "main.py", "language": "python",
            "kernel_type": "script", "is_private": True, "enable_gpu": True, "enable_tpu": False, "enable_internet": True,
            "machine_shape": "NvidiaTeslaT4", "dataset_sources": [f"{USER}/{d}" for d in datasets], "competition_sources": [], "kernel_sources": []}
    (build / "kernel-metadata.json").write_text(json.dumps(meta))
    r = kaggle("kernels", "push", "-p", str(build))
    print((r.stdout + r.stderr).strip())
    (OUT / f"{name}.topic").write_text(topic)
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


def fetch(stage, workers=8):
    """every output file: list all pages first (the CLI's `kernels output` stops after one page), then download 8 at a
    time with retries (one file at a time crawls); files already here are kept"""
    import requests
    from concurrent.futures import ThreadPoolExecutor
    from kaggle.api.kaggle_api_extended import KaggleApi
    from kagglesdk.kernels.types.kernels_api_service import ApiListKernelSessionOutputRequest
    dst = OUT / stage
    dst.mkdir(parents=True, exist_ok=True)
    api = KaggleApi(); api.authenticate()
    items, token = [], None
    while True:
        for attempt in range(6):
            try:
                with api.build_kaggle_client() as k:
                    rq = ApiListKernelSessionOutputRequest(); rq.user_name = USER; rq.kernel_slug = f"zu-audio-{stage}"
                    rq.page_size = 200
                    if token: rq.page_token = token
                    r = k.kernels.kernels_api_client.list_kernel_session_output(rq)
                break
            except Exception as e:
                print(f"  list retry {attempt + 1}: {type(e).__name__}", flush=True); time.sleep(10 * (attempt + 1))
        else:
            sys.exit("fetch failed: listing")
        items += [(f.file_name, f.url) for f in (r.files or [])]
        token = r.next_page_token
        if not token: break

    def get(it):
        name, url = it
        out = dst / name
        if out.exists() and out.stat().st_size > 0: return 0
        out.parent.mkdir(parents=True, exist_ok=True)
        for attempt in range(5):
            try:
                resp = requests.get(url, timeout=(20, 120))
                resp.raise_for_status()
                tmp = out.with_suffix(out.suffix + ".part"); tmp.write_bytes(resp.content); tmp.replace(out)
                return 1
            except Exception:
                time.sleep(5 * (attempt + 1))
        return -1

    with ThreadPoolExecutor(workers) as ex:
        res = list(ex.map(get, items))
    print(f"{len(items)} files listed: {res.count(1)} downloaded, {res.count(0)} already here, {res.count(-1)} failed -> {dst}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage")
    ap.add_argument("--env", nargs="*", default=[])
    ap.add_argument("--secret", nargs="*", default=[])
    ap.add_argument("--dataset", nargs="*", default=[], help="private datasets the kernel mounts (names under everaldtah/)")
    ap.add_argument("--src", help="upload this folder as the (first) --dataset before pushing")
    ap.add_argument("--fetch", action="store_true")
    ap.add_argument("--tag", default="", help="a separate kernel (zu-audio-<stage><tag>) so a rerun doesn't replace the last run's output")
    ap.add_argument("--no-wait", action="store_true")
    a = ap.parse_args()
    if a.fetch: return fetch(a.stage + a.tag)
    env = {}
    for kv in a.env:
        k, v = kv.split("=", 1)
        env[k] = Path(v[1:]).read_text(encoding="utf-8") if v.startswith("@") else v
    for k in a.secret:
        v = hf_token() if k == "HF_TOKEN" else os.environ.get(k)
        if not v: sys.exit(f"no value for secret {k} (for HF_TOKEN run: hf auth login)")
        env[k] = v
    if a.src: upload(a.dataset[0], a.src)
    topic = push(a.stage, env, a.dataset, a.tag)
    print("ntfy topic:", topic)
    if not a.no_wait and watch(a.stage + a.tag, topic): fetch(a.stage + a.tag)


if __name__ == "__main__":
    main()
