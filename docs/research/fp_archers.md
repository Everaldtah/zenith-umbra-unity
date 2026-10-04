# First-person archers: Seiran and Yuzu, from a frame-by-frame study of two Overwatch 2 viewmodels

evera-f4, 2026-10-04. Implements: `Assets/ZU/Game/FirstPerson/FirstPersonView.Archer.cs`.

What this is: our own measurements of timing, screen placement and arcs, taken by stepping through reference gameplay
frame by frame, and our own poses keyed from them. Nothing from the game is used or shipped: no footage, frames, curves,
rigs or names in the build. The reference videos and contact sheets stay in git-ignored scratch folders.

Mapping by kit:
- **Seiran = the bow archer** (charged bow, a volley shot, an air lunge, a sound-reveal arrow, a twin-spirit ult, wall
  climb). His whole primary cycle follows the bow archer's.
- **Yuzu = the crossbow hunter** (a charged shot through a held zoom = her Take Aim, an updraft = Sunhop). Her hold,
  aim-in/out and kick follow the crossbow hunter's; her per-arrow re-nock is our own compression of that hero's reload
  rhythm (tip up, load, tip back) into one arrow.

Conventions: screen fractions of the 16:9 frame, (0, 0) top-left, reticle (0.50, 0.50). Times in ms and in frames at
60 fps (f). View metres for our 58 deg vertical FP camera at depth z: `x = (2fx - 1) * z * 0.985`, `y = (1 - 2fy) * z * 0.554`.
Sources measured: an ability-overview clip of the bow archer (30 fps, older kit, clean static camera), OW2 gameplay of
the bow archer (60 fps), OW2 gameplay of the crossbow hunter (60 fps). Earlier notes: the web repo's
docs/research/archer_fp_study.md (evera-b6); this pass re-measured the timings and corrected two of them.

## 1. Seiran (the bow archer's hold)

### Hold
The bow is not upright: it lies across the bottom of the screen, rolled about 80 deg from vertical with the upper limb to
the RIGHT, and that limb recedes in depth (our yaw -0.3 rad; a first-person cheat, as the arrow still points ahead). The
bow forearm enters from the bottom-left corner; the fist sits at the bottom centre. The arrow points down the view and
reads as a short spike above the fist. The upper ~55% of the screen is empty in every phase except the quiver reach.

### Primary cycle (measured)
| phase | t | f @60 | bow fist (screen) | bow | string hand | notes |
|---|---|---|---|---|---|---|
| ready (nocked) | - | - | (0.46, 0.89) | nearly flat, bottom 15% | below the frame on the string | slow breathing only |
| draw start | 0 | 0 | lifts | rises | fingertips may show at the bottom centre | button down |
| anticipation | 0-60 ms | 0-4 | dips ~0.01 | - | sets on the string | tiny, but it sells the pull |
| most of the lift | 60-200 ms | 4-12 | to ~(0.47, 0.78) | right end up to ~(1.0, 0.62) | going back | ease-out |
| full draw pose | ~450 ms | 27 | (0.47, 0.75) | (0.0, 1.0) -> (1.0, 0.60) | past the jaw, out of frame | arrow tip ~(0.52, 0.72), reticle clear |
| full charge | ~700 ms | 42 | held | held | held | a small glint on the arrow; the ability overview's button-down to glint is 0.70 s |
| loose | 0 | 0 | kick down ~0.01, forward | flattens 3-5 deg | two fingertips flick open for ~4 f | no camera kick: the recoil is all in the viewmodel |
| follow-through | 0-100 ms | 0-6 | settles ~0.03 lower | flatter | drops away | |
| reach enters | ~100-160 ms | 6-10 | lowering | | forearm enters at the lower RIGHT | |
| over the shoulder | ~170-220 ms | 10-13 | | | sweeps up the right edge, hand out of the top-right | the quiver |
| arrow comes down | ~250-290 ms | 15-17 | | | from the upper right (0.78, 0.25) down-left | the only time anything crosses the upper half |
| arrow upright | ~300-320 ms | 18-19 | | | hand at the bottom centre, arrow vertical at x ~0.53 | |
| nocked | ~300-360 ms | 18-22 | (0.47, 0.85) | | the arrow tips forward onto the string | |
| settled | ~430-500 ms | 26-30 | (0.46, 0.89) | idle band | out of frame | the next draw may start |

Corrections to the older notes: (1) in the 60 fps OW2 footage the reach itself, arm-in to nocked, is ~170 ms (10 f);
(2) the full CHARGE (glint) comes at ~0.70 s, later than the full draw POSE (~0.45 s): the pose is reached early and held.
The OW2 recovery between shots is ~0.5 s.

### Fitted to our game
The sim fills the charge in 0.9 s (`Weapons.cs`, dt / 0.9) and unlocks the next draw 0.6 / rate after the loose
(Seiran: 0.57 s). Our keys: the draw pose at 0.45 s of draw (charge 0.5), held to 0.9 s; the loose cycle nocked at
0.36 s, settled at 0.50 s, so a held trigger chains draw -> loose -> reach -> draw with no dead time and no clipped reach.

### Other moments (keyed)
- Scatter Current (the volley; fires on the click): a snap half-draw pose loosed at once, a harder kick (flatten
  +0.1 rad, drop 0.02 m), then the same reach.
