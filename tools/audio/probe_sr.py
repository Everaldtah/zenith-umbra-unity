#!/usr/bin/env python3
"""A/B the voice super-resolution on whatever AudioSR renders came back: spectra of original / AudioSR / mastered per clip,
and listening copies in tools/audio/out/probe (<name>.orig.wav = the 24 kHz line as it ships today, <name>.wav = remastered).

    python tools/audio/probe_sr.py [sr_dir]"""
import glob, os, sys

import numpy as np
import soundfile as sf
from scipy.signal import welch

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import master  # noqa: E402

SR = 48000
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
srdir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "out", "kaggle", "audiosr", "sr")
out = os.path.join(HERE, "out", "probe")
os.makedirs(out, exist_ok=True)


def bands(a):
    m = a.mean(1) if a.ndim > 1 else a
    f, P = welch(m, SR, nperseg=4096)
    P = 10 * np.log10(P + 1e-20); pk = P.max()
    return " ".join(f"{b // 1000}k:{P[(f >= b * 0.95) & (f < b * 1.05)].mean() - pk:4.0f}" for b in [4000, 8000, 10000, 11000, 12000, 14000, 16000, 18000, 20000])


for p in sorted(glob.glob(os.path.join(srdir, "**", "*.wav"), recursive=True)):
    rel = os.path.relpath(p, srdir).replace(os.sep, "/")
    rel = rel.split("zu-audio-vo24/", 1)[-1]
    src = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio", "vo", rel[:-4] + ".ogg")
    name = rel[:-4].replace("/", "__")
    o = master.load48(src); s = master.load48(p)
    rep = master.master(src, os.path.join(out, name + ".wav"), "voice", p)
    a, _ = sf.read(os.path.join(out, name + ".wav"), always_2d=True)
    sf.write(os.path.join(out, name + ".orig.wav"), o.astype(np.float32), SR, subtype="FLOAT")
    print(rel, "lag", rep.get("lag"), "hf gain", rep.get("hf_gain_db"), "de-ess", round(rep["deess_db"], 1), "comp", round(rep["comp_db"], 1), "limit", round(rep["limit_db"], 2))
    print("   orig    ", bands(o)); print("   audioSR ", bands(s)); print("   mastered", bands(a))
    print("   before", rep["before"], "after", rep["after"])
