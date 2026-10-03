"""Measurements shared by the audio tools (audit, master, render checks): ITU-R BS.1770-4 loudness (K-weighting,
momentary / short-term / gated integrated), 4x-oversampled true peak, effective bandwidth (where the spectrum falls off a
cliff - a 24 kHz or 16 kHz model render has nothing above 12 / 8 kHz and sounds dull next to a 48 kHz recording), and
the defect checks from the web game's assetgen/audio_qa.py (clipping, saturation, clicks, edge steps, dropouts, DC).

Everything works on float arrays shaped (samples,) or (samples, channels) at any rate."""
import numpy as np
from scipy.signal import bilinear, lfilter, resample_poly, welch

from lpc import find_clicks


def mono(a):
    return a if a.ndim == 1 else a.mean(axis=1)


def chans(a):
    return a[:, None] if a.ndim == 1 else a


# ------------------------------------------------------------------------------------------------ loudness (BS.1770-4)
def _kweight(sr):
    """the two K-weighting biquads at sr (pre-filter shelf + RLB high-pass), from the analogue prototypes"""
    # stage 1: high shelf +4 dB around 1.5 kHz (the published 48 kHz coefficients, re-derived for any rate)
    f0, G, Q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    K = np.tan(np.pi * f0 / sr)
    Vh = 10 ** (G / 20); Vb = Vh ** 0.4996667741545416
    a0 = 1 + K / Q + K * K
    b1 = [(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0]
    a1 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    # stage 2: RLB high-pass at 38 Hz
    f0, Q = 38.13547087602444, 0.5003270373238773
    K = np.tan(np.pi * f0 / sr)
    a0 = 1 + K / Q + K * K
    b2 = [1, -2, 1]
    a2 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    return (b1, a1), (b2, a2)


def kpower(a, sr, win, hop):
    """mean-square K-weighted power per window (summed over channels, all weights 1 for L/R/mono)"""
    (b1, a1), (b2, a2) = _kweight(sr)
    x = chans(a)
    y = lfilter(b2, a2, lfilter(b1, a1, x, axis=0), axis=0)
    n = int(win * sr); h = int(hop * sr)
    if len(y) < n:                                            # shorter than one window: pad with silence (as a meter would)
        y = np.vstack([y, np.zeros((n - len(y), y.shape[1]))])
    out = []
    for s in range(0, len(y) - n + 1, h):
        out.append(float((y[s:s + n] ** 2).mean(axis=0).sum()))
    return np.array(out)


def lufs(p):
    return -0.691 + 10 * np.log10(np.maximum(p, 1e-12))


def loudness(a, sr):
    """{'I': gated integrated LUFS, 'M': max momentary (400 ms), 'S': max short-term (3 s)} - one-shots are judged by M"""
    pm = kpower(a, sr, 0.4, 0.1)
    ps = kpower(a, sr, 3.0, 0.1) if len(a) > sr * 3 else pm
    abs_g = pm[lufs(pm) > -70]
    if len(abs_g):
        rel = lufs(abs_g.mean()) - 10
        g = abs_g[lufs(abs_g) > rel]
        I = float(lufs(g.mean())) if len(g) else -70.0
    else:
        I = -70.0
    return {"I": round(I, 2), "M": round(float(lufs(pm).max()), 2), "S": round(float(lufs(ps).max()), 2)}


# ------------------------------------------------------------------------------------------------ peaks
def true_peak(a):
    """4x-oversampled peak in dBTP (inter-sample overs crackle after the engine resamples or sums them)"""
    x = chans(a)
    up = resample_poly(x, 4, 1, axis=0)
    return float(20 * np.log10(max(1e-9, np.abs(up).max())))


def sample_peak(a):
    return float(20 * np.log10(max(1e-9, np.abs(a).max())))


# ------------------------------------------------------------------------------------------------ spectrum
def bandwidth(a, sr):
    """effective top of the spectrum (Hz): the highest 1/6-octave band within 60 dB of the loudest band of the active
    signal, and the 99.5% energy roll-off. A render that was band-limited shows a cliff (top far below sr/2)."""
    m = mono(a)
    if len(m) < 2048: m = np.pad(m, (0, 2048 - len(m)))
    f, p = welch(m, sr, nperseg=2048)
    p = np.maximum(p, 1e-20)
    c = np.cumsum(p); roll = float(f[np.searchsorted(c, 0.995 * c[-1])])
    edges = 40 * 2 ** (np.arange(0, 60) / 6)
    edges = edges[edges < sr / 2]
    lv = []
    for lo, hi in zip(edges[:-1], edges[1:]):
        sel = (f >= lo) & (f < hi)
        lv.append(10 * np.log10(p[sel].mean()) if sel.any() else -200)
    lv = np.array(lv); lo = edges[:-1]
    # a cliff: above 3 kHz, the level 1/3 octave below a band edge is > 30 dB over everything from 1/3 octave above it on
    # (natural sounds roll off gradually; a band-limited render or a low sample rate drops like a wall)
    top = float(sr / 2)
    for i in range(2, len(lv) - 2):
        if lo[i] < 3000: continue
        if lv[i - 2] - lv[i + 2:].max() > 30:
            top = float(lo[i]); break
    return {"top": round(top), "roll995": round(roll)}


def hf_share(a, sr, cut=9000):
    """energy above `cut` vs the whole (fizz / hiss when high)"""
    m = mono(a)
    if len(m) < 2048: return 0.0
    f, p = welch(m, sr, nperseg=2048)
    return float(p[f >= cut].sum() / max(1e-20, p.sum()))


# ------------------------------------------------------------------------------------------------ defects (audio_qa.py)
def defects(a, sr, loop=False):
    m = mono(a)
    peak = float(np.abs(a).max()) if len(a) else 0.0
    out = {
        "clip": int((np.abs(a) >= 0.9995).sum()),
        "saturate": float((np.abs(m) >= peak * 0.891).mean()) if peak > 0 else 0.0,       # share of samples in the top 1 dB
        "dc": float(abs(m.mean())) if len(m) else 0.0,
        "edges": float(max(abs(m[0]), abs(m[-1]))) if len(m) else 0.0,
    }
    clicks = find_clicks(m.astype(np.float64), sr)
    out["clicks"] = int(len(clicks))
    # dropouts: runs of exact digital zero inside the sound (> 4 ms), excluding the leading / trailing silence
    nz = np.flatnonzero(m != 0)
    drop = 0
    if len(nz) > 1:
        inner = m[nz[0]:nz[-1] + 1] == 0
        run = 0
        for z in inner:
            run = run + 1 if z else 0
            if run == int(sr * 0.004): drop += 1
    out["dropouts"] = drop
    if loop and len(m) > 16:
        steps = np.abs(np.diff(m))
        out["seam"] = float(abs(m[0] - m[-1]) / max(1e-6, np.percentile(steps, 99.5)))
    return out
