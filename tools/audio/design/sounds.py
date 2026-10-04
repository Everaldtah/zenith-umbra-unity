"""Every sound in the Unity bank, designed: what Stable Audio 3 Small-SFX is asked for, how many takes, how many variations
ship, and the synthesised layers composed on top (tools/audio/design/compose.py; layer kinds in synth.py).

Prompting (Stable Audio 3 prompt guide + game practice):
  - name the source, the action and the recording - the model was trained on Freesound / AudioSparx descriptions
  - one-shots are asked for DRY and close-mic'd: the game adds the room itself (Space.cs reverb + reflections), and a
    baked-in room would double it and smear the transient
  - "variations" stay few for the iconic sounds (Overwatch keeps a weapon to 2-3 so it stays recognisable), more for
    steps and impacts (repetition there is what the ear catches)
  - the prompts describe the sound, never a source game: all original

Fields: cat (sfxbank category), secs (target length), prompt, takes (renders), vars (variations shipped), loop (seamless),
layers (synth.py recipes, each a dict with "k" = kind and its parameters; offsets in ms from the body's onset),
neg (extra negative prompt words).
"""

DRY = "close-mic'd, dry studio recording, no reverb"
FIELD = "stereo field recording, natural, high quality"

S = {}


def s(id, cat, secs, prompt, takes=8, vars=1, loop=False, layers=None, neg=""):
    S[id] = {"id": id, "cat": cat, "secs": secs, "prompt": prompt, "takes": takes, "vars": vars, "loop": loop, "layers": layers or [], "neg": neg}


# shared layer presets ---------------------------------------------------------------------------------------------------
def gun(heavy=False, mech=True, crack=1.0):
    L = [{"k": "crack", "ms": 6, "hp": 2500, "db": -8 + 2 * (crack - 1)}]
    if heavy: L.append({"k": "sub", "f0": 95, "f1": 42, "ms": 140, "db": -6})
    if mech: L.append({"k": "click", "n": 2, "gap": 14, "f": 3200, "db": -20, "at": 4})
    return L


def hit(mat):
    # a material's bite on top of the body: a crack tuned to the material, and the debris that falls after it
    return {
        "stone": [{"k": "crack", "ms": 4, "hp": 3000, "db": -9}, {"k": "debris", "ms": 380, "grain": "stone", "n": 18, "db": -17, "at": 25}],
        "metal": [{"k": "crack", "ms": 3, "hp": 4000, "db": -10}, {"k": "ring", "f": [2210, 3470, 5180, 7090], "decay": 0.35, "db": -16}],
        "glass": [{"k": "crack", "ms": 3, "hp": 5000, "db": -8}, {"k": "debris", "ms": 520, "grain": "glass", "n": 26, "db": -15, "at": 15}],
        "wood": [{"k": "crack", "ms": 5, "hp": 1800, "db": -10}, {"k": "debris", "ms": 260, "grain": "wood", "n": 10, "db": -20, "at": 20}],
        "tile": [{"k": "crack", "ms": 4, "hp": 3500, "db": -9}, {"k": "debris", "ms": 600, "grain": "tile", "n": 14, "db": -16, "at": 30}],
        "dirt": [{"k": "sub", "f0": 120, "f1": 60, "ms": 70, "db": -12}, {"k": "debris", "ms": 300, "grain": "dirt", "n": 30, "db": -20, "at": 10}],
    }[mat]


