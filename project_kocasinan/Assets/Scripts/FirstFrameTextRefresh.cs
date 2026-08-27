using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// One-shot fix for dynamic-font uGUI Text that renders BLANK when created during a synchronous build (the level
    /// load): on that frame the glyph may not be in the font atlas yet, and the atlas rebuild it triggers doesn't
    /// re-mesh the text — so it stays blank forever (while text created later, once the glyph is cached, is fine).
    /// This waits one frame (atlas rebuild now complete) then forces a fresh mesh, then self-destructs.
    /// Used by the mystery "?" so gray passengers ALREADY in line at level start show it, not just later spawns.
    /// </summary>
    public class FirstFrameTextRefresh : MonoBehaviour
    {
        int frames;

        void LateUpdate()
        {
            if (frames++ < 1) return; // wait a frame so the rebuild the new glyph triggered has finished
            var t = GetComponent<UnityEngine.UI.Text>();
            if (t != null) t.SetAllDirty(); // regenerate the mesh with the now-ready atlas UVs
            Destroy(this);
        }
    }
}
