using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Tags a button inside the baked in-game shop so <see cref="GameUI"/> can wire its
    /// action at runtime: grant coins (a coin pack), spend 100 gold (a joker), or close
    /// the shop. Placed by the editor tool "Tools ▸ 300Mind UI ▸ Bake In-Game Shop".
    /// </summary>
    public class InGameShopButton : MonoBehaviour
    {
        public enum Act { GrantCoins, SpendJoker, Close }

        public Act action;

        [Tooltip("Coins granted when action == GrantCoins.")]
        public int amount;
    }
}