# ---------------------------------------------------------------- weapons: the player's tools, the most-heard sounds
s("chaingun", "weapon", 0.45, f"a single heavy rotary cannon round fired, deep punchy chest-thumping boom with a sharp mechanical clank, {DRY}", 10, 3, layers=gun(True))
s("chaingun2", "weapon", 0.45, f"a single heavy machine gun round fired, bright cracking report with a metallic rattle, {DRY}", 10, 3, layers=gun(True, crack=1.3))
s("spinup", "weapon", 0.6, f"electric rotary minigun barrels spinning up fast, rising servo motor whine and whirring metal, {DRY}", 6)
s("spindown", "weapon", 1.1, f"rotary minigun barrels spinning down to a stop, falling motor whine with a soft mechanical rattle, {DRY}", 6)
s("cannon", "weapon", 0.8, f"a big plasma cannon shot, deep powerful boom with a sizzling electric energy discharge, {DRY}", 10, 2, layers=gun(True, mech=False))
s("blaster", "weapon", 0.4, f"a laser pistol shot, sharp bright electric zap with a punchy low thump, {DRY}", 10, 3, layers=gun(False, mech=False, crack=1.2))
s("shotgun", "weapon", 0.9, f"a heavy combat shotgun blast, huge punchy explosive boom with a metallic snap, {DRY}", 10, 2, layers=gun(True))
s("scattergun", "weapon", 0.9, f"a heavy pump-action shotgun blast followed by the pump racking, huge boom then sliding metal clack, {DRY}", 10, 2, layers=gun(True))
s("sonic", "weapon", 0.35, f"a punchy electronic bass blaster shot, a short deep synthesizer pulse with a hard attack, {DRY}", 8, 2, layers=[{"k": "sub", "f0": 110, "f1": 55, "ms": 110, "db": -7}])
s("star", "weapon", 0.5, f"a crystal shard launched by magic, bright glassy shimmering chime with a soft airy whoosh, {DRY}", 8, 2, layers=[{"k": "shimmer", "ms": 300, "db": -18}])
s("talisman", "weapon", 0.4, f"a paper charm flicked hard through the air, quick papery flutter whoosh with a tiny bell chime, {DRY}", 8, 3)
s("katana", "weapon", 0.4, f"a katana slashing through the air, a sharp thin metallic swish with a steel ring, {DRY}", 10, 3, layers=[{"k": "whoosh", "ms": 180, "f0": 1800, "f1": 6500, "db": -14}])
s("thunder", "weapon", 0.9, f"a bolt of lightning striking, a violent electric crackle snapping into a heavy thunder boom, {DRY}", 8, 2, layers=gun(True, mech=False, crack=1.4))
s("bow", "weapon", 0.5, f"a heavy recurve bow releasing an arrow, a deep taut string thwang and a fast arrow whoosh, {DRY}", 10, 3, layers=[{"k": "crack", "ms": 3, "hp": 2000, "db": -14}])
s("bowdraw", "weapon", 0.8, f"a recurve bow string drawn back slowly, creaking wood limbs and rising string tension, {DRY}", 6)
s("note", "weapon", 0.5, f"a haunting magical sung note fired like a projectile, an eerie choir shimmer with a soft swoosh, {DRY}", 8, 2)
s("needle", "weapon", 0.3, f"a small sharp dart fired from an air gun, quick pneumatic puff and a thin metallic whistle, {DRY}", 8, 3, layers=[{"k": "crack", "ms": 2, "hp": 4000, "db": -16}])
s("kunai", "weapon", 0.35, f"a throwing knife thrown very fast, a sharp thin metallic swish, {DRY}", 8, 3)
s("shuriken", "weapon", 0.4, f"a small metal throwing star thrown hard, a quick spinning whir and a bright metallic swish, {DRY}", 8, 3)
s("fang", "weapon", 0.45, f"a dark curved blade slashing, a growling low whoosh with a metallic edge, {DRY}", 8, 2)
s("fangreturn", "weapon", 0.8, f"a spinning heavy throwing blade whirring back through the air, a fast rhythmic metallic whoosh, {DRY}", 6)
s("fangcatch", "weapon", 0.3, f"a heavy knife caught in a leather gloved hand, a short metallic clink and leather slap, {DRY}", 6, 2)
s("flamestart", "weapon", 0.7, f"a gas flamethrower igniting, a click and a whooshing burst of roaring flame, {DRY}", 6)
s("flame", "loop", 2.5, f"a flamethrower roaring continuously, steady turbulent fire jet with gas hiss, {DRY}", 6, loop=True)
s("punch", "weapon", 0.4, f"a heavy armoured punch landing, a deep meaty thump with a leather slap, {DRY}", 8, 3, layers=[{"k": "sub", "f0": 90, "f1": 50, "ms": 90, "db": -10}])
s("hammer", "weapon", 0.8, f"a giant rocket powered hammer swung, a short rocket thruster roar into a heavy whoosh, {DRY}", 8, 2)
s("whiff", "weapon", 0.3, f"a fast empty swing whooshing through the air, {DRY}", 8, 3)
s("cut", "weapon", 0.3, f"a fast sword cut, a sharp slicing swish with a faint steel ring, {DRY}", 8, 3)
s("reload", "weapon", 0.9, f"a futuristic rifle reload, magazine released and slid out, new magazine slammed in, bolt clack, {DRY}", 8, 2)
s("reapwind", "weapon", 0.5, f"a heavy battle axe heaved back over the shoulder, a deep metallic whoosh building up, {DRY}", 6)
s("reaping", "weapon", 0.7, f"a giant battle axe cleaving through the air, a heavy whoosh into a sharp ringing metallic slash, {DRY}", 8, 2)

