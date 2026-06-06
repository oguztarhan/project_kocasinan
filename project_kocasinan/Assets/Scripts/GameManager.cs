using UnityEngine;
using BusJam; // BusJamGame and SaveSystem live in this namespace

/// <summary>
/// Drives the custom "Continue / Game Over" UI flow in the gameplay scene:
///   * Listens for BusJamGame's OnGameOver event and pops up the ContinuePanel.
///   * Handles the three ContinuePanel buttons:
///       - Pay gold to continue  (+1 parking spot)
///       - Watch an ad to continue (+1 parking spot)
///       - No Thanks -> close panel and show the definitive Failed screen
/// Attach this to a manager object in the gameplay scene and assign the panels
/// in the Inspector. No test keys, no manual triggers — it reacts to the real
/// loss state, so the flow works automatically on scene load.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("Custom Continue / Game Over panel offering to keep playing.")]
    [SerializeField] private GameObject continuePanel;

    [Tooltip("Definitive Failed panel, shown when the player declines to continue.")]
    [SerializeField] private GameObject failedPanel;

    [Header("Continue settings")]
    [Tooltip("Gold cost of the 'Pay to continue' option.")]
    [SerializeField] private int continueCost = 100;

    // Reference to the core game so we can resume play and read the loss event.
    private BusJamGame game;

    void Awake()
    {
        // BusJamGame is the single gameplay controller in this scene.
        game = FindAnyObjectByType<BusJamGame>();
        if (game == null)
            Debug.LogWarning("GameManager: no BusJamGame found in the scene; the Continue flow will not trigger.");
    }

    void OnEnable()
    {
        // Subscribe to the real failure event so the panel pops up automatically.
        if (game != null) game.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        // Always unsubscribe to avoid dangling delegates / duplicate calls.
        if (game != null) game.OnGameOver -= HandleGameOver;
    }

    void Start()
    {
        // Both panels start hidden; they only appear when the player fails.
        if (continuePanel != null) continuePanel.SetActive(false);
        if (failedPanel != null) failedPanel.SetActive(false);
    }

    // Called by BusJamGame the moment the player fails the level (time-out or stuck).
    private void HandleGameOver(string reason)
    {
        if (continuePanel != null) continuePanel.SetActive(true);
    }

    // --- ContinuePanel button handlers (wire these to the buttons' OnClick) ---

    /// <summary>"Pay 100 Gold to continue" button.</summary>
    public void ContinueWithCoin()
    {
        if (game == null) return;

        // Only continue if the player can actually afford it.
        if (!SaveSystem.TrySpend(continueCost))
        {
            Debug.Log($"GameManager: not enough gold to continue (need {continueCost}).");
            return; // keep the panel open so the player can pick another option
        }

        Debug.Log($"GameManager: spent {continueCost} gold. Continuing with +1 parking spot.");
        ResumeAfterContinue();
    }

    /// <summary>"Watch an ad to continue" button.</summary>
    public void ContinueWithAd()
    {
        // TODO: plug a real rewarded-ad SDK here and call ResumeAfterContinue()
        //       only from the ad's success/reward callback. For now we grant it directly.
        Debug.Log("GameManager: ad watched. Continuing with +1 parking spot.");
        ResumeAfterContinue();
    }

    /// <summary>"No Thanks" button: close this panel and show the definitive Failed screen.</summary>
    public void RejectContinue()
    {
        Debug.Log("GameManager: player declined to continue. Showing Failed screen.");
        if (continuePanel != null) continuePanel.SetActive(false);
        if (failedPanel != null) failedPanel.SetActive(true);
    }

    // Shared helper: hide the Continue panel and resume play with an extra parking bay.
    private void ResumeAfterContinue()
    {
        if (continuePanel != null) continuePanel.SetActive(false);
        if (game != null) game.ContinueLevel();
    }
}
