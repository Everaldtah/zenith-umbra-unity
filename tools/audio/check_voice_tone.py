#!/usr/bin/env python3
"""Tone check of the voice remaster against the originals: per line, the level change (after loudness matching) in the
bands a remaster can damage - presence 2-4.5 kHz, sibilance 5-9 kHz, and the original's top 9-11 kHz - plus how many lines
hit the AudioSR top-band level clamp. A de-esser that lisps shows as a big sibilance drop; a crossover that over-brightens
as a big 9-11 kHz rise.

    python tools/audio/check_voice_tone.py"""
import glob, json, os, sys

import numpy as np
from scipy.signal import welch

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import master  # noqa: E402

SR = 48000
ORIG = os.path.join(HERE, "out", "kaggle_in", "vo")
NEW = os.path.join(HERE, "out", "master", "vo")


def bands(a):
    f, p = welch(a.mean(1), SR, nperseg=4096)
    tot = p[(f > 100) & (f < 9000)].sum()
    return {k: 10 * np.log10(p[(f >= lo) & (f < hi)].sum() / tot + 1e-12) for k, (lo, hi) in
            {"presence": (2000, 4500), "sibilance": (5000, 9000), "top": (9000, 11000)}.items()}


rows = []
for p in sorted(glob.glob(os.path.join(NEW, "**", "*.ogg"), recursive=True)):
    rel = os.path.relpath(p, NEW)
    o = os.path.join(ORIG, rel)
    if not os.path.exists(o): continue
    bo, bn = bands(master.load48(o)), bands(master.load48(p))
    rows.append({"rel": rel.replace(os.sep, "/"), **{k: round(bn[k] - bo[k], 2) for k in bo}})
d = {k: np.array([r[k] for r in rows]) for k in ("presence", "sibilance", "top")}
for k, v in d.items():
    print(f"{k:10s} change vs original: median {np.median(v):+.2f} dB, 5th pct {np.percentile(v, 5):+.2f}, 95th {np.percentile(v, 95):+.2f}")
rep = json.load(open(os.path.join(HERE, "out", "master_voices.json")))
clamped = [r for r in rep if abs(r.get("hf_gain_db", 0)) >= 6.0]
print(f"top-band level clamped (|gain| = 6 dB): {len(clamped)} of {sum('hf_gain_db' in r for r in rep)}")
worst = sorted(rows, key=lambda r: r["sibilance"])[:5]
print("most de-essed:", [(r["rel"], r["sibilance"]) for r in worst])
json.dump(rows, open(os.path.join(HERE, "out", "voice_tone.json"), "w"), indent=1)
