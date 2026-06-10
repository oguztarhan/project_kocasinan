using UnityEngine;
using UnityEngine.SceneManagement;   // required to reload / switch scenes
using BusJam;                         // BusJamGame and SaveSystem live in this namespace.
                                      // THIS 'using' is what fixes the "BusJamGame not found"
                                      // (red squiggly) compile error.

/// <summary>
/// Drives the custom failure UI flow in the gameplay scene:
///   * Listens to BusJamGame.OnGameOver -> shows the Continue panel.
///   * Continue buttons resume the level (+1 parking spot) via BusJamGame.ContinueLevel().
///   * "No Thanks" closes Continue and shows the definitive Failed panel.
///   * Failed panel's Restart reloads the level; Main Menu returns to the menu scene.
/// IMPORTANT: this component must live in the GAMEPLAY scene (SampleScene), in the same
/// scene as BusJamGame — otherwise it can't find the game and the panels won't trigger.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject continuePanel;   // shown first on a loss
    [SerializeField] private GameObject failedPanel;     // shown after "No Thanks"

    [Header("Continue Settings")]
    [SerializeField] private int continueCost = 100;     // gold cost of "Pay to continue"

    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Reference to the core game loop so we can react to its events and resume play.
    private BusJamGame game;

    void Awake()
    {
        // BusJamGame is the single gameplay controller in this scene.
        game = FindAnyObjectByType<BusJamGame>();
        if (game == null)
            Debug.LogWarning("GameManager: BusJamGame not found. Place this GameManager in the " +
                             "gameplay scene (SampleScene) alongside BusJamGame.");
    }

    void OnEnable()
    {
        // Subscribe to the real failure event so the Continue panel pops up automatically.
        if (game != null) game.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        // Always unsubscribe to avoid dangling delegates after a scene reload.
        if (game != null) game.OnGameOver -= HandleGameOver;
    }

    void Start()
    {
        // Both panels start hidden; they only appear when the player fails.
        if (continuePanel != null) continuePanel.SetActive(false);
        if (failedPanel != null) failedPanel.SetActive(false);
    }

    // Called automatically by BusJamGame the moment the player fails.
    // NOTE: the Continue/Failed UI is now owned by GameUI (runtime-built and styled with the
    // Colorful UI pack), driven directly from BusJamGame.Lose(). We deliberately do NOTHING here
    // so a second (scene-wired) Continue panel never appears on top of it.
    private void HandleGameOver(string reason)
    {
        // intentionally empty — see note above.
    }

    // ---------------- Continue panel buttons ----------------

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
        // TODO: hook a real rewarded-ad SDK here and call ResumeAfterContinue()
        //       only from the ad's success/reward callback. For now we grant it directly.
        Debug.Log("GameManager: ad watched. Continuing with +1 parking spot.");
        ResumeAfterContinue();
    }

    /// <summary>"No Thanks" button: close Continue and show the definitive Failed panel.</summary>
    public void RejectContinue()
    {
        Debug.Log("GameManager: player declined to continue. Showing Failed panel.");
        if (continuePanel != null) continuePanel.SetActive(false);
        if (failedPanel != null) failedPanel.SetActive(true);
    }

    // ---------------- Failed panel buttons ----------------

    /// <summary>"Restart" button: reload the current level from the start.</summary>
    public void RestartGame()
    {
        Debug.Log("GameManager: restarting the level...");
        // Reloading the active gameplay scene fully resets the board. BusJamGame
        // auto-starts the saved level on load, so the player replays the same level.
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>"Main Menu" button: leave gameplay and return to the menu scene.</summary>
    public void GoToMainMenu()
    {
        Debug.Log("GameManager: returning to the main menu...");
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // Shared helper: hide the Continue panel and resume play with an extra parking bay.
    private void ResumeAfterContinue()
    {
        if (continuePanel != null) continuePanel.SetActive(false);
        if (game != null) game.ContinueLevel();
    }
}
