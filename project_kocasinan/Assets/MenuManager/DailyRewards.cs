using System;
using System.Collections;
using UnityEngine;
using BusJam;

/// <summary>
/// Runtime claim logic for the baked Daily-Reward cards. One reward can be claimed per
/// real day, in order (Day 1 → Day 7); claiming grants the coin reward, shows the
/// card's checkmark with a pop animation, and persists progress via PlayerPrefs. After
/// all 7 are claimed the cycle restarts on the next day.
///
/// Attached to the Daily panel by the editor baker; it auto-discovers the DailyCard
/// children and wires their buttons.
/// </summary>
public class DailyRewards : MonoBehaviour
{
    const string KeyCount = "bj_dailyClaimed"; // how many days claimed in the current cycle
    const string KeyLast  = "bj_dailyLast";    // date (yyyy-MM-dd) of the last claim

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
        Reconcile();
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
        Claimed = claimed + 1;
        Last = Today;

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
