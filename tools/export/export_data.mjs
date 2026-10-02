// Bundle export_data.ts against the zenith-umbra source with that repo's own rolldown, then run it.
// usage: node export_data.mjs <zenith-umbra repo> <out dir>
import { createRequire } from 'node:module';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

const [zu, out] = process.argv.slice(2).map(p => resolve(p));
if (!zu || !out) { console.error('usage: node export_data.mjs <zenith-umbra repo> <out dir>'); process.exit(2); }
const here = dirname(fileURLToPath(import.meta.url));
const req = createRequire(join(zu, 'package.json'));
const { build } = await import(pathToFileURL(req.resolve('rolldown')).href);
const bundle = join(here, '.export_data.bundle.mjs');
await build({
  input: join(here, 'export_data.ts'),
  platform: 'node',
  resolve: { alias: { ZU: join(zu, 'src') } },
  define: { 'import.meta.env': '{}' },
  output: { file: bundle, format: 'esm' },
  logLevel: 'warn',
});
execFileSync(process.execPath, [bundle, out], { stdio: 'inherit' });
