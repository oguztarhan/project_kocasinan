using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Data tag placed on each Daily-Reward day card by the editor baker. Holds the day
/// index and the reward that day pays out (gold, free jokers, a chest key), plus the
/// checkmark overlay and the card's button so the runtime <see cref="DailyRewards"/>
/// manager can drive the claim logic.
///
/// The values below are a CACHE of <see cref="DailyRewards.Plan"/> — the manager
/// re-applies the plan to every card on open, so editing the table in code is enough
/// (no re-bake needed).
/// </summary>
public class DailyCard : MonoBehaviour
{
    public int day;            // 1..7
    public int coins;          // gold reward
    public int recolorJokers;  // free RECOLOR charges
    public int swapJokers;     // free SWAP charges
    public int heliJokers;     // free HELICOPTER charges
    public string chestKeyTier = ""; // "" = none, else "Bronze" / "Silver" / "Gold" / "Legendary"
    public GameObject check;   // checkmark overlay (shown once claimed)
    public Button button;      // the card's clickable button
}