# ---------------------------------------------------------------- feedback (in your head; crisp, short, never fatiguing)
s("hit", "feedback", 0.15, f"a crisp short tick, a tiny clean percussive click with a soft body, {DRY}", 8, 1)
s("crit", "feedback", 0.4, f"a bright metallic ding, a crisp small bell ringing once, satisfying, {DRY}", 8, 1)
s("kill", "feedback", 0.6, f"a satisfying reward chime, two bright bell notes rising, clean, {DRY}", 8, 1)
s("healhit", "feedback", 0.4, f"a soft gentle magical chime, a warm sparkle, {DRY}", 6, 1)
s("healthpack", "feedback", 0.6, f"a health pickup, a bright rising synthesizer chime with a soft sparkle, {DRY}", 6, 1)
s("barrierup", "ability", 0.7, f"an energy shield powering up, a rising electric hum with a glassy shimmer, {DRY}", 6)

# ---------------------------------------------------------------- impacts: bullets and blades on the world (the building clash)
s("impact_stone", "impact", 0.45, f"a bullet hitting a stone wall, a sharp crack with chips of rock and dust falling, {DRY}", 10, 3, layers=hit("stone"))
s("impact_metal", "impact", 0.6, f"a bullet hitting thick sheet metal, a sharp clang with a short metallic ricochet ping, {DRY}", 10, 3, layers=hit("metal"))
s("impact_glass", "impact", 0.6, f"a bullet hitting a window pane, a sharp glass crack with small shards tinkling down, {DRY}", 10, 3, layers=hit("glass"))
s("impact_wood", "impact", 0.45, f"a bullet hitting a thick wooden beam, a dull hard thwack with splinters, {DRY}", 10, 3, layers=hit("wood"))
s("impact_tile", "impact", 0.6, f"a bullet hitting ceramic roof tiles, a sharp brittle crack with tile fragments clattering, {DRY}", 10, 3, layers=hit("tile"))
s("impact_dirt", "impact", 0.4, f"a bullet hitting packed earth, a dull thud with a puff of dirt and gravel scattering, {DRY}", 10, 3, layers=hit("dirt"))
s("impact_body", "impact", 0.3, f"a bullet hitting body armour, a dull meaty thud with a small metallic tick, {DRY}", 8, 3, layers=[{"k": "sub", "f0": 110, "f1": 60, "ms": 60, "db": -12}])
s("boom", "impact", 1.8, f"a large explosion, a deep powerful blast with rock and debris raining down, {DRY}", 10, 2,
  layers=[{"k": "sub", "f0": 70, "f1": 28, "ms": 450, "db": -4}, {"k": "crack", "ms": 10, "hp": 1500, "db": -8}, {"k": "debris", "ms": 1200, "grain": "stone", "n": 40, "db": -18, "at": 120}])
s("slam", "impact", 1.5, f"a massive ground slam, a huge boom cracking stone with rumbling debris, {DRY}", 8, 2,
  layers=[{"k": "sub", "f0": 60, "f1": 26, "ms": 500, "db": -4}, {"k": "debris", "ms": 1000, "grain": "stone", "n": 34, "db": -18, "at": 80}])
