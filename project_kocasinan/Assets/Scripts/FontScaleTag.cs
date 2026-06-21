using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Runtime marker added by <see cref="GlobalFontApplier"/> to each text it scales. It remembers the text's
    /// ORIGINAL (authored) font size, so the global FontConfig.fontScale multiplier is always applied from that base
    /// (target = baseSize × scale) instead of compounding on every re-apply pass. Editor-invisible; never serialized.
    /// </summary>
    [AddComponentMenu("")]
    public class FontScaleTag : MonoBehaviour
    {
        public float baseSize;   // the authored font size, captured the first time this text is seen
        public bool captured;
    }
}
