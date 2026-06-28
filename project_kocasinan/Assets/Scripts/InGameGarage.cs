using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Marker placed by the editor tool "BusJam ▸ Bake Garage Panels" on the dedicated overlay canvas that holds the
    /// scene-authored Garage + Vehicles (wardrobe) windows. At play start it registers itself and hides the panels,
    /// so <see cref="GameUI"/> ADOPTS these Inspector-editable panels instead of building them in code (the same
    /// adopt-or-build pattern as <see cref="InGameHud"/> / <see cref="InGamePanels"/>).
    ///
    /// Keep this canvas ACTIVE in the editor; the two panels are baked inactive so they don't cover the screen. To
    /// reposition / restyle one, tick it active in the Hierarchy, edit, then untick. Only the WINDOW CHROME is baked
    /// (window, title, close, gold counter, scroll area) — the chest/vehicle cards inside the scroll area are still
    /// generated at runtime, so leave the "Content" object empty.
    /// </summary>
    public class InGameGarage : MonoBehaviour
    {
        public static InGameGarage Instance;

        [Header("Garage window")]
        public GameObject garageRoot;     // full-screen panel, shown/hidden by ShowGarage/HideGarage
        public Transform  garageContent;  // scroll "Content" the chest/entry cards are spawned under
        public Text       garageGold;     // gold counter label
        public Button     garageClose;    // red X (wired to HideGarage at runtime)

        [Header("Vehicles (wardrobe) window")]
        public GameObject vehiclesRoot;
        public Transform  vehiclesContent;
        public Text       vehiclesGold;
        public Button     vehiclesClose;

        // ---- Garage layout: tweak these in the Inspector to reposition/resize without baking. The runtime reads them
        //      each time the garage is built, so changes show on the next Play. Leave at default to keep the current look.
        [Header("Garage layout (Inspector-tunable, no bake)")]
        public Vector2 chestCellSize = new Vector2(275, 275); // size of each chest button
        public Vector2 chestSpacing  = new Vector2(15, 18);   // gap between chest buttons
        public int     chestColumns  = 3;                      // chests per row (1 = stack them vertically)
        public Vector2 shardOffset   = new Vector2(0, -102);   // shard counter offset from the gold chip (+x right, +y up; default = just below gold)
        public Vector2 revealSize    = new Vector2(820, 980); // chest-open popup size
        public Vector2 revealPos     = Vector2.zero;           // chest-open popup offset from screen centre

        // Optional DRAGGABLE slots created by "BusJam ▸ Bake Garage Cards". Drag/resize them in the Hierarchy to place
        // the chests / popup / shard counter; the runtime fills them. Leave empty to keep the default (Inspector) layout.
        [Header("Baked card slots (optional — 'BusJam ▸ Bake Garage Cards')")]
        public RectTransform chestArea;    // CHEST section (chests + legendary + free) is generated inside this box
        public RectTransform revealCard;   // chest-open popup takes this object's position + size
        public RectTransform shardSlot;    // shard counter chip is placed inside this object

        void Awake()
        {
            Instance = this;
            if (garageRoot)   garageRoot.SetActive(false);
            if (vehiclesRoot) vehiclesRoot.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
