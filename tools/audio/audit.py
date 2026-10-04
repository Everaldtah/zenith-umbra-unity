#!/usr/bin/env python3
"""Audit the Unity sound bank (Assets/ZU/Resources/ZUAudio): every clip decoded and measured - loudness (BS.1770 M / I),
true peak, effective bandwidth (dull band-limited renders), the crackle / pop / clipping checks - and grouped per sound id
and category so the worst offenders head the list.

    python tools/audio/audit.py                       # report -> tools/audio/out/audit.json + audit.md, summary to stdout
    python tools/audio/audit.py --root <dir>          # audit another bank folder (e.g. a remastered one)
    python tools/audio/audit.py --selftest            # the meters against known signals

The bank's category of each sfx id comes from ZUData/sfxbank.json; voice lines are category "voice"."""
import argparse, json, os, sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import meter  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
BANK = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio")
DATA = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUData", "sfxbank.json")
OUT = os.path.join(HERE, "out")

# targets the remaster aims at (max momentary loudness for one-shots, integrated for beds; true peak ceiling) - see
# docs/audio/AUDIO_STANDARD.md for why each sits where it does
TARGET_M = {"weapon": -14, "impact": -16, "ability": -15, "move": -21, "step": -22, "feedback": -16, "loop": -20, "amb": -26, "voice": -16}
TP_MAX = -1.0
# one-shots are judged by the RMS of their active part (dBFS): a 400 ms loudness window under-reads short sounds
RMS_TARGET = {"weapon": -15, "impact": -17, "ability": -16, "move": -21, "step": -22, "feedback": -17}


def active_rms_db(a):
    m = a if a.ndim == 1 else a.mean(1)
    blk = 480
    e = np.array([np.sqrt(np.mean(m[i:i + blk] ** 2)) for i in range(0, max(1, len(m) - blk), blk)]) + 1e-12
    act = e[e > e.max() * 0.1]
    return float(20 * np.log10(np.sqrt(np.mean(act ** 2))))


def measure(job):
    path, cat, sid = job
    try:
        a, sr = sf.read(path, always_2d=False, dtype="float64")
    except Exception as e:
        return {"path": path, "error": str(e)}
    loop = cat in ("loop", "amb")
    r = {"path": os.path.relpath(path, BANK).replace("\\", "/"), "id": sid, "cat": cat, "sr": sr,
         "ch": 1 if a.ndim == 1 else a.shape[1], "dur": round(len(a) / sr, 3)}
    r.update(meter.loudness(a, sr))
    r["tp"] = round(meter.true_peak(a), 2)
    r["rms"] = round(active_rms_db(a), 2)
    r["peak"] = round(meter.sample_peak(a), 2)
    r.update(meter.bandwidth(a, sr))
    r["hf"] = round(meter.hf_share(a, sr), 3)
    r.update(meter.defects(a, sr, loop=loop))
    fails = []
    if r["clip"] > 0: fails.append("clip")
    if r["tp"] > TP_MAX: fails.append("truepeak")
    if r["clicks"] > 2: fails.append("clicks")
    if r["edges"] > 0.02 and not loop: fails.append("edges")
    if r["dropouts"] > 0: fails.append("dropouts")
    if r["dc"] > 0.01: fails.append("dc")
    if loop and r.get("seam", 0) > 1.5: fails.append("seam")
    warns = []
    if r["top"] < 15000: warns.append(f"dull<{r['top'] // 1000}k")
    if r["saturate"] > 0.004: warns.append("squashed")
    if r["hf"] > 0.3: warns.append("fizz")
    if cat in RMS_TARGET and not loop:
        if abs(r["rms"] - RMS_TARGET[cat]) > 3: warns.append(f"level{r['rms'] - RMS_TARGET[cat]:+.0f}")
    else:
        t = TARGET_M.get(cat)
        if t is not None and abs((r["I"] if loop else r["M"]) - t) > 3: warns.append(f"level{(r['I'] if loop else r['M']) - t:+.0f}")
    r["fail"] = fails; r["warn"] = warns
    return r


def jobs(root):
    bank = json.load(open(DATA, encoding="utf-8"))
    cats = {k: v.get("cat", "ability") for k, v in bank["sfx"].items()}
    out = []
    for d, _, files in os.walk(root):
        for f in files:
            if f.endswith(".meta") or not f.lower().endswith((".ogg", ".wav", ".flac")): continue
            p = os.path.join(d, f)
            rel = os.path.relpath(p, root).replace("\\", "/").split("/")
            if rel[0] == "vo":
                out.append((p, "voice", f"vo/{rel[1]}/{os.path.splitext(f)[0].rsplit('_', 1)[0]}"))
            else:
                sid = rel[1] if rel[0] == "sfx" else rel[0]
                out.append((p, cats.get(sid, "ability"), sid))
    return sorted(out)


