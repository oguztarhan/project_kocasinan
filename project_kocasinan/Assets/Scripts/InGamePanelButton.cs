using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Tags a button inside a baked in-game panel (Settings / Continue / Failed) so
    /// <see cref="GameUI"/> can wire its action at runtime. Placed by the editor tool
    /// "Tools ▸ 300Mind UI ▸ Bake In-Game Panels".
    /// </summary>
    public class InGamePanelButton : MonoBehaviour
    {
        public enum Act { Home, Replay, Close, ContinueAd, ContinuePay, ToggleSound, ToggleMusic, Claim }

        public Act action;
        [Tooltip("Reward amount granted when action == Claim (Success panel buttons).")]
        public int amount;
    }
}
