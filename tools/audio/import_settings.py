#!/usr/bin/env python3
"""Set the import settings of every clip in the sound bank (edits the .meta files; Unity reimports on the next open).

    python tools/audio/import_settings.py            # apply
    python tools/audio/import_settings.py --check    # list the metas that differ (exit 1 if any)

Rules (docs/audio/AUDIO_STANDARD.md):
  normalize 0        the bank is mastered to per-category loudness targets; Unity's import-time PEAK normalisation pushed
                     every clip to 0 dBFS, undoing that balance and summing the mix into clipping
  loadType 0         decompress on load: no decoding on the audio thread mid-fight (the whole bank is ~70 MB as PCM)
  Vorbis, quality 1  encoded once at full quality from the lossless masters
  preload 1 (sfx)    the effects are in memory before the first shot (a main-thread load mid-fight is a hitch, and a hitch
                     starves the audio thread); voice lines load on demand, in the background
  sample rate        preserved (the masters are 48 kHz, the output runs at 48 kHz: no resampling anywhere)
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
BANK = os.path.join(ROOT, "Assets", "ZU", "Resources", "ZUAudio")


def want(rel):
    vo = rel.startswith("vo/")
    return {
        "loadType": "0", "sampleRateSetting": "0", "compressionFormat": "1", "quality": "1", "conversionMode": "0",
        "preloadAudioData": "0" if vo else "1", "forceToMono": "0", "normalize": "0", "loadInBackground": "1" if vo else "0",
    }


def main():
    check = "--check" in sys.argv
    changed = 0
    for d, _, files in os.walk(BANK):
        for f in files:
            if not f.endswith((".ogg.meta", ".wav.meta")): continue
            p = os.path.join(d, f)
            rel = os.path.relpath(p, BANK).replace("\\", "/")
            src = open(p, encoding="utf-8").read()
            out = src
            for k, v in want(rel).items():
                out = re.sub(rf"(\n\s+{k}: )\S+", lambda m: m.group(1) + v, out)
            if out != src:
                changed += 1
                if check: print("differs:", rel)
                else: open(p, "w", encoding="utf-8", newline="\n").write(out)
    print(f"{changed} metas {'differ' if check else 'updated'}")
    return 1 if check and changed else 0


if __name__ == "__main__":
    sys.exit(main())
