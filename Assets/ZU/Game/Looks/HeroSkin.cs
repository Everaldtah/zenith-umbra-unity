// Hero skins on the body (port of src/data/skins.ts's equip side and CharacterView.applySkin): which skin an actor wears,
// and the look values a skin sets. The skins themselves are exported data (ZuData Skins, from skins.ts: palette recolours of
// each hero's own texture - the costume's dominant hue -> primary, its second hue -> accent, whites / blacks -> a neutral
// tint; epic and legendary add glowing trims, metal and flowing energy; a model skin swaps the whole body instead, through
// ActorViews.SkinModel). TS Game.addView: the local player's hero wears its equipped skin, everyone else Classic; the
// Hero Viewer shows the skin being previewed (HeroViewer: view.setSkin) - Show(actor, id).
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.Looks
{
    public static class HeroSkin
    {
        /// <summary>the equipped skin per hero (TS equippedSkin; the front end's store - HeroViewerView.EquippedSkin / EquipSkin)</summary>
        public static string Equipped(string heroId) => PlayerPrefs.GetString("zu-skin-" + heroId, "classic");

        /// <summary>which skin an actor's body wears (TS Game.addView: a.isPlayer ? equippedSkin(a.def.id) : 'classic'); the
        /// front end may replace it</summary>
        public static System.Func<Actor, string> For = a => a.isPlayer ? Equipped(a.def.id) : "classic";

        static readonly ConditionalWeakTable<Actor, string> shown = new ConditionalWeakTable<Actor, string>();
        /// <summary>the Hero Viewer's preview (TS HeroViewer: view.setSkin(id)): this actor shows `skinId` whatever is equipped
        /// (null: back to For)</summary>
        public static void Show(Actor a, string skinId) { if (a == null) return; shown.Remove(a); if (skinId != null) shown.Add(a, skinId); }
        /// <summary>the skin this actor's body shows now</summary>
        public static string Of(Actor a) => a == null ? "classic" : shown.TryGetValue(a, out var s) ? s : For?.Invoke(a) ?? "classic";

        /// <summary>TS skinsFor(heroId).find(s => s.id === id) ?? the first (Classic); null for a hero without skins</summary>
        public static Skin Find(string heroId, string id)
        {
            var d = ZuData.Get();
            if (d?.Skins == null || heroId == null || !d.Skins.TryGetValue(heroId, out var list) || list == null || list.Count == 0) return null;
            foreach (var s in list) if (s.id == id) return s;
            return list[0];
        }

        /// <summary>what a skin puts in the hero shader (the TS look uniforms applySkin sets)</summary>
        public struct Values
        {
            public float remap, has1, has2, metal, glow, pattern;
            public Vector4 dst1, dst2, neutral, patternColor;     // linear rgb, as the TS uniforms hold them
            public static Values Classic => new Values { dst1 = Vector4.one, dst2 = Vector4.one, neutral = Vector4.one, patternColor = Vector4.one };
        }

        /// <summary>TS applySkin</summary>
        public static Values ValuesOf(Skin s)
        {
            if (s == null) return Values.Classic;
            string neutral = s.neutral ?? "#ffffff";
            bool on = s.primary != null || s.accent != null || neutral.ToLowerInvariant() != "#ffffff";
            return new Values
            {
                remap = on ? 1 : 0,
                has1 = s.primary != null ? 1 : 0, has2 = s.accent != null ? 1 : 0,
                // TS-PARITY: three's Color.set already turns the sRGB hex into linear (ColorManagement is on in r186), and
                // applySkin calls convertSRGBToLinear() on top - the skin colours reach the shader linearised twice
                dst1 = Twice(s.primary ?? "#ffffff"), dst2 = Twice(s.accent ?? "#ffffff"), neutral = Twice(neutral),
                metal = (float)s.metal, glow = (float)s.glow, pattern = (float)s.pattern,
                patternColor = Lin(s.patternColor ?? "#ffffff"),
            };
        }

        /// <summary>a hex colour as three holds it after Color.set: sRGB -> linear (three's SRGBToLinear)</summary>
        public static Vector4 Lin(string hex) { var c = Conv.Hex(hex); return new Vector4(S2L(c.r), S2L(c.g), S2L(c.b), 1); }
        static Vector4 Twice(string hex) { var c = Lin(hex); return new Vector4(S2L(c.x), S2L(c.y), S2L(c.z), 1); }
        static float S2L(float c) => c < 0.04045f ? c * 0.0773993808f : Mathf.Pow(c * 0.9478672986f + 0.0521327014f, 2.4f);
    }
}