def selftest():
    sr = 48000
    t = np.arange(sr * 2) / sr
    s = 0.1 * np.sin(2 * np.pi * 1000 * t)                  # -20 dBFS peak sine: -23.0 LUFS mono (BS.1770 reference)
    L = meter.loudness(s, sr)
    assert abs(L["I"] - (-23.01)) < 0.1, L
    stereo = np.stack([s, s], 1)                             # the same in both channels sums +3 dB
    assert abs(meter.loudness(stereo, sr)["I"] - (-20.0)) < 0.1
    # an inter-sample over: a sine at fs/4 with 45 deg phase peaks between samples
    x = np.sin(2 * np.pi * 12000 * t[:4800] + np.pi / 4) * 0.99
    assert meter.true_peak(x) > meter.sample_peak(x) + 2.5, (meter.true_peak(x), meter.sample_peak(x))
    # band-limited noise reads dull, full-band noise doesn't
    from scipy.signal import butter, sosfilt
    n = np.random.default_rng(1).standard_normal(sr) * 0.1
    assert meter.bandwidth(n, sr)["top"] > 20000
    from scipy.signal import resample_poly
    assert meter.bandwidth(resample_poly(resample_poly(n, 1, 2), 2, 1), sr)["top"] < 14000      # a 24 kHz render, upsampled
    assert meter.bandwidth(sosfilt(butter(2, 3000, fs=sr, output="sos"), n), sr)["top"] > 20000   # dark but no cliff
    # a click in a sine is found
    c = 0.3 * np.sin(2 * np.pi * 220 * t[:sr // 2]); c[12000] += 0.6
    assert meter.defects(c, sr)["clicks"] >= 1
    print("selftest OK")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=BANK)
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--name", default="audit", help="report name: out/<name>.json + .md (audit_after for the Sound Lab's after column)")
    ap.add_argument("--workers", type=int, default=max(1, (os.cpu_count() or 4) // 2))
    args = ap.parse_args()
    if args.selftest: return selftest()
    os.makedirs(OUT, exist_ok=True)
    js = jobs(args.root)
    with ProcessPoolExecutor(args.workers) as ex:
        rows = list(ex.map(measure, js, chunksize=8))
    rows = [r for r in rows if "error" not in r] + [r for r in rows if "error" in r]
    json.dump(rows, open(os.path.join(OUT, args.name + ".json"), "w"), indent=1)
    ok = [r for r in rows if "error" not in r]
    n = len(ok)
    fails = [r for r in ok if r["fail"]]
    by = {}
    for r in ok:
        for k in r["fail"] + [w.split("<")[0].rstrip("+-0123456789") for w in r["warn"]]:
            by[k] = by.get(k, 0) + 1
    sr = {}
    for r in ok: sr[r["sr"]] = sr.get(r["sr"], 0) + 1
    print(f"{n} clips, {len(rows) - n} unreadable; sample rates {sr}")
    print("issues:", json.dumps(dict(sorted(by.items(), key=lambda x: -x[1]))))
    cats = {}
    for r in ok: cats.setdefault(r["cat"], []).append(r)
    lines = ["# Sound bank audit", "", f"{n} clips. Issue counts: `{json.dumps(by)}`", "",
             "| category | clips | M LUFS median (target) | true peak max | bandwidth median | dull <15k | clicks>2 |", "|---|---|---|---|---|---|---|"]
    for c, rs in sorted(cats.items()):
        M = np.median([r["M"] for r in rs]); tp = max(r["tp"] for r in rs); top = np.median([r["top"] for r in rs])
        dull = sum(r["top"] < 15000 for r in rs); ck = sum(r["clicks"] > 2 for r in rs)
        lines.append(f"| {c} | {len(rs)} | {M:.1f} ({TARGET_M.get(c, '-')}) | {tp:+.1f} | {top / 1000:.1f} kHz | {dull} | {ck} |")
        print(f"  {c:9s} {len(rs):4d} clips  M med {M:6.1f} (target {TARGET_M.get(c, '-')})  tp max {tp:+5.1f}  bw med {top / 1000:4.1f}k  dull {dull:3d}  clicks {ck}")
    lines += ["", "## Failing clips", "", "| clip | cat | fails | warns | M | tp | top |", "|---|---|---|---|---|---|---|"]
    for r in sorted(fails, key=lambda r: (-len(r["fail"]), r["path"]))[:200]:
        lines.append(f"| {r['path']} | {r['cat']} | {', '.join(r['fail'])} | {', '.join(r['warn'])} | {r['M']} | {r['tp']} | {r['top']} |")
    open(os.path.join(OUT, args.name + ".md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print(f"failing clips: {len(fails)} -> {os.path.join(OUT, args.name + '.md')}")


if __name__ == "__main__":
    main()
