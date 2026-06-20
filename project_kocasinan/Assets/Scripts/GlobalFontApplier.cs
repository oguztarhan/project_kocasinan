using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace BusJam
{
    /// <summary>
    /// Forces EVERY uGUI Text and TMP_Text in each loaded scene to the game font (<see cref="GameFont"/>),
    /// so baked AND procedural text all render in Matcha Cih. Self-spawns at launch (no scene/Inspector
    /// wiring), re-runs on every scene load plus a couple of delayed passes to catch UI built in Start().
    /// Runtime-only — it sets live component fields, it never touches the asset files on disk.
    /// </summary>
    public class GlobalFontApplier : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("GlobalFontApplier");
            DontDestroyOnLoad(go);
            var self = go.AddComponent<GlobalFontApplier>();
            SceneManager.sceneLoaded += (s, m) => { if (self != null) self.StartCoroutine(self.Passes()); };
            self.StartCoroutine(self.Passes());
        }

        IEnumerator Passes()
        {
            Apply();
            yield return null;                              // after Start() builds procedural UI
            Apply();
            yield return new WaitForSecondsRealtime(0.6f);
            Apply();                                        // after on-demand UI (panels / coach)
        }

        public static void Apply()
        {
            Font f = GameFont.UGUI;
            if (f != null)
                foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue; // live scene objects only (skip assets/prefabs)
                    if (t.font != f) t.font = f;
                }

            TMP_FontAsset tmp = GameFont.TMP;
            if (tmp != null)
                foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue;
                    if (t.font != tmp) t.font = tmp;
                }
        }
    }
}
