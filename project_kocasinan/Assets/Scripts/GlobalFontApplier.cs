using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace Ridebury
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
            // Several spaced passes so text built on a DELAY (pop-up panels, the tutorial coach, ad-callback UI,
            // a level rebuild) is caught too — Apply() is idempotent + cheap (only touches a Text whose font differs).
            float[] waits = { 0.3f, 0.6f, 1.2f, 2.5f };
            foreach (var w in waits) { yield return new WaitForSecondsRealtime(w); Apply(); }
        }

        public static void Apply()
        {
            Font f = GameFont.UGUI;
            float scale = GameFont.UiScale;   // global size multiplier from FontConfig

            if (f != null)
                foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue; // live scene objects only (skip assets/prefabs)
                    if (t.font != f) t.font = f;
                    var tag = t.GetComponent<FontScaleTag>(); if (tag == null) tag = t.gameObject.AddComponent<FontScaleTag>();
                    if (!tag.captured) { tag.baseSize = t.fontSize; tag.captured = true; } // remember the authored size once
                    int target = Mathf.Max(1, Mathf.RoundToInt(tag.baseSize * scale));
                    if (t.fontSize != target) t.fontSize = target;
                }

            TMP_FontAsset tmp = GameFont.TMP;
            if (tmp != null)
                foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue;
                    if (t.font != tmp) t.font = tmp;
                    var tag = t.GetComponent<FontScaleTag>(); if (tag == null) tag = t.gameObject.AddComponent<FontScaleTag>();
                    if (!tag.captured) { tag.baseSize = t.fontSize; tag.captured = true; }
                    float target = Mathf.Max(1f, tag.baseSize * scale);
                    if (!Mathf.Approximately(t.fontSize, target)) t.fontSize = target;
                }
        }
    }
}
