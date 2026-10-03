#!/usr/bin/env python3
"""Pick, layer, master and install the generated sound effects.

    python tools/audio/design/compose.py --takes tools/audio/out/kaggle/sa3/takes          # every id with takes
    python tools/audio/design/compose.py --takes <dir> --only chaingun,impact_glass --dry  # report, write nothing
    python tools/audio/design/compose.py --demo impact_glass                                # layers only, no body (smoke)

For each id in sounds.py:
  1. pick: every take is scored - its CLAP match to the prompt (from the Kaggle stage), its bandwidth (crisp), its onset
     (a weapon / impact / step must start hard), clipping and length fit - and the best `vars` takes that differ from
     each other ship as the id's variations
  2. compose: the body (mono for 3D sounds - a stereo clip in a 3D source only blurs its position; stereo for the 2D beds)
     plus the synthesised layers (synth.py) at their offsets from the body's onset, levels relative to the body's peak
  3. loops: made seamless (equal-power crossfade of the tail into the head)
  4. master: tools/audio/master.py's chain for the category (high-pass, loudness target, -1 dBTP, edges)
  5. install: Assets/ZU/Resources/ZUAudio/sfx/<id>/<i>.wav (16-bit 48 kHz) replacing the old clips, full AudioImporter
     metas (normalize off, preload), and sfxbank.json's count / category
"""
import argparse, glob, json, os, re, shutil, sys, uuid

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.dirname(HERE))
import master as M  # noqa: E402
import meter  # noqa: E402
import synth  # noqa: E402
from sounds import SOUNDS  # noqa: E402

SR = 48000
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
BANK = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio", "sfx")
DATA = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUData", "sfxbank.json")
OUT = os.path.join(HERE, "..", "out", "design")
TRANSIENT = {"weapon", "impact", "step", "feedback"}
STEREO = {"amb"}            # 2D beds keep their width; everything placed in the world is mono


# ------------------------------------------------------------------------------------------------ pick
def onset(a, thresh_db=-30):
    m = np.abs(a).max(1)
    pk = m.max()
    if pk <= 0: return 0
    return int(np.flatnonzero(m > pk * 10 ** (thresh_db / 20))[0])


def attack_ratio(a):
    """peak in the first 15 ms after the onset vs the RMS of the next 150 ms: high = a hard, defined start"""
    o = onset(a); m = np.abs(a).max(1)
    head = m[o:o + int(SR * 0.015)]; body = m[o:o + int(SR * 0.15)]
    if not len(head) or not len(body): return 0.0
    return float(head.max() / (np.sqrt((body ** 2).mean()) + 1e-9))


def score(path, spec, clap):
    a = M.load48(path)
    bw = meter.bandwidth(a, SR)["top"]
    s = clap * 10                                        # CLAP cosine ~0.2-0.6 -> 2-6
    s += min(bw, 20000) / 20000 * 2                      # full band up to +2
    if spec["cat"] in TRANSIENT: s += min(attack_ratio(a), 6) / 6 * 2
    if (np.abs(a) >= 0.999).sum() > 3: s -= 3            # a clipped render
    act = np.flatnonzero(np.abs(a).max(1) > np.abs(a).max() * 0.01)
    used = (act[-1] - act[0]) / SR if len(act) else 0
    if not spec["loop"] and used < spec["secs"] * 0.25: s -= 1.5      # a near-empty render
    return s, a


def spectrum(a):
    from scipy.signal import welch
    _, p = welch(a.mean(1), SR, nperseg=2048)
    v = np.log10(p + 1e-12); return (v - v.mean()) / (v.std() + 1e-9)


def pick(takes, spec):
    """the best takes that aren't near-copies of each other (spectral distance)"""
    ranked = sorted(takes, key=lambda x: -x[0])
    chosen = []
    for sc, path, a in ranked:
        sp = spectrum(a)
        if all(np.mean((sp - c[3]) ** 2) > 0.08 for c in chosen):
            chosen.append((sc, path, a, sp))
        if len(chosen) >= spec["vars"]: break
    for sc, path, a in ranked:                          # not enough distinct ones: fill from the top
        if len(chosen) >= spec["vars"]: break
        if all(path != c[1] for c in chosen): chosen.append((sc, path, a, spectrum(a)))
    return chosen


# ------------------------------------------------------------------------------------------------ compose
def to_mono(a):
    if a.shape[1] == 1: return a
    L, R = a[:, 0], a[:, 1]
    c = np.corrcoef(L, R)[0, 1] if L.std() > 0 and R.std() > 0 else 1
    if c < 0.3:                                          # wide / out-of-phase render: the stronger channel, not a cancelled sum
        return a[:, [0]] if (L ** 2).sum() >= (R ** 2).sum() else a[:, [1]]
    return a.mean(1, keepdims=True)


