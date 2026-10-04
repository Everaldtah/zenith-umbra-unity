# Run arm carriage: Quaternius, Mixamo, Tripo and Overwatch, frame by frame

Asked for by the user after the v0.2.2 smoke test: "when some characters run their hands look like they are dangling
behind". Seen in the build: Kagemaru running with both hands tucked behind his hips (smoke shot `kagura_2`).

Measured the same way everywhere (`tools/runstudy/runscan.py <pack.glb> [clip filter] [--frames]` for clip packs,
`zu_run_strip --hero <id>` in the Editor for the game): per arm, the hand's offset from its shoulder along the body's
forward / up / outward axes in arm lengths, the elbow's flexion, the swing's range and its phase against the opposite
foot; per body, the torso's lean and the cycle time.

## 1. The clip packs (16 phases a cycle)

| clip | cycle s | torso lean | hand forward mean [min..max] | swing | hand up | hand out | elbow flex | opposite-foot phase |
|---|---|---|---|---|---|---|---|---|
| Quaternius `Jog_Fwd_Loop` | 0.93 | +19 deg | +0.04 [-0.45..+0.48] | 0.9 | -0.52 | +0.30 | 88 (79..94) | r = +0.9 |
| Quaternius `Sprint_Loop` | 0.67 | +34 deg | +0.02 [-0.43..+0.40] | 0.8 | -0.56 | +0.30 | 88 (79..94) | +0.9 |
| Quaternius `Walk_Loop` | 1.33 | +10 deg | +0.11 [-0.13..+0.42] | 0.55 | -0.90 | +0.20 | 36 | +0.97 |
| Mixamo `MX_Jog_Fwd` | 0.87 | +12 deg | +0.14 [-0.22..+0.49] | 0.65 | -0.75 | +0.08 | 65 (40..97) | +0.7 |
| Mixamo `MX_Run_Fwd` | 0.77 | +24 deg | -0.05 [-0.70..+0.53] | 1.15 | -0.50 | +0.09 | 88 (60..126) | +0.7 |
| Tripo `TR_Walk_Fwd` (in game) | 1.04 | **-9 deg** | +0.10 [-0.07..+0.30] | 0.25 | -0.91 | +0.30 | 27 | +0.96 |
| Tripo `TR_Jog_Fwd` (in game) | 0.62 | **-16 deg** | +0.18 [+0.06..+0.34] | **0.23** | -0.68 | **+0.52** | 56 | +0.95 |
| Tripo `TR_Run_Fwd` (in game) | 0.75 | +21 deg | -0.05 [-0.48..+0.26] | 0.6 | -0.55 | +0.21 | 100 (87..118) | +0.9 |

What the mocap agrees on: the hands swing about the shoulder line (mean forward offset within +-0.15 of it), opposite to
the legs (the hand is forward when the opposite foot is), the elbow holds 80-90 degrees on a jog or a sprint, the hands
ride 0.2-0.3 arm lengths outside the shoulders, and the torso leans INTO the run (12-34 degrees). No clip has both
hands behind the shoulders at once.

The game's locomotion set is the Tripo text-to-motion pack (the manifest excludes the Quaternius and Mixamo gaits). Its
jog - the speed most heroes move at - is the odd one out: the torso leans BACK 16 degrees, the arms are held half an arm
length out to the sides, low, and barely swing. Its walk leans back too.

## 2. Overwatch, frame by frame (1080p60 gameplay; a teammate running out of spawn seen from behind, 0.8 s at 30 fps)

Footage: the project's reference clip `work/ref/hanzo_ow2.webm` (git-ignored, not shipped), 40.0-41.0 s and 42.6-43.4 s.
- The arms are held OUT from the torso, 30-45 degrees from the body, elbows a little bent; from behind both hands are
  always outside the hip line - the silhouette shows two arms on every frame.
- The hands ride at hip height and stay level with or in front of the shoulder line; an arm swings back only as the
  opposite leg drives, and never past the back of the hips. Both hands are never behind the body together.
- The swing is small (a readable sway, not a sprinter's pump) and opposite to the legs.
- The torso leans forward 15-20 degrees and the head stays level.
This is the hero-shooter rule the TS Animator already applies to the torso ("an Overwatch hero stays upright with the
weapon forward (readability)"): a pose reads by its silhouette, so limbs stay clear of the body.

## 3. The rule the game should hold on a run

1. Never both hands behind the shoulder line; one hand at most 0.3 arm lengths behind it, at the back of its swing.
2. Hands 0.15 arm lengths or more outside the shoulders (visible from behind).
3. The swing opposite to the legs, 0.3-0.6 arm lengths, elbows 60-90 degrees.
4. The torso leans forward on a jog or run (10-20 degrees), never back.

## 4. The game against the rule

(`zu_run_strip` results per hero and the fix are added below once measured in the Editor.)
