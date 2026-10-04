# Play of the Game, the kill cam and highlights: how Overwatch does it, and what this game does

Asked for by the user on 2026-10-04 for 0.2.4 (Unity only; the web game has none of the three).

## How Overwatch picks its Play of the Game

Sources: Blizzard's U.S. patent 10,456,680, "Determining play of the game based on gameplay events" (summary at
patentarcade.com/video-game-patent/US10456680); the coverage of it (thegamer.com, tweaktown.com, dotesports.com
"How does Overwatch decide Play of the Game"); Jeff Kaplan's and Rowan Hamilton's remarks on it.

- The server keeps a log of gameplay events (damage, healing, kills, abilities used, objective events), each with a
  time and its participants.
- Every event is scored in one of four categories:
  - **High Score**: kills in quick succession. A multikill is worth more than its count; a solo kill and an
    environmental kill are worth more; a kill made with an ultimate is worth less (they would win every time);
    being on the objective adds.
  - **Lifesaver**: saving a teammate from nearly certain death (the attacker killed or stopped in time).
  - **Sharpshooter**: the difficulty of a shot: distance, target or shooter in the air, a critical hit, target speed.
  - **Shutdown**: stopping an enemy as it uses a powerful ability, above all an ultimate; more with teammates in
    its reach.
- A sliding window as long as the replay passes over the log; for each player and category the scores inside the
  window are added; the best window in each category is found.
- High Score is the default category; another takes the play when it scores higher (and High Score below a
  threshold gives way).
- The replay is that window, played back from the player's view.

## How Overwatch keeps highlights

Sources: pcgamer.com "You can now save your Overwatch highlights", blizzardwatch.com (June 2017), sportskeeda.com
"How to save Overwatch 2 gameplay highlights", gameskinny.com "Overwatch 2 highlights location".

- Highlights are replay data re-rendered by the game, not stored video.
- "Today's Top 5" is generated automatically and lasts 24 hours (or until a patch); "Recently Captured" holds up to 36
  twelve-second highlights the player asked for.
- "Record" renders a highlight to a video file at a chosen resolution, up to 4K at 60 frames a second whatever the
  graphics settings, into Documents\Overwatch\videos.

## What this game does

| Piece | File | Notes |
|---|---|---|
| Detector | `Assets/ZU/Sim/Game/Plays.cs` | Listens on `World.taps`; the four categories plus an Impact fallback; a 10 s window; `Best()` for everyone or one player. Headless test: `tools/simtest` `plays`. |
| Record | `Assets/ZU/Game/AbilityFx/KillCam.cs`, `PlayClip.cs` | 20 s of frozen hero and projectile copies at 30 Hz plus the effect and sound events; `Cut`, `Play`, `Save`, `Load`, `Watch`. |
| Kill cam | `KillCam.cs` | On the local player's death: the seconds before it, from behind the killer's shoulder. `-zu-killcam=0`. |
| End of match | `Assets/ZU/Game/PlayOfTheGame.cs` | Card, then the clip, then the results. `-zu-potg=0`. |
| Highlights | `Assets/ZU/Game/Highlights.cs`, Career > History | The player's best play of every match. 48 hours, 10 at most (the user's rule). |
| Video | `Highlights.cs` | 1080p / 1440p / 4K at 60 fps: MP4 with ffmpeg, else Motion-JPEG AVI. `Videos\ZENITH UMBRA\Highlights`. No sound yet. |

### The scores

| Event | Category | Points |
|---|---|---|
| Elimination | High Score | 100; x1.25 solo; x1.4 environmental; x0.8 with an ultimate running; +30 on the objective; +40 within 2.5 s of the last one; the nth kill of a window counts x(1 + 0.5 (n - 1)) |
| A mech's frame broken | High Score | 70 with the same factors |
| Kill on an enemy that cast its ultimate within 4 s | Shutdown | 240; +80 with two teammates within 15 m |
| A hero counter | Shutdown | 160 |
| Kill on an enemy that hit a teammate at 35 % health or less within 2 s | Lifesaver | 150 to 250 by how low |
| A revive | Lifesaver | 220 |
| A heal of 40 or more on a teammate under 30 % and under fire | Lifesaver | 60 |
| A weapon kill at 18 m or more, on an airborne or fast target, from the air, or critical | Sharpshooter | 80 + up to 330; only the best shot of a window counts in full |
| Damage and healing | Impact | half a point each; never the Play of the Game, only a player's fallback best moment |

Bars to be the Play of the Game: High Score 250 (a double kill), Shutdown 240, Lifesaver 200, Sharpshooter 190. With
nothing over a bar the highest of the four wins; with none of the four, the window of greatest impact.
Training dummies and campaign minions count 0.3, a campaign boss 3.
