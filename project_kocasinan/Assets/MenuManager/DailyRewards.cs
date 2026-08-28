using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Ridebury;

/// <summary>
/// Runtime claim logic for the baked Daily-Reward cards. One reward can be claimed per
/// real day, in order (Day 1 → Day 7); claiming grants that day's gold + free jokers +
/// chest key, shows the card's checkmark with a pop animation, and persists progress via
/// PlayerPrefs. After all 7 are claimed the cycle restarts on the next day.
///
/// <see cref="Plan"/> is the SINGLE SOURCE OF TRUTH for the rewards: the manager re-applies
/// it to every <see cref="DailyCard"/> (values, icon and label) each time the panel opens,
/// and the editor baker builds the cards from the same table — so changing the numbers here
/// takes effect immediately, with no scene re-bake.
///
/// Attached to the Daily panel by the editor baker; it auto-discovers the DailyCard
/// children and wires their buttons.
/// </summary>
public class DailyRewards : MonoBehaviour
{
    const string KeyCount = "bj_dailyClaimed"; // how many days claimed in the current cycle
    const string KeyLast  = "bj_dailyLast";    // date (yyyy-MM-dd) of the last claim

    // ======================= THE 7-DAY REWARD PLAN =======================
    // Gold escalates through the week and day 7 is the jackpot. Jokers are free charges
    // (spent before gold in-game); a chest key opens that tier's garage chest for free.
    // Gold is scaled at claim time by GameConfig.DailyGoldScalePct, so the whole curve can
    // be dialled up/down from Firebase Remote Config without an app update.
    public struct Reward
    {
        public int gold, recolor, swap, heli;
        public string keyTier; // "" | "Bronze" | "Silver" | "Gold" | "Legendary"

        public Reward(int gold, int recolor = 0, int swap = 0, int heli = 0, string keyTier = "")
        { this.gold = gold; this.recolor = recolor; this.swap = swap; this.heli = heli; this.keyTier = keyTier ?? ""; }
    }

    // Index 0 = Day 1.
    public static readonly Reward[] Plan =
    {
        new Reward(200),                                  // Day 1
        new Reward(300, recolor: 1),                      // Day 2
        new Reward(400, swap: 1),                         // Day 3
        new Reward(600, keyTier: "Bronze"),               // Day 4
        new Reward(800, heli: 1),                         // Day 5
        new Reward(1000, recolor: 2, swap: 2),            // Day 6
        new Reward(2500, recolor: 1, swap: 1, heli: 1, keyTier: "Gold"), // Day 7 — JACKPOT
    };

    public static Reward PlanFor(int day)
        => (day >= 1 && day <= Plan.Length) ? Plan[day - 1] : new Reward(0);

    // Gold actually paid for a day (shipped value × the remote scale).
    public static int GoldFor(int day)
    {
        int pct = GameConfig.DailyGoldScalePct > 0 ? GameConfig.DailyGoldScalePct : 100;
        return Mathf.Max(0, Mathf.RoundToInt(PlanFor(day).gold * pct / 100f));
    }

    // Days whose headline reward is a chest key draw the REAL code-built chest (the same art as
    // the garage) instead of an atlas icon; returns the tier to draw, or "" for a sprite icon.
    // The day-7 banner keeps the gold-pile sprite — its headline is the 2500 gold jackpot.
    public static string ChestArtTier(int day)
    {
        var r = PlanFor(day);
        return (!string.IsNullOrEmpty(r.keyTier) && day < Plan.Length) ? r.keyTier : "";
    }

    // The card's reward icon: the headline item of that day (key > joker > gold). Every one of these
    // now comes from the cut icon kit; the gold days pick a coin pile that grows with the payout, so
    // the week visibly escalates towards the day-7 jackpot.
    public static Sprite IconFor(int day)
    {
        var r = PlanFor(day);
        if (!string.IsNullOrEmpty(r.keyTier)) return day >= Plan.Length ? UIKit.CoinPack(6) : null; // chest days draw art, not a sprite
        if (r.heli > 0) return UIKit.JokerHeli();
        if (r.swap > 0 && r.recolor == 0) return UIKit.JokerSwap();      // -> joker_shuffle
        if (r.recolor > 0) return UIKit.JokerRecolor();                  // -> joker_recolor
        int gold = GoldFor(day);
        return UIKit.CoinPack(gold >= 1000 ? 4 : gold >= 600 ? 3 : gold >= 300 ? 2 : 1);
    }