s("implode", "impact", 1.2, f"a black hole collapsing, a sucking reverse whoosh into a huge implosion boom, {DRY}", 6, layers=[{"k": "sub", "f0": 50, "f1": 30, "ms": 500, "db": -5}])
s("barrierhit", "impact", 0.3, f"an energy shield absorbing a bullet, a short electric hum ping with a glassy tick, {DRY}", 8, 3)
s("barrierbreak", "impact", 1.1, f"an energy shield shattering like glass, an electric burst with crystal shards scattering, {DRY}", 8, 2, layers=hit("glass"))
s("anchorhit", "impact", 0.5, f"a heavy iron anchor slamming into armour and grabbing, a deep metallic clang, {DRY}", 6, 2)
s("arrowhit", "impact", 0.3, f"an arrow thudding hard into packed ground, a short woody thunk, {DRY}", 8, 3)
s("chainhit", "impact", 0.5, f"a heavy chain wrapping tight around armour, clanking links snapping taut, {DRY}", 6, 2)
s("pin", "impact", 0.4, f"a heavy body slammed into a stone wall, a crunchy thud with plaster cracking, {DRY}", 8, 2, layers=hit("stone"))
s("body_slam", "impact", 0.5, f"a person slamming into a wall, a heavy body impact thud with gear rattling, {DRY}", 8, 2)
s("mechdown", "impact", 2.2, f"a giant robot collapsing and exploding, a huge metallic crash with an explosion and scattering parts, {DRY}", 6,
  layers=[{"k": "sub", "f0": 70, "f1": 30, "ms": 500, "db": -5}, {"k": "debris", "ms": 1200, "grain": "metal", "n": 30, "db": -17, "at": 150}])
s("down", "impact", 0.8, f"a person collapsing to the ground, a body fall with gear and armour rattling, {DRY}", 6, 2)
s("bodyfall", "impact", 0.6, f"a body falling onto a hard floor, a heavy soft thud with a little armour clatter, {DRY}", 8, 3)
s("botdown", "impact", 0.8, f"a small robot powering down, a falling electronic whine with crackling sparks, {DRY}", 6)
s("casing", "impact", 0.35, f"a single brass bullet casing dropping and bouncing on concrete, small bright metallic tinkle, {DRY}", 10, 4)

# ---------------------------------------------------------------- movement + steps (enemy steps must read at 20 m)
s("step_stone", "step", 0.35, f"a single boot footstep on stone pavement, a crisp heel click and sole scuff, {DRY}", 12, 4, layers=[{"k": "crack", "ms": 2, "hp": 3000, "db": -18}])
s("step_wood", "step", 0.35, f"a single boot footstep on a wooden deck, a hollow woody knock, {DRY}", 12, 4)
s("step_metal", "step", 0.35, f"a single boot footstep on a metal grate floor, a ringing metallic clank, {DRY}", 12, 4)
s("step_heavy", "step", 0.5, f"one heavy footstep of a giant armoured man on stone, a deep thud with armour clink, {DRY}", 10, 3, layers=[{"k": "sub", "f0": 80, "f1": 45, "ms": 120, "db": -10}])
s("mechstep", "step", 0.7, f"a giant robot footstep, a heavy metallic stomp with a hydraulic hiss, {DRY}", 8, 3, layers=[{"k": "sub", "f0": 70, "f1": 35, "ms": 200, "db": -8}])
s("skate", "move", 0.6, f"an inline skater pushing off, polyurethane wheels rolling on pavement, {DRY}", 8, 2)
s("skate_roll", "loop", 3.0, f"inline skates rolling smoothly on pavement, a continuous wheel whir, {DRY}", 6, loop=True)
s("grind", "loop", 3.0, f"skates grinding along a metal rail, continuous metallic scraping, {DRY}", 6, loop=True)
s("jump", "move", 0.3, f"a quick jump off the ground, a cloth rustle and a small gear jingle, {DRY}", 8, 3)
s("land", "move", 0.4, f"landing on the ground after a jump, a boot thud with gear rattle, {DRY}", 8, 3)
s("land_heavy", "move", 0.6, f"landing hard after a long fall, a heavy boot thud and gravel scrape, {DRY}", 8, 2, layers=[{"k": "sub", "f0": 90, "f1": 45, "ms": 120, "db": -10}])
s("mechjump", "move", 1.0, f"a giant robot jumping with jet thrusters firing, a roaring jet burst, {DRY}", 6)
s("mechland", "move", 0.9, f"a giant robot landing heavily, a huge metallic slam with a deep rumble, {DRY}", 6, layers=[{"k": "sub", "f0": 65, "f1": 30, "ms": 300, "db": -6}])
s("doublejump", "move", 0.4, f"a magical double jump, an airy whoosh with a small electric crackle, {DRY}", 8, 2)
s("dash", "move", 0.4, f"a fast dash, a sharp close whoosh, {DRY}", 8, 2)
s("flashstep", "move", 0.5, f"a lightning fast teleport dash, a sharp electric crackle and whoosh, {DRY}", 8, 2)
s("wind", "loop", 3.0, f"strong wind rushing past at high speed, buffeting whooshing gusts, {FIELD}", 6, loop=True)
s("wings", "move", 0.6, f"large wings flapping once, a heavy feathery whoosh, {DRY}", 8, 2)
s("swoop", "move", 0.8, f"a magical angelic swoop, a fast airy whoosh with a rising chime, {DRY}", 6)
s("pad", "move", 0.6, f"a jump pad launching someone upward, a springy sci-fi boing with a rising whoosh, {DRY}", 6)

