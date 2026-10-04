"""Synthesised layers a sound designer stacks on a recorded / generated body (tools/audio/design/sounds.py "layers"):
the parts a generator smears or leaves out - a transient that cuts through a mix, real sub weight, mechanical detail, a
material's ring and the debris that falls after an impact. All mono, 48 kHz, peak-normalised to 1 (compose.py sets each
layer's level against the body). Only sines and filtered noise: no square or saw edges (they read as clicks and fizz).
"""
import numpy as np
from scipy.signal import butter, sosfilt

SR = 48000


def _t(ms):
    return np.arange(int(SR * ms / 1000)) / SR


def _norm(x):
    p = np.abs(x).max()
    return x / p if p > 0 else x


def _bp(x, lo, hi, order=2):
    hi = min(hi, SR / 2 * 0.95)
    return sosfilt(butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x)


def _hp(x, f, order=4):
    return sosfilt(butter(order, f, btype="high", fs=SR, output="sos"), x)


def crack(rng, ms=6, hp=2500, **_):
    """the transient: a burst of high-passed noise with a fast exponential decay - what makes a shot or a hit cut through"""
    t = _t(max(ms * 6, 20))
    x = rng.standard_normal(len(t)) * np.exp(-t / (ms / 1000))
    x[: int(SR * 0.0003)] *= np.linspace(0, 1, int(SR * 0.0003))     # 0.3 ms rise: sharp, not a digital step
    return _norm(_hp(x, hp))


def sub(rng, f0=90, f1=45, ms=140, **_):
    """weight: a sine gliding down from f0 to f1, decaying over ms (felt more than heard; the punch of a heavy shot)"""
    t = _t(ms * 4)
    f = f1 + (f0 - f1) * np.exp(-t / (ms / 1000 * 0.35))
    ph = 2 * np.pi * np.cumsum(f) / SR
    env = np.exp(-t / (ms / 1000)) * np.minimum(1, t / 0.0015)
    return _norm(np.sin(ph) * env)


def click(rng, n=2, gap=14, f=3200, **_):
    """mechanical detail: n tiny resonant knocks (a bolt, a latch) gap ms apart"""
    t = _t(gap * n + 30)
    x = np.zeros(len(t))
    for i in range(n):
        s = int(SR * (i * gap + rng.uniform(-2, 2)) / 1000)
        s = max(0, s)
        tt = np.arange(len(t) - s) / SR
        ff = f * rng.uniform(0.85, 1.15)
        x[s:] += np.sin(2 * np.pi * ff * tt) * np.exp(-tt / 0.004) * rng.uniform(0.6, 1)
    return _norm(x)


def ring(rng, f=(2210, 3470, 5180, 7090), decay=0.35, **_):
    """a struck metal's modes: inharmonic partials, the higher ones dying faster"""
    t = _t(decay * 1000 * 3)
    x = np.zeros(len(t))
    for i, fi in enumerate(f):
        fi *= rng.uniform(0.97, 1.03)
        d = decay / (1 + i * 0.6)
        x += np.sin(2 * np.pi * fi * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / d) / (1 + i * 0.4)
    x *= np.minimum(1, t / 0.0005)
    return _norm(x)


GRAINS = {   # material: (band lo, band hi, grain ms, ping Hz range or None)
    "stone": (1800, 7000, (2, 7), None),
    "glass": (4000, 14000, (1, 4), (3500, 9000)),
    "wood": (700, 2800, (3, 9), None),
    "tile": (1500, 6000, (2, 6), (1800, 4200)),
    "dirt": (250, 1600, (4, 12), None),
    "metal": (2000, 8000, (2, 5), (2500, 7000)),
}


def debris(rng, ms=400, grain="stone", n=18, **_):
    """what falls after the hit: n little impacts scattered over ms (dense at first, thinning out), quieter as they go"""
    lo, hi, (gmin, gmax), ping = GRAINS.get(grain, GRAINS["stone"])
    t = _t(ms + 60)
    x = np.zeros(len(t))
    for _ in range(n):
        at = rng.exponential(ms / 3.5)
        if at > ms: continue
        s = int(SR * at / 1000)
        gl = int(SR * rng.uniform(gmin, gmax) / 1000)
        if s + gl >= len(x): continue
        g = rng.standard_normal(gl) * np.exp(-np.arange(gl) / (gl / 3))
        g = _bp(g, lo * rng.uniform(0.8, 1.2), hi * rng.uniform(0.8, 1.1))
        if ping is not None and rng.random() < 0.5:
            pf = rng.uniform(*ping); tt = np.arange(gl * 3) / SR
            pg = np.sin(2 * np.pi * pf * tt) * np.exp(-tt / 0.012) * 0.5
            e = min(len(x), s + len(pg)); x[s:e] += pg[: e - s]
        x[s:s + gl] += g * (1 - at / ms) ** 1.5 * rng.uniform(0.3, 1)
    return _norm(x)


def whoosh(rng, ms=200, f0=1500, f1=6000, **_):
    """air moved by a blade or a body: noise through a band-pass that sweeps f0 -> f1, swelling and fading"""
    from scipy.signal import stft, istft
    t = _t(ms)
    n = rng.standard_normal(len(t) + 1024)
    # a moving band in the STFT domain (overlap-added: smooth, no block edges - a block-wise filter would click)
    f, fr, Z = stft(n, SR, nperseg=1024, noverlap=768)
    k = np.clip(fr / (ms / 1000), 0, 1)
    fc = f0 * (f1 / f0) ** k
    mask = np.exp(-0.5 * (np.log2(np.maximum(f[:, None], 1) / fc[None, :]) / 0.6) ** 2)
    _, out = istft(Z * mask, SR, nperseg=1024, noverlap=768)
    out = out[512:512 + len(t)]
    env = np.sin(np.pi * np.clip(t / (ms / 1000), 0, 1)) ** 2
    return _norm(out * env)


def shimmer(rng, ms=400, **_):
    """magic's sparkle: short high sine glints scattered over ms, thinning out"""
    t = _t(ms + 100)
    x = np.zeros(len(t))
    for _ in range(int(ms / 12)):
        at = rng.uniform(0, ms) * rng.uniform(0.3, 1)
        s = int(SR * at / 1000)
        dur = rng.uniform(0.02, 0.08); tt = np.arange(int(SR * dur)) / SR
        g = np.sin(2 * np.pi * rng.uniform(5000, 13000) * tt) * np.sin(np.pi * tt / dur) ** 2
        e = min(len(x), s + len(g)); x[s:e] += g[: e - s] * (1 - at / ms) * rng.uniform(0.3, 1)
    return _norm(x)


KINDS = {"crack": crack, "sub": sub, "click": click, "ring": ring, "debris": debris, "whoosh": whoosh, "shimmer": shimmer}


def render(layer, seed=0):
    rng = np.random.default_rng(seed)
    return KINDS[layer["k"]](rng, **{k: v for k, v in layer.items() if k not in ("k", "db", "at")})
