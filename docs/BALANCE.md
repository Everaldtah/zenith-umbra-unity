# Balance (Unity edition)

Asked for by the user on 2026-10-04: "every character needs to go through a balancing system that makes sure no
character is stronger than the other and pure skill is what makes a character stronger". The kits' numbers are the web
game's October 2026 balance patch (its `docs/BALANCE_PATCH.md` and `docs/research/ow_balance_study.md`: the Overwatch
role framework - role passives, sub-roles, health / armor / ultimate-cost bands). This adds the system that checks the
result hero by hero and corrects what is out of line.

## The lab: `tools/simtest` `balance`

    powershell -File tools/simtest/run.ps1 balance census [--games 400] [--secs 150] [--seed 1] [--workers 6] [--yuzu 5]
    powershell -File tools/simtest/run.ps1 balance tune   [--games 600] [--iters 10]

- Every game draws a role-locked line-up (one tank, two supports, two damage a side) from the whole roster regardless
  of faction, on each map in turn, and plays it twice with the sides swapped. Over a few hundred games every hero has had
  every other as ally and enemy on both spawns: what is left in its win rate is the hero. (The web census fixed the
  line-ups by faction and map, so a hero's numbers were partly its team's.)
- Bots at one skill setting are the instrument. The lab finds kits that are out of line at equal skill; it does not
  say what a person will do with a hero. A bot that wins too much shows real kit power; a bot that loses may just be a
  poor bot - so the correction is lopsided (below).
- `tune` moves each hero's power scalar against its win-rate error, replays the same games, and repeats until every
  hero is inside 45-55 % or its scalar is at the clamp. It then checks the table on games it never saw
  (`docs/balance-validation.json`) and measures the game as shipped (`docs/balance-census.json`).

## The table: `Assets/ZU/Sim/Data/Balance.cs` + generated `BalanceTable.cs`

One scalar per hero on all the damage it deals and all the healing it does (its summons' too), applied in
`World.Damage` / `World.Heal`. Range x0.70 .. x1.15. **Off switch: `-zu-balance=0`** on the command line (player,
Editor, headless tools): every scalar reads 1 and the simulation is bit for bit what it was (checked: `aimatch` with
the switch and `--yuzu 1` prints the same kills / deaths / damage / healing for every hero on all eight maps as the
tree before the table). Online, both ends must run with the same setting.

## Result of the first tune (2026-10-04, 600 games an iteration, 10 iterations)

Win rate at equal bot skill, before (no table) and after (the table, on unseen games), Yuzu at her original numbers:

| hero | role | before | scalar | after | |
|---|---|---|---|---|---|
| Hayate | damage | 0.79 | x0.70 | 0.68 | at the clamp: the kit needs its own change (the seeking shuriken / Dragon Gate Blade) |
| Seiran | damage | 0.48 | x0.914 | 0.53 | |
| Kagemaru | damage | 0.51 | x0.923 | 0.49 | |
| Enra | damage | 0.38 | x1.15 | 0.42 | at the clamp: bot or kit |
| Raijin | damage | 0.26 | x1.15 | 0.33 | at the clamp: his bot dies 32 times / 10 min and almost never ults |
| Mirei | support | 0.61 | x0.70 | 0.60 | at the clamp: her value is the rebirth / barrier, which the scalar doesn't touch |
| Hibiki | support | 0.56 | x0.821 | 0.54 | |
| Kaien | support | 0.47 | x1.013 | 0.50 | |
| Hex | support | 0.43 | x1.07 | 0.48 | |
| Nocturne | support | 0.42 | x1.15 | 0.39 | at the clamp |
| Gantetsu | tank | 0.64 | x0.718 | 0.54 | |
| Tomoe | tank | 0.54 | x0.898 | 0.53 | |
| Tenkai-Oh | tank | 0.60 | x0.83 | 0.49 | |
| Gorgoth | tank | 0.24 | x1.15 | 0.44 | at the clamp |

Spread before: 0.24 .. 0.79. After: nine of fourteen inside 0.45 .. 0.55; five at a clamp, listed for kit work.

## Pinned by the user: Yuzu x5

"make yuzu 5 times stronger" (2026-10-04): Dawnshot 125 -> 625, Hundred Suns landings 100 -> 500 and the swarm
24 -> 120 damage a second (`UnityDivergence.YuzuPower`). The lab never tunes a pinned hero; it measures her. As shipped
she wins 0.97 of her games with 197 kills per 10 minutes (K/D 38): the two requests pull against each other, and the
choice is the user's. `--yuzu 1` measures the roster with her original numbers (0.54).

## After any kit change

Rerun `balance tune`, commit the regenerated `BalanceTable.cs` and the two census files, and read the clamped heroes:
a clamp is a kit or a bot to fix, not a number to widen.

## The headless tests and the table

- `run.ps1 training` asserts the kits' own numbers (a 100-damage hit is 100), so it runs with the table off.
- `run.ps1 aimatch` is green with the table on and `--yuzu 1` (8 of 8 maps). With Yuzu x5 her side wins in about 40
  seconds, before the test's "more than 20 casts" mark, so 3-4 maps print FAIL for that reason alone: the test is left as
  it is, because that is what x5 does to a match.