# ---------------------------------------------------------------- abilities + ults (character; mostly magic and machinery)
s("eject", "ability", 1.2, f"a pilot ejection seat firing, a rocket blast with a whoosh, {DRY}", 6)
s("ignite", "ability", 0.7, f"something catching fire suddenly, a burst of flames whoosh, {DRY}", 6)
s("burn", "loop", 2.0, f"fire crackling and burning steadily, {DRY}", 6, loop=True)
s("rocketfist", "ability", 0.8, f"a rocket propelled metal fist launched, a rocket roar and a fast whoosh, {DRY}", 6)
s("sunburst", "ability", 0.9, f"a radiant burst of holy light, a bright shimmering whoosh with a warm glow, {DRY}", 6, layers=[{"k": "shimmer", "ms": 500, "db": -18}])
s("ultcall", "ability", 1.2, f"a dramatic power surge, a rising energy whoosh into a heavy cinematic hit, {DRY}", 8, layers=[{"k": "sub", "f0": 70, "f1": 35, "ms": 300, "db": -7, "at": 900}])
s("constellation", "ability", 0.9, f"magical starlight connecting, shimmering crystal chimes rising, {DRY}", 6)
s("wish", "ability", 0.8, f"a magical protective bubble forming, a warm shimmering hum, {DRY}", 6)
s("nova", "ability", 2.0, f"a heavenly choir swell with a burst of magical light, {DRY}", 6)
s("spiritstep", "ability", 0.6, f"a fast ghostly whoosh with a soft temple bell, {DRY}", 6)
s("seal", "ability", 0.9, f"a deep gong strike with a magical shimmer, {DRY}", 6)
s("sanctuary", "ability", 1.6, f"a sacred barrier rising, deep temple bells and a holy hum, {DRY}", 6)
s("parrystance", "ability", 0.4, f"a sword raised to parry, a short metallic ring, {DRY}", 6)
s("parry", "ability", 0.5, f"a sword parrying a blade, a bright metallic clang with sparks, {DRY}", 8, 2, layers=[{"k": "ring", "f": [1870, 2950, 4410, 6230], "decay": 0.5, "db": -16}])
s("thunderclap", "ability", 1.6, f"a colossal thunderclap, a lightning crack and rolling thunder, {DRY}", 6, layers=[{"k": "crack", "ms": 8, "hp": 1500, "db": -8}])
s("chainlightning", "ability", 0.5, f"crackling electricity arcing and zapping between metal, {DRY}", 8, 2)
s("sunhop", "ability", 0.4, f"a quick bounce jump with a bright magical twang, {DRY}", 6)
s("reveal", "ability", 0.7, f"a bright magical arrow shot with a ringing bell chime, {DRY}", 6)
s("arrowrain", "ability", 2.5, f"a volley of many arrows falling from the sky, a dense rain of whooshes, {DRY}", 6)
s("plating", "ability", 0.7, f"heavy metal armour plates clanking and locking into place, {DRY}", 6)
s("charge", "ability", 1.1, f"a giant robot charging forward, roaring engines and heavy stomps, {DRY}", 6)
s("lance", "ability", 0.6, f"a dark energy spear launched, a deep electric zap with a whoosh, {DRY}", 6)
s("singularity", "ability", 2.5, f"a black hole forming, a deep sucking rumble and swirling wind, {DRY}", 6)
s("silence", "ability", 0.8, f"a magical silencing shriek wave, a sharp high whine fading, {DRY}", 6)
s("bloodpact", "ability", 0.7, f"a dark blood magic spell, a low eerie pulse, {DRY}", 6)
s("requiem", "ability", 2.2, f"a haunting gothic choir requiem swelling, {DRY}", 6)
s("strings", "ability", 0.6, f"magical puppet strings shooting out, taut thin twangs, {DRY}", 6)
s("yank", "ability", 0.4, f"a rope yanked hard, a quick twang and whoosh, {DRY}", 6)
s("throw", "ability", 0.3, f"a quick whoosh of an object thrown hard through the air, {DRY}", 6, 2)
s("hexburst", "ability", 0.7, f"a burst of dark cursed magic, a distorted crackling zap, {DRY}", 6)
s("theater", "ability", 1.4, f"a creepy music box melody with eerie whispers, {DRY}", 6)
s("shadowstep", "ability", 0.5, f"a shadow teleport, a dark whoosh with a hiss, {DRY}", 6)
s("veil", "ability", 0.7, f"going invisible, a soft dark shimmer fading away, {DRY}", 6)
s("unveil", "ability", 0.3, f"a quick magical shimmer sparkle appearing, {DRY}", 6)
s("chainthrow", "ability", 0.6, f"a heavy chain thrown, rattling iron links flying through the air, {DRY}", 6)
s("brand", "ability", 0.8, f"a burning brand pressed, sizzling flames and a dark boom, {DRY}", 6)
s("taiko", "ability", 0.9, f"two big taiko drum strikes, deep booming, {DRY}", 6)
s("taikobeat", "ability", 0.6, f"a single big taiko drum strike, a deep boom, {DRY}", 6)
s("dohyo", "ability", 1.8, f"a ceremonial ring ritual, two sharp hand claps then a long deep drum roll, {DRY}", 6)
s("roar", "ability", 2.2, f"a huge stadium crowd cheering and roaring, {FIELD}", 6)
s("blessing", "ability", 0.6, f"a gentle holy blessing spell, a warm chime with a soft rising shimmer, {DRY}", 6)
s("stitch", "ability", 0.5, f"a magic needle and thread stitching quickly, a sharp zip with a twang, {DRY}", 6)
s("decoy", "ability", 0.6, f"a doll decoy bursting into cloth and stuffing with a puff of magic, {DRY}", 6)
s("warcall", "ability", 1.6, f"a huge conch shell war horn blown hard, a deep resonant bellowing horn call, {DRY}", 6)
s("track_heal", "ability", 0.9, f"a DJ record scratch transition into a mellow warm groove, {DRY}", 6)
s("track_speed", "ability", 0.9, f"a DJ record scratch transition into a fast energetic beat, {DRY}", 6)
s("amp", "ability", 0.9, f"a DJ volume boost, a rising filter sweep ending in a punchy bass hit, {DRY}", 6)
s("scratch", "ability", 0.6, f"a turntable record scratch blasted out with a deep bass whump, {DRY}", 6)
s("scratch_big", "ability", 0.8, f"a huge turntable record scratch blast with a massive bass whump, {DRY}", 6)
s("pumped", "ability", 0.7, f"a short loud air horn blast, {DRY}", 6)
s("bassrise", "ability", 1.0, f"a rising white noise sweep riser getting louder, electronic build up, {DRY}", 6)
s("bassdrop", "ability", 1.8, f"a massive electronic bass drop impact, a huge sub bass boom, {DRY}", 6, layers=[{"k": "sub", "f0": 60, "f1": 30, "ms": 700, "db": -5}])
s("healbeam", "loop", 2.5, f"a soothing magical healing beam, a warm humming shimmer, continuous, {DRY}", 6, loop=True)
s("healbeam2", "loop", 2.5, f"a low pulsing dark magic energy beam humming, continuous, {DRY}", 6, loop=True)
s("groove_heal", "loop", 8.0, "a mellow lo-fi hip hop beat with a warm bassline and soft keys, 90 BPM, seamless loop", 4, loop=True)
s("groove_speed", "loop", 8.0, "an energetic drum and bass breakbeat with a driving bassline, 174 BPM, seamless loop", 4, loop=True)

