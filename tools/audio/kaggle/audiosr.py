# ---------------------------------------------------------------- audio super-resolution on Kaggle (2 x T4): AudioSR
# AudioSR (Haohe Liu et al., Apache-2.0; weights haoheliu/audiosr_speech + audiosr_basic, not gated) regenerates the top
# octave a 24 kHz render never had: every clip in the mounted dataset comes out at 48 kHz with content up to 24 kHz.
# tools/audio/master.py then keeps the ORIGINAL below ~10.5 kHz (the voice's timbre exactly as cast) and takes only the
# new air above it from here.
#   env MODEL   speech | basic (sound effects, music)     STEPS  DDIM steps (50)     GUIDE  guidance scale (3.5)
#   in   /kaggle/input/<dataset>/**.{ogg,wav,flac}  (zips are unpacked)
#   out  /kaggle/working/sr/<same relative path>.wav  (48 kHz float, mono)  + /kaggle/working/sr/_log.json
import glob, json, os, re, subprocess, sys, threading, time, traceback, urllib.request, zipfile

TOPIC = os.environ.get("NTFY_TOPIC", "zu-audio-audiosr")
MODEL = os.environ.get("MODEL", "speech")
STEPS = int(os.environ.get("STEPS", "50"))
GUIDE = float(os.environ.get("GUIDE", "3.5"))
LIMIT = int(os.environ.get("LIMIT", "0"))          # a probe: this many files spread over the set (0 = all)
OUT = "/kaggle/working/sr"


def publish(phase, **extra):
    print("PHASE", phase, extra, flush=True)
    try:
        body = json.dumps({"topic": TOPIC, "message": json.dumps({"phase": phase, **extra})[:3800]}).encode()
        urllib.request.urlopen(urllib.request.Request("https://ntfy.sh", data=body, headers={"Content-Type": "application/json"}), timeout=15).read()
    except Exception as e:
        print("ntfy failed", e)


WORKER = r'''
import os, sys, json, time, traceback
import numpy as np
# AudioSR predates numpy 1.24 / librosa 0.10: the aliases and positional mel() it still uses
for k, v in {"float": float, "int": int, "complex": complex, "bool": bool}.items():
    if not hasattr(np, k): setattr(np, k, v)
import librosa, librosa.filters
_mel = librosa.filters.mel
def mel(*a, **k):
    names = ["sr", "n_fft", "n_mels", "fmin", "fmax"]
    k.update({n: v for n, v in zip(names, a)})
    return _mel(**k)
librosa.filters.mel = mel
import soundfile as sf, torch, audiosr
shard, n, model_name, steps, guide, out, files = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3], int(sys.argv[4]), float(sys.argv[5]), sys.argv[6], json.loads(open(sys.argv[7]).read())
mine = files[shard::n]
model = audiosr.build_model(model_name=model_name, device="cuda")
log = []
for i, (src, rel) in enumerate(mine):
    dst = os.path.join(out, os.path.splitext(rel)[0] + ".wav")
    if os.path.exists(dst): continue
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    t0 = time.time()
    try:
        # AudioSR reads the file itself (any rate) and returns (1, 1, N) at 48 kHz, padded to its window
        w = audiosr.super_resolution(model, src, seed=42, guidance_scale=guide, ddim_steps=steps, latent_t_per_second=12.8)
        w = np.asarray(w).reshape(-1).astype(np.float32)
        a, sr = sf.read(src)
        n48 = int(round(len(a) * 48000 / sr))
        sf.write(dst, w[:n48], 48000, subtype="FLOAT")
        log.append({"rel": rel, "secs": round(time.time() - t0, 2)})
    except Exception as e:
        log.append({"rel": rel, "error": repr(e)[:300]})
        traceback.print_exc()
    if i % 10 == 0: print(f"PROGRESS {shard} {i + 1}/{len(mine)}", flush=True)
json.dump(log, open(os.path.join(out, f"_log{shard}.json"), "w"))
'''

try:
    publish("boot", model=MODEL)
    t0 = time.time()
    # dependencies: AudioSR without its old pins (numpy<=1.23.5, librosa 0.9.2, transformers 4.30 would break the image)
    r = subprocess.run(f"{sys.executable} -m pip install -q --no-deps audiosr==0.0.7 && "
                       f"{sys.executable} -m pip install -q torchlibrosa ftfy progressbar unidecode phonemizer timm einops", shell=True, capture_output=True, text=True)
    publish("installed", ok=r.returncode == 0, tail=(r.stdout + r.stderr)[-500:])
    # inputs: every audio file under /kaggle/input (zips unpacked into /tmp/in)
    for z in glob.glob("/kaggle/input/**/*.zip", recursive=True):
        zipfile.ZipFile(z).extractall("/tmp/in/" + os.path.splitext(os.path.basename(z))[0])
    files = []
    for root in ["/kaggle/input", "/tmp/in"]:
        for p in glob.glob(root + "/**/*", recursive=True):
            if p.lower().endswith((".ogg", ".wav", ".flac")) and "/_" not in p:
                rel = os.path.relpath(p, root)
                if root == "/kaggle/input":                                   # drop [datasets/<user>/]<dataset>/
                    rel = re.sub(r"^(datasets/[^/]+/)?[^/]+/", "", rel)
                files.append((p, rel))
    files = sorted({rel: (p, rel) for p, rel in files}.values(), key=lambda x: x[1])      # a zip and its unpacked copy: once
    if LIMIT: files = [files[int(i * len(files) / LIMIT)] for i in range(LIMIT)]
    os.makedirs(OUT, exist_ok=True)
    json.dump(files, open("/tmp/files.json", "w"))
    open("/tmp/worker.py", "w").write(WORKER)
    publish("inputs", n=len(files), first=files[:3])
    procs = [subprocess.Popen([sys.executable, "/tmp/worker.py", str(g), "2", MODEL, str(STEPS), str(GUIDE), OUT, "/tmp/files.json"],
                              env={**os.environ, "CUDA_VISIBLE_DEVICES": str(g)}, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
             for g in range(2)]

    def pump(p, g):
        tail = []
        for line in p.stdout:
            tail = (tail + [line])[-30:]
            if line.startswith("PROGRESS"): publish("progress", gpu=g, at=line.split()[-1], mins=round((time.time() - t0) / 60, 1))
        p.wait()
        if p.returncode: publish("worker-error", gpu=g, code=p.returncode, tail="".join(tail)[-1500:])

    th = [threading.Thread(target=pump, args=(p, g)) for g, p in enumerate(procs)]
    for x in th: x.start()
    for x in th: x.join()
    made = len(glob.glob(OUT + "/**/*.wav", recursive=True))
    errs = []
    for f in glob.glob(OUT + "/_log*.json"): errs += [e for e in json.load(open(f)) if "error" in e]
    publish("done", made=made, of=len(files), errors=len(errs), first_errors=errs[:3], mins=round((time.time() - t0) / 60, 1))
except Exception:
    publish("crash", tb=traceback.format_exc()[-1500:])
    raise
