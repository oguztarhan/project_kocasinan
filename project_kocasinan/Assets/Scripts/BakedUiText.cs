using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// "Every Text under this object is already authored at its FINAL on-screen size."
    ///
    /// Put on a UI prefab root by "Ridebury ▸ UI ▸ Bake WYSIWYG into Prefabs", which multiplies the authored font
    /// sizes by FontConfig.fontScale ONCE and writes the result into the prefab. <see cref="GlobalFontApplier"/>
    /// then skips the multiplier for this hierarchy — otherwise the scale would be applied a second time at play.
    ///
    /// The point is that the Inspector stops lying: the size you type is the size that ships, so a prefab opened at
    /// the device aspect looks exactly like the game. Code-built UI carries no marker and is still scaled at runtime.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BakedUiText : MonoBehaviour { }
}
