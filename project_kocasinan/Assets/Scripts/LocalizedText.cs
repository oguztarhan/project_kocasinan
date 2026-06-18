using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to any UI Text / TMP_Text. Holds the ENGLISH source string as <see cref="key"/> and
/// rewrites the label to the current language on enable and whenever the language changes.
/// Added automatically at runtime by <see cref="Localizer"/> — no need to place it by hand.
/// </summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    public string key;

    Text _text;
    TMP_Text _tmp;
    bool _resolved;

    void Resolve()
    {
        if (_resolved) return;
        _text = GetComponent<Text>();
        _tmp = GetComponent<TMP_Text>();
        _resolved = true;
    }

    void OnEnable()
    {
        Apply();
        Loc.OnLanguageChanged += Apply;
    }

    void OnDisable()
    {
        Loc.OnLanguageChanged -= Apply;
    }

    public void Apply()
    {
        if (string.IsNullOrEmpty(key)) return;
        Resolve();
        string v = Loc.T(key);
        if (_text != null) _text.text = v;
        if (_tmp != null) _tmp.text = v;
    }
}
