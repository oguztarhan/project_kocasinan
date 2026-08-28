using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ridebury
{
    /// <summary>
    /// Bonus-level REWARD flow (partial of <see cref="GameUI"/>). Coin Rush / Mystery Rush / Traffic Dodge finish into
    /// the STOP-THE-BAR mini-game (tap to stop a sweeping marker; narrow GOLD centre / SILVER band / BRONZE outsides
    /// pick the tier). Time Attack skips the bar (its finish time already chose the tier). Then a "YOU WON A &lt;TIER&gt;
    /// CHEST!" screen shows the cute chest + an OPEN button; opening runs the garage reveal, then advances the level.
    /// </summary>
    public partial class GameUI
    {
        GameObject stopBarPanel, chestWonPanel;
        RectTransform stopMarker;
        bool stopTapped;
        System.Action<ChestTier> stopResult;

        // Called by RideburyGame.BonusSuccess. useStopBar == false => Time Attack (tier already decided by finish time).
        public void ShowBonusReward(bool useStopBar, ChestTier timeTier, System.Action onDone)
        {
            if (useStopBar) ShowStopBar(t => ShowChestWon(t, onDone));
            else ShowChestWon(timeTier, onDone);
        }

        // ---- "you won a chest" -> tap OPEN -> reveal --------------------------------------------------------
        public void ShowChestWon(ChestTier tier, System.Action onDone)
        {
            if (chestWonPanel != null) Destroy(chestWonPanel); // rebuild fresh (the chest differs per tier)
            chestWonPanel = Panel("ChestWon", Dim);
            MakeExclusive(chestWonPanel); // bonus level-complete: nothing (Garage etc.) may remain layered behind it
            var cv = chestWonPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 84;
            chestWonPanel.AddComponent<GraphicRaycaster>();

            // Same deep-blue card + drawn chest as the garage, so winning a chest here and buying one
            // there look like the same game (this screen was still on the old purple atlas panel).
            var card = Img(chestWonPanel.transform, UIKit.CardDaily(), new Color(0.10f, 0.17f, 0.42f)); card.raycastTarget = false;
            Center(card.rectTransform, new Vector2(860, 940));
            Sliced(card, new Vector2(860, 940));
            Label(card.transform, Loc.T("YOU WON"), num, new Vector2(0, 330), new Vector2(700, 60), 44, White);
            // The tier tint is a mid-tone (copper / steel); lift it towards white so it reads on the navy card.
            Label(card.transform, Loc.T(tier.ToString().ToUpper() + " CHEST!"), title, new Vector2(0, 250), new Vector2(800, 100), 70, Color.Lerp(ChestTint(tier), White, 0.25f));
            BuildGarageChestArt(Holder(card.transform, new Vector2(0, -10), new Vector2(360, 320)), tier, ChestTint(tier), 280);
            var open = Btn(card.transform, GarageActionSprite(), White, new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(400, 128),
                () => { if (chestWonPanel) chestWonPanel.SetActive(false); GrantBonusChest(tier, onDone); });
            Sliced(open.GetComponent<Image>(), new Vector2(400, 128));
            Label(open.transform, Loc.T("OPEN"), title, Vector2.zero, new Vector2(400, 84), 54, White);
        }

        void GrantBonusChest(ChestTier tier, System.Action onDone)
        {
            var res = ChestService.Open(tier);                 // grant (no gold cost) + roll the car
            if (res.car == null) { onDone?.Invoke(); return; } // catalog not built -> just advance, no reveal
            SetRevealChestTier(tier);                          // the reveal's opening chest matches the tier they won
            revealThenDo = onDone;                             // the reveal's OK button advances the level
            ShowRevealCar(res.car, res.wasDupe ? ("DUPLICATE  +" + res.shardsGained + " shards") : "NEW!", res.keyDropped, res.keyTier);
        }

        // ---- stop-the-bar mini-game (cute rounded pills, like the chests) -----------------------------------
        public void ShowStopBar(System.Action<ChestTier> onResult)
        {
            if (stopBarPanel == null) BuildStopBar();
            MakeExclusive(stopBarPanel); // bonus level-complete mini-game: force any open screen (Garage etc.) shut first
            stopResult = onResult; stopTapped = false;
            stopBarPanel.SetActive(true);
            stopBarPanel.transform.SetAsLastSibling();
            StartCoroutine(StopBarSweep());
        }

        void BuildStopBar()
        {
            stopBarPanel = Panel("StopBar", Dim);
            var cv = stopBarPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 84; // below the reveal (85)
            stopBarPanel.AddComponent<GraphicRaycaster>();

            var card = Img(stopBarPanel.transform, UIKit.CardDaily(), new Color(0.10f, 0.17f, 0.42f)); card.raycastTarget = false;
            Center(card.rectTransform, new Vector2(900, 620));
            Sliced(card, new Vector2(900, 620));
            Label(card.transform, Loc.T("STOP ON GOLD!"), title, new Vector2(0, 190), new Vector2(800, 90), 58, new Color(1f, 0.86f, 0.34f));
            Label(card.transform, Loc.T("Tap to stop the bar"), num, new Vector2(0, 116), new Vector2(700, 50), 32, new Color(0.85f, 0.9f, 1f));

            // Zone bands, layered: bronze (full) -> silver (band) -> gold (narrow centre = the "hard place").
            // Each band is a METALLIC bar: MetalSprite bakes a vertical gloss gradient (dark base at the bottom ->
            // bright sheen -> near-white specular rim on top, as if lit from above), so copper / steel-silver / gold
            // read as polished metal instead of flat paint — fits the game's shiny theme. (We must NOT tint the UIKit
            // atlas sprites: they're pre-shaded reddish art, so Image.color multiplied and turned the tiers into
            // red/purple/orange — that was the earlier bug. This gradient sprite carries the colour itself; tint White.)
            // Each band sits on a slightly larger DARK quad for a crisp inset rim between tiers.
            void Zone(Color col, float w)
            {
                var rim = Img(card.transform, null, new Color(0.07f, 0.08f, 0.12f, 1f)); rim.raycastTarget = false;
                Place(rim.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(w + 8, 104));
                var z = Img(card.transform, MetalSprite(col), White); z.raycastTarget = false;
                Place(z.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(w, 96));
            }
            Zone(new Color(0.82f, 0.45f, 0.16f), 700); // BRONZE — warm copper
            Zone(new Color(0.66f, 0.74f, 0.86f), 280); // SILVER — cool steel
            Zone(new Color(1f, 0.82f, 0.20f), 84);     // GOLD — rich yellow-gold

            // Marker: a bright WHITE needle with a dark rim (both flat quads), so it stands out over EVERY zone colour.
            var m = Img(card.transform, null, new Color(0.08f, 0.09f, 0.13f, 1f)); m.raycastTarget = false; // dark rim = the moving root
            Place(m.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(28, 150));
            var needle = Img(m.transform, null, new Color(0.97f, 0.98f, 1f)); needle.raycastTarget = false;
            Place(needle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 138));
            var nub = Img(m.transform, null, new Color(0.97f, 0.98f, 1f)); nub.raycastTarget = false;
            Place(nub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, 18), new Vector2(46, 40));
            stopMarker = m.rectTransform;

            // transparent full-screen tap catcher ON TOP so a tap anywhere stops the bar (card is non-raycast)
            var tap = Img(stopBarPanel.transform, null, new Color(0, 0, 0, 0));
            Stretch(tap.rectTransform);
            var b = tap.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => stopTapped = true);
        }

        // ---- metallic band gradient ---------------------------------------------------------------------------
        // A 1×N vertical gloss ramp baked from a base colour: deep shade at the bottom, base through the middle, a
        // bright sheen up top, and a near-white specular rim at the very top edge — the classic "polished metal"
        // look. A 1-px-wide sprite stretches to any band width (bilinear) so the sheen stays perfectly horizontal.
        // Cached per colour so the three tiers each bake exactly once.
        static readonly System.Collections.Generic.Dictionary<int, Sprite> _metalCache = new System.Collections.Generic.Dictionary<int, Sprite>();
        static Sprite MetalSprite(Color c)
        {
            int key = (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);
            if (_metalCache.TryGetValue(key, out var cached)) return cached;

            const int N = 64;
            var tex = new Texture2D(1, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            // bottom (y=0) -> top (y=1): shade, base, sheen, bright rim
            var stops = new (float p, Color col)[]
            {
                (0.00f, Color.Lerp(c, Color.black, 0.55f)),
                (0.30f, Color.Lerp(c, Color.black, 0.14f)),
                (0.50f, c),
                (0.66f, Color.Lerp(c, Color.white, 0.32f)),
                (0.88f, Color.Lerp(c, Color.white, 0.66f)),
                (1.00f, Color.Lerp(c, Color.white, 0.94f)),
            };
            for (int y = 0; y < N; y++) tex.SetPixel(0, y, EvalStops(stops, y / (N - 1f)));
            tex.Apply(false);

            var sp = Sprite.Create(tex, new Rect(0, 0, 1, N), new Vector2(0.5f, 0.5f), 100f);
            _metalCache[key] = sp;
            return sp;
        }

        static Color EvalStops((float p, Color col)[] s, float f)
        {
            if (f <= s[0].p) return s[0].col;
            for (int i = 1; i < s.Length; i++)
                if (f <= s[i].p) return Color.Lerp(s[i - 1].col, s[i].col, (f - s[i - 1].p) / (s[i].p - s[i - 1].p));
            return s[s.Length - 1].col;
        }

        IEnumerator StopBarSweep()
        {
            float t = 0f, dir = 1f; const float speed = 1.7f, halfW = 350f;   // fast enough that the gold centre takes skill/luck
            while (!stopTapped)
            {
                t += dir * speed * Time.unscaledDeltaTime;                    // unscaled: works even though gameplay may be paused
                if (t >= 1f) { t = 1f; dir = -1f; }
                else if (t <= 0f) { t = 0f; dir = 1f; }
                if (stopMarker != null) stopMarker.anchoredPosition = new Vector2((t - 0.5f) * 2f * halfW, -30f);
                yield return null;
            }
            float d = Mathf.Abs(t - 0.5f);                                    // 0 = dead centre (84/700 -> d<0.06 gold, 280/700 -> d<0.20 silver)
            ChestTier tier = d < 0.06f ? ChestTier.Gold : d < 0.20f ? ChestTier.Silver : ChestTier.Bronze;
            Sfx.Ensure().Win();
            yield return new WaitForSecondsRealtime(0.5f);                    // hold so the player sees where it landed
            if (stopBarPanel != null) stopBarPanel.SetActive(false);
            var cb = stopResult; stopResult = null; cb?.Invoke(tier);
        }
    }
}
