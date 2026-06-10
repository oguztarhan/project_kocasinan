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
        static Sprite person;

        /// <summary>A soft-edged white circle, tinted by each Image's color.</summary>
        public static Sprite Circle()
        {
            // Re-generate if the cached sprite OR its texture was destroyed (editor
            // preview textures are cleaned up on play-mode transitions; a stale sprite
            // with a dead texture renders as a plain white square).
            if (circle != null && circle.texture != null) return circle;

            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
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
            circle.hideFlags = HideFlags.HideAndDontSave;
            return circle;
        }

        /// <summary>A simple white person silhouette (round head + shoulders dome), tinted by the Image.</summary>
        public static Sprite Person()
        {
            if (person != null && person.texture != null) return person;

            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[S * S];
            float hx = 64, hy = 86, hr = 24;                 // head circle (texture y=0 is bottom, so high y = top)
            float bx = 64, by = 20, brx = 44, bry = 46;      // shoulders = upper half of an ellipse
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float a = 0f;
                    float dh = Mathf.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy));
                    a = Mathf.Max(a, Mathf.Clamp01(hr - dh)); // 1px AA head
                    if (y >= by)
                    {
                        float ex = (x - bx) / brx, ey = (y - by) / bry;
                        float de = Mathf.Sqrt(ex * ex + ey * ey); // 1 at the ellipse edge
                        a = Mathf.Max(a, Mathf.Clamp01((1f - de) * bry)); // ~1px AA dome
                    }
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            person = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            person.hideFlags = HideFlags.HideAndDontSave;
            return person;
        }
    }
}
