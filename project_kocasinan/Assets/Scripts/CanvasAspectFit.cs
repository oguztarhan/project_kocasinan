using UnityEngine;
using UnityEngine.UI;

namespace Ridebury
{
    /// <summary>
    /// Every canvas here is ScaleWithScreenSize / reference 1080x1920 / match WIDTH (see SafeArea's doc
    /// comment for the same premise), so the visible canvas HEIGHT shrinks or grows with the device's
    /// aspect ratio instead of the authored 1920 units always being on screen. On an iPhone (~9:19.5)
    /// that yields extra headroom past 1920 — harmless. On an iPad (~4:3, aspect ~0.7) it yields only
    /// ~1550 visible units: top- and bottom-anchored chrome authored near the 1920 edge is cut off the
    /// physical screen entirely. That is a different problem from SafeArea (which nudges chrome clear of
    /// the notch/home-indicator) — this is the visible canvas AREA itself coming up short.
    ///
    /// FIX. Flip the CanvasScaler's match slider to HEIGHT whenever the device is squarer than the
    /// reference (1080/1920 = 0.5625). Matching height guarantees the full authored 1920 units are
    /// always visible; the trade-off is extra horizontal margin on those devices, which every canvas
    /// already tolerates via full-stretch backgrounds/columns. Devices taller/narrower than the
    /// reference keep matching width, unchanged from today.
    ///
    /// Applied once, at instantiation — call sites: UIPrefabs.Ensure (Hud, GamePanels, GaragePanel,
    /// TutorialPanel, MenuUI) and ShopUI.Ensure (ShopPanel). Portrait is the only orientation this app
    /// allows and the device's screen shape cannot change mid-session, so a one-shot fix at spawn time
    /// is enough — no Update loop needed. Reversible by deleting this file plus its two call sites.
    /// </summary>
    public static class CanvasAspectFit
    {
        public static void Apply(GameObject root)
        {
            if (root == null) return;
            var scaler = root.GetComponent<CanvasScaler>() ?? root.GetComponentInChildren<CanvasScaler>(true);
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;

            float refAspect = scaler.referenceResolution.x / scaler.referenceResolution.y;
            float screenAspect = (float)Screen.width / Screen.height;
            scaler.matchWidthOrHeight = screenAspect > refAspect ? 1f : 0f;
        }
    }
}
