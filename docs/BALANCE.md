# Balance (Unity edition)

Asked for by the user on 2026-10-04: "every character needs to go through a balancing system that makes sure no
character is stronger than the other and pure skill is what makes a character stronger" - then, shown the first
results: Yuzu, Hayate, Tenkai-Oh and Raijin are the strongest on purpose at a 60 % win rate, every other hero inside
45-55 %, reached by buffing the weak heroes rather than cutting the rest. The kits' numbers are the web game's October
2026 balance patch (its `docs/BALANCE_PATCH.md` and `docs/research/ow_balance_study.md`: the Overwatch role framework).
This adds the system that measures the result hero by hero and corrects what is out of line.

## The lab: `tools/simtest` `balance`

    powershell -File tools/simtest/run.ps1 balance census [--games 400] [--secs 150] [--seed 1] [--workers 6] [--yuzu 2]
    powershell -File tools/simtest/run.ps1 balance tune   [--games 480] [--iters 12] [--workers 8]

- Every game draws a role-locked line-up (one tank, two supports, two damage a side) from the whole roster regardless
  of faction, on each map in turn, and plays it twice with the sides swapped. Over a few hundred games every hero has had
  every other as ally and enemy on both spawns: what is left in its win rate is the hero. (The web census fixed the
  line-ups by faction and map, so a hero's numbers were partly its team's.)
- Bots at one skill setting are the instrument. The lab finds kits that are out of line at equal skill; it does not
  say what a person will do with a hero. **A hero whose bot plays it badly (Raijin and Enra dive and die) gets a large
  buff to reach its target: in a person's hands that hero will be stronger than its bot win rate says.**
- Targets (`TARGET` in BalanceLab.cs): the four strongest at 0.60; the others share what the zero-sum leaves, 0.467.
- `tune` moves each hero's power against its error, replays the same games, and repeats. Where power is at its clamp
  (or pinned), the hero's toughness moves instead. It writes the table after every pass, then measures the roster with
  no table (`docs/balance-before.json`) and with the table (`docs/balance-census.json`) on the same unseen games.

## The table: `Assets/ZU/Sim/Data/Balance.cs` + generated `BalanceTable.cs`

Per hero: **power** on all the damage it deals and all the healing it does (its summons' too), x0.85 .. x1.6, and
**taken** on every hit it takes, x0.5 .. x1.5 (below 1 = tougher). Applied in `World.Damage` / `World.Heal`.
**Off switch: `-zu-balance=0`** on the command line (player, Editor, headless tools): every scalar reads 1 and the
simulation is bit for bit what it was (checked after every change: `aimatch -zu-balance=0 --yuzu 1` prints the same
kills / deaths / damage / healing for every hero on all eight maps as the tree before the table). Online, both ends
must run with the same setting.

## Result of the tune of 2026-10-04 (480 games a pass, 12 passes, calibrated with Yuzu x2 in the pool)

Win rate at equal bot skill on the same 480 unseen games, without and with the table:

| hero | role | target | before | power | taken | after | |
|---|---|---|---|---|---|---|---|
| Yuzu | damage | 0.60 | 0.86 | x2 (pinned) | x1.5 | 0.72 | over by 0.12: her damage is the user's x2; toughness is at its clamp |
| Hayate | damage | 0.60 | 0.60 | x1.081 | 1 | 0.60 | on target |
| Tenkai-Oh | tank | 0.60 | 0.56 | x1.292 | 1 | 0.68 | over by 0.08 (0.58 on the tuning games: within the noise of 240 games) |
| Raijin | damage | 0.60 | 0.32 | x1.6 | x0.5 | 0.43 | under by 0.17 with both scalars at their clamps: his bot, not his numbers |
| Enra | damage | 0.467 | 0.38 | x1.6 | x0.5 | 0.47 | in the band, at both clamps |
| Seiran | damage | 0.467 | 0.45 | x1.6 | 1 | 0.44 | 0.01 under the band |
| Kagemaru | damage | 0.467 | 0.38 | x1.523 | 1 | 0.35 | under: still moving when the passes ran out |
| Hibiki | support | 0.467 | 0.52 | x0.869 | 1 | 0.53 | in the band |
| Mirei | support | 0.467 | 0.59 | x0.85 | x1.5 | 0.52 | in the band, at both clamps |
| Kaien | support | 0.467 | 0.50 | x0.982 | 1 | 0.52 | in the band |
| Hex | support | 0.467 | 0.51 | x0.891 | 1 | 0.51 | in the band |
| Nocturne | support | 0.467 | 0.40 | x1.49 | 1 | 0.42 | 0.03 under the band |
| Gantetsu | tank | 0.467 | 0.57 | x0.861 | x1.11 | 0.51 | in the band |
| Tomoe | tank | 0.467 | 0.50 | x0.893 | 1 | 0.44 | 0.01 under the band |
| Gorgoth | tank | 0.467 | 0.36 | x1.6 | 1 | 0.37 | under by 0.08: power at its clamp, toughness not yet moved |

Six of the eleven band heroes are inside 45-55 %, two are within 0.01 of it, three are short (Kagemaru, Nocturne,
Gorgoth). Of the four 60 % heroes Hayate is on target, Tenkai-Oh and Yuzu are above it, Raijin is far below.
One hero's win rate on 240-330 games has a standard error of about 0.03.

Still to do (the next tune): more passes and more games a pass (Kagemaru, Gorgoth and Tenkai-Oh were still moving);
Raijin's and Enra's bots (they dive and die: 28-40 deaths per 10 minutes), which no scalar fixes; Yuzu at x2 needs
either more than x1.5 damage taken or less than x2 damage to come down to 0.60 - the user's call.

## Pinned by the user: Yuzu x2

"put yuzu at 2x" (2026-10-04, after x5 - 0.97 of her games won - and x3 - 0.92): Dawnshot 125 -> 250, Hundred Suns
landings 100 -> 200 and the swarm 24 -> 48 damage a second (`UnityDivergence.YuzuPower`). The lab never moves her
damage; it moves her toughness toward her 0.60 target.

## The headless tests and the table

- `run.ps1 training` asserts the kits' own numbers (a 100-damage hit is 100), so it runs with the table off: 143 checks.
- `run.ps1 aimatch` as shipped (the table on, Yuzu x2): 8 of 8 maps.

## After any kit change

Rerun `balance tune`, commit the regenerated `BalanceTable.cs` and the two census files, and read the heroes at a
clamp: a clamp is a kit or a bot to fix, not a number to widen.
