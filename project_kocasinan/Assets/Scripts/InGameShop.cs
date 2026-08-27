using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// LEGACY marker from the old two-shop setup (a shop baked into each scene). The game now
    /// has ONE shop — the <see cref="ShopUI"/> prefab at Resources/UI/ShopPanel — so this only
    /// survives so that a scene still holding a baked shop keeps working, and so
    /// "Tools ▸ 300Mind UI ▸ Unify Shop" can find those bakes and migrate them.
    /// Nothing new should use it.
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
