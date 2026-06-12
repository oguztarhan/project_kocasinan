using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Marker placed by the editor tool "Tools ▸ 300Mind UI ▸ Bake In-Game Panels" on the
    /// root canvas that holds the scene-authored Settings / Continue / Failed pop-ups. At
    /// play start it registers itself and hides the panels, so <see cref="GameUI"/> adopts
    /// these Inspector-editable panels instead of building them in code.
    ///
    /// Keep this root canvas ACTIVE in the editor; the panels themselves are baked inactive
    /// so they don't cover the screen. To edit one, tick it active in the Hierarchy.
    /// </summary>
    public class InGamePanels : MonoBehaviour
    {
        public static InGamePanels Instance;

        public GameObject settings;
        public GameObject continuePanel;
        public GameObject failed;
        public GameObject success;

        [Header("Joker buy pop-ups (one per joker)")]
        public GameObject jokerBuyRecolor, jokerBuySwap, jokerBuyHeli;
        public Button jokerBuyRecolorBtn, jokerBuySwapBtn, jokerBuyHeliBtn;

        [Header("Language pop-up")]
        public GameObject language;

        void Awake()
        {
            Instance = this;
            if (settings) settings.SetActive(false);
            if (continuePanel) continuePanel.SetActive(false);
            if (failed) failed.SetActive(false);
            if (success) success.SetActive(false);
            if (jokerBuyRecolor) jokerBuyRecolor.SetActive(false);
            if (jokerBuySwap) jokerBuySwap.SetActive(false);
            if (jokerBuyHeli) jokerBuyHeli.SetActive(false);
            if (language) language.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
