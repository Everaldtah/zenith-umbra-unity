# ZENITH//UMBRA Unity: audio standard

The rules every sound in the Unity build follows, why, and the tool that checks each one. Owner: the sound engineer
session (2026-10: evera-eb). Code: `Assets/ZU/Game/Audio`, tools: `tools/audio`.

## 1. Nothing clips, nothing clicks

A scratchy, crackling fight is almost always one of these, so each is closed off at the source.

| cause | fix | where |
|---|---|---|
| The summed mix passes full scale and the output clips | A master chain on the listener: soft-knee glue compressor, then a look-ahead **true-peak** limiter (4x polyphase interpolation, 2 ms look-ahead, ceiling **-1 dBTP**), then a safety clamp | `MasterChain.cs`, `MasterBus.cs` |
| Unity's real-voice limit is lower than the pool, so Unity cuts the overflow mid-wave | Real voices 80 ≥ pool 72 > budget 44; `AudioSource.priority` by category | `ProjectSettings/AudioManager.asset`, `AudioKit.cs` |
| A voice is stolen with a hard stop | Steal only faded or near-finished, important-only; the oldest of a sound fades out over ~12 ms | `AudioKit.Free/Admit` |
| Import-time peak normalisation pushes every clip to 0 dBFS | `normalize: 0` on every clip; levels come from mastering | `tools/audio/import_settings.py` |
| A clip starts or ends off zero | 0.5 ms fade in, 8-12 ms fade out, tails cut 70 dB down | `tools/audio/master.py` |
| A loop restarts every frame (re-trigger clicks) | Loops get one frame of grace, so they can be refreshed from LateUpdate | `AudioKit.EndFrame` |
| Square or saw edges in synthesised layers | Synth layers use sines and filtered noise only | `tools/audio/design/synth.py` |
| A filter changed in steps (zipper noise) | Cutoffs and gains glide (one-pole smoothing per frame); time-varying filters run in the STFT domain | `AudioKit.Tick`, `synth.whoosh` |
| The audio thread starves on a main-thread hitch | SFX preloaded (decompress on load); the bank warms 6 ids a frame at start | `import_settings.py`, `AudioKit.Tick` |

**Test:** `tools/audio/dsptest` (9 cases: 30 stacked full-scale shots stay ≤ -1 dBTP, inter-sample overs, no limiter
clicks, cost per block). In a running game, the master meter (`zu_audio_diag`, and the Player.log line every 30 s)
must show **clips out 0**, and clicks in single digits.

## 2. Loudness

One-shots are levelled by the **RMS of their active part** (the 10 ms blocks within 20 dB of the loudest). A BS.1770
momentary reading averages 400 ms, so it under-reads anything shorter, which is most weapons, hits and steps. Voice lines
use max momentary loudness, and beds and loops use integrated loudness. Every clip stays at or under **-1 dBTP**. Reaching
the target may cost a capped amount of limiting: weapon 4 dB, impact 3, ability 2.5, step 2, feedback and move 1.5,
loop 1, bed 0.5. Past that, the clip sits under its target with its transient intact, and the mix's bus gains place it.

| category | one-shot active RMS | voice / bed / loop |
|---|---|---|
| weapon | -15 dBFS | |
| ability | -16 dBFS | |
| impact, feedback | -17 dBFS | |
| move | -21 dBFS | |
| step | -22 dBFS | |
| voice | | -16 LUFS-M |
| loop | | -20 LUFS-I |
| amb | | -26 LUFS-I |

The momentary targets below are what the categories sum to in play:

| category | target | why |
|---|---|---|
| weapon | -14 LUFS-M | the most important sound in an FPS; sits on top |
| ability | -15 | character and readability |
| impact, feedback, voice | -16 | voice must cut through without shouting over guns |
| move | -21 | foley: present, never in the way |
| step | -22 | enemy steps get +45% in the mix (threat), friends -45% |
| loop | -20 (integrated) | beams, flames, skates |
| amb | -26 (integrated) | the bed under everything |