# first-person foley (evera-f4's viewmodel calls AudioKit.PlayFp at the clip's event times; the sim's shot / draw sounds
# stay as they are - these fill the detail between them, in your head, quiet)
s("fp_bow_ready", "move", 0.3, f"a bow string reaching full tension, a tiny bright taut ting with a faint wooden creak, {DRY}", 8, 2)
s("fp_quiver_reach", "move", 0.25, f"a hand sweeping up to a leather quiver, a quick cloth and leather swish, {DRY}", 8, 3)
s("fp_arrow_draw", "move", 0.25, f"an arrow pulled out of a leather quiver, wooden arrow shafts rattling softly, {DRY}", 8, 3)
s("fp_nock", "move", 0.2, f"an arrow nocked onto a bow string, a small crisp wooden click, {DRY}", 10, 3, layers=[{"k": "click", "n": 1, "gap": 10, "f": 2800, "db": -16}])
s("fp_aim_in", "move", 0.2, f"raising a bow to aim, a quick leather and cloth shift with a soft creak, {DRY}", 8, 2)
s("fp_aim_out", "move", 0.15, f"lowering a bow, a quick cloth rustle and a soft leather snap, {DRY}", 8, 2)
s("fp_bow_kick", "move", 0.2, f"a bow limb snapping forward hard, a sharp wooden thwack, {DRY}", 8, 2, layers=[{"k": "crack", "ms": 3, "hp": 2500, "db": -14}])

