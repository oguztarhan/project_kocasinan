using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Runtime-generated UI sprites, so the UI never depends on built-in editor
    /// resources like "UI/Skin/Knob.psd" — that resource is not available in Unity 6
    /// players and requesting it logs a sprite error / returns null. Generating our
    /// own circle keeps the round HUD/menu elements (level badge, coin/diamond dots,
    /// avatars) working everywhere with no console errors.
    /// </summary>
    public static class UISprites
    {
        static Sprite circle;

        /// <summary>A soft-edged white circle, tinted by each Image's color.</summary>
        public static Sprite Circle()
        {
            if (circle != null) return circle;

            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = S * 0.5f, c = r - 0.5f;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d); // 1px anti-aliased edge
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return circle;
        }
    }
}
