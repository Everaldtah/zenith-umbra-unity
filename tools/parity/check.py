"""Parity check: what the TypeScript game (~/Projects/zenith-umbra, the source of truth) has that this Unity port lacks.

Reads both source trees (no engine needed) and reports, per area, every TS item without a Unity counterpart:
  - fx event kinds the TS draws (Fx.ts onEvent + the hero showpieces) vs the kinds MatchFx / AbilityFx handle
  - projectile colours (FXCOL), personas (Animator PERSONA), held weapons (HeldProps HELD + the special-cased heroes)
  - voice-line keys the TS speaks (Soundscape / Game) vs the keys MatchAudio speaks
  - TS render / client source files vs the Unity files that name them as their port ("port of X.ts", "the TS X.ts")
Usage: python tools/parity/check.py [--ts ~/Projects/zenith-umbra] [--out docs/parity-report.md]   exit 1 if anything is missing
"""
import argparse, os, re, sys, glob

ap = argparse.ArgumentParser()
ap.add_argument('--ts', default=os.path.expanduser('~/Projects/zenith-umbra'))
ap.add_argument('--out', default='docs/parity-report.md')
args = ap.parse_args()
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
TS = os.path.abspath(args.ts)


def read(p):
    with open(p, encoding='utf-8') as f:
        return f.read()


def cs_files(*parts):
    return glob.glob(os.path.join(ROOT, 'Assets', 'ZU', *parts, '**', '*.cs'), recursive=True)


def cs_text(*parts):
    return '\n'.join(read(p) for p in cs_files(*parts))


report, missing_total = [], 0


def section(title, ts_items, unity_items, note=''):
    global missing_total
    miss = sorted(set(ts_items) - set(unity_items))
    missing_total += len(miss)
    report.append(f'## {title}\n')
    if note:
        report.append(note + '\n')
    report.append(f'TS {len(set(ts_items))}, Unity has {len(set(ts_items) & set(unity_items))}, missing {len(miss)}\n')
    if miss:
        report.append('Missing: ' + ', '.join(f'`{m}`' for m in miss) + '\n')


# ---- fx kinds: every `case 'x'` in the TS Fx.ts onEvent switch and the showpieces that consume events themselves
fx_ts = read(os.path.join(TS, 'src', 'render', 'Fx.ts'))
ts_kinds = set(re.findall(r"case '([a-z0-9_]+)'", fx_ts))
for extra in ['SealStorm.ts', 'SpiritDragon.ts', 'ChainCage.ts', 'PuppetSwarm.ts']:
    p = os.path.join(TS, 'src', 'render', extra)
    if os.path.exists(p):
        ts_kinds |= set(re.findall(r"(?:case|kind === ) ?'([a-z0-9_]+)'", read(p)))
unity_fx = cs_text('Game', 'Fx') + cs_text('Game', 'AbilityFx')
unity_kinds = set(re.findall(r'case "([a-z0-9_]+)"', unity_fx)) | set(re.findall(r'kind == "([a-z0-9_]+)"', unity_fx))
section('Effect kinds (Fx.ts and the showpieces)', ts_kinds, unity_kinds,
        'Each TS fx event kind should have a Unity case (MatchFx, AbilityFx) - a missing one falls into the generic burst.')

# ---- projectile colours
fxcol_ts = dict(re.findall(r"(\w+): '(#[0-9a-f]{6})'", re.search(r'const FXCOL[^{]*\{(.*?)\};', fx_ts, re.S).group(1)))
pv = read(os.path.join(ROOT, 'Assets', 'ZU', 'Game', 'ProjectileViews.cs'))
fxcol_u = dict(re.findall(r'\{ "(\w+)", "(#[0-9a-f]{6})" \}', pv))
section('Projectile colours (FXCOL)', [f'{k}={v}' for k, v in fxcol_ts.items()], [f'{k}={v}' for k, v in fxcol_u.items()])

# ---- personas (hero -> the 12 numbers)
anim_ts = read(os.path.join(TS, 'src', 'render', 'Animator.ts'))
def nums(s):
    return tuple(round(float(x), 4) for x in re.findall(r'-?\d+(?:\.\d+)?', s))