    // Draw the chest into a card's reward slot: the slot Image goes invisible and holds the art.
    // Safe to call repeatedly — an existing chest is left alone.
    public static void BuildChestArt(Image slot, string tier)
    {
        if (slot == null || string.IsNullOrEmpty(tier)) return;
        // The cut kit has a drawn chest per tier — use it directly in the slot instead of assembling
        // the old code-built one out of rectangles.
        var drawn = UIKit.Chest(tier);
        if (drawn != null)
        {
            var stale = slot.transform.Find("ChestArt");
            if (stale) GameObject.Destroy(stale.gameObject); // (not `Object.` — this file imports System too)
            slot.sprite = drawn; slot.color = Color.white; slot.preserveAspect = true;
            return;
        }
        slot.sprite = null;
        slot.color = new Color(1f, 1f, 1f, 0f); // the slot itself is just the anchor now
        if (slot.transform.Find("ChestArt")) return;

        var go = new GameObject("ChestArt", typeof(RectTransform));
        go.transform.SetParent(slot.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = slot.rectTransform.sizeDelta;
        UIKit.BuildChest(go.transform, UIKit.ChestTint(tier), Mathf.Max(40f, slot.rectTransform.sizeDelta.x));
    }

    // Card caption. Days 1-6 are narrow -> "+gold" over one short extra line; day 7 is the
    // wide jackpot banner -> everything on one line.
    public static string LabelFor(int day)
    {
        var r = PlanFor(day);
        string gold = "+" + GoldFor(day);
        if (day >= Plan.Length)
        {
            string s = gold;
            if (!string.IsNullOrEmpty(r.keyTier)) s += "  •  " + Loc.T(r.keyTier.ToUpperInvariant() + " KEY");
            int jokers = r.recolor + r.swap + r.heli;
            if (jokers > 0) s += "  •  " + Loc.T("JOKERS") + " ×" + jokers;
            return s;
        }

        string extra = "";
        if (!string.IsNullOrEmpty(r.keyTier)) extra = Loc.T(r.keyTier.ToUpperInvariant() + " KEY");
        else
        {
            int jokers = r.recolor + r.swap + r.heli;
            bool mixed = (r.recolor > 0 ? 1 : 0) + (r.swap > 0 ? 1 : 0) + (r.heli > 0 ? 1 : 0) > 1;
            if (jokers > 0)
            {
                string name = mixed ? Loc.T("JOKERS") : r.heli > 0 ? Loc.T("HELI") : r.swap > 0 ? Loc.T("SWAP") : Loc.T("RECOLOR");
                extra = jokers > 1 ? name + " ×" + jokers : name;
            }
        }
        return string.IsNullOrEmpty(extra) ? gold : gold + "\n" + extra;
    }

    // Copy the plan onto a card's data fields (used by the runtime AND the editor baker).
    public static void ApplyData(DailyCard c)
    {
        if (c == null) return;
        var r = PlanFor(c.day);
        c.coins = GoldFor(c.day);
        c.recolorJokers = r.recolor; c.swapJokers = r.swap; c.heliJokers = r.heli;
        c.chestKeyTier = r.keyTier;
    }
    // =====================================================================

    DailyCard[] cards;

    void OnEnable()
    {
        if (cards == null)
        {
            cards = GetComponentsInChildren<DailyCard>(true);
            Array.Sort(cards, (a, b) => a.day.CompareTo(b.day));
            foreach (var c in cards)
            {
                var card = c; // capture
                if (card.button) card.button.onClick.AddListener(() => TryClaim(card));
            }
        }
        ApplyPlanToCards();
        Loc.OnLanguageChanged += ApplyPlanToCards; // labels are composed here, so re-compose on a language switch
        Reconcile();
    }

    void OnDisable() { Loc.OnLanguageChanged -= ApplyPlanToCards; }

    // Push the plan onto every card.
    //
    // PRESENTATION BELONGS TO THE PREFAB. The day cards are hand-authored in the Inspector — panel
    // sprite, reward icon, fonts, colours, rects — so nothing here restyles them; whatever the
    // prefab shows is what the game shows. The one exception is the payout caption, which is the
    // promise the claim has to keep, so it is still written from the plan.
    void ApplyPlanToCards()
    {
        if (cards == null) return;
        foreach (var c in cards)
        {
            if (c == null) continue;
            ApplyData(c);

            var amountT = c.transform.Find("Amount");
            var amount = amountT ? amountT.GetComponent<Text>() : null;
            if (!amount) continue;

            amount.text = LabelFor(c.day);

            // The baked caption ("Recolor", "SWAP  +75") is a translation key, so Localizer
            // tagged it — and that tag re-applies the OLD text on every open / language switch.
            // Point the tag at our already-translated caption instead (Loc.T echoes unknown
            // keys back), which keeps it correct whatever order the two run in.
            var lt = amount.GetComponent<LocalizedText>();
            if (lt) { lt.key = amount.text; }
        }
    }

    int Claimed
    {
        get => PlayerPrefs.GetInt(KeyCount, 0);
        set { PlayerPrefs.SetInt(KeyCount, value); PlayerPrefs.Save(); }
    }
    string Last
    {
        get => PlayerPrefs.GetString(KeyLast, "");
        set { PlayerPrefs.SetString(KeyLast, value); PlayerPrefs.Save(); }
    }
    static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    // Refresh checkmarks + which card is claimable right now.
    void Reconcile()
    {
        if (cards == null || cards.Length == 0) return;
        int claimed = Claimed;
        if (claimed >= cards.Length && Last != Today) { claimed = 0; Claimed = 0; } // new cycle

        bool canToday = Last != Today && claimed < cards.Length;
        int claimableDay = claimed + 1; // 1-based

        foreach (var c in cards)
        {
            bool isClaimed = c.day <= claimed;
            if (c.check) { c.check.SetActive(isClaimed); c.check.transform.localScale = Vector3.one; }
            if (c.button) c.button.interactable = canToday && c.day == claimableDay;
        }
    }

    void TryClaim(DailyCard c)
    {
        int claimed = Claimed;
        if (claimed >= cards.Length && Last != Today) claimed = 0; // begin a fresh cycle
        bool canToday = Last != Today && claimed < cards.Length;
        if (!canToday || c.day != claimed + 1) return; // not the claimable day / already claimed today

        if (c.coins > 0) SaveSystem.AddCoins(c.coins);
        if (c.recolorJokers > 0) SaveSystem.AddFreeJoker(0, c.recolorJokers);
        if (c.swapJokers > 0)    SaveSystem.AddFreeJoker(1, c.swapJokers);
        if (c.heliJokers > 0)    SaveSystem.AddFreeJoker(2, c.heliJokers);
        if (!string.IsNullOrEmpty(c.chestKeyTier)) SaveSystem.AddKeys(c.chestKeyTier, 1);
        Claimed = claimed + 1;
        Last = Today;

        // The menu's gold counter is hidden behind the panel — refresh it so it is already
        // up to date when the player closes the daily screen.
        var ctrl = GetComponentInParent<MenuController>();
        if (ctrl) ctrl.Refresh();

        if (c.check)
        {
            c.check.SetActive(true);
            StartCoroutine(Pop(c.check.transform));
        }
        Reconcile();
    }

    IEnumerator Pop(Transform t)
    {
        float e = 0f, dur = 0.3f;
        while (e < dur && t != null)
        {
            e += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(e / dur) * Mathf.PI);
            t.localScale = Vector3.LerpUnclamped(Vector3.one, Vector3.one * 1.4f, k);
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one;
    }
}
