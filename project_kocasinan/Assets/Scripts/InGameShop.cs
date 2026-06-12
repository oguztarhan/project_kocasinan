using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Marker placed by the editor tool "Tools ▸ 300Mind UI ▸ Bake In-Game Shop" on the
    /// scene-authored shop's root canvas. At play start it registers itself and hides the
    /// shop <see cref="panel"/>, so <see cref="GameUI"/> adopts this Inspector-editable
    /// shop (opened by tapping the coin during gameplay) instead of building one in code.
    ///
    /// Keep the root canvas (this GameObject) ACTIVE in the editor — to hide the shop while
    /// editing the rest of the scene, disable the child "Panel_GameShop", NOT the canvas.
    /// </summary>
    public class InGameShop : MonoBehaviour
    {
        public static InGameShop Instance;

        [Tooltip("The dim backdrop + card that GameUI shows when the coin is tapped.")]
        public GameObject panel;

        void Awake()
        {
            Instance = this;
            // Safety net: if the reference was lost (e.g. baked without saving), find the
            // panel by name so it is still hidden at start instead of staying on screen.
            if (panel == null)
            {
                var t = transform.Find("Panel_GameShop");
                if (t) panel = t.gameObject;
            }
            if (panel) panel.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
