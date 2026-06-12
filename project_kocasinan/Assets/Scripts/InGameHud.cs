using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>One joker button's dynamic parts, assigned by the HUD baker.</summary>
    [System.Serializable]
    public class HudJoker
    {
        public Button button;
        public Image background;     // button bg (faded when out of stock)
        public Image icon;           // joker icon (faded when out; reused in the buy panel)
        public GameObject lockGo;    // "LV n" lock overlay (shown until level-unlocked)
        public GameObject counterGo; // owned-count badge (atlas1_34)
        public Text counterText;     // the owned count
    }

    /// <summary>
    /// Marker on the baked in-game HUD root (made by "Tools ▸ 300Mind UI ▸ Bake In-Game HUD").
    /// Holds direct references to every dynamic element so <see cref="GameUI"/> can adopt the
    /// scene HUD (instead of building it in code) and keep driving coins / level / people /
    /// jokers. Edit any element's colour / size / sprite in the Inspector.
    /// </summary>
    public class InGameHud : MonoBehaviour
    {
        public static InGameHud Instance;

        public GameObject hudRoot;                    // shown/hidden by ShowHud/HideHud
        public Text coinText, levelText, themeText, peopleText, comboText;
        public Image peopleIcon;                      // people-left silhouette (set at runtime)
        public Button coinButton, gearButton;         // coin -> shop, gear -> settings
        public HudJoker recolor, swap, heli;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
