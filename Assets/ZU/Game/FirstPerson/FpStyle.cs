// Each hero's viewmodel personality (port of FirstPerson.ts FP_STYLE): how the hero holds their weapon in first person -
// the rest targets of the two hands in VIEW space (metres right, up, forward from the eye; the Unity camera's own
// local axes, so no mirroring: the sim's right-handed model space is already mirrored into Unity's by the import), the
// recoil per shot and the per-hero liberties the arms-only cut needs (keep / drape / squeeze / clip / push).
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.FirstPerson
{
    public enum Grip { Rifle, Pistol, Katana, Bow, Caster, Kunai, Fists, Hammer, Shotgun, Dual }

    public class FpStyle
    {
        public Grip grip;
        public Vector3 R;                 // the main (right) hand's rest target
        public Vector3? L;                // the off hand (null: hangs out of view)
        public float recoil;
        /// <summary>eye forward past a bulky collar / coat (m)</summary>
        public float push;
        /// <summary>cut viewmodel geometry closer than this to the camera (m): the overlay camera's near plane</summary>
        public float clip;
        /// <summary>how much hand weight a triangle needs to stay in the viewmodel (armsOnly)</summary>
        public float keep = 0.75f;
        /// <summary>drop arm-weighted triangles further than this (x forearm length) from their bone (sleeve cloth and sashes)</summary>
        public float drape;
        /// <summary>pull arm geometry further than this (x forearm length) from its bone radially in to that distance (wide sleeves)</summary>
        public float squeeze;
        /// <summary>the held props' size in the viewmodel (concept-sized world props would fill the view)</summary>
        public float gunScale = 1;
        /// <summary>a viewmodel liberty for rigs whose hands can't meet in front of the lens: shoulders drawn in to this
        /// half-width (m), upper arms lengthened by this factor (both behind the camera)</summary>
        public float shoulderW, reach;
        /// <summary>rigid first-person forearms + hands mounted on the held haft (Resources/ZUProps/<name>.prefab, nodes
        /// gauntlet_L / gauntlet_R) for a rig whose own arms can't carry its hands</summary>
        public string gauntlets;

        static FpStyle S(Grip g, float rx, float ry, float rz, float? lx, float? ly, float? lz, float recoil) =>
            new FpStyle { grip = g, R = new Vector3(rx, ry, rz), L = lx.HasValue ? new Vector3(lx.Value, ly.Value, lz.Value) : (Vector3?)null, recoil = recoil };

        public static readonly FpStyle DEFAULT = S(Grip.Rifle, 0.16f, -0.15f, 0.34f, 0.03f, -0.14f, 0.5f, 0.04f);

        public static readonly Dictionary<string, FpStyle> TABLE = new Dictionary<string, FpStyle>
        {
            { "raijin", With(S(Grip.Katana, 0.21f, -0.21f, 0.44f, -0.17f, -0.24f, 0.42f, 0), s => { s.clip = 0.14f; s.keep = 0.97f; }) },
            // the bow held left of the reticle and canted (an archer's first-person read), sized down for the viewmodel; the
            // string hand rests on the nocked arrow beside the grip (Hanzo's ready pose)
            { "yuzu", With(S(Grip.Bow, 0.06f, -0.24f, 0.1f, -0.03f, -0.18f, 0.5f, 0), s => { s.clip = 0.12f; s.gunScale = 0.8f; }) },
            // the wide kimono sleeves are squeezed into slim tubes for the viewmodel (they'd fill the screen), hands well forward
            { "kaien", With(S(Grip.Caster, 0.21f, -0.2f, 0.44f, -0.21f, -0.17f, 0.46f, 0.03f), s => { s.keep = 0.97f; s.squeeze = 0.12f; }) },
            { "mirei", S(Grip.Caster, 0.22f, -0.2f, 0.42f, -0.19f, -0.24f, 0.4f, 0.02f) },
            { "nocturne", S(Grip.Caster, 0.17f, -0.17f, 0.42f, -0.18f, -0.19f, 0.42f, 0.02f) },
            { "hex", S(Grip.Caster, 0.18f, -0.16f, 0.42f, -0.18f, -0.16f, 0.42f, 0.025f) },
            { "kagemaru", S(Grip.Kunai, 0.19f, -0.16f, 0.4f, -0.2f, -0.23f, 0.4f, 0) },
            // his spiked pauldrons and gauntlet spikes crowd the lens: clipped close, only hand-weighted triangles kept
            { "enra", With(S(Grip.Fists, 0.21f, -0.18f, 0.44f, -0.21f, -0.2f, 0.44f, 0), s => { s.push = 0.05f; s.clip = 0.22f; s.keep = 0.95f; s.drape = 0.35f; }) },
            { "haruto", S(Grip.Pistol, 0.17f, -0.16f, 0.4f, 0.02f, -0.26f, 0.34f, 0.05f) },
            // Reinhardt's viewmodel: both gauntlets on the haft low right, the haft out to the right, the head resting right of
            // centre (the rest pose of FpHammer.REST - the hammer proc drives the whole swing)
            { "tenkai", With(S(Grip.Hammer, 0.2f, -0.27f, 0.5f, 0.329f, -0.27f, 0.653f, 0), s => { s.shoulderW = 0.2f; s.reach = 1.9f; s.gauntlets = "prop_tenkai_gauntlets"; }) },
            // mech claws half a metre across: out in the bottom corners and well forward (as Gantetsu's guns), the near arm cut away
            { "gorgoth", With(S(Grip.Shotgun, 0.34f, -0.34f, 0.56f, -0.34f, -0.34f, 0.56f, 0.09f), s => { s.clip = 0.25f; s.gunScale = 0.8f; }) },
            // hip-held twin chainguns in the bottom corners, angled in on the reticle, the rear of each gun out of view
            { "gantetsu", With(S(Grip.Dual, 0.4f, -0.28f, 0.56f, -0.4f, -0.28f, 0.56f, 0.03f), s => { s.push = 0.08f; s.gunScale = 0.7f; }) },
            { "hibiki", S(Grip.Pistol, 0.2f, -0.2f, 0.44f, -0.18f, -0.21f, 0.4f, 0.05f) },
            // the scattergun one-handed on the right, the Crescent Fang held low in the left fist
            { "tomoe", With(S(Grip.Shotgun, 0.21f, -0.2f, 0.44f, -0.24f, -0.24f, 0.38f, 0.1f), s => { s.push = 0.04f; s.keep = 0.97f; s.drape = 0.5f; }) },
            // koi-scale shuriken flicked from the chest (the scarf is cut out: it wraps the neck, not the arms)
            { "hayate", With(S(Grip.Kunai, 0.17f, -0.17f, 0.4f, -0.17f, -0.18f, 0.4f, 0), s => { s.keep = 0.9f; }) },
            // the Riverbow in the left hand, the draw hand on the right; his robe sleeves squeezed to slim tubes and the
            // quiver over his shoulder clipped, so the bow arm doesn't wall off the view
            { "seiran", With(S(Grip.Bow, 0.06f, -0.24f, 0.1f, -0.03f, -0.18f, 0.5f, 0), s => { s.keep = 0.9f; s.squeeze = 0.16f; s.clip = 0.14f; s.gunScale = 0.8f; }) },
        };
        static FpStyle With(FpStyle s, System.Action<FpStyle> f) { f(s); return s; }
        public static FpStyle For(string heroId) => TABLE.TryGetValue(heroId, out var s) ? s : DEFAULT;
    }

    /// <summary>the viewmodel events a hero's library can hold (the FP controller's state names)</summary>
    public static class FpClips
    {
        public static readonly string[] NAMES = { "fp_idle", "fp_fire", "fp_fire2", "fp_alt", "fp_melee", "fp_reload", "fp_ability1", "fp_ability2", "fp_ult", "fp_hit", "fp_land",
            "fp_beam", "fp_draw", "fp_inspect", "fp_equip" };
        public const string RATE = "Rate";
    }
}
