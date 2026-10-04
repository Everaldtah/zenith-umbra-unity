#!/usr/bin/env python3
"""Measure generated takes against the clips that ship today: spectrum ceiling (crisp), attack (a hard start for weapons /
impacts / steps), loudness, how much of the render holds sound, clipping, CLAP score.

    python tools/audio/probe_takes.py tools/audio/out/kaggle/sa3probe/takes"""
import glob, os, re, sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "design"))
import master as M  # noqa: E402
import meter  # noqa: E402
from compose import attack_ratio  # noqa: E402

SR = 48000
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
d = sys.argv[1]


def stats(a):
    m = np.abs(a).max(1)
    act = np.flatnonzero(m > m.max() * 0.01) if m.max() > 0 else []
    used = (act[-1] - act[0]) / SR if len(act) else 0
    return f"top {meter.bandwidth(a, SR)['top'] / 1000:5.1f}k  atk {attack_ratio(a):4.1f}  M {meter.loudness(a, SR)['M']:6.1f}  used {used:5.2f}s/{len(a) / SR:5.2f}s  clip {int((np.abs(a) >= 0.999).sum())}  ch {a.shape[1]}"


for sid in sorted(os.listdir(d)):
    print(sid)
    for p in sorted(glob.glob(os.path.join(d, sid, "*.wav")))[:10]:
        clap = re.search(r"_(-?[\d.]+)\.wav$", p)
        print(f"   new  {os.path.basename(p):14s} clap {float(clap.group(1)) if clap else 0:5.3f}  {stats(M.load48(p))}")
    for p in sorted(glob.glob(os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio", "sfx", sid, "*.*"))):
        if p.endswith(".meta"): continue
        print(f"   now  {os.path.basename(p):14s}              {stats(M.load48(p))}")
