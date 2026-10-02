#!/usr/bin/env bash
# Play-test captures of heroes in a running match (the animation / weapons / effects parity check): builds the match
# scene, enters play mode, prints the console's errors, then every few seconds finds each listed hero on the field and
# renders it unoccluded from 1.6 x its height at chest height. Writes Screenshots/heroes/<hero>_<t>.png and a contact sheet.
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
# each round, in ONE eval (so nobody moves between finding a hero and taking its picture): every listed hero on the
# field is framed from 1.6 x its height away at chest height - straight in front, else 45 deg to either side, else
# behind - whichever view the level's colliders don't block, and rendered through ZuCapture
for k in $(seq 1 "$rounds"); do
  CODE='var r = UnityEngine.Object.FindFirstObjectByType<ZU.Game.MatchRunner>(); if (r == null || r.World == null) return "no match";
var ci = System.Globalization.CultureInfo.InvariantCulture; var sb = new System.Text.StringBuilder();
foreach (var h in "'"$heroes"'".Split(new[]{(char)32}, System.StringSplitOptions.RemoveEmptyEntries)) {
  var a = r.World.actors.Find(x => x.def.id == h && x.alive); if (a == null) { sb.Append(h).Append(":absent "); continue; }
  var p = r.DrawPos(a); float H = (float)a.Height, yaw = (float)a.yaw; var chest = p + UnityEngine.Vector3.up * (0.55f * H);
  UnityEngine.Vector3 cam = UnityEngine.Vector3.zero; bool found = false;
  foreach (var off in new[] { 0f, 45f, -45f, 90f, -90f, 180f }) {
    var f = UnityEngine.Quaternion.Euler(0, -yaw * UnityEngine.Mathf.Rad2Deg + off, 0) * UnityEngine.Vector3.forward;
    var c = p + f * (1.6f * H) + UnityEngine.Vector3.up * (0.7f * H);
    if (!UnityEngine.Physics.Linecast(c, chest)) { cam = c; found = true; break; } }
  if (!found) { sb.Append(h).Append(":blocked "); continue; }
  string F(UnityEngine.Vector3 v) => v.x.ToString("0.00", ci) + "," + v.y.ToString("0.00", ci) + "," + v.z.ToString("0.00", ci);
  var res = ZU.EditorTools.ZuCapture.Capture("'"$S"'/" + h + "_'"$k"'.png", 960, 720, F(cam), F(chest), 55);
  sb.Append(h).Append(":").Append(res != null && res.Contains("Screenshots") ? "ok" : res).Append(" "); }
return sb.ToString();'
  u eval --timeout 300 -- --code "$CODE" 2>&1 | python -c "import sys,json
try: print('round $k:', json.load(sys.stdin).get('result'))
except Exception as e: print('round $k: eval failed', e)"
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