- Echo Arrow and Twin Koi Torrent: the loose cycle from a full-draw pose (the bow archer's sonic and spirit arrows are the
  same draw and loose with a glowing head).
- Quick melee: the bow driven forward like a staff: wound back left (70 ms), struck in toward the centre (150 ms), back
  at 450 ms.
- Riverstep: the bow arm braces down and in (80 ms) and returns (350 ms).

## 2. Yuzu (the crossbow hunter's hold)

### Hold
Two-handed, flat, in the bottom-right quadrant. The crossbow's limbs run along the bottom from x ~0.38 to ~0.95 at y
0.82-0.93; its rail angles up-left with the muzzle near (0.60, 0.67), right of and below the reticle; the hands are mostly
hidden under the weapon. Ours: the bow flat (roll ~97 deg, the right limb a little low and turned toward the lens), the
fist at ~(0.66, 0.88), the arrow on the right of the fist running up toward the reticle, its head near (0.60, 0.69); the
string hand low right under the weapon. Nothing crosses the reticle.

### Take Aim / Hawk Eye (measured at 30 and 60 fps)
| phase | t | f @60 | what happens |
|---|---|---|---|
| aim in | 0-110 ms | 0-7 | the weapon slides right and rotates so the glowing rail runs down the line of sight; the limbs drop out of the bottom; the zoom narrows over the same time |
| held | any | - | perfectly steady; the rail glow pulses brighter as the charge builds |
| release | 0-50 ms | 0-3 | the zoom snaps back out in 1-2 frames at 30 fps (faster than it went in) |
| kick | ~+100 ms | ~6 | a visible kick of the view on the charged shot, settling over ~200 ms (12 f) |
Auto-fire kick (her rapid bolts, for scale): <= 0.02 of the screen per bolt, recovered in 4-6 f. A big reload: the weapon
tips up to near-vertical on the right over ~200 ms, is held ~1 s while the left hand works under it, comes back in ~170 ms.

### Fitted to our game
Hawk Eye is a held zoom (`sv.zoom`, MatchCamera FOV 38). The viewmodel blends to the aim pose at ~0.11 s in, ~0.05 s out.
The draw (charge) slides the arrow back across the bow: the arrowhead ends just past the fist at full draw. The loose:
kick up and back at 40 ms (sharper in Hawk Eye), back by 150 ms; then the re-nock, our compression of the reload rhythm:
the bow tips up on the right (pitch +0.3 rad, rolled toward upright) while the string hand goes down to the hip quiver
(the hand is out of frame 100-200 ms), brings the next arrow up into view at the bottom right (~240-320 ms), lays it on
and nocks it (400 ms), and the bow tips back down (settled 500 ms; the recovery is 0.55 s). In Hawk Eye the tip-up is
smaller so the arrow stays near the line of sight.

### Other moments (keyed)
- Sunhop (the updraft): the weapon is pressed down by the launch (100 ms), floats a little high through the glide.
- Hundred Suns Barrage: the bow swung up at the sky, loosed at once, brought back down by 0.85 s. Line its release up
  with evera-a0's reworked ult timeline (5 great arrows, then the homing rain) once that lands.
- Revealing Dawn Arrow: the loose cycle.

## 3. Why the old viewmodel read as wobbly / distorted, and what the keyed version does instead
- The old clips were converted body motion retargeted through a Humanoid avatar, then canted after the fact
  (bowCant / bowTilt): the bow followed the forearm, so every wrist wobble swung the whole bow. Now the bow is placed from
  a keyed frame on the solved fist.
- Elbows came from a fixed IK pole and could flip near full extension. Now every key carries its own pole.
- Hand twist was all at the wrist (one forearm bone, no twist bones: the skin pinched). Now the twist onto the bow is
  shared half and half by the forearm (about its own axis, so the wrist stays put) and the wrist.
- Cuts between moments snapped or blended linearly. Now a cut leaves an offset that decays as a critically damped spring
  (4.5 Hz): continuous, and it can't overshoot.
- Sway / bob are unchanged here (shared layer).

## 4. The Tripo text-to-motion baseline (user ask: rough base, improve after)
Three 5 s clips generated in Tripo Studio on Seiran's model (auto-rigged, Mixamo skeleton; Yuzu's HD mesh was over the
auto-rig polygon limit, so her prompt ran on the same skeleton). Measured by FK (hands relative to the head):
- "draw, hold, loose, reach over the shoulder, nock": the bow arm lifts in a smooth ~0.5 s ease-in-out arc (forward, then
  up). The string hand moves ~6 cm in total: no draw to the jaw, no reach to the quiver.
- "rapid-fire five arrows": near-static; the bow arm bobs twice.
- "hip hold, snap up to aim, hold, loose, re-nock" (Yuzu): near-static.
Used: the bow-lift arc's shape for the draw keys (forward-then-up, eased). Everything else is keyed from the study.
The clips are kept as scratch references only (not imported, not committed).

## 5. Not yet covered here (next)
Third-person states for both (walk / run / strafe, jump / land, wall climb, hit react, death, inspect, equip) and the
other heroes' FP. Fingers per phase are driven (fist on the grip; hook / open / relaxed / pinch on the string hand).
Sound events for evera-eb once the keys are final (loose 0, reach ~170 ms, nock ~360 ms for Seiran; aim-in, loose, nock
400 ms for Yuzu).
