# Porting the ZENITH//UMBRA simulation from TypeScript to C#

The TypeScript game (`~/Projects/zenith-umbra`, read-only for us) is the source of truth. Its simulation is pure TS with
no rendering. We port it file by file into `Assets/ZU/Sim/` as plain C# (no `UnityEngine` references), so it compiles:
- inside Unity (assembly `ZU.Sim`)
- headless with the .NET 8 SDK that ships inside the Unity editor (`tools/simtest`)

The C# simulation must behave exactly like the TS one. Parity tests replay the same seeded scenario in both and compare
every actor's state tick by tick (`tools/parity`).

## Rules

1. **Same structure, same names.**
   - One TS file becomes one C# file; a huge TS file becomes `partial` files.
   - Keep function order, variable names and comments; constants keep their TS names in `UPPER_CASE`.
   - TS `camelCase` methods become `PascalCase`; fields keep their TS names: `a.hp`, `a.sv`, `w.time`.
   A reader must be able to put the two files side by side.
2. **Numbers are `double`**, never `float`. JS numbers are doubles, so the parity tests can only pass with doubles.
3. **Randomness:** every `Math.random()` becomes `Rng.Random()` (seeded, shared). Never use `System.Random` directly.
4. **JS semantics to watch:**
   - `x ?? y` on a missing dictionary key: use `a.Sv("key", y)` / `a.St("key", y)`, or `dict.TryGetValue`.
   - `a.sv.foo = 1` becomes `a.sv["foo"] = 1`; `delete a.sv.foo` becomes `a.sv.Remove("foo")`.
   - `!!x` / truthiness: `undefined`, `null`, `0`, `NaN` and `""` are all false. Write the explicit test.
   - `Math.round` rounds .5 up (toward +infinity): use `JsMath.Round(x)`.
   - `Math.max()` with no args, `-Infinity`/`Infinity`: use `double.NegativeInfinity` / `double.PositiveInfinity`.
   - `arr.filter/map/find/some/every/sort`: use LINQ or loops. `sort` must be stable with the same comparator
     (use `JsSort.Sort(list, cmp)`, a stable sort).
   - `Array.prototype.indexOf` on doubles is fine; `for...of` over a copy (`[...list]`) becomes `foreach (var x in list.ToList())`.
   - Object spread `{ ...pos }` of a V3 is a plain copy (V3 is a struct).
5. **V3 is a struct.** TS code that mutates a V3 in place (`p.x += ...`) on an object field must write back:
   `a.pos.x += ...` works on a field. A V3 returned from a getter or method is a copy.
6. **Optional fields** in data (`?` in TS) are nullable in `Defs.cs` (`double?`, `int?`): `w.ammo ?? 0` becomes
   `w.ammo ?? 0`, the same.
7. **Unions** (`WeaponDef | AbilityDef`) are one `SlotDef` class: `isAbility(x)` is `x.IsAbility`.
8. **Events** (`GameEvent`) are `SimEvent` subclasses: `SfxEvent`, `FxEvent`, `DmgEvent`, `KillEvent`, `DemechEvent`,
   `CastEvent`, `CounterEvent`, `MsgEvent`. `w.Fx(kind, pos, new FxOpts { color = ..., actor = ... })`.
9. **Closures** (`w.after(0.5, () => ...)`, `forced.onEnd`) become C# lambdas (`Action`).
10. **No new behaviour.** If the TS looks like a bug, port it as it is and add a `// TS-PARITY:` comment. Fixes come
    later, in both codebases.
11. **Compile before you finish:** `tools/simtest/build.ps1` must report 0 errors.

## Where things live

| TS | C# |
|---|---|
| src/engine/Physics.ts | Sim/Core/Level.cs (`ILevel`, `BoxLevel`) |
| src/game/Actor.ts | Sim/Game/Actor.cs |
| src/game/World.ts | Sim/Game/World*.cs (partial: Core, Combat, Projectiles, Move, Objectives) |
| src/game/weapons.ts | Sim/Game/Weapons.cs |
| src/game/abilities.ts | Sim/Game/Abilities*.cs (partial static class, one file per hero group) |
| src/game/{puppets,susanoo,effigy,rebirth,roles,ranks,stadium,setup}.ts | Sim/Game/<Name>.cs |
| src/ai/{Bot,Nav}.ts | Sim/AI/Bot.cs, Sim/AI/Nav.cs |
| src/campaign/{Director,data}.ts | Sim/Campaign/Director.cs |
| src/data/*.ts | JSON in Assets/ZU/Data (tools/export_data.mjs), loaded by Sim/Data/GameData.cs |