The mix lands near **-23 to -18 LUFS integrated** for a full match: the console standard (ASWG-R001: -24 ±2) at its
loud end, which is right for a PC shooter on headphones. Night mode (Mix Preset NIGHT MODE) narrows the range.

**Test:** `python tools/audio/audit.py` measures every clip and flags any category more than 3 dB from its target.

## 3. Crisp means full band

A clip whose spectrum stops dead below 15 kHz is a band-limited render. The 2026-10 audit found 801 of 827 clips
like that: voices were 24 kHz renders, and the effects came from 16-24 kHz generators. Those clips sound dull and
"behind glass" next to anything recorded.

- Masters are 48 kHz with content to 20 kHz. The output runs at 48 kHz too (`m_SampleRate: 48000`), so nothing resamples.
- Voices: the original below 10.5 kHz plus an AudioSR-regenerated top octave above it, joined with a linear-phase
  crossover and level-matched. The cast voice stays exactly as it was.
- Effects: Stable Audio 3 Small-SFX (44.1 kHz stereo, licensed training data) bodies, plus synthesised transients.

**Test:** `audit.py` reports each clip's spectrum ceiling (`top`) and counts the dull ones per category.

## 4. Sound design

- **One-shots are dry, close, mono.** The game adds the room (Space.cs reverb plus wall reflections, more send with
  distance). A baked room would double up and smear the attack. A stereo clip in a 3D source only blurs its
  position. 2D beds keep their width.
- **Layering.** A weapon is body + transient crack (2-6 ms high-passed noise) + sub (a falling sine, heavy weapons
  only) + mechanical clicks. An impact is body + material crack + the debris that falls after it. Layers are set
  against the body's peak in `design/sounds.py`.
- **Variations.** 2-3 for weapons, which stay iconic and recognisable. 3-4 for impacts and steps, where repetition is
  what the ear catches. 1 for ults and beds. Never the same variation twice in a row; ±3% pitch at most.
- **Materials (the building clash).** World hits sound like what they hit: stone/wall, metal (trim, the hangar), glass
  (windows), wood, roof tile, dirt. Each falls back to stone or metal until its sound exists.
- **Beds** are 60 s seamless loops (a 7 s bed repeats audibly). Positional ambience uses `AmbientEmitter`: the nearest
  2 of each sound, 6 in all, fading out at their radius.
- **First person.** The sim's weapon sounds play in your head. The viewmodel adds foley between them through
  `AudioKit.PlayFp` (nock, quiver, aim), never a second shot.

## 5. The mix (play by sound)

These Overwatch "play by sound" pillars came over from the web game and stay:
- threat mixing (`MatchAudio.Threat`): the enemy aiming at you is louder than the one who isn't
- enemy footsteps louder than friendly ones
- voice ducks the world (critical lines duck more)
- enemy ult lines stay loud from anywhere
- air absorption with distance, occlusion behind walls, indoor/outdoor reverb, wall reflections

Settings > Sound > Mix Preset:
- DEFAULT and SPEAKERS: Unity panning.
- HEADPHONES (3D): Steam Audio HRTF. You hear above, behind and in front, not only left and right.
- NIGHT MODE: a stronger glue compressor with makeup gain.

## 6. Licences

- Generators: Stable Audio 3 (Stability AI Community License, free under $1M annual revenue), AudioSR (Apache-2.0), and
  LAION-CLAP for scoring (Apache-2.0).
- Plugin: Steam Audio (Apache-2.0, notices in `third_party/steamaudio`).
- MMAudio's weights (CC-BY-NC-4.0) made much of the inherited web bank: those clips are being replaced.

## 7. Listening

- **Sound Lab page** (`tools/audio/lab`, published privately): the overload test (before/after the master chain,
  the same DSP ported to the browser), voice A/B with spectrograms, and the bank audit.
- **ZU > Sound Lab** Editor window: audition any id at a distance and bearing, fire the overload burst, live master
  meter.
- **`zu_audio_capture`** records the real master output to a WAV (AudioRenderer, which works in batch mode too).
  `zu_audio_report` returns that capture's peak, clips, clicks and loudness.
