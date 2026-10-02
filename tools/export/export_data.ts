// Exports ZENITH//UMBRA's game data from the TypeScript source (the single source of truth) to JSON for the Unity port.
// Bundled with the zenith-umbra repo's own rolldown (tools/export_data.mjs) and run with node; nothing in that repo changes.
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { HEROES, PILOTS, TEAM_NAME, TEAM_COLOR } from 'ZU/data/heroes';
import { MAPS } from 'ZU/data/maps';
import { ROBOTS } from 'ZU/data/robots';
import { skinsFor } from 'ZU/data/skins';
import { ENEMIES, BOSSES, LEVELS, CAMPAIGN_HEROES } from 'ZU/campaign/data';
import * as ROLES from 'ZU/game/roles';
import { ITEMS, ROUNDS_TO_WIN, ARMORY_SECS, FIRST_ARMORY_SECS, ROUND_SECS, POWER_ROUNDS, START_CASH, MAX_ITEMS, powersFor } from 'ZU/game/stadium';
import { TIERS, TIER_COLOR, PLACEMENTS } from 'ZU/game/ranks';
import { QUICK_MELEE } from 'ZU/game/weapons';

const out = process.argv[2];
if (!out) throw new Error('usage: node export_data.mjs <out dir>');
mkdirSync(out, { recursive: true });

// functions don't survive JSON: drop them, keep everything else exactly as the game sees it
const plain = (v: unknown) => JSON.parse(JSON.stringify(v, (_k, x) => (typeof x === 'function' ? undefined : x)));
const write = (name: string, data: unknown) => {
  writeFileSync(join(out, name), JSON.stringify(plain(data), null, 1));
  console.log('wrote', name);
};

write('heroes.json', {
  heroes: HEROES, pilots: Object.values(PILOTS), robots: Object.values(ROBOTS),
  teams: { names: TEAM_NAME, colors: TEAM_COLOR },
  skins: Object.fromEntries(HEROES.map(h => [h.id, skinsFor(h.id, h.team)])),
  // Stadium powers carry an apply() function: export their names / descriptions, the C# side implements apply
  powers: Object.fromEntries(HEROES.map(h => [h.id, powersFor(h).map(p => ({ id: p.id, name: p.name, desc: p.desc }))])),
});
write('maps.json', { maps: MAPS });
write('campaign.json', { enemies: Object.values(ENEMIES), bosses: Object.values(BOSSES), levels: LEVELS, heroes: CAMPAIGN_HEROES });
write('rules.json', {
  roles: Object.fromEntries(Object.entries(ROLES).filter(([, v]) => typeof v !== 'function')),
  quickMelee: QUICK_MELEE,
  stadium: { items: ITEMS, roundsToWin: ROUNDS_TO_WIN, armorySecs: ARMORY_SECS, firstArmorySecs: FIRST_ARMORY_SECS, roundSecs: ROUND_SECS,
    powerRounds: POWER_ROUNDS, startCash: START_CASH, maxItems: MAX_ITEMS },
  ranks: { tiers: TIERS, tierColors: TIER_COLOR, placements: PLACEMENTS },
});
