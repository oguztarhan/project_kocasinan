using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Testing helper: the garage tutorial (and the pulsing GARAGE-button highlight that leads to it) runs ONCE,
    /// gated by the PlayerPrefs flag "garage_tut_done". Use this to clear the flag so you can watch it again.
    /// </summary>
    public static class GarageTutorialMenu
    {
        [MenuItem("BusJam/Reset Garage Tutorial")]
        static void ResetGarageTutorial()
        {
            PlayerPrefs.DeleteKey("garage_tut_done");
            PlayerPrefs.Save();
            Debug.Log("[BusJam] Garage tutorial reset — the HUD GARAGE button will pulse again and the tour runs on the next IN-GAME garage open.");
        }

        // Clears EVERY one-shot coach/tip flag so the complete first-run flow can be watched again.
        // NOTE: the joker resets also re-grant that joker's free tutorial charge on the next trigger level
        // (5 / 11 / 16) — that's the point of re-watching them, but it does hand out free charges.
        [MenuItem("BusJam/Reset All Tutorials")]
        static void ResetAllTutorials()
        {
            PlayerPrefs.DeleteKey("garage_tut_done");         // garage pulse + 4-step tour
            PlayerPrefs.DeleteKey("bj_minivan_tip");          // Lv6 "Minivans seat 6 people!" banner
            PlayerPrefs.DeleteKey("bj_tutorial_done");        // Lv1 "Tap a car..." coach
            PlayerPrefs.DeleteKey("bj_freejoker_granted");    // Lv5 RECOLOR unlock coach (+1 free charge)
            PlayerPrefs.DeleteKey("bj_jokertut_1");           // Lv11 SWAP unlock coach (+1 free charge)
            PlayerPrefs.DeleteKey("bj_jokertut_2");           // Lv16 HELI unlock coach (+1 free charge)
            PlayerPrefs.Save();
            Debug.Log("[BusJam] ALL tutorials reset — Lv1 coach, Lv5/11/16 joker coaches, Lv6 minivan banner and the garage tour will each run once again.");
        }
    }
}