def compose(body, spec, seed):
    a = body if spec["cat"] in STEREO else to_mono(body)
    o = onset(a)
    pk = np.abs(a).max() or 1.0
    out = a.copy()
    for i, L in enumerate(spec["layers"]):
        x = synth.render(L, seed * 31 + i)
        at = o + int(SR * L.get("at", 0) / 1000)
        if at >= len(out): continue
        need = at + len(x)
        if need > len(out): out = np.vstack([out, np.zeros((need - len(out), out.shape[1]))])
        out[at:at + len(x)] += (x * pk * 10 ** (L["db"] / 20))[:, None]
    return out


def make_loop(a, xf=0.5):
    n = int(SR * min(xf, len(a) / SR / 4))
    if n < 16: return a
    head, tail = a[:n], a[-n:]
    t = np.linspace(0, np.pi / 2, n)[:, None]
    seam = tail * np.cos(t) + head * np.sin(t)          # equal power: the tail fades into the head
    return np.vstack([seam, a[n:-n]])


def finish(a, spec):
    cat = spec["cat"]
    a = M.highpass(a, M.HPF.get(cat, 30))
    if not spec["loop"]: a = M.edges(a, fout_ms=8)
    target = M.TARGET_M.get(cat, -16)
    a, _ = M.level_to_integrated(a, target) if spec["loop"] else M.level_to(a, target)
    a, red = M.tp_limit(a)
    if spec["loop"]: a = make_loop(a)
    return a, red


# ------------------------------------------------------------------------------------------------ install
META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def install(sid, clips, spec):
    d = os.path.join(BANK, sid)
    if os.path.isdir(d):
        for f in glob.glob(os.path.join(d, "*")):
            if re.search(r"\.(ogg|wav)(\.meta)?$", f): os.remove(f)
    else:
        os.makedirs(d)
        open(d + ".meta", "w", newline="\n").write(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    for i, a in enumerate(clips):
        p = os.path.join(d, f"{i}.wav")
        dith = (np.random.default_rng(i).random(a.shape) - np.random.default_rng(i + 99).random(a.shape)) / 32768
        sf.write(p, np.clip(a + dith, -1, 1 - 1 / 32768), SR, subtype="PCM_16")
        open(p + ".meta", "w", newline="\n").write(META.format(guid=uuid.uuid4().hex))
    bank = json.load(open(DATA, encoding="utf-8"))
    bank["sfx"][sid] = {"n": len(clips), "cat": spec["cat"]}
    json.dump(bank, open(DATA, "w", encoding="utf-8", newline="\n"), separators=(",", ":"))


# ------------------------------------------------------------------------------------------------ main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--takes", help="folder of <id>/<take>_<clap>.wav from the Kaggle sa3 stage")
    ap.add_argument("--only")
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--demo", help="render one id's layers alone (no body) to tools/audio/out/design/demo_<id>.wav")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    if a.demo:
        spec = SOUNDS[a.demo]
        body = np.zeros((SR // 2, 1)); body[0, 0] = 1.0
        x, _ = finish(compose(body, spec, 1), spec)
        sf.write(os.path.join(OUT, f"demo_{a.demo}.wav"), x.astype(np.float32), SR, subtype="FLOAT")
        print("demo ->", os.path.join(OUT, f"demo_{a.demo}.wav"), meter.loudness(x, SR), meter.true_peak(x)); return
    only = set(a.only.split(",")) if a.only else None
    report = []
    for sid, spec in SOUNDS.items():
        if only and sid not in only: continue
        files = glob.glob(os.path.join(a.takes, sid, "*.wav"))
        if not files: continue
        takes = []
        for f in files:
            m = re.search(r"_(-?[\d.]+)\.wav$", f)
            sc, au = score(f, spec, float(m.group(1)) if m else 0.3)
            takes.append((sc, f, au))
        chosen = pick(takes, spec)
        clips, reps = [], []
        for i, (sc, path, au, _) in enumerate(chosen):
            x, red = finish(compose(au, spec, i), spec)
            clips.append(x)
            reps.append({"take": os.path.basename(path), "score": round(sc, 2), "M": meter.loudness(x, SR)["M"], "tp": round(meter.true_peak(x), 2),
                         "top": meter.bandwidth(x, SR)["top"], "limit_db": round(red, 2), "ch": x.shape[1], "secs": round(len(x) / SR, 2)})
            sf.write(os.path.join(OUT, f"{sid}_{i}.wav"), x.astype(np.float32), SR, subtype="FLOAT")      # listening copies
        if not a.dry: install(sid, clips, spec)
        report.append({"id": sid, "takes": len(files), "shipped": reps})
        print(f"{sid:16s} {len(files)} takes -> {len(clips)} vars  " + "  ".join(f"[{r['score']} M{r['M']:.0f} tp{r['tp']:+.1f} {r['top'] // 1000}k]" for r in reps))
    json.dump(report, open(os.path.join(OUT, "compose.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
