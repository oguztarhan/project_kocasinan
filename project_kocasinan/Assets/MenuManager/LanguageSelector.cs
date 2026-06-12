using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using BusJam;

/// <summary>
/// Self-contained driver for a language pop-up: auto-discovers its <see cref="LanguageOption"/>
/// rows, shows the checkmark on the current language, and on tap stores the choice
/// (<see cref="SaveSystem.Language"/>) with a pop animation. Tapping the backdrop or the
/// close button hides the pop-up. Works the same in the main menu and in-game.
/// </summary>
public class LanguageSelector : MonoBehaviour
{
    // Languages shown in the pop-up, each written in its own language. Index = SaveSystem.Language.
    public static readonly string[] Names =
    {
        "Türkçe", "English", "Deutsch", "Italiano", "Español",
        "中文", "Français", "Português", "Bahasa Indonesia"
    };

    [Tooltip("The pop-up root to hide on close.")]
    public GameObject panelRoot;
    public Button closeButton;
    public Button backdropButton;

    LanguageOption[] options;
    bool wired;

    void OnEnable()
    {
        if (!wired)
        {
            options = GetComponentsInChildren<LanguageOption>(true);
            foreach (var o in options)
            {
                var oo = o; // capture
                if (oo.button) oo.button.onClick.AddListener(() => Select(oo.index));
            }
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (backdropButton) backdropButton.onClick.AddListener(Close);
            wired = true;
        }
        Reconcile();
    }

    void Select(int index)
    {
        SaveSystem.Language = index;
        Reconcile();
        foreach (var o in options)
            if (o.index == index && o.check) StartCoroutine(Pop(o.check.transform));
    }

    void Reconcile()
    {
        if (options == null) return;
        int cur = SaveSystem.Language;
        foreach (var o in options)
            if (o.check) { o.check.SetActive(o.index == cur); o.check.transform.localScale = Vector3.one; }
    }

    void Close() { if (panelRoot) panelRoot.SetActive(false); }

    IEnumerator Pop(Transform t)
    {
        float e = 0f, dur = 0.3f;
        while (e < dur && t != null)
        {
            e += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(e / dur) * Mathf.PI);
            t.localScale = Vector3.LerpUnclamped(Vector3.one, Vector3.one * 1.4f, k);
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one;
    }
}
