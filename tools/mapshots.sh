#!/usr/bin/env bash
# Review captures of a map in the running Unity editor: builds the match scene for it (Kaien on autopilot), enters play
# mode, waits for the level to stand, renders four standard views through zu_capture and packs them into
# Screenshots/maps/<map>_sheet.png. Usage: tools/mapshots.sh <map> [<map> ...]   (needs the editor open on this project)
set -u
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1
P="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
S=Screenshots/maps; mkdir -p "$P/$S"
u() { local c="$1"; shift; unity command "$c" --project-path "$P" --result-only "$@"; }   # CLI flags before the `--`
for map in "$@"; do
  read X Z < <(python -c "
import json
d=json.load(open(r'$P/Assets/ZU/Resources/ZUData/maps.json',encoding='utf-8'))
m=d if isinstance(d,list) else d.get('maps',d)
m=list(m.values()) if isinstance(m,dict) else m
s=[x for x in m if x['id']=='$map'][0]['size']; print(s[0], s[1])")
  u editor_stop >/dev/null 2>&1
  for try in 1 2 3 4 5 6; do   # a domain reload in progress rejects commands for a few seconds
    u zu_match_scene --timeout 300 -- --map "$map" --hero kaien --mode quickplay --third true --autopilot true 2>/dev/null | grep -q '"saved' && break
    u eval --timeout 60 -- --code 'return 1;' >/dev/null 2>&1
  done
  u eval --timeout 60 -- --code 'UnityEngine.Application.runInBackground = true; return "ok";' >/dev/null 2>&1
  for try in 1 2 3; do u editor_play --timeout 600 2>/dev/null | grep -qE 'Entered|Already' && break; done
  # wait for the level to stand: play time has to advance past a couple of seconds (the outer world builds on load)
  for i in $(seq 1 90); do
    t=$(u eval --timeout 60 -- --code 'return UnityEngine.Time.time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);' 2>/dev/null | grep -oE '"result": "[0-9.]+"' | grep -oE '[0-9.]+' | tail -1)
    [ -n "$t" ] && python -c "import sys; sys.exit(0 if float('$t') > 3 else 1)" && break
  done
  u console -- --level error 2>&1 | grep '"message"' | sort | uniq -c | head -5
  shots=(
    "a|0,6,$(python -c "print(-$Z*0.8)")|0,4,$Z"
    "b|$(python -c "print(-$X*0.7)"),26,$(python -c "print(-$Z-30)")|$(python -c "print($X*0.3)"),0,$(python -c "print($Z*0.5)")"
    "c|0,95,$(python -c "print(-$Z-170)")|0,0,40"
    "d|$(python -c "print($X*0.5)"),3,0|$(python -c "print($X+300)"),30,100"
  )
  for shot in "${shots[@]}"; do
    IFS='|' read n pos look <<< "$shot"
    for try in 1 2 3; do
      u zu_capture --timeout 120 -- --out "$S/${map}_$n.png" --width 1280 --height 720 --pos "$pos" --look "$look" --fov 70 > "$S/_last.txt" 2>&1
      grep -q '"Screenshots' "$S/_last.txt" && break
      cat "$S/_last.txt" >> "$S/_log.txt"
    done
  done
  python -c "
from PIL import Image
ims=[Image.open(r'$P/$S/${map}_'+c+'.png').convert('RGB').resize((960,540)) for c in 'abcd']
o=Image.new('RGB',(1920,1080))
for i,im in enumerate(ims): o.paste(im,((i%2)*960,(i//2)*540))
o.save(r'$P/$S/${map}_sheet.png')
print('sheet', r'$S/${map}_sheet.png')"
done
u editor_stop >/dev/null 2>&1
