// evera-a1's web half of the ult comparison (not part of the repo): the Ult Viewer for each hero, screenshots at fixed
// SIMULATION times after the cast (the same times the Unity side shoots: zu_ult_shots), from tests/e2e/ult_viewer.mjs.
//   node tests/e2e/a1_ult_cmp.mjs <url> <times.json> <outDir> [ids]
import puppeteer from 'puppeteer-core';
import fs from 'node:fs';
let [url = 'http://localhost:5299/play.html', timesPath, outDir = 'tests/e2e/shots/a1ult', idsArg = ''] = process.argv.slice(2);
if (!/platform=/.test(url)) url += (url.includes('?') ? '&' : '?') + 'platform=desktop';
const TIMES = JSON.parse(fs.readFileSync(timesPath, 'utf8'));
const CHROME = ['C:/Program Files/Google/Chrome/Application/chrome.exe'].find(p => fs.existsSync(p));
const b = await puppeteer.launch({ executablePath: CHROME, headless: 'new', args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist', '--window-size=1280,720', '--autoplay-policy=no-user-gesture-required'], defaultViewport: { width: 1280, height: 720 }, timeout: 120000, protocolTimeout: 300000 });
setTimeout(() => { console.log('TIMEOUT'); process.exit(2); }, 25 * 60 * 1000);
const p = await b.newPage();
const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
await p.goto(url, { waitUntil: 'domcontentloaded' });
await new Promise(r => setTimeout(r, 1500));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const ids = idsArg ? idsArg.split(',') : Object.keys(TIMES);
fs.mkdirSync(outDir, { recursive: true });
const out = {};
for (const id of ids) {
  const t0 = Date.now();
  await p.evaluate(id => window.__zu.menu.viewer(id), id);
  await sleep(600);
  const shown = await p.evaluate(() => { const b = document.querySelector('.vult'); if (!b || b.style.display === 'none') return false; b.click(); return true; });
  if (!shown) { out[id] = { error: 'no ULT VIEWER button' }; console.log(id, 'no button'); continue; }
  try {
    await p.waitForFunction(id => { const g = window.__zu.menu.game; return g.running && g.match?.world.actors[0].baseDef.id === id && g.match.world.actors[0].controller?.phase && document.querySelector('.ultsc'); }, { timeout: 180000 }, id);
    await p.waitForFunction(() => window.__zu.menu.game.match.world.actors[0].controller.phase === 'show', { timeout: 20000 });
  } catch (e) { out[id] = { error: 'no cast: ' + e.message }; console.log(id, 'no cast'); await p.evaluate(() => window.__zu.menu.game.onExit?.()); await sleep(500); continue; }
  const castT = await p.evaluate(() => window.__zu.menu.game.match.world.time);
  const shots = [];
  for (const [n, at] of TIMES[id].entries()) {
    // poll the sim clock and shoot the first frame at or past the moment
    for (let k = 0; k < 2000; k++) { const t = await p.evaluate(() => window.__zu.menu.game.match?.world.time ?? -1); if (t < 0 || t - castT >= at) break; await sleep(15); }
    const t = await p.evaluate(() => window.__zu.menu.game.match?.world.time ?? -1);
    await p.screenshot({ path: `${outDir}/${id}_${n + 1}.png` });
    shots.push(+(t - castT).toFixed(2));
  }
  out[id] = { loadMs: Date.now() - t0, shots };
  console.log(id, JSON.stringify(out[id]));
  await p.evaluate(() => window.__zu.menu.game.onExit?.()); await sleep(500);
}
fs.writeFileSync(`${outDir}/info.json`, JSON.stringify({ out, errs: errs.slice(0, 20) }, null, 1));
console.log('errors', JSON.stringify(errs.slice(0, 8)));
await Promise.race([b.close(), sleep(5000)]);
process.exit(0);
