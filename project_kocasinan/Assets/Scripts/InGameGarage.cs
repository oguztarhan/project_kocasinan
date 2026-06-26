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

        void Awake()
        {
            Instance = this;
            if (garageRoot)   garageRoot.SetActive(false);
            if (vehiclesRoot) vehiclesRoot.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
