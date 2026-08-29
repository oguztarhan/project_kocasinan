using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace Ridebury
{
    /// <summary>
    /// Forces EVERY uGUI Text and TMP_Text in each loaded scene to the game font (<see cref="GameFont"/>),
    /// so baked AND procedural text all render in Matcha Cih. Self-spawns at launch (no scene/Inspector
    /// wiring), re-runs on every scene load plus a couple of delayed passes to catch UI built in Start().
    /// Runtime-only — it sets live component fields, it never touches the asset files on disk.
    ///
    /// It also KEEPS EVERY LABEL INSIDE ITS BOX. The authored font size is only a wish: it is multiplied by
    /// FontConfig.fontScale (currently 1.575), and translations run much longer than the English the box was
    /// drawn around ("REMOVE ADS" -> "SUPPRIMER LES PUBS"). Both used to spill, because practically every Text
    /// in the project is authored Overflow/Overflow — that is why labels ran over their buttons and into each
    /// other in game while the prefab looked fine. So each text is switched to Wrap + Truncate + Best Fit with
    /// its MAXIMUM pinned to the scaled authored size: it renders at exactly the authored look whenever that
    /// fits, and shrinks only as far as it must to stay inside the rect. Never larger than intended, never
    /// outside its box, in all nine languages.
    ///
    /// "Its box" means the button's FACE, not its rect. A label's rect is normally the whole button, but the art is
    /// a thick moulded rim around a much smaller coloured face, so text that fits the rect still runs over the rim —
    /// that is why PLAY spilled off its oval. <see cref="FitRect"/> reads the rim straight off the sprite's 9-slice
    /// border and shrinks the label's box to the face, so no one has to inset labels by hand.
    ///
    /// Skipped: world-space text (bus seat counts, floating penalties, the neon sign — their rects are placement
    /// anchors, not boxes), anything carrying <see cref="NoTextFit"/>, self-sizing text (ContentSizeFitter), and
    /// degenerate rects that layout has not resolved yet (a later pass picks those up).
    /// </summary>
    public class GlobalFontApplier : MonoBehaviour
    {
        /// <summary>Never shrink a label below this, however small its box — past it the text is unreadable and
        /// the box itself is the bug (widen it in the Inspector).</summary>
        const int MinFitSize = 8;

        /// <summary>How much of a button sprite's 9-slice frame counts as off-limits for its label. The frame is
        /// drawn generously (it also has to survive slicing), so the whole of it is more padding than the art needs:
        /// 0.7 puts the label neatly inside the coloured face without kissing the dark rim. 0 = ignore the frame.</summary>
        const float FramePad = 0.7f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("GlobalFontApplier");
            DontDestroyOnLoad(go);
            var self = go.AddComponent<GlobalFontApplier>();
            SceneManager.sceneLoaded += (s, m) => { if (self != null) self.StartCoroutine(self.Passes()); };
            self.StartCoroutine(self.Passes());
        }

        IEnumerator Passes()
        {
            Apply();
            yield return null;                              // after Start() builds procedural UI
            Apply();
            // Several spaced passes so text built on a DELAY (pop-up panels, the tutorial coach, ad-callback UI,
            // a level rebuild) is caught too — Apply() is idempotent + cheap (only touches a Text whose font differs).
            float[] waits = { 0.3f, 0.6f, 1.2f, 2.5f };
            foreach (var w in waits) { yield return new WaitForSecondsRealtime(w); Apply(); }
        }

        public static void Apply()
        {
            Font f = GameFont.UGUI;
            float scale = GameFont.UiScale;   // global size multiplier from FontConfig

            if (f != null)
                foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue; // live scene objects only (skip assets/prefabs)
                    if (t.font != f) t.font = f;
                    var tag = t.GetComponent<FontScaleTag>(); if (tag == null) tag = t.gameObject.AddComponent<FontScaleTag>();
                    if (!tag.captured) { tag.baseSize = t.fontSize; tag.captured = true; } // remember the authored size once
                    int target = Mathf.Max(MinFitSize, Mathf.RoundToInt(tag.baseSize * Scale(t, scale)));
                    if (t.fontSize != target) t.fontSize = target;
                    if (Fittable(t)) { FitRect(t, tag); Fit(t, target); }
                }

            TMP_FontAsset tmp = GameFont.TMP;
            if (tmp != null)
                foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (t == null || !t.gameObject.scene.IsValid()) continue;
                    if (t.font != tmp) t.font = tmp;
                    var tag = t.GetComponent<FontScaleTag>(); if (tag == null) tag = t.gameObject.AddComponent<FontScaleTag>();
                    if (!tag.captured) { tag.baseSize = t.fontSize; tag.captured = true; }
                    float target = Mathf.Max(MinFitSize, tag.baseSize * Scale(t, scale));
                    if (!Mathf.Approximately(t.fontSize, target)) t.fontSize = target;
                    if (Fittable(t)) { FitRect(t, tag); Fit(t, target); }
                }
        }

        /// <summary>The size multiplier this applier will use for one text: none for a hierarchy whose sizes were
        /// already baked at their final value (<see cref="BakedUiText"/>) — applying it again would double the scale —
        /// and the global FontConfig one for everything else, which is all the UI built in code. Public so code that
        /// builds text at its FINAL size can cancel the multiply the same way (see GameUI.PreserveAuthoredFontSizes);
        /// hard-coding GameFont.UiScale there would shrink text spawned inside a baked hierarchy.</summary>
        public static float ScaleFor(Component c)
            => c != null && c.GetComponentInParent<BakedUiText>(true) != null ? 1f : GameFont.UiScale;

        static float Scale(Graphic g, float global)
            => g.GetComponentInParent<BakedUiText>(true) != null ? 1f : global;

        // ---- fit-in-the-box -------------------------------------------------

        /// <summary>
        /// Shrink a label's box to the FACE of the button it sits on. A label's rect is normally the whole button,
        /// but the art is a thick moulded rim around a much smaller coloured face — so text that merely fits the rect
        /// still runs over the rim (that is why PLAY spilled off its oval and ANASAYFA off its bar). The artist
        /// already recorded where the rim ends: the sprite's 9-slice border. Only the PARENT's image counts — a Text
        /// sharing a GameObject with its Image would shrink the artwork along with itself.
        ///
        /// Runtime-only and idempotent: the authored sizeDelta is captured once and every pass re-derives the box
        /// from it, so repeated passes never creep the label smaller.
        /// </summary>
        static void FitRect(Graphic g, FontScaleTag tag)
        {
            var rt = g.rectTransform;
            if (!tag.deltaCaptured) { tag.baseDelta = rt.sizeDelta; tag.deltaCaptured = true; }

            var holder = g.transform.parent != null ? g.transform.parent.GetComponent<Image>() : null;
            Sprite sprite = holder != null ? holder.sprite : null;
            Vector4 b = sprite != null ? sprite.border : Vector4.zero;
            if (sprite == null || b == Vector4.zero || sprite.rect.width < 1f || sprite.rect.height < 1f)
            {
                if (rt.sizeDelta != tag.baseDelta) rt.sizeDelta = tag.baseDelta;   // nothing to inset against
                return;
            }

            // The authored size, recovered from wherever this pass left the rect (sizeDelta moves the rect 1:1).
            Vector2 authored = rt.rect.size + (tag.baseDelta - rt.sizeDelta);
            Rect hr = holder.rectTransform.rect;
            Vector2 frame = DrawnFrame(holder);
            var face = new Vector2(
                Mathf.Max(hr.width  * 0.1f, hr.width  - FramePad * frame.x),
                Mathf.Max(hr.height * 0.1f, hr.height - FramePad * frame.y));

            Vector2 want  = Vector2.Min(authored, face);            // never GROW a label's box
            Vector2 delta = tag.baseDelta + (want - authored);
            if ((rt.sizeDelta - delta).sqrMagnitude > 0.01f) rt.sizeDelta = delta;
        }

        /// <summary>How thick the sprite's frame comes out ON SCREEN, in the image's own rect units. A Simple image
        /// stretches the whole sprite, so its frame scales with the rect; a Sliced one draws the frame at a fixed size
        /// governed by pixelsPerUnitMultiplier. Reading that wrong is what let a label believe it had room it did not
        /// have — the sliced PLAY button's face is a third of what the stretched art suggests.</summary>
        static Vector2 DrawnFrame(Image img)
        {
            Vector4 b = img.sprite.border;
            Vector2 src = img.sprite.rect.size;
            Rect r = img.rectTransform.rect;
            if (img.type == Image.Type.Sliced || img.type == Image.Type.Tiled)
            {
                float ppu = Mathf.Max(0.01f, img.pixelsPerUnit * img.pixelsPerUnitMultiplier);
                return new Vector2((b.x + b.z) / ppu, (b.y + b.w) / ppu);
            }
            if (src.x < 1f || src.y < 1f) return Vector2.zero;
            return new Vector2((b.x + b.z) / src.x * r.width, (b.y + b.w) / src.y * r.height);
        }

        /// <summary>True for text whose rect is a real box we must stay inside. False for world-space text, for
        /// opted-out text, for self-sizing text, and while the rect is still unresolved (a later pass retries).</summary>
        static bool Fittable(Graphic g)
        {
            if (g.GetComponent<NoTextFit>() != null) return false;
            if (g.GetComponent<ContentSizeFitter>() != null) return false;   // the box follows the text, not the other way round
            // Graphic.canvas only sees ACTIVE parents, and almost every panel in this game is authored hidden — the
            // shop, the garage, every pop-up, the tutorial banner. Reading it alone made this pass skip all of them,
            // so their labels were never fitted and the first long string shown at play time ran straight out of its
            // box. Fall back to a search that includes inactive parents.
            var canvas = g.canvas;
            if (canvas == null) canvas = g.GetComponentInParent<Canvas>(true);
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) return false;
            Rect r = g.rectTransform.rect;
            return r.width >= 4f && r.height >= 4f;
        }

        // Best Fit caps at the scaled authored size (further reduced to clear the button's rim), so a label that
        // already fits its face is left looking exactly as authored.
        static void Fit(Text t, int target)
        {
            if (t.horizontalOverflow != HorizontalWrapMode.Wrap) t.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (t.verticalOverflow != VerticalWrapMode.Truncate) t.verticalOverflow = VerticalWrapMode.Truncate;
            if (t.resizeTextMinSize != MinFitSize) t.resizeTextMinSize = MinFitSize;
            if (t.resizeTextMaxSize != target) t.resizeTextMaxSize = target;
            if (!t.resizeTextForBestFit) t.resizeTextForBestFit = true;
        }

        // TMP's equivalent: auto-sizing between the same bounds, with the overflowing tail clipped rather than
        // drawn over the neighbouring element.
        static void Fit(TMP_Text t, float target)
        {
            if (t.overflowMode != TextOverflowModes.Truncate) t.overflowMode = TextOverflowModes.Truncate;
            if (!Mathf.Approximately(t.fontSizeMin, MinFitSize)) t.fontSizeMin = MinFitSize;
            if (!Mathf.Approximately(t.fontSizeMax, target)) t.fontSizeMax = target;
            if (!t.enableAutoSizing) t.enableAutoSizing = true;
        }
    }
}
