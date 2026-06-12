using UnityEngine;
using UnityEngine.UI;
using BusJam;

/// <summary>
/// Sound / Music on-off toggle for the baked main-menu Settings panel. The toggle is a
/// button (atlas1_37) with the sound/music logo on top. When the channel is ON the button
/// shows its full colour; when OFF the whole button (background + logo) fades. State is
/// persisted via <see cref="SaveSystem"/>. Placed and wired by the editor tool
/// "Tools ▸ 300Mind UI ▸ Rebuild Settings".
/// </summary>
public class SettingsToggle : MonoBehaviour
{
    public enum Kind { Sound, Music }

    public Kind kind;
    [Tooltip("Button background image (atlas1_37).")]
    public Image background;
    [Tooltip("Sound / music logo on top of the button.")]
    public Image icon;
    public Button button;

    [Tooltip("Tint when the channel is ON (full colour).")]
    public Color onColor = Color.white;
    [Tooltip("Tint when the channel is OFF (faded).")]
    public Color offColor = new Color(0.8f, 0.8f, 0.82f, 0.45f);

    bool wired;

    bool On
    {
        get => kind == Kind.Sound ? SaveSystem.Sound : SaveSystem.Music;
        set { if (kind == Kind.Sound) SaveSystem.Sound = value; else SaveSystem.Music = value; }
    }

    void OnEnable()
    {
        if (!wired && button) { button.onClick.AddListener(Toggle); wired = true; }
        Apply();
    }

    void Toggle()
    {
        On = !On;
        Apply();
    }

    void Apply()
    {
        var c = On ? onColor : offColor;
        if (background) background.color = c;
        if (icon) icon.color = c;
    }
}
