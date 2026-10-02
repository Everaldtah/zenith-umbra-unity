// Golden vectors from the TS codec (zenith-umbra src/net/codec.ts), for `run.ps1 golden <out.json>`: the C# Codec must
// write the same bytes for the same packets.
//   node --experimental-transform-types tools/nettest/golden.mjs <path to codec.ts> <out.json>
import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const [codecPath, out] = process.argv.slice(2);
const K = await import(pathToFileURL(codecPath).href);
const hex = u => Buffer.from(u).toString('hex');

// awkward values on purpose: halves (JS rounding), truncating u8 (scale x 50), negative angles, clamped vitals,
// a u32 projectile id, an ack that wraps, non-ASCII cold JSON
const header = { seq: 65535, time: 1234.5678, ackInput: 65534, tier: 3, hostMs: 123456.75 };
const actors = [
  { id: 7, x: 12.345, y: 1.5, z: -40.25, vx: 3.215, vy: -9.805, vz: 0.5, yaw: -2.5, pitch: 0.30005, hp: 250.5, armor: 25.49, shield: 75, bhp: 1400, flags: K.AF_ALIVE | K.AF_GROUNDED, scale: 1.099, beam: 3 },
  { id: 65535, x: -0.001, y: 0, z: 9999.5, vx: -400, vy: 400, vz: -0.005, yaw: 7.0, pitch: -1.5, hp: 70000, armor: -5, shield: 0.5, bhp: 0, flags: 255, scale: 2.2, beam: 0 },
];
const projs = [{ id: 4000000001, x: 1, y: 2, z: 3, vx: 60.01, vy: -4.99, vz: 0.01 }, { id: 0, x: -1.25, y: 0.125, z: 3e3, vx: -700, vy: 0, vz: 0.5 }];
const cold = JSON.stringify({ a: { 7: { d: 'raijin', st: { stun: 12.3 } } }, m: { r: 'control' }, n: 'Zénith ✦' });
const w = new K.Writer(16);
K.writeSnapHeader(w, header); K.writeHot(w, actors, projs); w.str(cold);

const packet = { seq: 65535, ackSnap: 3, yaw: -3.14159, pitch: 1.2345, mx: -0.705, mz: 0.995, held: 0b101010101010, presses: [0, 1, 2, 3, 127, 128, 254, 255, 9], viewMs: 85.5 };
const wi = new K.Writer(8); K.writeInput(wi, packet);

writeFileSync(out, JSON.stringify({ snap: { header, actors, projs, cold, hex: hex(w.done()) }, input: { packet, hex: hex(wi.done()) } }, null, 1));
console.log('wrote', out);
