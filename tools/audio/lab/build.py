#!/usr/bin/env python3
"""Assemble the Sound Lab page's files (tools/audio/lab/index.html + audio + data.json) into tools/audio/out/lab, ready
to open locally (python -m http.server -d tools/audio/out/lab) or to publish as the private Sound Lab artifact.

    python tools/audio/lab/build.py

What goes in:
  stress   the bank's weapon / impact / ability one-shots (the overload test fires 40 at once)
  voices   every voice line with an AudioSR render: the line as it ships (24 kHz) vs remastered (48 kHz), plus subtitles
  sfx      every designed sound in tools/audio/out/design (old clip vs new), once compose.py has run
  audit    tools/audio/out/audit.json (the shipped bank) and audit_after.json when present
"""
import glob, json, os, shutil, subprocess, sys, time

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)
sys.path.insert(0, TOOLS)
import meter  # noqa: E402

ROOT = os.path.normpath(os.path.join(TOOLS, "..", ".."))
BANK = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio")
OUT = os.path.join(TOOLS, "out", "lab")
DATA = json.load(open(os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUData", "sfxbank.json"), encoding="utf-8"))
MAX_VOICES = int(os.environ.get("LAB_VOICES", "48"))


def ogg(src, dst, q=6):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-c:a", "libvorbis", "-q:a", str(q), "-ar", "48000", dst], check=True)


def summary(rows):
    cats = {}
    for r in rows:
        if "error" in r: continue
        cats.setdefault(r["cat"], []).append(r)
    target = {"weapon": -14, "impact": -16, "ability": -15, "move": -21, "step": -22, "feedback": -16, "loop": -20, "amb": -26, "voice": -16}
    return [{"cat": c, "clips": len(rs), "M": round(float(np.median([r["M"] for r in rs])), 1), "target": target.get(c),
             "tp": round(max(r["tp"] for r in rs), 1), "bw": round(float(np.median([r["top"] for r in rs])) / 1000, 1),
             "dull": sum(r["top"] < 15000 for r in rs)} for c, rs in sorted(cats.items())]


def main():
    shutil.rmtree(OUT, ignore_errors=True)
    os.makedirs(OUT)
    shutil.copyfile(os.path.join(HERE, "index.html"), os.path.join(OUT, "index.html"))
    data = {"built": time.strftime("%Y-%m-%d %H:%M"), "stress": [], "voices": [], "sfx": [], "audit": {}}
    # the overload test: up to 2 variations of every loud one-shot
    for sid, meta in sorted(DATA["sfx"].items()):
        if meta.get("cat") not in ("weapon", "impact", "ability"): continue
        for i in range(min(2, meta.get("n", 0))):
            for ext in ("wav", "ogg"):
                p = os.path.join(BANK, "sfx", sid, f"{i}.{ext}")
                if os.path.exists(p):
                    dst = f"a/stress/{sid}_{i}.ogg"; ogg(p, os.path.join(OUT, dst), 5); data["stress"].append(dst); break
    # voices: original vs remastered (master.py's output, else the probe)
    remastered = sorted(glob.glob(os.path.join(TOOLS, "out", "master", "vo", "**", "*.ogg"), recursive=True))
    pairs = []
    if remastered:
        for p in remastered:
            rel = os.path.relpath(p, os.path.join(TOOLS, "out", "master", "vo")).replace(os.sep, "/")
            orig = os.path.join(TOOLS, "out", "kaggle_in", "vo", rel)          # the line as it shipped (the bank may hold the remaster now)
            pairs.append((rel, orig if os.path.exists(orig) else os.path.join(BANK, "vo", rel), p))
    else:
        for p in sorted(glob.glob(os.path.join(TOOLS, "out", "probe", "*.orig.wav"))):
            name = os.path.basename(p)[:-9]
            pairs.append((name.replace("__", "/") + ".ogg", p, p.replace(".orig.wav", ".wav")))
    # a spread across heroes and line kinds (the full set would make the page heavy)
    if len(pairs) > MAX_VOICES:
        step = len(pairs) / MAX_VOICES
        pairs = [pairs[int(i * step)] for i in range(MAX_VOICES)]
    subs = DATA.get("subs", {})
    for rel, a, b in pairs:
        voice, file = rel.split("/", 1)
        key, take = file[:-4].rsplit("_", 1)
        words = ""
        try: words = subs.get(voice, {}).get(key, [])[int(take)]
        except Exception: pass
        name = rel[:-4].replace("/", "__")
        da, db = f"a/vo/{name}.a.ogg", f"a/vo/{name}.b.ogg"
        ogg(a, os.path.join(OUT, da), 6); ogg(b, os.path.join(OUT, db), 6)
        xa, sa = sf.read(a); xb, sb = sf.read(b)
        data["voices"].append({"voice": voice, "key": key, "words": words, "a": da, "b": db,
                               "aTop": meter.bandwidth(xa, sa)["top"], "bTop": meter.bandwidth(xb, sb)["top"],
                               "aM": meter.loudness(xa, sa)["M"], "bM": meter.loudness(xb, sb)["M"]})
    # designed sound effects: the clip that ships today vs the new one
    rep = os.path.join(TOOLS, "out", "design", "compose.json")
    old = os.path.join(TOOLS, "out", "design_old")          # compose.py keeps nothing old: lab reads the git copy
    if os.path.exists(rep):
        for r in json.load(open(rep)):
            sid = r["id"]
            for i, s in enumerate(r["shipped"][:2]):
                nb = os.path.join(TOOLS, "out", "design", f"{sid}_{i}.wav")
                if not os.path.exists(nb): continue
                db = f"a/sfx/{sid}_{i}.b.ogg"; ogg(nb, os.path.join(OUT, db), 6)
                da = None
                for ext in ("ogg", "wav"):
                    po = os.path.join(old, sid, f"{i}.{ext}")
                    if os.path.exists(po): da = f"a/sfx/{sid}_{i}.a.ogg"; ogg(po, os.path.join(OUT, da), 6); break
                data["sfx"].append({"id": sid, "cat": SOUNDS_CAT.get(sid, ""), "a": da, "b": db, "bTop": s["top"], "bM": s["M"]})
    for name in ("audit", "audit_after"):
        p = os.path.join(TOOLS, "out", f"{name}.json")
        if os.path.exists(p): data["audit"][name] = summary(json.load(open(p)))
    data["stressBytes"] = sum(os.path.getsize(os.path.join(OUT, p)) for p in data["stress"])
    json.dump(data, open(os.path.join(OUT, "data.json"), "w"), separators=(",", ":"))
    size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(OUT) for f in fs)
    print(f"lab: {len(data['stress'])} stress clips, {len(data['voices'])} voice pairs, {len(data['sfx'])} sfx -> {OUT} ({size / 1e6:.1f} MB)")


SOUNDS_CAT = {k: v.get("cat", "") for k, v in DATA["sfx"].items()}

if __name__ == "__main__":
    main()
