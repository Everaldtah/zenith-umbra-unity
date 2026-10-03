#!/usr/bin/env python3
"""Export sounds.py as the Kaggle sa3 stage's job list.

    python tools/audio/design/jobs.py > tools/audio/out/sa3_jobs.json
    python tools/audio/design/jobs.py --only chaingun,impact_glass --takes 4 > tools/audio/out/sa3_probe.json"""
import argparse, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sounds import SOUNDS  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("--only")
ap.add_argument("--takes", type=int)
a = ap.parse_args()
only = set(a.only.split(",")) if a.only else None
jobs = [{"id": s["id"], "prompt": s["prompt"], "secs": s["secs"], "takes": a.takes or s["takes"], "loop": s["loop"]}
        for s in SOUNDS.values() if not only or s["id"] in only]
json.dump(jobs, sys.stdout, separators=(",", ":"))
