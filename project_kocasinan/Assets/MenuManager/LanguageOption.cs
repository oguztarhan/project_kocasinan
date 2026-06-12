using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One selectable language row inside a language pop-up. Placed by the baker; the runtime
/// <see cref="LanguageSelector"/> reads these to drive selection + the checkmark.
/// </summary>
public class LanguageOption : MonoBehaviour
{
    public int index;        // 0 = Türkçe, 1 = English
    public GameObject check; // checkmark shown when this language is the selected one
    public Button button;    // the row's clickable button
}
