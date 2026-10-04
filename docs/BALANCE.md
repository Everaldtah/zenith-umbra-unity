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

## The shipped table (second tune of 2026-10-04), measured on 800 unseen games

The table in `BalanceTable.cs` is the second tune of 2026-10-04 (600 games a pass, 11 passes, Yuzu x2 in the pool). It
ships in 0.2.2 (rebuild), 0.2.3 and 0.2.4. Win rate at equal bot skill on the same 800 unseen games (seed 1001, 150 s a
game), without and with the table; one hero plays 390-660 of them, so a figure has a standard error of 0.02-0.025:

| hero | role | target | no table | damage dealt | damage taken | with the table | |
|---|---|---|---|---|---|---|---|
| Yuzu | damage | 0.60 | 0.87 | x2 (pinned) | x1.5 | 0.70 | over by 0.10: her damage is the user's x2 |
| Tenkai-Oh | tank | 0.60 | 0.56 | x1.129 | 1 | 0.61 | on target |
| Hayate | damage | 0.60 | 0.60 | x1.132 | 1 | 0.58 | on target (within the noise) |
| Raijin | damage | 0.60 | 0.34 | x1.6 | x0.5 | 0.41 | under by 0.19 with both scalars at their clamps: his bot, not his numbers |
| Seiran | damage | 45-55 % | 0.42 | x1.6 | x0.921 | 0.47 | in the band |
| Enra | damage | 45-55 % | 0.40 | x1.6 | x0.5 | 0.45 | in the band, at both clamps |
| Kagemaru | damage | 45-55 % | 0.40 | x1.6 | 1 | 0.40 | 0.05 under the band, damage at its clamp |
| Mirei | support | 45-55 % | 0.58 | x0.85 | x1.5 | 0.60 | 0.05 over the band, at both clamps |
| Kaien | support | 45-55 % | 0.47 | x0.876 | 1 | 0.49 | in the band |
| Nocturne | support | 45-55 % | 0.44 | x1.445 | 1 | 0.49 | in the band |
| Hex | support | 45-55 % | 0.48 | x0.85 | x1.157 | 0.47 | in the band |
| Hibiki | support | 45-55 % | 0.53 | x0.85 | x1.096 | 0.45 | in the band (its lower edge) |
| Tomoe | tank | 45-55 % | 0.46 | x0.979 | 1 | 0.48 | in the band |
| Gantetsu | tank | 45-55 % | 0.58 | x0.917 | x1.11 | 0.47 | in the band |
| Gorgoth | tank | 45-55 % | 0.40 | x1.583 | 1 | 0.44 | 0.01 under the band |

Eight of the eleven band heroes are inside 45-55 % (Gorgoth 0.01 under, Kagemaru 0.05 under, Mirei 0.05 over). Of the
four 60 % heroes Tenkai-Oh and Hayate are on target, Yuzu is above (0.70), Raijin far below (0.41). Without the table
four of the eleven are in the band and the spread over all fifteen is 0.34-0.87; with it 0.40-0.70.
(The same table on the 600 games of its own tune read nine of eleven; Mirei's 0.53 there and 0.60 here is the largest
difference between the two samples.)

### A third tune was run and not shipped (2026-10-04, 800 games a pass, 16 passes)

It let the pinned hero's damage taken go to x2 (the shipped table stops at x1.5) and ran longer. On the same 800 games:

| | shipped (tune 2) | tune 3 | tune 3 with Yuzu's taken back at x1.5 |
|---|---|---|---|
| band heroes inside 45-55 % | 8 of 11 | 8 of 11 | 9 of 11 |
| Yuzu / Hayate / Tenkai-Oh / Raijin (target 0.60) | 0.70 / 0.58 / 0.61 / 0.41 | 0.68 / 0.55 / 0.60 / 0.38 | 0.72 / 0.53 / 0.58 / 0.40 |
| outside the band | Kagemaru 0.40, Mirei 0.60, Gorgoth 0.44 | Kagemaru 0.41, Mirei 0.56, Tomoe 0.445 | Kagemaru 0.40, Mirei 0.56 |
| sum of the misses (60 % heroes + band edges) | 0.43 | 0.40 | 0.47 |

The three are the same within the noise (0.02 a hero). Tune 3 pays for its 0.03 with larger multipliers - Yuzu takes
x2 damage instead of x1.5, Hex x1.35 instead of x1.16, Hibiki x1.27 instead of x1.10 - which a person playing those
heroes feels in every fight, so the shipped table stays. What the run did show: **doubling the damage Yuzu takes moves
her win rate by 0.02-0.04** (0.70-0.72 at x1.5, 0.68 at x2). She wins from range before she is reached; only her damage
(the user's x2) brings her to 0.60. And Raijin does not move with anything: 0.38-0.41 at the clamps in all three.

## What the multipliers mean for a person playing the hero

The table is tuned on bots of equal skill. A bot plays some heroes badly, and the table pays for that with numbers -
which a person who plays the hero well keeps:

- **Raijin and Enra deal x1.6 and take x0.5.** Their bots walk into melee range of a whole team, die about 28 times per
  10 minutes each, and Raijin's bot rarely finds three enemies under open sky for his ultimate; even with these numbers
  their bots win 0.41 and 0.45. A person who picks their fights has a hero that hits 60 % harder and is twice as hard to
  kill as in the web game: **expect both to be overpowered in players' hands.** The user's decision for 0.2.4
  (2026-10-04): ship it so, and do not change the bots (`Sim/AI/Bot.cs`) in this release.
- Kagemaru, Seiran (x1.6), Gorgoth (x1.58) and Nocturne (x1.45) also deal more than in the web game; Seiran takes x0.92.
- Mirei and Yuzu take x1.5; Hex x1.16, Gantetsu x1.11, Hibiki x1.10. Mirei, Hex and Hibiki deal and heal x0.85.
- Everything goes back to the web game's numbers with `-zu-balance=0` on the command line.

The numbers-only way is used up: every hero outside its target is at a clamp (Raijin, Enra, Kagemaru, Mirei) or within
0.01 of the band. The next step is the bots - Raijin's and Enra's approach (range, retreat, when to dive) - after which
their x1.6 / x0.5 can come back toward 1; and Yuzu's 0.60 needs her damage under x2, which is the user's number.

## Pinned by the user: Yuzu x2

"put yuzu at 2x" (2026-10-04, after x5 - 0.97 of her games won - and x3 - 0.92): Dawnshot 125 -> 250, Hundred Suns
landings 100 -> 200 and the swarm 24 -> 48 damage a second (`UnityDivergence.YuzuPower`). The lab never moves her
damage; it moves her toughness toward her 0.60 target (x1.5 taken, its clamp - and x2 would not bring her there, see
the third tune above).

## The headless tests and the table

- `run.ps1 training` asserts the kits' own numbers (a 100-damage hit is 100), so it runs with the table off: 143 checks.
- `run.ps1 aimatch` as shipped (the table on, Yuzu x2): 8 of 8 maps.

## After any kit change

Rerun `balance tune`, commit the regenerated `BalanceTable.cs` and the two census files, and read the heroes at a
clamp: a clamp is a kit or a bot to fix, not a number to widen.
