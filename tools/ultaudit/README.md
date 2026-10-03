# Ult audit (evera-a1): web Ult Viewer vs Unity UltShowcase, same sim moments

- `ult_times.json`: per hero, the sim seconds after the cast to capture (0.25, 0.6, then 30 / 55 / 85% of the ult's PLAN secs).
- Web half: copy `a1_ult_cmp.mjs` into a zenith-umbra worktree's `tests/e2e/` (puppeteer-core resolves from there), run a dev
  server (`node_modules/.bin/vite --port 5299 --strictPort`), then hold the gpu slot and run it outside lg's job object
  (Chrome can stall inside one): `lg acquire gpu --owner <me> --ttl 1800` (retry until "acquired"), then
  `node tests/e2e/a1_ult_cmp.mjs http://localhost:5299/play.html <ult_times.json> tests/e2e/shots/a1ult`, then `lg release gpu`.
- Unity half: batch Editor on the worktree (lg), play mode on Assets/ZU/Scenes/Match.unity, then per hero
  `unity command zu_ult_shots --project-path <wt> -- --hero <id> --times "<times>" --out Screenshots/ultaudit` and poll
  `zu_ult_shots_status` until done.
- Sheets: `python ult_sheets.py <webShotsDir> <unityShotsDir> ult_times.json <outDir>` (web row over Unity row).
