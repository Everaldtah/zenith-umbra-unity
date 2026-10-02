#!/usr/bin/env bash
# Build and install the desktop app "Zenith Umbra Unity" through the running editor:
#   zu_app_setup (menu scene, build order, player settings) -> the Pipeline's async `build` (Win64, Mono) -> poll
#   build_status -> tools/install_app.ps1 (%LOCALAPPDATA%\Programs\ZenithUmbraUnity + shortcuts).
# usage: tools/build_app.sh [version]
set -u
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1
P="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
u() { local c="$1"; shift; unity command "$c" --project-path "$P" --result-only "$@"; }
ver="${1:-0.1.0}"
u editor_stop >/dev/null 2>&1
u zu_app_setup --timeout 300 -- --version "$ver" | tail -1
u build --timeout 120 -- --target StandaloneWindows64 --outputPath "Builds/ZenithUmbraUnity/Zenith Umbra Unity.exe" --confirm true | grep -E 'status|buildId'
for i in $(seq 1 480); do
  st=$(u build_status 2>/dev/null | python -c "import sys,json
try: print(json.load(sys.stdin).get('status'))
except Exception: print('?')")
  [ "$st" = "completed" ] && break
  sleep 15
done
u build_status 2>/dev/null | python -c "
import sys,json
d=json.load(sys.stdin); r=d.get('report') or d; s=r.get('summary', {}) if isinstance(r,dict) else {}
print('build', s.get('result'), 'errors', s.get('totalErrors'), 'size', s.get('totalSize'))"
powershell -NoProfile -ExecutionPolicy Bypass -File "$P/tools/install_app.ps1"
