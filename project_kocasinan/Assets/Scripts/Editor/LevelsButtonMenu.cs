using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// "BusJam ▸ LEVELS Test Button" — a CHECKED menu toggle for the level-testing tools: the on-screen LEVELS
    /// jump button + the unlock-first-100 level grid (both read the same PlayerPrefs flag in LevelSelect).
    /// Editor PlayerPrefs never ship in a build, so a device install always has this OFF — nothing to revert
    /// for release. Takes effect on the next Play (the button is created during scene build).
    /// </summary>
    public static class LevelsButtonMenu
    {
        const string Key = "bj_debug_levels";
        const string Path = "BusJam/LEVELS Test Button";

        [MenuItem(Path)]
        static void Toggle()
        {
            bool on = PlayerPrefs.GetInt(Key, 0) != 1; // flip
            PlayerPrefs.SetInt(Key, on ? 1 : 0);
            PlayerPrefs.Save();
            Menu.SetChecked(Path, on);
            Debug.Log("[BusJam] LEVELS test button " + (on ? "ON — the jump button appears and the first 100 levels are tappable on the next Play."
                                                          : "OFF — no jump button, normal level progression."));
        }

        [MenuItem(Path, true)]
        static bool Validate()
        {
            Menu.SetChecked(Path, PlayerPrefs.GetInt(Key, 0) == 1); // keep the checkmark in sync with the flag
            return true;
        }
    }
}
