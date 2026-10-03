#!/usr/bin/env python3
"""The mastering chain for the Unity sound bank: every clip leaves here at 48 kHz, full-band, at its category's loudness,
under -1 dBTP, with clean edges - the standard in docs/audio/AUDIO_STANDARD.md.

    python tools/audio/master.py voices --sr tools/audio/out/kaggle/audiosr/sr      # remaster all voice lines
    python tools/audio/master.py voices --only kaien --dry                          # report only
    python tools/audio/master.py file <in> <out> --cat weapon [--sr <48k.wav>]       # one clip

Per clip:
  1. decode, resample to 48 kHz (polyphase, Kaiser)
  2. bandwidth: when an AudioSR render of the clip exists, a linear-phase crossover keeps the ORIGINAL below `xover` (the
     sound exactly as designed / cast) and takes only the regenerated air above it, level-matched in the band just under
     the crossover
  3. DC + high-pass (rumble below the category's floor eats headroom for nothing)
  4. voice: de-esser (sibilance 4.5-10 kHz ducked when it pokes out over the voice) + gentle 2.5:1 compression
  5. loudness to the category target (max momentary LUFS, tools/audio/audit.py TARGET_M)
  6. true-peak limit at -1 dBTP (look-ahead, the same construction as the game's MasterChain, vectorised)
  7. edges: leading silence trimmed to a 2 ms pre-roll, the tail cut once it's 70 dB down, 0.5 ms fade in / 8 ms out
"""
import argparse, glob, json, os, subprocess, sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
import soundfile as sf
from scipy.ndimage import minimum_filter1d, uniform_filter1d
from scipy.signal import butter, firwin, oaconvolve, resample_poly, sosfilt, sosfiltfilt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import meter  # noqa: E402
from audit import TARGET_M  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
BANK = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio")
SR = 48000
HPF = {"voice": 70, "weapon": 30, "impact": 28, "ability": 30, "move": 40, "step": 40, "feedback": 60, "loop": 30, "amb": 25}