per_ts = {h: nums(body) for h, body in re.findall(r"^\s+(\w+): \{ (weight: [^}]*)\},", anim_ts, re.M)}
proc = cs_text('Game', 'Anim')
per_u = {h: nums(body) for h, body in re.findall(r'\{ "(\w+)", new Persona\(([^)]*)\) \}', proc)}
section('Hero personas (Animator PERSONA)', [f'{h}{v}' for h, v in per_ts.items()], [f'{h}{v}' for h, v in per_u.items()],
        'Each hero\'s stance, weight, carriage and aim springs, number for number.')

# ---- held weapons: the TS HELD table plus the heroes CharacterView special-cases
held_ts = read(os.path.join(TS, 'src', 'render', 'HeldProps.ts'))
m = re.search(r'export const HELD[^{]*\{(.*?)\n\};', held_ts, re.S)
ts_held = set(re.findall(r'^  (\w+): \{', m.group(1), re.M)) if m else set()     # (top-level keys only)
cv = read(os.path.join(TS, 'src', 'render', 'CharacterView.ts'))
ts_held |= set(re.findall(r"actor\.def\.id === '(\w+)' && anim\.ok", cv))
hp = read(os.path.join(ROOT, 'Assets', 'ZU', 'Game', 'HeldProps.cs'))
u_held = set(re.findall(r'\{ "(\w+)", new HeldSpec', hp))
section('Held weapons (HeldProps HELD + special-cased heroes)', ts_held, u_held)

# ---- voice-line keys the TS speaks
sound_ts = read(os.path.join(TS, 'src', 'client', 'Soundscape.ts')) + read(os.path.join(TS, 'src', 'client', 'Game.ts'))
ts_keys = set(re.findall(r"voice\.say\([^,]+, (?:\w+ \? )?'(\w+)'", sound_ts)) | set(re.findall(r"voice\.announce\('(\w+)'\)", sound_ts))
audio_u = cs_text('Game', 'Audio')
u_keys = set(re.findall(r'"(\w+)"', audio_u))
section('Voice lines spoken (Soundscape / Game)', ts_keys, u_keys)

# ---- source files: each TS render / client / audio file should have a Unity file naming it as its port
ts_files = []
for d in ['render', 'client', 'audio']:
    ts_files += [os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(TS, 'src', d, '*.ts'))]
unity_all = cs_text('Game') + cs_text('Editor') + cs_text('Dynamics')
named = {f for f in ts_files if re.search(rf'\b{re.escape(f)}\.ts\b', unity_all)}
# deliberately not ported - Unity's own systems do their job: the asset loader (Resources / prefabs), the skeleton's bone
# names (the humanoid Avatar's bone map) and retargeting (Mecanim humanoid)
NOT_APPLICABLE = {'Assets': 'Resources + prefabs', 'Rig': 'the humanoid Avatar bone map', 'Retarget': 'Mecanim humanoid retargeting'}
section('TS view / client / audio files with a named Unity port', ts_files, named | set(NOT_APPLICABLE),
        'A Unity file names its TS source in its header ("port of X.ts"); a file nobody names is unported or only partly ported. '
        'Not applicable (Unity does it): ' + ', '.join(f'{k}.ts ({v})' for k, v in NOT_APPLICABLE.items()) + '.')

# ---- intentional Unity divergences (each the user's call): listed so a reader sees them, never counted as gaps (the TS
# effect kinds they stop using - Yuzu's arrowsmark / arrowhit - keep their MatchFx cases, so nothing above goes missing)
DIVERGENCES = {
    'Yuzu - Hundred Suns': 'the user\'s rework, 2026-10-03: five giant sword-arrows land in a ring, then a 1000-arrow swarm hunts '
                           'every enemy within 30 m for 15 s (Sim Abilities.Hundredsuns + the sunswarm tick, AbilityFx/SunSwarm.cs, '
                           'ult charge 2400 in Sim/Data/UnityDivergence.cs); the web keeps the TS 3 s arrow rain',
}
report.append('## Intentional Unity divergences\n')
report.append('Not gaps: the Unity edition differs here on purpose.\n')
for k, v in DIVERGENCES.items():
    report.append(f'- **{k}**: {v}\n')

os.makedirs(os.path.dirname(os.path.join(ROOT, args.out)), exist_ok=True)
with open(os.path.join(ROOT, args.out), 'w', encoding='utf-8', newline='\n') as f:
    f.write('# TS -> Unity parity report\n\nGenerated by tools/parity/check.py from the two source trees.\n\n' + '\n'.join(report))
print('\n'.join(report))
print(f'TOTAL missing: {missing_total}  ->  {args.out}')
sys.exit(1 if missing_total else 0)
