using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Single source of truth for the game's STABLE materials (same every level).
    /// Each one can exist as an editable .mat asset under Resources/Materials; if the
    /// asset is missing it falls back to a runtime-generated URP/Lit material with the
    /// same default values, so nothing breaks. (Theme-tinted materials — ground, props,
    /// etc. — are intentionally NOT here; their color comes from Theme.cs per level.)
    /// </summary>
    public static class MaterialLibrary
    {
        public const string ResourcePrefix = "Materials/";          // for Resources.Load
        public const string AssetFolder = "Assets/Resources/Materials"; // for the editor generator

        public struct Spec
        {
            public string key;
            public Color color;
            public float smoothness;
            public float emission;
            public Spec(string k, Color c, float s, float e) { key = k; color = c; smoothness = s; emission = e; }
        }

        public static string BusKey(PieceColor c) => "Bus_" + c;

        /// <summary>The canonical list, shared by the runtime loader and the editor generator
        /// so defaults never drift. Default values reproduce the current look (emissive pop).</summary>
        public static List<Spec> AllSpecs()
        {
            var list = new List<Spec>();
            foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
                list.Add(new Spec(BusKey(c), Palette.ToColor(c), 0.65f, 0.12f)); // smooth candy bodies (metallic forced 0 in MakeRuntime; lower emission = no eye-hurt)

            list.Add(new Spec("Glass",     new Color(0.18f, 0.26f, 0.40f), 0.85f, 0f));
            list.Add(new Spec("Wheel",     new Color(0.12f, 0.12f, 0.14f), 0.2f,  0f));
            list.Add(new Spec("Headlight", new Color(1f,    0.96f, 0.72f), 0.6f,  0.5f));
            list.Add(new Spec("Skin",      Palette.Skin,                   0.1f,  0f));
            list.Add(new Spec("SeatEmpty", Palette.SeatEmpty,              0.2f,  0f));
            list.Add(new Spec("Mystery",   Palette.Mystery,                0.2f,  0f));
            list.Add(new Spec("Gold",      Palette.Gold,                   0.7f,  0.4f));
            list.Add(new Spec("Arrow",     new Color(0.99f, 0.99f, 0.99f), 0.3f,  0.25f));
            list.Add(new Spec("Lock",      new Color(0.42f, 0.9f,  0.48f), 0.2f,  0.25f));
            list.Add(new Spec("Board",     new Color(0.20f, 0.22f, 0.28f), 0.08f, 0f));
            list.Add(new Spec("GridLine",  new Color(0.36f, 0.40f, 0.50f), 0.1f,  0.12f));
            list.Add(new Spec("SlotPad",   new Color(0.35f, 0.38f, 0.45f), 0.25f, 0f));
            return list;
        }

        // ---- Theme (environment) materials: one editable asset per theme × type ----
        // Glossy candy look: smoothness lifted off matte + a soft emission pop on the colorful types.
        static readonly (string type, float smooth, float emission)[] ThemeTypes =
        {
            ("Ground", 0.35f, 0.05f), ("Field", 0.28f, 0.03f), ("Road", 0.30f, 0f), ("Accent", 0.45f, 0.06f),
            ("PropMain", 0.45f, 0.05f), ("PropAlt", 0.45f, 0.05f), ("Foliage", 0.35f, 0.06f), ("Trunk", 0.25f, 0f),
            ("Window", 0.7f, 0.25f), ("Cloud", 0f, 0.18f), ("Grass", 0.30f, 0.06f),
            ("Facade", 0.40f, 0.05f), ("FacadeTrim", 0.45f, 0.06f), ("FacadeDoor", 0.55f, 0.12f)
        };

        static Color ThemeColor(Theme th, string type)
        {
            switch (type)
            {
                case "Ground":   return th.ground;
                case "Field":    return th.field;
                case "Road":     return th.road;
                case "Accent":   return th.accent;
                case "PropMain": return th.propMain;
                case "PropAlt":  return th.propAlt;
                case "Foliage":  return th.foliage;
                case "Trunk":    return th.trunk;
                case "Grass":    return th.grass;
                case "Window":   return new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f);
                case "Facade":     return th.propMain;
                case "FacadeTrim": return th.propAlt;
                case "FacadeDoor": return new Color(th.accent.r * 0.35f, th.accent.g * 0.35f, th.accent.b * 0.35f, 1f); // dark doorway
                default:         return Color.white; // Cloud
            }
        }

        public static string ThemeKey(string themeName, string type) => themeName + "_" + type;

        /// <summary>Per-theme env material specs (key = "City_Ground" etc.), shared by the generator.</summary>
        public static List<Spec> ThemeSpecs()
        {
            var list = new List<Spec>();
            foreach (var th in Themes.AllThemes())
                foreach (var (type, smooth, emission) in ThemeTypes)
                    list.Add(new Spec(ThemeKey(th.name, type), ThemeColor(th, type), smooth, emission));
            return list;
        }

        /// <summary>Loads the editable per-theme asset if present, else a runtime fallback.</summary>
        public static Material GetTheme(string themeName, string type, Color defColor, float smooth, float emission = 0f)
        {
            var asset = Resources.Load<Material>(ResourcePrefix + ThemeKey(themeName, type));
            return asset != null ? asset : MakeRuntime(defColor, smooth, emission);
        }

        /// <summary>Resolves every spec: the editable asset if present, else a runtime material.</summary>
        public static Dictionary<string, Material> BuildAll()
        {
            var dict = new Dictionary<string, Material>();
            foreach (var s in AllSpecs())
            {
                var asset = Resources.Load<Material>(ResourcePrefix + s.key);
                dict[s.key] = asset != null ? asset : MakeRuntime(s.color, s.smoothness, s.emission);
            }
            return dict;
        }

        /// <summary>
        /// Punches a color toward candy vibrance: HSV saturation (and a touch of value) up.
        /// Greys / near-neutrals (S≈0 — wheels, board, arrow) stay neutral, so only the
        /// colorful bodies pop. This is the per-material lift; a global grade in BusJamGame's
        /// post-processing volume then lifts the whole frame (incl. the baked theme materials).
        /// </summary>
        public static Color Vibrant(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 1.28f + 0.04f);
            v = Mathf.Clamp01(v * 1.03f);
            Color rgb = Color.HSVToRGB(h, s, v);
            rgb.a = c.a;
            return rgb;
        }

        /// <summary>The single URP/Lit factory used by the runtime fallback and the asset generator.</summary>
        public static Material MakeRuntime(Color col, float smooth, float emission = 0f)
        {
            col = Vibrant(col); // bright candy look — faded source colors come out vivid
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (emission > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", col * emission);
            }
            return m;
        }
    }
}
