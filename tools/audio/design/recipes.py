#!/usr/bin/env python3
"""The game's synthesised cues - the hit-marker tick, UI clicks, the capture / victory / ult-ready stings, counter,
denied, interrupt, zone break, rebirth - rendered offline from the web game's own recipes (src/audio/Sfx.ts `R`, desktop
recipes: sine and triangle only) so the Unity bank plays the same sounds. Unity never had a synth: these ids were silent.

    python tools/audio/design/recipes.py            # render, master, install (Assets/ZU/Resources/ZUAudio/sfx/<id>/0.wav)
    python tools/audio/design/recipes.py --dry      # render to tools/audio/out/design only

A layer (TS `Layer`): w sine|triangle, f -> f1 an exponential glide over d seconds, v level reached after attack a
(exponential from silence, default 5 ms), then an exponential fall to -80 dB at d; n = white noise instead; lp / hp / bp
(with q) a biquad on it; dl = start delay. Rendered at 48 kHz, oscillators band-limited (a triangle is its odd harmonics
up to 20 kHz), so nothing aliases."""
import argparse, os, sys

import numpy as np
from scipy.signal import butter, sosfilt, iirpeak

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.dirname(HERE))
import compose  # noqa: E402

SR = 48000

# (category, layers) - the TS recipes verbatim; grindstart has none in the TS (silent there too): a short skate-on-rail clack
R = {
    "hit": ("feedback", [dict(w="triangle", f=700, f1=500, d=0.05, v=0.12)]),
    "interrupt": ("impact", [dict(w="triangle", f=300, f1=150, d=0.2, v=0.2), dict(n=True, d=0.15, v=0.2, bp=2000, q=2)]),
    "denied": ("feedback", [dict(w="triangle", f=200, d=0.08, v=0.15), dict(w="triangle", f=150, d=0.1, v=0.15, dl=0.1)]),
    "zonebreak": ("impact", [dict(n=True, d=0.4, v=0.35, hp=2500), dict(w="triangle", f=1600, f1=200, d=0.35, v=0.18)]),
    "rebirthcast": ("ability", [dict(w="sine", f=392, f1=523, d=1.8, v=0.16, a=0.15), dict(w="sine", f=494, f1=659, d=1.8, v=0.14, a=0.2),
                                dict(w="sine", f=587, f1=784, d=2, v=0.12, a=0.25), dict(w="triangle", f=1046, d=1.2, v=0.06, a=0.5)]),
    "rebirth": ("ability", [dict(w="sine", f=1318, d=0.8, v=0.12, a=0.02), dict(w="sine", f=1568, d=0.9, v=0.1, a=0.05),
                            dict(w="triangle", f=2093, d=0.7, v=0.06, a=0.08)]),
    "capture": ("feedback", [dict(w="triangle", f=659, d=0.2, v=0.2), dict(w="triangle", f=880, d=0.2, v=0.2, dl=0.15),
                             dict(w="triangle", f=1046, d=0.4, v=0.2, dl=0.3)]),
    "victory": ("feedback", [dict(w="triangle", f=523, d=0.3, v=0.12, lp=3000), dict(w="triangle", f=659, d=0.3, v=0.12, lp=3000, dl=0.25),
                             dict(w="triangle", f=784, d=0.3, v=0.12, lp=3000, dl=0.5), dict(w="triangle", f=1046, d=1.2, v=0.14, lp=3000, dl=0.75)]),
    "counter": ("feedback", [dict(w="triangle", f=880, d=0.08, v=0.12), dict(w="triangle", f=1320, d=0.15, v=0.12, dl=0.08)]),
    "ult_ready": ("feedback", [dict(w="sine", f=660, d=0.15, v=0.15), dict(w="sine", f=990, d=0.25, v=0.15, dl=0.12)]),
    "ui_click": ("feedback", [dict(w="triangle", f=900, f1=1200, d=0.05, v=0.1)]),
    "ui_buy": ("feedback", [dict(w="sine", f=1318, d=0.08, v=0.12), dict(w="sine", f=1760, d=0.18, v=0.12, dl=0.07)]),
    "ui_hover": ("feedback", [dict(w="sine", f=1500, d=0.03, v=0.04)]),
    "grindstart": ("move", [dict(n=True, d=0.08, v=0.3, bp=3200, q=3), dict(w="sine", f=2400, f1=1900, d=0.25, v=0.08, dl=0.01),
                            dict(w="sine", f=3700, f1=3300, d=0.18, v=0.05, dl=0.01)]),
}
# how loud each lands relative to its category target: UI and the hit tick sit well under the world (AudioKit adds the UI
# and hit-marker volume settings on top), stings at the target
TRIM_DB = {"ui_hover": -12, "ui_click": -6, "ui_buy": -4, "hit": -3}


