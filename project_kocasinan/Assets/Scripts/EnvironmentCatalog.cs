using UnityEngine;

namespace BusJam
{
    /// <summary>Optional per-theme color/light override fed into ApplyTheme's GetTheme defaults, so even
    /// prefab-LESS (procedural) elements re-tint to match — edit a theme's palette without recompiling Theme.cs.</summary>
    [System.Serializable]
    public class ThemeColors
    {
        public Color sky = new Color(0.55f, 0.78f, 0.95f);
        public Color ground = new Color(0.55f, 0.78f, 0.55f);
        public Color field = new Color(0.45f, 0.70f, 0.45f);
        public Color road = new Color(0.30f, 0.31f, 0.34f);
        public Color accent = new Color(0.85f, 0.80f, 0.55f);
        public Color propMain = new Color(0.85f, 0.55f, 0.45f);
        public Color propAlt = new Color(0.65f, 0.40f, 0.35f);
        public Color foliage = new Color(0.35f, 0.65f, 0.40f);
        public Color trunk = new Color(0.45f, 0.32f, 0.22f);
        public Color grass = new Color(0.40f, 0.70f, 0.42f);
        public Color ambient = new Color(0.55f, 0.58f, 0.62f);
        public Color lightColor = Color.white;
        public float lightIntensity = 1f;
    }

    /// <summary>
    /// Drop-in ENVIRONMENT (cosmetic) per THEME — assign your OWN prefabs for the ground, road, fence, facade,
    /// house, side props, back row, and clouds, and tweak fit/colors from the Inspector. One asset per theme at
    /// Resources/Environments/Environment_&lt;Theme&gt;.asset, looked up by themeName (must equal a Theme.name).
    ///
    /// Any slot left EMPTY falls back to today's procedural LowPolyBuilder look UNCHANGED. This is COSMETIC ONLY:
    /// it never touches occ / slots / queue / solver geometry. The single gameplay-adjacent detail is the facade's
    /// door — a custom facade should expose a child transform named `doorAnchorName` to place the boarding queue;
    /// without one it defaults to x=3.5 (today's value). Build the 11 assets with "BusJam ▸ Build Environment Catalogs".
    /// </summary>
    [CreateAssetMenu(fileName = "Environment_Theme", menuName = "BusJam/Environment Catalog")]
    public class EnvironmentCatalog : ScriptableObject
    {
        [Tooltip("MUST equal a Theme.name (e.g. \"Park\"). Lookup key — a typo silently falls back to procedural.")]
        public string themeName;

        [Header("Prefabs (empty = procedural fallback)")]
        public GameObject groundPrefab;      // replaces the field + ground slabs
        public GameObject roadPrefab;        // replaces the RoadZ road slab
        public GameObject fencePrefab;       // replaces the fence post/bar loop
        public GameObject facadePrefab;      // replaces BuildFacade; should expose a `doorAnchorName` child
        public GameObject housePrefab;       // replaces the hasHouse centerpiece
        public GameObject[] sidePropPrefabs; // replaces the th.prop/th.prop2 side scatter (alternated)
        public GameObject[] backRowPrefabs;  // replaces the no-facade back row
        public GameObject[] cloudPrefabs;    // replaces the procedural sky clouds
        public Material skyboxMaterial;      // optional: Skybox clear instead of the solid th.sky color

        [Header("Fit")]
        public float propScale = 1f;
        public float yOffset = 0f;
        public Vector2 sideScatterX = new Vector2(-6.8f, 6.8f);
        [Tooltip("Child transform in facadePrefab whose world-X sets the boarding-queue door. Fallback: x=3.5.")]
        public string doorAnchorName = "DoorAnchor";

        [Header("Theme colors (optional — re-tints procedural elements too)")]
        public bool overrideColors;
        public ThemeColors colors = new ThemeColors();
    }
}