# ------------------------------------------------------------------------------------------------ building blocks
def load48(path):
    a, sr = sf.read(path, dtype="float64", always_2d=True)
    if sr != SR:
        from math import gcd
        g = gcd(sr, SR)
        a = resample_poly(a, SR // g, sr // g, axis=0, window=("kaiser", 10.0))
    return a


def lin_lowpass(a, fc, taps=1023):
    """zero-phase (linear-phase FIR, centred) low-pass, so its complement (a - lowpass) is an exact high-pass"""
    h = firwin(taps, fc, fs=SR, window=("kaiser", 9.0))
    return np.stack([oaconvolve(a[:, c], h, mode="same") for c in range(a.shape[1])], 1)


def align(ref, x, max_lag=480):
    """shift x to line up with ref (cross-correlation of their low bands)"""
    n = min(len(ref), len(x))
    r = ref[:n].mean(1); y = x[:n].mean(1)
    sos = butter(4, 4000, fs=SR, output="sos")
    r = sosfilt(sos, r); y = sosfilt(sos, y)
    c = np.correlate(np.pad(y, max_lag), r, mode="valid")          # lags -max..+max
    lag = int(np.argmax(c)) - max_lag
    if lag > 0: x = np.vstack([x[lag:], np.zeros((lag, x.shape[1]))])
    elif lag < 0: x = np.vstack([np.zeros((-lag, x.shape[1])), x[:lag]])
    return x, lag


def band_rms(a, lo, hi):
    sos = butter(6, [lo, hi], btype="band", fs=SR, output="sos")
    return float(np.sqrt(np.mean(sosfilt(sos, a.mean(1)) ** 2)) + 1e-12)


def merge_sr(orig, sr, xover):
    """original below xover + the super-resolved render above it, the render's top level-matched to the original's band
    just under the crossover (where both have real content)"""
    n = len(orig)
    sr = sr[:n] if len(sr) >= n else np.vstack([sr, np.zeros((n - len(sr), sr.shape[1]))])
    if sr.shape[1] != orig.shape[1]: sr = np.repeat(sr.mean(1, keepdims=True), orig.shape[1], 1)
    sr, lag = align(orig, sr)
    k = band_rms(orig, xover * 0.72, xover * 0.95) / band_rms(sr, xover * 0.72, xover * 0.95)
    k = float(np.clip(k, 0.5, 2.0))
    low = lin_lowpass(orig, xover)
    high = sr * k - lin_lowpass(sr * k, xover)
    return low + high, {"lag": lag, "hf_gain_db": round(20 * np.log10(k), 2)}


def highpass(a, fc):
    sos = butter(2, fc, btype="high", fs=SR, output="sos")
    a = a - a.mean(0, keepdims=True)
    return sosfiltfilt(sos, a, axis=0)


def env(x, att, rel):
    """one-pole attack / release envelope of |x| (vectorised enough: a short python loop over 1 ms blocks)"""
    blk = 48
    m = np.abs(x)
    nb = (len(m) + blk - 1) // blk
    peaks = np.array([m[i * blk:(i + 1) * blk].max() for i in range(nb)])
    aa = np.exp(-blk / (att * SR)); ar = np.exp(-blk / (rel * SR))
    e = np.zeros(nb); v = 0.0
    for i, p in enumerate(peaks):
        v = p + (v - p) * (aa if p > v else ar)
        e[i] = v
    return np.repeat(e, blk)[:len(m)]


def deess(a, thresh_db=-6.0, max_cut_db=8.0):
    """duck the 4.5-10 kHz band where it pokes out over the voice's body"""
    mono = a.mean(1)
    sos = butter(4, [4500, 10000], btype="band", fs=SR, output="sos")
    s = np.stack([sosfiltfilt(sos, a[:, c]) for c in range(a.shape[1])], 1)
    es = env(s.mean(1), 0.002, 0.06); ev = env(mono, 0.005, 0.08)
    over = 20 * np.log10((es + 1e-9) / (ev + 1e-9)) - thresh_db
    cut = np.clip(over, 0, max_cut_db)
    g = 10 ** (-cut / 20)
    g = uniform_filter1d(g, 96)
    return a - s * (1 - g)[:, None], float(cut.max())


def compress(a, ratio=2.5, knee_db=6.0, att=0.005, rel=0.08):
    """gentle compression around the clip's own loud level (its 80th-percentile envelope)"""
    e = env(a.max(1) if a.shape[1] > 1 else a[:, 0], att, rel)
    ed = 20 * np.log10(e + 1e-9)
    active = ed[ed > ed.max() - 30]
    if not len(active): return a, 0.0
    thr = float(np.percentile(active, 80)) - 4
    o = ed - thr
    red = np.where(2 * o < -knee_db, 0, np.where(np.abs(2 * o) <= knee_db, (1 - 1 / ratio) * (o + knee_db / 2) ** 2 / (2 * knee_db), (1 - 1 / ratio) * o))
    g = 10 ** (-red / 20)
    return a * g[:, None], float(red.max())


def tp_limit(a, ceiling_db=-1.0, look=96, hold=480):
    """look-ahead true-peak limiter: required gain from the 4x oversampled peak, running min over hold, running mean over
    look (the gain is down before the peak; no step anywhere), applied to the signal delayed to line up"""
    c = 10 ** (ceiling_db / 20) * 0.977
    up = np.abs(resample_poly(a, 4, 1, axis=0)).max(1)
    pk = up.reshape(-1, 4).max(1)[:len(a)] if len(up) >= 4 * len(a) else np.pad(up, (0, 4 * len(a) - len(up))).reshape(-1, 4).max(1)
    req = np.minimum(1.0, c / np.maximum(pk, 1e-9))
    if req.min() >= 1.0: return a, 0.0
    # centre the windows on each sample: the running min reaches back and forward, the mean smooths it symmetrically
    m = minimum_filter1d(req, size=hold + look, mode="nearest")
    g = uniform_filter1d(m, size=look, mode="nearest")
    g = np.minimum(g, req)     # never above what a sample needs (the symmetric windows make this a no-op almost always)
    return a * g[:, None], float(-20 * np.log10(g.min()))


def edges(a, pre_ms=2, tail_db=-70, fin_ms=0.5, fout_ms=8):
    mono = np.abs(a).max(1)
    pk = mono.max() if len(mono) else 0
    if pk <= 0: return a
    on = np.flatnonzero(mono > pk * 10 ** (-50 / 20))
    start = max(0, on[0] - int(SR * pre_ms / 1000)) if len(on) else 0
    alive = np.flatnonzero(mono > pk * 10 ** (tail_db / 20))
    end = min(len(a), alive[-1] + int(SR * 0.02)) if len(alive) else len(a)
    a = a[start:end].copy()
    fi = max(1, int(SR * fin_ms / 1000)); fo = max(1, min(len(a) // 4, int(SR * fout_ms / 1000)))
    a[:fi] *= np.linspace(0, 1, fi)[:, None]
    a[-fo:] *= (np.cos(np.linspace(0, np.pi, fo)) * 0.5 + 0.5)[:, None]
    return a


def level_to(a, target_m):
    L = meter.loudness(a, SR)["M"]
    return a * 10 ** ((target_m - L) / 20), round(target_m - L, 2)


# ------------------------------------------------------------------------------------------------ one clip
def master(src, dst, cat, sr_path=None, xover=10500, fmt="wav"):
    rep = {"src": os.path.relpath(src, ROOT).replace("\\", "/"), "cat": cat}
    a = load48(src)
    before = {"M": meter.loudness(a, SR)["M"], "tp": round(meter.true_peak(a), 2), "top": meter.bandwidth(a, SR)["top"]}
    if sr_path and os.path.exists(sr_path):
        s = load48(sr_path)
        a, info = merge_sr(a, s, xover); rep.update(info)
    a = highpass(a, HPF.get(cat, 30))
    if cat == "voice":
        a, rep["deess_db"] = deess(a)
        a, rep["comp_db"] = compress(a)
    a = edges(a, fout_ms=8 if cat != "voice" else 12)
    a, rep["gain_db"] = level_to(a, TARGET_M.get(cat, -16))
    a, rep["limit_db"] = tp_limit(a)
    a = edges(a, pre_ms=2, tail_db=-80)       # tidy again after the gain (fades only; the trim is a no-op now)
    after = {"M": meter.loudness(a, SR)["M"], "tp": round(meter.true_peak(a), 2), "top": meter.bandwidth(a, SR)["top"]}
    rep["before"], rep["after"] = before, after
    if dst:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if fmt == "ogg":
            tmp = dst[:-4] + ".tmp.wav"
            sf.write(tmp, a.astype(np.float32), SR, subtype="FLOAT")
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", tmp, "-c:a", "libvorbis", "-q:a", "8", dst], check=True)
            os.remove(tmp)
        else:
            # 16-bit with TPDF dither
            d = (np.random.default_rng(0).random(a.shape) - np.random.default_rng(1).random(a.shape)) / 32768
            sf.write(dst, np.clip(a + d, -1, 1 - 1 / 32768), SR, subtype="PCM_16")
    return rep


def _job(j):
    try: return master(**j)
    except Exception as e: return {"src": j["src"], "error": repr(e)}


def voices(args):
    out = os.path.join(HERE, "out", "master", "vo")
    jobs = []
    for p in sorted(glob.glob(os.path.join(BANK, "vo", "**", "*.ogg"), recursive=True)):
        rel = os.path.relpath(p, os.path.join(BANK, "vo")).replace("\\", "/")
        if args.only and rel.split("/")[0] not in args.only.split(","): continue
        srp = os.path.join(args.sr, os.path.splitext(rel)[0] + ".wav") if args.sr else None
        jobs.append({"src": p, "dst": None if args.dry else os.path.join(out, rel), "cat": "voice", "sr_path": srp, "fmt": "ogg"})
    with ProcessPoolExecutor(args.workers) as ex:
        reps = list(ex.map(_job, jobs, chunksize=4))
    json.dump(reps, open(os.path.join(HERE, "out", "master_voices.json"), "w"), indent=1)
    ok = [r for r in reps if "error" not in r]
    err = [r for r in reps if "error" in r]
    print(f"{len(ok)} voice lines mastered, {len(err)} errors -> {out}")
    if ok:
        def med(k, s): return float(np.median([r[s][k] for r in ok]))
        print(f"  M  {med('M', 'before'):.1f} -> {med('M', 'after'):.1f} LUFS   tp max {max(r['after']['tp'] for r in ok):+.2f} dBTP   "
              f"top {med('top', 'before') / 1000:.1f} -> {med('top', 'after') / 1000:.1f} kHz   with SR: {sum('lag' in r for r in ok)}")
    for r in err[:5]: print("  error", r)


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    v = sub.add_parser("voices"); v.add_argument("--sr"); v.add_argument("--only"); v.add_argument("--dry", action="store_true")
    v.add_argument("--workers", type=int, default=max(1, (os.cpu_count() or 4) // 2))
    f = sub.add_parser("file"); f.add_argument("src"); f.add_argument("dst"); f.add_argument("--cat", default="ability"); f.add_argument("--sr")
    f.add_argument("--xover", type=float, default=10500)
    a = ap.parse_args()
    if a.cmd == "voices": voices(a)
    else: print(json.dumps(master(a.src, a.dst, a.cat, a.sr, a.xover, "ogg" if a.dst.endswith(".ogg") else "wav"), indent=1))


if __name__ == "__main__":
    main()