# Yuzu's Unity ult (evera-a0's rework: 5 giant sun arrows -> ~1000 homing arrows for 15 s)
s("yuzuult_land", "impact", 1.2, f"a giant glowing arrow slamming into the earth, a heavy blade impact into rock with a bright solar flare whoosh, {DRY}", 10, 3,
  layers=[{"k": "sub", "f0": 80, "f1": 35, "ms": 300, "db": -6}, {"k": "debris", "ms": 700, "grain": "stone", "n": 24, "db": -19, "at": 60}])
s("yuzuult_split", "impact", 1.4, f"a giant crystal arrow shattering into a thousand small arrows, a glassy metallic burst into a rising swarm of whooshes, {DRY}", 8, 1,
  layers=[{"k": "shimmer", "ms": 700, "db": -16}])
s("yuzuult_swarm", "loop", 6.0, f"a dense swarm of hundreds of small arrows flying and circling, a continuous fluttering whoosh with high whistles, {DRY}", 6, loop=True)
s("yuzuult_hit", "impact", 0.3, f"a small arrow thudding into armour, a short sharp thunk, {DRY}", 8, 3)

# ---------------------------------------------------------------- ambience: 60 s beds (a 7 s bed repeats audibly) + emitters
for mid, desc in {
    "amatsu": "a mountain shrine at dawn, gentle wind through trees, wind chimes and distant birds",
    "kurogane": "a rainy neon city street at night, steady rain on pavement, distant traffic and electric hum",
    "hangar": "a huge empty industrial hall, low machinery hum, distant metal clanks echoing",
    "cathedral": "a dark gothic cathedral at night, howling wind outside and a distant bell",
    "rift": "an eerie alien void, deep slowly shifting drones and faint airy whispers",
    "hanabi": "a harbour festival at night, distant crowd murmur, water lapping on the quay, fireworks far away",
    "cloudstep": "high mountain terraces above the clouds, strong steady wind and distant birds",
    "kagura": "a Japanese festival street, distant taiko drums and crowd chatter",
    "training": "a quiet indoor training facility, soft ventilation and faint electronic hum",
    "lantern": "a lantern-lit night market, distant crowd murmur, soft wind chimes, a canal trickling, faint taiko far away",
    "starfall": "a mountaintop observatory above the snowline, cold wind gusting, distant temple bells, soft snow hiss",
    "foundry": "a busy forge foundry, rhythmic distant hammering, furnace roar, molten metal hiss and heavy machinery hum",
}.items():
    s(f"amb_{mid}", "amb", 60.0, f"{desc}, {FIELD}, seamless ambience", 3, loop=True)

# positional emitters (evera-7a's AmbienceSpots, evera-fb's Iron Gulch props)
s("amb_waves", "amb", 20.0, f"gentle sea waves lapping against a stone harbour wall, {FIELD}", 4, loop=True)
s("amb_cloudwind", "amb", 20.0, f"high altitude wind rushing over a cliff edge above the clouds, {FIELD}", 4, loop=True)
s("amb_lanterns", "amb", 12.0, f"paper lanterns swaying in a light breeze, soft creaks and a faint electric hum, {DRY}", 4, loop=True)
s("amb_windpump_creak", "amb", 12.0, f"an old metal windmill water pump turning slowly, rhythmic creaking and squeaking metal, {FIELD}", 4, loop=True)
s("amb_lamp_hum", "amb", 8.0, f"an old electric street lamp buzzing, a steady low electrical hum, {DRY}", 4, loop=True)

SOUNDS = S
