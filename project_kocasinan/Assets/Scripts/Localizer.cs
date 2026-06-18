using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-shot pass that walks every Text / TMP_Text in the scene (active or inactive), and for any
/// whose current text is a known translatable key (see <see cref="Loc"/>) attaches a
/// <see cref="LocalizedText"/> that captures the English source and applies the current language.
/// Already-tagged objects are skipped. Call it once after building the menu (MenuController) and the
/// in-game UI (GameUI). Re-callable safely; finishes by refreshing everything already tagged.
/// Non-destructive: components are added at runtime only, never saved to the scene.
/// </summary>
public static class Localizer
{
    public static void LocalizeScene()
    {
        foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Tag(t.gameObject, t.text);
        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Tag(t.gameObject, t.text);
        Loc.OnLanguageChanged?.Invoke(); // refresh anything already tagged to the current language
    }

    static void Tag(GameObject go, string current)
    {
        if (go == null || go.GetComponent<LocalizedText>() != null) return;
        if (!Loc.HasKey(current)) return; // only translatable strings; numbers / names / prices pass through
        var lt = go.AddComponent<LocalizedText>();
        lt.key = current;
        lt.Apply();
    }
}
