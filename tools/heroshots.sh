#!/usr/bin/env bash
# Play-test captures of heroes in a running match (the animation / weapons / effects parity check): builds the match
# scene, enters play mode, prints the console's errors, then every few seconds finds each listed hero on the field and
# renders it from 3.2 m in front at chest height. Writes Screenshots/heroes/<hero>_<t>.png and a contact sheet.
# usage: tools/heroshots.sh <map> <player hero> <mode> "<hero ids...>" [rounds=3] [gap seconds=2.5]
#   e.g. tools/heroshots.sh hanabi tenkai spectate "tenkai hayate tomoe gantetsu" 4
# (needs an editor open on this project: GUI, or `Unity.exe -batchmode -projectPath <p>` left running)
set -u
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1
P="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
map="${1:-hanabi}"; me="${2:-tenkai}"; mode="${3:-spectate}"; heroes="${4:-tenkai hayate}"; rounds="${5:-3}"; gap="${6:-2.5}"
S=Screenshots/heroes; mkdir -p "$P/$S"
u() { local c="$1"; shift; unity command "$c" --project-path "$P" --result-only "$@"; }
u editor_stop >/dev/null 2>&1
for try in 1 2 3 4 5 6; do   # a domain reload in progress rejects commands for a few seconds
  u zu_match_scene --timeout 300 -- --map "$map" --hero "$me" --mode "$mode" --third true --autopilot true 2>/dev/null | grep -q '"saved' && break
  u eval --timeout 60 -- --code 'return 1;' >/dev/null 2>&1
done
u eval --timeout 60 -- --code 'UnityEngine.Application.runInBackground = true; return "ok";' >/dev/null 2>&1
for try in 1 2 3; do u editor_play --timeout 600 2>/dev/null | grep -qE 'Entered|Already' && break; done
for i in $(seq 1 90); do
  t=$(u eval --timeout 60 -- --code 'return UnityEngine.Time.time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);' 2>/dev/null | grep -oE '"result": "[0-9.]+"' | grep -oE '[0-9.]+' | tail -1)
  [ -n "$t" ] && python -c "import sys; sys.exit(0 if float('$t') > 6 else 1)" && break
done
echo "== console errors"; u console -- --level error 2>&1 | grep '"message"' | sort | uniq -c | sort -rn | head -15
# every actor: id, Unity draw position, yaw (radians), alive
LOC='var r = UnityEngine.Object.FindFirstObjectByType<ZU.Game.MatchRunner>(); if (r == null || r.World == null) return "none";
var ci = System.Globalization.CultureInfo.InvariantCulture; var sb = new System.Text.StringBuilder();
foreach (var a in r.World.actors) { var p = r.DrawPos(a); sb.Append(a.def.id).Append(" ").Append(p.x.ToString("0.00", ci)).Append(" ").Append(p.y.ToString("0.00", ci)).Append(" ").Append(p.z.ToString("0.00", ci)).Append(" ").Append(a.yaw.ToString("0.000", ci)).Append(" ").Append(a.alive ? 1 : 0).Append(" ").Append(a.Height.ToString("0.00", ci)).Append("|"); }
return sb.ToString();'
for k in $(seq 1 "$rounds"); do
  locs=$(u eval --timeout 60 -- --code "$LOC" 2>/dev/null | python -c "import sys,json; print(json.load(sys.stdin).get('result',''))" 2>/dev/null)
  for h in $heroes; do
    cam=$(python - "$locs" "$h" <<'EOF'
import sys, math
locs, h = sys.argv[1], sys.argv[2]
for row in locs.split('|'):
    f = row.split()
    if len(f) < 7 or f[0] != h or f[5] != '1': continue
    x, y, z, yaw, H = float(f[1]), float(f[2]), float(f[3]), float(f[4]), float(f[6])
    fx, fz = -math.sin(yaw), math.cos(yaw)          # Unity forward of the sim yaw (Conv.Yaw)
    d = 1.8 * H                                      # far enough for the whole body and a hammer's arc
    print(f"{x + fx * d:.2f},{y + 0.75 * H:.2f},{z + fz * d:.2f}|{x:.2f},{y + 0.55 * H:.2f},{z:.2f}")
    break
EOF
)
    [ -z "$cam" ] && { echo "$h: not on the field / dead"; continue; }
    IFS='|' read pos look <<< "$cam"
    u zu_capture --timeout 120 -- --out "$S/${h}_${k}.png" --width 960 --height 720 --pos "$pos" --look "$look" --fov 55 2>&1 | grep -q '"Screenshots' && echo "$h round $k: $S/${h}_${k}.png"
  done
  sleep "$gap"
done
u editor_stop >/dev/null 2>&1
python - "$P/$S" "$heroes" "$rounds" <<'EOF'
import sys, os
from PIL import Image, ImageDraw
d, heroes, rounds = sys.argv[1], sys.argv[2].split(), int(sys.argv[3])
W, H = 480, 360
sheet = Image.new('RGB', (W * rounds, H * len(heroes)), (20, 20, 24))
dr = ImageDraw.Draw(sheet)
for r, h in enumerate(heroes):
    for k in range(rounds):
        p = os.path.join(d, f"{h}_{k + 1}.png")
        if os.path.exists(p): sheet.paste(Image.open(p).convert('RGB').resize((W, H)), (k * W, r * H))
        dr.text((k * W + 8, r * H + 6), f"{h} {k + 1}", fill=(255, 230, 120))
sheet.save(os.path.join(d, 'sheet.png')); print('sheet', os.path.join(d, 'sheet.png'))
EOF
