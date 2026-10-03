# ---------------------------------------------------------------- game SFX on Kaggle (2 x T4): Stable Audio 3 Small-SFX
# Stable Audio 3 Small-SFX (Stability AI Community License: free under $1M annual revenue; T5Gemma under the Gemma Terms)
# renders every job in SA3_JOBS (tools/audio/design/sounds.py, exported by design/jobs.py) as `takes` takes at 44.1 kHz
# stereo - full band, unlike the 16-24 kHz models the web bank came from - resampled to 48 kHz, each take scored with
# LAION-CLAP (Apache-2.0) against its prompt: /kaggle/working/takes/<id>/<take>_<clap>.wav. tools/audio/design/compose.py
# picks, layers, masters and installs them.
#   env SA3_JOBS (JSON list), HF_TOKEN (the model is gated: the user accepted its licence), MODEL (small-sfx), STEPS (8)
import json, os, subprocess, sys, threading, time, traceback, urllib.request

TOPIC = os.environ.get("NTFY_TOPIC", "zu-audio-sa3")
JOBS = json.loads(os.environ.get("SA3_JOBS", "[]"))
MODEL = os.environ.get("MODEL", "small-sfx")
STEPS = int(os.environ.get("STEPS", "8"))
OUT = "/kaggle/working/takes"


def publish(phase, **extra):
    print("PHASE", phase, extra, flush=True)
    try:
        body = json.dumps({"topic": TOPIC, "message": json.dumps({"phase": phase, **extra})[:3800]}).encode()
        urllib.request.urlopen(urllib.request.Request("https://ntfy.sh", data=body, headers={"Content-Type": "application/json"}), timeout=15).read()
    except Exception as e:
        print("ntfy failed", e)


WORKER = r'''
import os, sys, json, time, traceback
import numpy as np, torch, soundfile as sf
from scipy.signal import resample_poly
from stable_audio_3 import StableAudioModel
shard, n, model_name, steps, out, jobs = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3], int(sys.argv[4]), sys.argv[5], json.load(open(sys.argv[6]))
mine = jobs[shard::n]
model = StableAudioModel.from_pretrained(model_name, device="cuda")
done = 0
for j in mine:
    d = os.path.join(out, j["id"]); os.makedirs(d, exist_ok=True)
    if len([f for f in os.listdir(d) if f.endswith(".raw.wav")]) >= j["takes"]: continue
    dur = max(1.0, float(j["secs"]) + (0.6 if not j.get("loop") else 1.0))
    try:
        left, k = j["takes"], 0
        while left > 0:
            bs = min(left, 4 if dur > 20 else 8)
            audio = model.generate(prompt=j["prompt"], duration=dur, steps=steps, batch_size=bs, seed=1000 + 7919 * k + 31 * shard)
            a = audio.detach().float().cpu().numpy()          # (batch, channels, samples) at 44.1 kHz
            for b in range(a.shape[0]):
                x = resample_poly(a[b].T, 160, 147, axis=0)   # 44.1 -> 48 kHz
                sf.write(os.path.join(d, f"{k + b}.raw.wav"), x.astype(np.float32), 48000, subtype="FLOAT")
            left -= bs; k += bs
        done += 1
    except Exception as e:
        traceback.print_exc()
        print("JOBERROR", j["id"], repr(e)[:300], flush=True)
    print(f"PROGRESS {shard} {done}/{len(mine)} {j['id']}", flush=True)
'''

try:
    publish("boot", jobs=len(JOBS), model=MODEL)
    t0 = time.time()
    # the package pins torch 2.7.1 + transformers >= 5.8 (T5Gemma); install it without touching the image's torch first,
    # and fall back to its full dependency set if the import fails
    cmd = (f"git clone -q https://github.com/Stability-AI/stable-audio-3 /tmp/sa3 && "
           f"{sys.executable} -m pip install -q 'transformers>=5.8.0' 'huggingface-hub>=1.7.1' einops einops-exts safetensors && "
           f"{sys.executable} -m pip install -q --no-deps -e /tmp/sa3")
    r = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    ok = subprocess.run([sys.executable, "-c", "import stable_audio_3"], capture_output=True, text=True)
    if ok.returncode != 0:
        publish("deps-fallback", err=ok.stderr[-400:])
        r = subprocess.run(f"{sys.executable} -m pip install -q -e /tmp/sa3", shell=True, capture_output=True, text=True)
    publish("installed", ok=r.returncode == 0, tail=(r.stdout + r.stderr)[-400:], mins=round((time.time() - t0) / 60, 1))
    os.makedirs(OUT, exist_ok=True)
    json.dump(JOBS, open("/tmp/jobs.json", "w"))
    open("/tmp/worker.py", "w").write(WORKER)
    procs = [subprocess.Popen([sys.executable, "/tmp/worker.py", str(g), "2", MODEL, str(STEPS), OUT, "/tmp/jobs.json"],
                              env={**os.environ, "CUDA_VISIBLE_DEVICES": str(g)}, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
             for g in range(2)]

    def pump(p, g):
        tail = []
        for line in p.stdout:
            tail = (tail + [line])[-40:]
            if line.startswith("PROGRESS"):
                k = line.split()[2]
                if k.split("/")[0].endswith("0") or k.split("/")[0] == "1": publish("progress", gpu=g, at=k, mins=round((time.time() - t0) / 60, 1))
            elif line.startswith("JOBERROR"): publish("job-error", gpu=g, line=line[:300])
        p.wait()
        if p.returncode: publish("worker-error", gpu=g, code=p.returncode, tail="".join(tail)[-1500:])

    th = [threading.Thread(target=pump, args=(p, g)) for g, p in enumerate(procs)]
    for x in th: x.start()
    for x in th: x.join()
    # score every take against its prompt with CLAP (48 kHz), rename <k>.raw.wav -> <k>_<score>.wav
    import glob, numpy as np, soundfile as sf, torch
    from transformers import ClapModel, ClapProcessor
    clap = ClapModel.from_pretrained("laion/clap-htsat-unfused").to("cuda").eval()
    cp = ClapProcessor.from_pretrained("laion/clap-htsat-unfused")
    made = 0
    for j in JOBS:
        takes = sorted(glob.glob(f"{OUT}/{j['id']}/*.raw.wav"))
        if not takes: continue
        text = j["prompt"].split(", close-mic")[0].split(", stereo field")[0]
        for p in takes:
            a, _ = sf.read(p); m = a.mean(1).astype(np.float32) if a.ndim > 1 else a.astype(np.float32)
            inp = cp(text=[text], audio=[m[:48000 * 10]], sampling_rate=48000, return_tensors="pt", padding=True)
            with torch.no_grad():
                o = clap(**{k: v.to("cuda") for k, v in inp.items()})
            s = float(torch.nn.functional.cosine_similarity(o.text_embeds, o.audio_embeds)[0])
            os.replace(p, p.replace(".raw.wav", f"_{s:.3f}.wav")); made += 1
    publish("done", takes=made, ids=len(JOBS), mins=round((time.time() - t0) / 60, 1))
except Exception:
    publish("crash", tb=traceback.format_exc()[-1500:])
    raise
