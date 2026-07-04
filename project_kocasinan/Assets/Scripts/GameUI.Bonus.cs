using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BusJam
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

        // Called by BusJamGame.BonusSuccess. useStopBar == false => Time Attack (tier already decided by finish time).
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
            var cv = chestWonPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 84;
            chestWonPanel.AddComponent<GraphicRaycaster>();

            var card = Img(chestWonPanel.transform, UIKit.PanelTall(), new Color(0.22f, 0.24f, 0.36f)); card.raycastTarget = false;
            Center(card.rectTransform, new Vector2(860, 940));
            Label(card.transform, Loc.T("YOU WON"), num, new Vector2(0, 330), new Vector2(700, 60), 44, White);
            Label(card.transform, Loc.T(tier.ToString().ToUpper() + " CHEST!"), title, new Vector2(0, 250), new Vector2(800, 100), 70, ChestTint(tier));
            BuildChest(Holder(card.transform, new Vector2(0, -10), new Vector2(360, 320)), ChestTint(tier), 300);
            var open = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(440, 140),
                () => { if (chestWonPanel) chestWonPanel.SetActive(false); GrantBonusChest(tier, onDone); });
            Label(open.transform, Loc.T("OPEN"), title, Vector2.zero, new Vector2(440, 90), 56, White);
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

            var card = Img(stopBarPanel.transform, UIKit.PanelTall(), new Color(0.22f, 0.24f, 0.36f)); card.raycastTarget = false;
            Center(card.rectTransform, new Vector2(900, 620));
            Label(card.transform, Loc.T("STOP ON GOLD!"), title, new Vector2(0, 190), new Vector2(800, 90), 58, new Color(1f, 0.86f, 0.34f));
            Label(card.transform, Loc.T("Tap to stop the bar"), num, new Vector2(0, 116), new Vector2(700, 50), 32, new Color(0.85f, 0.9f, 1f));

            // ROUNDED zone pills, layered: bronze (full) -> silver (band) -> gold (narrow centre = the "hard place")
            var track = Img(card.transform, UIKit.ShopIconBgA(), White); track.color = new Color(0.72f, 0.46f, 0.20f); track.raycastTarget = false;
            Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(700, 96));
            var silver = Img(card.transform, UIKit.ShopIconBgA(), White); silver.color = new Color(0.84f, 0.86f, 0.92f); silver.raycastTarget = false;
            Place(silver.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(280, 96));
            var gold = Img(card.transform, UIKit.ShopIconBgA(), White); gold.color = new Color(1f, 0.82f, 0.26f); gold.raycastTarget = false;
            Place(gold.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(84, 96));

            // cute rounded marker + a rounded "head" nub
            var m = Img(card.transform, UIKit.ShopIconBgA(), White); m.color = new Color(0.22f, 0.98f, 0.56f); m.raycastTarget = false;
            Place(m.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(26, 142));
            var nub = Img(m.transform, UIKit.ShopIconBgA(), White); nub.color = new Color(0.22f, 0.98f, 0.56f); nub.raycastTarget = false;
            Place(nub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, 20), new Vector2(48, 42));
            stopMarker = m.rectTransform;

            // transparent full-screen tap catcher ON TOP so a tap anywhere stops the bar (card is non-raycast)
            var tap = Img(stopBarPanel.transform, null, new Color(0, 0, 0, 0));
            Stretch(tap.rectTransform);
            var b = tap.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => stopTapped = true);
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
