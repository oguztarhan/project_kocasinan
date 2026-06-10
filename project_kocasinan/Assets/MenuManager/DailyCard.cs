using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Data tag placed on each Daily-Reward day card by the editor baker. Holds the day
/// index, the coin reward, the checkmark overlay and the card's button so the runtime
/// <see cref="DailyRewards"/> manager can drive the claim logic.
/// </summary>
public class DailyCard : MonoBehaviour
{
    public int day;          // 1..7
    public int coins;        // coin reward (0 = non-coin reward, e.g. a joker)
    public GameObject check; // checkmark overlay (shown once claimed)
    public Button button;    // the card's clickable button
}