def osc(w, f, f1, n):
    t = np.arange(n) / SR
    if f1 and f1 != f:
        fr = f * (f1 / f) ** (t / (n / SR))                       # exponential glide (WebAudio exponentialRamp)
    else:
        fr = np.full(n, float(f))
    ph = 2 * np.pi * np.cumsum(fr) / SR
    if w == "sine": return np.sin(ph)
    # triangle: odd harmonics, alternating sign, 1/k^2, up to 20 kHz at the highest frequency of the glide
    out = np.zeros(n); kmax = int(20000 / max(fr.max(), 1))
    for k in range(1, kmax + 1, 2):
        out += ((-1) ** ((k - 1) // 2)) * np.sin(k * ph) / (k * k)
    return out * 8 / np.pi ** 2


def env(n, v, a):
    t = np.arange(n) / SR
    na = max(1, int(a * SR))
    e = np.empty(n)
    e[:na] = 0.0001 * (max(v, 0.0002) / 0.0001) ** (np.arange(na) / na)                  # 0.0001 -> v (exponential)
    rest = n - na
    if rest > 0: e[na:] = v * (0.0001 / max(v, 0.0002)) ** (np.arange(rest) / rest)      # v -> 0.0001 (exponential)
    return e


def layer(L, rng):
    n = int(L["d"] * SR)
    x = rng.standard_normal(n) if L.get("n") else osc(L.get("w", "sine"), L.get("f", 440), L.get("f1"), n)
    if "lp" in L: x = sosfilt(butter(2, L["lp"], fs=SR, output="sos"), x)
    if "hp" in L: x = sosfilt(butter(2, L["hp"], btype="high", fs=SR, output="sos"), x)
    if "bp" in L:
        b, a = iirpeak(L["bp"], L.get("q", 1), fs=SR)
        from scipy.signal import lfilter
        x = lfilter(b, a, x)
    return x * env(n, L.get("v", 0.2), L.get("a", 0.005))


def render(layers, seed=1):
    rng = np.random.default_rng(seed)
    end = max(L.get("dl", 0) + L["d"] for L in layers)
    out = np.zeros(int(end * SR) + int(SR * 0.02))
    for L in layers:
        x = layer(L, rng); s = int(L.get("dl", 0) * SR)
        out[s:s + len(x)] += x
    return out[:, None]


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--dry", action="store_true"); a = ap.parse_args()
    import soundfile as sf
    os.makedirs(compose.OUT, exist_ok=True)
    for sid, (cat, layers) in R.items():
        x = render(layers)
        spec = {"id": sid, "cat": cat, "secs": len(x) / SR, "loop": False, "layers": [], "vars": 1}
        y, red = compose.finish(x, spec)
        y = y * 10 ** (TRIM_DB.get(sid, 0) / 20)
        sf.write(os.path.join(compose.OUT, f"{sid}_0.wav"), y.astype(np.float32), SR, subtype="FLOAT")
        if not a.dry: compose.install(sid, [y], spec)
        print(f"{sid:12s} {cat:9s} {len(y) / SR:5.2f} s  rms {compose.active_rms_db(y):6.1f}  tp {compose.meter.true_peak(y):+.2f} dBTP  limit {red:.1f} dB")


if __name__ == "__main__":
    main()
