using UnityEngine;

namespace BusJam
{
    public enum PropKind { Building, Cactus, Pine, Palm, RoundTree, Bush, House }

    public struct Theme
    {
        public string name;
        public Color sky;
        public Color ground;   // play plaza (lot under the grid)
        public Color field;    // far ground / surround
        public Color road;
        public Color accent;   // slots / fences
        public Color propMain; // building/house body
        public Color propAlt;  // roof / secondary
        public Color foliage;
        public Color trunk;
        public Color grass;     // grass-tuft color
        public PropKind prop;   // primary side scatter
        public PropKind prop2;  // secondary scatter (bushes / trees)
        public bool hasHouse;   // build a house centerpiece (Park hero)
        public bool hasFacade;  // build the closed mall/terminal facade behind the people band (people emerge from its doors)
        public Color ambient;
        public Color lightColor;
        public float lightIntensity;
    }

    public static class Themes
    {
        public const int LevelsPerTheme = 4;

        // Candy-pastel set. Park is FIRST so Themes.For(1) = the polished hero level.
        static readonly Theme[] All =
        {
            new Theme {
                name = "Park", sky = new Color(0.56f,0.80f,0.95f), ground = new Color(0.56f,0.80f,0.46f),
                field = new Color(0.43f,0.73f,0.41f), road = new Color(0.72f,0.67f,0.58f),
                accent = new Color(0.92f,0.93f,0.80f), propMain = new Color(0.96f,0.72f,0.56f),
                propAlt = new Color(0.86f,0.45f,0.40f), foliage = new Color(0.40f,0.73f,0.43f),
                trunk = new Color(0.50f,0.36f,0.24f), grass = new Color(0.50f,0.79f,0.43f),
                prop = PropKind.RoundTree, prop2 = PropKind.Bush, hasHouse = true, hasFacade = true,
                ambient = new Color(0.64f,0.68f,0.62f), lightColor = new Color(1f,0.97f,0.88f), lightIntensity = 1.35f
            },
            new Theme {
                name = "City", sky = new Color(0.62f,0.78f,0.92f), ground = new Color(0.79f,0.81f,0.86f),
                field = new Color(0.56f,0.69f,0.55f), road = new Color(0.40f,0.42f,0.48f),
                accent = new Color(0.56f,0.63f,0.73f), propMain = new Color(0.83f,0.58f,0.55f),
                propAlt = new Color(0.60f,0.68f,0.80f), foliage = new Color(0.43f,0.71f,0.46f),
                trunk = new Color(0.45f,0.32f,0.22f), grass = new Color(0.50f,0.73f,0.47f),
                prop = PropKind.Building, prop2 = PropKind.RoundTree, hasHouse = false, hasFacade = true,
                ambient = new Color(0.58f,0.60f,0.64f), lightColor = new Color(1f,0.98f,0.92f), lightIntensity = 1.3f
            },
            new Theme {
                name = "Candy", sky = new Color(0.98f,0.80f,0.90f), ground = new Color(0.97f,0.79f,0.87f),
                field = new Color(0.85f,0.66f,0.86f), road = new Color(0.80f,0.62f,0.74f),
                accent = new Color(0.98f,0.71f,0.52f), propMain = new Color(0.96f,0.55f,0.71f),
                propAlt = new Color(0.70f,0.85f,0.96f), foliage = new Color(0.71f,0.90f,0.71f),
                trunk = new Color(0.80f,0.56f,0.46f), grass = new Color(0.85f,0.92f,0.71f),
                prop = PropKind.RoundTree, prop2 = PropKind.Bush, hasHouse = false, hasFacade = true,
                ambient = new Color(0.72f,0.64f,0.70f), lightColor = new Color(1f,0.95f,0.96f), lightIntensity = 1.35f
            },
            new Theme {
                name = "Forest", sky = new Color(0.60f,0.82f,0.78f), ground = new Color(0.46f,0.67f,0.43f),
                field = new Color(0.31f,0.53f,0.35f), road = new Color(0.53f,0.46f,0.36f),
                accent = new Color(0.62f,0.72f,0.50f), propMain = new Color(0.52f,0.41f,0.30f),
                propAlt = new Color(0.41f,0.61f,0.41f), foliage = new Color(0.27f,0.59f,0.35f),
                trunk = new Color(0.42f,0.30f,0.20f), grass = new Color(0.37f,0.63f,0.37f),
                prop = PropKind.Pine, prop2 = PropKind.RoundTree, hasHouse = false, hasFacade = true,
                ambient = new Color(0.50f,0.58f,0.50f), lightColor = new Color(0.95f,0.98f,0.88f), lightIntensity = 1.2f
            },
            new Theme {
                name = "Sunset", sky = new Color(0.98f,0.63f,0.56f), ground = new Color(0.86f,0.63f,0.56f),
                field = new Color(0.71f,0.51f,0.56f), road = new Color(0.56f,0.43f,0.46f),
                accent = new Color(0.98f,0.79f,0.51f), propMain = new Color(0.81f,0.46f,0.51f),
                propAlt = new Color(0.61f,0.46f,0.66f), foliage = new Color(0.56f,0.56f,0.46f),
                trunk = new Color(0.45f,0.32f,0.28f), grass = new Color(0.67f,0.59f,0.43f),
                prop = PropKind.Palm, prop2 = PropKind.Bush, hasHouse = false, hasFacade = true,
                ambient = new Color(0.63f,0.53f,0.53f), lightColor = new Color(1f,0.80f,0.62f), lightIntensity = 1.3f
            },
            new Theme {
                name = "Beach", sky = new Color(0.55f,0.84f,0.92f), ground = new Color(0.96f,0.88f,0.68f),
                field = new Color(0.42f,0.76f,0.84f), road = new Color(0.86f,0.78f,0.58f),
                accent = new Color(0.80f,0.72f,0.52f), propMain = new Color(0.94f,0.80f,0.54f),
                propAlt = new Color(0.52f,0.80f,0.82f), foliage = new Color(0.42f,0.74f,0.48f),
                trunk = new Color(0.55f,0.42f,0.28f), grass = new Color(0.80f,0.84f,0.55f),
                prop = PropKind.Palm, prop2 = PropKind.Bush, hasHouse = false, hasFacade = true,
                ambient = new Color(0.64f,0.68f,0.66f), lightColor = new Color(1f,0.98f,0.88f), lightIntensity = 1.4f
            },
            new Theme {
                name = "Desert", sky = new Color(0.97f,0.84f,0.62f), ground = new Color(0.93f,0.81f,0.56f),
                field = new Color(0.87f,0.73f,0.46f), road = new Color(0.72f,0.62f,0.44f),
                accent = new Color(0.82f,0.68f,0.46f), propMain = new Color(0.88f,0.64f,0.40f),
                propAlt = new Color(0.80f,0.57f,0.36f), foliage = new Color(0.50f,0.70f,0.42f),
                trunk = new Color(0.52f,0.42f,0.30f), grass = new Color(0.78f,0.72f,0.45f),
                prop = PropKind.Cactus, prop2 = PropKind.Cactus, hasHouse = false, hasFacade = true,
                ambient = new Color(0.68f,0.62f,0.50f), lightColor = new Color(1f,0.95f,0.78f), lightIntensity = 1.45f
            },
            new Theme {
                name = "Snow", sky = new Color(0.80f,0.88f,0.96f), ground = new Color(0.92f,0.95f,0.99f),
                field = new Color(0.82f,0.88f,0.94f), road = new Color(0.70f,0.76f,0.84f),
                accent = new Color(0.70f,0.82f,0.92f), propMain = new Color(0.85f,0.90f,0.96f),
                propAlt = new Color(0.60f,0.74f,0.88f), foliage = new Color(0.40f,0.62f,0.50f),
                trunk = new Color(0.42f,0.32f,0.26f), grass = new Color(0.86f,0.92f,0.96f),
                prop = PropKind.Pine, prop2 = PropKind.RoundTree, hasHouse = false, hasFacade = true,
                ambient = new Color(0.66f,0.70f,0.78f), lightColor = new Color(0.92f,0.95f,1f), lightIntensity = 1.25f
            },
            new Theme {
                name = "Night", sky = new Color(0.14f,0.16f,0.30f), ground = new Color(0.31f,0.33f,0.47f),
                field = new Color(0.20f,0.22f,0.34f), road = new Color(0.20f,0.21f,0.28f),
                accent = new Color(0.50f,0.55f,0.80f), propMain = new Color(0.32f,0.36f,0.55f),
                propAlt = new Color(0.45f,0.50f,0.72f), foliage = new Color(0.30f,0.55f,0.42f),
                trunk = new Color(0.32f,0.28f,0.24f), grass = new Color(0.30f,0.46f,0.40f),
                prop = PropKind.Building, prop2 = PropKind.RoundTree, hasHouse = false, hasFacade = true,
                ambient = new Color(0.42f,0.45f,0.58f), lightColor = new Color(0.70f,0.74f,0.95f), lightIntensity = 1.0f
            },
            new Theme {
                name = "Autumn", sky = new Color(0.92f,0.82f,0.70f), ground = new Color(0.80f,0.66f,0.46f),
                field = new Color(0.68f,0.54f,0.38f), road = new Color(0.58f,0.46f,0.36f),
                accent = new Color(0.88f,0.66f,0.42f), propMain = new Color(0.80f,0.50f,0.34f),
                propAlt = new Color(0.86f,0.58f,0.34f), foliage = new Color(0.85f,0.52f,0.30f),
                trunk = new Color(0.44f,0.32f,0.24f), grass = new Color(0.72f,0.62f,0.40f),
                prop = PropKind.RoundTree, prop2 = PropKind.Bush, hasHouse = false, hasFacade = true,
                ambient = new Color(0.62f,0.56f,0.48f), lightColor = new Color(1f,0.90f,0.72f), lightIntensity = 1.3f
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
