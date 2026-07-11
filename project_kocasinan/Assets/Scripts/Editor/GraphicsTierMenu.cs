#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// BusJam ▸ Graphics Tier ▸ Low / Mid / High / Auto — forces the device graphics tier in the EDITOR so you can
    /// preview each budget (Low/Mid/High) in Play mode without the target hardware. The choice is stored in EditorPrefs
    /// (DeviceSetup.EditorTierKey) so it survives entering Play mode, where DeviceSetup.ClassifyTier() reads it. If you
    /// change it WHILE playing, it re-applies live (renderScale/MSAA/shadow distance immediately; toon outlines + shadow
    /// STYLE update on the next level build — press Retry / reload the gameplay scene). "Auto" clears the override so
    /// classification is by real RAM+cores again (editor with no override = High). Editor-only; never ships in a build.
    /// </summary>
    static class GraphicsTierMenu
    {
        const string Root = "BusJam/Graphics Tier/";
        static int Current => EditorPrefs.GetInt(DeviceSetup.EditorTierKey, -1); // -1 = Auto

        [MenuItem(Root + "Low",  priority = 0)] static void SetLow()  => Set((int)DeviceSetup.Tier.Low);
        [MenuItem(Root + "Mid",  priority = 1)] static void SetMid()  => Set((int)DeviceSetup.Tier.Mid);
        [MenuItem(Root + "High", priority = 2)] static void SetHigh() => Set((int)DeviceSetup.Tier.High);
        [MenuItem(Root + "Auto (real device)", priority = 20)] static void SetAuto() => Set(-1);

        // Checkmark the active selection.
        [MenuItem(Root + "Low",  true)] static bool VLow()  { Menu.SetChecked(Root + "Low",  Current == (int)DeviceSetup.Tier.Low);  return true; }
        [MenuItem(Root + "Mid",  true)] static bool VMid()  { Menu.SetChecked(Root + "Mid",  Current == (int)DeviceSetup.Tier.Mid);  return true; }
        [MenuItem(Root + "High", true)] static bool VHigh() { Menu.SetChecked(Root + "High", Current == (int)DeviceSetup.Tier.High); return true; }
        [MenuItem(Root + "Auto (real device)", true)] static bool VAuto() { Menu.SetChecked(Root + "Auto (real device)", Current < 0); return true; }

        static void Set(int tier)
        {
            if (tier < 0) EditorPrefs.DeleteKey(DeviceSetup.EditorTierKey);
            else          EditorPrefs.SetInt(DeviceSetup.EditorTierKey, tier);

            string label = tier < 0 ? "Auto (real device)" : ((DeviceSetup.Tier)tier).ToString();
            if (Application.isPlaying)
            {
                DeviceSetup.EditorApplyTier(); // live: renderScale/MSAA/shadow-distance now; outlines/shadow-style on next level build
                Debug.Log($"[GraphicsTier] {label} — applied live (Retry / reload the level to see outline + shadow-style changes).");
            }
            else
            {
                Debug.Log($"[GraphicsTier] {label} — press Play to preview this tier.");
            }
        }
    }
}
#endif
