using UnityEngine;

namespace BusJam
{
    public enum PropKind { Building, Cactus, Pine, Palm }

    public struct Theme
    {
        public string name;
        public Color sky;
        public Color ground;   // play plaza
        public Color field;    // far ground / surround
        public Color road;
        public Color accent;   // slots / fences
        public Color propMain;
        public Color propAlt;
        public Color foliage;
        public Color trunk;
        public PropKind prop;
        public Color ambient;
    }

    public static class Themes
    {
        public const int LevelsPerTheme = 4;

        static readonly Theme[] All =
        {
            new Theme {
                name = "City", sky = new Color(0.58f,0.74f,0.90f), ground = new Color(0.82f,0.84f,0.88f),
                field = new Color(0.62f,0.68f,0.62f), road = new Color(0.34f,0.36f,0.40f),
                accent = new Color(0.46f,0.52f,0.62f), propMain = new Color(0.80f,0.55f,0.52f),
                propAlt = new Color(0.58f,0.66f,0.78f), foliage = new Color(0.42f,0.70f,0.45f),
                trunk = new Color(0.45f,0.32f,0.22f), prop = PropKind.Building, ambient = new Color(0.55f,0.57f,0.62f)
            },
            new Theme {
                name = "Desert", sky = new Color(0.96f,0.82f,0.60f), ground = new Color(0.92f,0.80f,0.55f),
                field = new Color(0.86f,0.72f,0.45f), road = new Color(0.70f,0.60f,0.42f),
                accent = new Color(0.80f,0.66f,0.44f), propMain = new Color(0.86f,0.62f,0.38f),
                propAlt = new Color(0.78f,0.55f,0.34f), foliage = new Color(0.45f,0.66f,0.40f),
                trunk = new Color(0.50f,0.40f,0.28f), prop = PropKind.Cactus, ambient = new Color(0.66f,0.60f,0.50f)
            },
            new Theme {
                name = "Park", sky = new Color(0.60f,0.80f,0.92f), ground = new Color(0.66f,0.82f,0.60f),
                field = new Color(0.48f,0.72f,0.46f), road = new Color(0.62f,0.58f,0.50f),
                accent = new Color(0.52f,0.66f,0.50f), propMain = new Color(0.55f,0.75f,0.55f),
                propAlt = new Color(0.70f,0.80f,0.62f), foliage = new Color(0.36f,0.68f,0.40f),
                trunk = new Color(0.45f,0.32f,0.22f), prop = PropKind.Pine, ambient = new Color(0.58f,0.62f,0.56f)
            },
            new Theme {
                name = "Night", sky = new Color(0.16f,0.18f,0.32f), ground = new Color(0.28f,0.30f,0.42f),
                field = new Color(0.20f,0.22f,0.32f), road = new Color(0.18f,0.19f,0.26f),
                accent = new Color(0.45f,0.50f,0.75f), propMain = new Color(0.30f,0.34f,0.52f),
                propAlt = new Color(0.40f,0.44f,0.66f), foliage = new Color(0.30f,0.55f,0.40f),
                trunk = new Color(0.32f,0.28f,0.24f), prop = PropKind.Building, ambient = new Color(0.40f,0.42f,0.55f)
            },
            new Theme {
                name = "Beach", sky = new Color(0.55f,0.84f,0.92f), ground = new Color(0.94f,0.86f,0.66f),
                field = new Color(0.40f,0.74f,0.82f), road = new Color(0.84f,0.76f,0.56f),
                accent = new Color(0.78f,0.70f,0.50f), propMain = new Color(0.92f,0.78f,0.52f),
                propAlt = new Color(0.50f,0.78f,0.80f), foliage = new Color(0.40f,0.72f,0.46f),
                trunk = new Color(0.55f,0.42f,0.28f), prop = PropKind.Palm, ambient = new Color(0.62f,0.66f,0.66f)
            },
        };

        public static Theme For(int level)
        {
            int idx = ((Mathf.Max(1, level) - 1) / LevelsPerTheme) % All.Length;
            return All[idx];
        }

        /// <summary>All themes (used to generate per-theme material assets).</summary>
        public static Theme[] AllThemes() => All;
    }
}
