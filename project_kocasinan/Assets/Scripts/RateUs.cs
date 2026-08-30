using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ridebury
{
    /// <summary>
    /// "Did you like the game? — rate us" prompt. Self-spawns at launch into a DontDestroyOnLoad object and builds its
    /// own top-most Canvas, so it needs NO scene or Inspector wiring (same pattern as NotificationService /
    /// MusicManager). Two surfaces, one state machine:
    ///
    ///   • IN-GAME popup  — shown after a level win once <see cref="FirstAskAfterWins"/> wins are banked.
    ///   • LOCAL NOTIFICATION — NotificationService schedules message 13 ("did you like the game?") a couple of days
    ///     out while the prompt is still pending; when the player next opens the app after it fired, the menu shows
    ///     the same popup (see <see cref="MaybeShowFromNotification"/>).
    ///
    /// >>> iOS SHOWS NO POPUP AT ALL. <<< The App Store Review Guidelines' "Ratings and Reviews" rule requires the
    /// system API (SKStoreReviewController) for any app-initiated rating ask and explicitly disallows custom review
    /// prompts — and a YES/NO sentiment gate in front of the store link is the exact pattern review looks for. So on
    /// iOS <see cref="ShowNow"/> banks the ask against the same cadence gates and calls Device.RequestStoreReview();
    /// everything below this line — the card, the YES/NO step, ASK ME LATER / NEVER ASK AGAIN and the
    /// ?action=write-review deep link — is the ANDROID surface only. Do not "unify" the two platforms.
    ///
    /// Android flow — YES and NO BOTH lead to the store, because a 1-star review with a written reason is worth more
    /// than silence: YES asks for the rating + comment, NO asks the player to tell us what went wrong in a comment.
    /// Every step carries ASK ME LATER (snooze <see cref="LaterDelayDays"/> days) and NEVER ASK AGAIN (permanent).
    ///
    /// Delete this one file + its three call sites (RideburyGame.AdvanceAfterWin, MenuController.Start,
    /// NotificationService.ScheduleAll) to remove the whole feature.
    /// </summary>
    public class RateUs : MonoBehaviour
    {
        // ---- Store targets --------------------------------------------------
        // Android: Application.identifier, so the package can never drift out of sync with Player Settings.
        // iOS: the NUMERIC App Store id (App Store Connect ▸ App Information ▸ "Apple ID"), NOT the bundle id. There is
        // no way to derive it from the bundle id, so it has to be pasted here. If it is ever blanked, the iOS path
        // falls back to the system in-app rating sheet (Device.RequestStoreReview) — stars only, no comment box.
        const string AppleAppId = "6791205940";

        // ============================================================================================
        // TESTING — set true to re-check the prompt. It then IGNORES every gate (wins banked, the 3-day
        // ASK ME LATER snooze, NEVER ASK AGAIN, the MaxAsks ceiling), so it reopens after EVERY level win
        // no matter what you tapped last time. Pair it with ResetState() for a clean run.
        // In the EDITOR the store buttons only log — Play/App Store links need a real device build.
        //   >>> MUST stay false for release. <<<
        // ============================================================================================
        public static bool DebugAlwaysAsk = false;

        /// <summary>Wipes all saved rating state (back to a fresh install). Testing/QA helper.</summary>
        public static void ResetState()
        {
            foreach (var k in new[] { K_State, K_NextAt, K_Asks, K_Wins, K_NotifAt }) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }

        // ---- When to ask ----------------------------------------------------
        const int FirstAskAfterWins  = 3;  // never ask before the player has actually finished a few levels
        const int RepeatAfterWins    = 10; // wins that must pass between one ask and the next
        const int LaterDelayDays     = 3;  // "ASK ME LATER" snooze
        const int MaxAsks            = 4;  // after this many asks we stop for good (auto NEVER) — no nagging
        const int NotifyAfterDays    = 2;  // how far out the "did you like it?" local notification is scheduled

        // ---- Persisted state (PlayerPrefs) ----------------------------------
        const string K_State   = "bj_rate_state";    // 0 pending · 1 never ask again · 2 done (rated)
        const string K_NextAt  = "bj_rate_next_at";  // epoch seconds — earliest next ask
        const string K_Asks    = "bj_rate_asks";     // how many times the popup has been shown
        const string K_Wins    = "bj_rate_wins";     // level wins banked since the last ask
        const string K_NotifAt = "bj_rate_notif_at"; // epoch seconds the rate notification is due (0 = none pending)

        const int StatePending = 0, StateNever = 1, StateRated = 2;

        static int  State   { get => PlayerPrefs.GetInt(K_State, StatePending); set { PlayerPrefs.SetInt(K_State, value); PlayerPrefs.Save(); } }
        static int  Asks    { get => PlayerPrefs.GetInt(K_Asks, 0);             set { PlayerPrefs.SetInt(K_Asks, value); PlayerPrefs.Save(); } }
        static int  Wins    { get => PlayerPrefs.GetInt(K_Wins, 0);             set { PlayerPrefs.SetInt(K_Wins, value); PlayerPrefs.Save(); } }
        static long NextAt  { get => Epoch(K_NextAt);  set => SetEpoch(K_NextAt, value); }
        static long NotifAt { get => Epoch(K_NotifAt); set => SetEpoch(K_NotifAt, value); }

        // Epoch seconds are stored as STRINGS: PlayerPrefs has no long, and an int overflows in 2038.
        static long Epoch(string k) => long.TryParse(PlayerPrefs.GetString(k, "0"), out long v) ? v : 0L;
        static void SetEpoch(string k, long v) { PlayerPrefs.SetString(k, v.ToString()); PlayerPrefs.Save(); }
        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>True while the player has neither rated nor opted out — the only state that may ever ask.</summary>
        public static bool Pending => State == StatePending;

        /// <summary>Player has finished a level. Banks a win; the popup itself is shown by <see cref="MaybeShow"/>.</summary>
        public static void NotifyLevelWon()
        {
            if (!Pending) return;
            Wins = Wins + 1;
        }

        // Enough wins banked (a higher bar for every ask after the first), the snooze has expired, and we have not
        // burned through MaxAsks. GameConfig.RateUsEnabled is the Remote Config kill-switch on top.
        static bool Eligible()
        {
            if (DebugAlwaysAsk) return true; // testing — every gate below is bypassed
            if (!Pending || !GameConfig.RateUsEnabled) return false;
            if (Asks >= MaxAsks) { State = StateNever; return false; } // asked enough — stop for good
            if (Wins < (Asks == 0 ? FirstAskAfterWins : RepeatAfterWins)) return false;
            return Now >= NextAt;
        }

        /// <summary>Show the prompt if the player is due for it. Returns true if it opened. Safe to call anywhere.</summary>
        public static bool MaybeShow()
        {
            if (!Eligible()) return false;
            ShowNow();
            return true;
        }

        /// <summary>
        /// Called from the main menu on launch: if the "did you like the game?" notification's fire time has passed,
        /// the player has seen it, so open the popup they were nudged towards (subject to the same eligibility gate).
        /// </summary>
        public static void MaybeShowFromNotification()
        {
            long due = NotifAt;
            if (due == 0 || Now < due) return;
            NotifAt = 0;                            // consumed — it only counts once
            if (!Pending) return;
            Wins = Mathf.Max(Wins, RepeatAfterWins); // the notification IS the invitation — clear the win/snooze gates
            NextAt = 0;                             // (RepeatAfterWins is the higher of the two thresholds)
            MaybeShow();
        }

        // ---- Notification hooks (NotificationService) -----------------------

        /// <summary>
        /// True when the rate nudge deserves a slot in the reminder ladder. Gated on BestLevel, NOT on
        /// <see cref="Wins"/> — Wins resets on every ask, and a player who is away (exactly who this notification is
        /// for) is banking none, so it would gate the nudge off permanently after the first ask.
        /// </summary>
        public static bool WantsNotification =>
            Pending && GameConfig.RateUsEnabled && Asks < MaxAsks && SaveSystem.BestLevel > FirstAskAfterWins;

        /// <summary>
        /// Local time the rate nudge should fire: <see cref="NotifyAfterDays"/> out, but never before an ASK ME LATER
        /// snooze has expired. Pure — the caller confirms with <see cref="MarkNotificationScheduled"/>.
        /// </summary>
        public static DateTime NotificationSlot(DateTime now, int hour)
        {
            DateTime at = now.Date.AddDays(NotifyAfterDays).AddHours(hour);
            DateTime snoozeEnd = NextAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(NextAt).LocalDateTime : now;
            while (at < snoozeEnd) at = at.AddDays(1); // keep the daily hour, just push past the snooze
            return at;
        }

        /// <summary>Record the nudge's fire time, so the first launch after it lands opens the popup.</summary>
        public static void MarkNotificationScheduled(DateTime at) =>
            NotifAt = new DateTimeOffset(at.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();

        // ---- Popup ----------------------------------------------------------

        static RateUs instance;

        /// <summary>Force the prompt open, bypassing the eligibility gate (kept public for a Settings "RATE US" entry).</summary>
        public static void ShowNow()
        {
#if UNITY_IOS
            // iOS: the system rating sheet, never our own card — see the class summary. Fire-and-forget by design
            // (Apple never reports whether the sheet appeared, and iOS caps it at three prompts a year), so the ask
            // is banked exactly as the popup banks it: burn one of MaxAsks, reset the win counter and re-arm the
            // snooze. Eligible() therefore still spaces the calls out and stops for good after MaxAsks, which is
            // what keeps us well inside Apple's own quota instead of leaning on it.
            Asks = Asks + 1;
            Wins = 0;
            NextAt = Now + LaterDelayDays * 86400L;
            NotifAt = 0; // a pending "did you like the game?" nudge has now been answered by the sheet
            FirebaseManager.LogEvent("rate_prompt_shown", "asks", Asks);
#if UNITY_EDITOR
            Debug.Log("[RateUs] iOS build target — system review sheet requested (no-op in the Editor). " +
                      "The custom popup is deliberately NOT shown on iOS.");
#else
            UnityEngine.iOS.Device.RequestStoreReview();
#endif
            return;
#else
            if (instance != null) return; // already up
            var go = new GameObject("~RateUsPopup");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RateUs>();
            instance.Build();

            Asks = Asks + 1;
            Wins = 0;
            NextAt = Now + LaterDelayDays * 86400L; // pre-arm the snooze, so a force-quit mid-prompt still backs off
            FirebaseManager.LogEvent("rate_prompt_shown", "asks", Asks);
#endif
        }

        Transform card;      // the blue panel everything is parented to — rebuilt from scratch for each step
        float prevTimeScale; // gameplay is frozen while the prompt is up and restored exactly as it was

        void Build()
        {
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // above every in-game panel (the highest of those is 70) and the menu
            var sc = canvasGo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);
            sc.matchWidthOrHeight = 0f; // match WIDTH, like GameUI — the card always fits the screen width
            canvasGo.AddComponent<GraphicRaycaster>();

            var dim = Img(canvasGo.transform, null, new Color(0, 0, 0, 0.72f)); // also eats taps outside the card
            var drt = dim.rectTransform;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;

            var box = Img(canvasGo.transform, UIKit.EmptyBoxBlue(), Color.white);
            box.color = new Color(0.631f, 0.161f, 0.161f); // #A12929 — the same card tint as the Settings panel
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(900, 1120);
            card = box.transform;

            StepAsk();
        }

        // Wipes the card's contents so the next step can be laid out on the same panel (no second window popping up).
        // Destroy only takes effect at the end of the frame, so the outgoing step is deactivated FIRST — otherwise
        // step 1's buttons would draw (and stay tappable) on top of step 2 for a frame.
        void Clear()
        {
            for (int i = card.childCount - 1; i >= 0; i--)
            {
                var child = card.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        // ---- Step 1: did you like it? ---------------------------------------
        void StepAsk()
        {
            Clear();
            Title(Loc.T("ENJOYING THE GAME?"));
            Body(Loc.T("Are you having fun so far? Your answer helps us make the game better."));

            Btn(new Vector2(-200, -20), new Vector2(340, 160), new Color(0.30f, 0.72f, 0.36f), Loc.T("YES"), 52, () =>
            {
                FirebaseManager.LogEvent("rate_answer", "liked", "yes");
                StepRate(true);
            });
            Btn(new Vector2(200, -20), new Vector2(340, 160), new Color(0.85f, 0.30f, 0.28f), Loc.T("NO"), 52, () =>
            {
                FirebaseManager.LogEvent("rate_answer", "liked", "no");
                StepRate(false);
            });

            LaterAndNever();
            Close(Later); // the ✕ is the gentlest option: treat it as "ask me later", never as a refusal
        }

        // ---- Step 2: both answers ask for a rating + a written comment ------
        void StepRate(bool liked)
        {
            Clear();
            if (liked)
            {
                Title(Loc.T("AWESOME!"));
                Body(Loc.T("Then please rate us and leave a comment on the store. It only takes a moment and it really helps us."));
            }
            else
            {
                Title(Loc.T("SORRY TO HEAR THAT"));
                Body(Loc.T("Please rate us and tell us what went wrong in a comment. We read every one and we will fix it."));
            }

            Btn(new Vector2(0, -20), new Vector2(600, 160), new Color(0.95f, 0.72f, 0.20f),
                Loc.T(liked ? "RATE US" : "WRITE A REVIEW"), 48, () =>
            {
                State = StateRated; // the store page is open — never ask again either way
                FirebaseManager.LogEvent("rate_opened_store", "liked", liked ? "yes" : "no");
                OpenStoreReview();
                Dismiss();
            });

            LaterAndNever();
            Close(Later);
        }

        // The two escape hatches, identical on both steps.
        void LaterAndNever()
        {
            Btn(new Vector2(0, -190), new Vector2(600, 120), new Color(0.35f, 0.55f, 0.85f), Loc.T("ASK ME LATER"), 38, Later);
            Btn(new Vector2(0, -350), new Vector2(600, 120), new Color(0.45f, 0.45f, 0.50f), Loc.T("NEVER ASK AGAIN"), 38, () =>
            {
                State = StateNever;
                NotifAt = 0; // and drop the pending notification follow-up
                FirebaseManager.LogEvent("rate_never");
                Dismiss();
            });
        }

        void Later()
        {
            NextAt = Now + LaterDelayDays * 86400L;
            NotifAt = 0;
            FirebaseManager.LogEvent("rate_later", "asks", Asks);
            Dismiss();
        }

        void Dismiss()
        {
            Time.timeScale = prevTimeScale;
            instance = null;
            Destroy(gameObject);
        }

        // ---- Store ----------------------------------------------------------

        /// <summary>
        /// Open the platform's review surface. Every path is guarded — this can never throw at the caller.
        ///
        /// >>> ONLY call this from a control the player deliberately tapped (a "RATE US" row in Settings). <<<
        /// It is NOT reachable on iOS today: <see cref="ShowNow"/> uses the system sheet there, and nothing else
        /// calls this. Apple's rule is about app-INITIATED prompts, so the ?action=write-review deep link below
        /// stays correct for an explicit tap (and is better than the system sheet there, which may silently show
        /// nothing) — but routing an automatic prompt back through here is exactly the 4.3-adjacent rejection we
        /// just removed. Wire it to a button, never to a timer, a level win or a notification.
        /// </summary>
        public static void OpenStoreReview()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UseInAppReview && TryPlayInAppReview()) return;
            // market:// opens the Play app straight on the listing (where the write-review control lives); the https
            // form is the fallback for devices with no Play app (it opens in a browser).
            string pkg = Application.identifier;
            try { Application.OpenURL("market://details?id=" + pkg); }
            catch { Application.OpenURL("https://play.google.com/store/apps/details?id=" + pkg); }
#elif UNITY_IOS && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(AppleAppId))
            {
                // ?action=write-review lands directly on the review composer (stars + comment box).
                Application.OpenURL("itms-apps://itunes.apple.com/app/id" + AppleAppId + "?action=write-review");
                return;
            }
            // No App Store id pasted above -> Apple's in-app rating sheet. Stars only, no comment, and iOS caps it at
            // three prompts a year, but it is the only thing that works without the numeric id.
            UnityEngine.iOS.Device.RequestStoreReview();
#else
            Debug.Log("[RateUs] store review requested (no-op in the Editor / on desktop).");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Google Play In-App Review (the overlay that rates without leaving the game). OFF by default because it needs
        // a build change and Play silently quota-limits it (it can show nothing at all, and it never guarantees the
        // comment box) — the store listing always works and always allows a written review. To turn it on, add
        //     implementation 'com.google.android.play:review:2.0.2'
        // to the dependencies block of Assets/Plugins/Android/mainTemplate.gradle (OUTSIDE the "Android Resolver
        // Dependencies Start/End" markers — EDM4U rewrites everything between them) and flip this to true. If the
        // class is missing at runtime the JNI call throws and we fall through to the store listing anyway.
        // (static readonly, not const: a const false would make the call site unreachable and warn on every compile.)
        static readonly bool UseInAppReview = false;

        // Google Play In-App Review via JNI. Fire-and-forget: Play decides whether to actually show the overlay and
        // NEVER tells us the outcome (by design), so we only report whether the request was dispatched. Requires the
        // com.google.android.play:review dependency — see UseInAppReview above. Any failure returns false and the
        // caller falls through to the store listing.
        static bool TryPlayInAppReview()
        {
            try
            {
                using (var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var factory  = new AndroidJavaClass("com.google.android.play.core.review.ReviewManagerFactory"))
                using (var manager  = factory.CallStatic<AndroidJavaObject>("create", activity))
                using (var task     = manager.Call<AndroidJavaObject>("requestReviewFlow"))
                {
                    task.Call<AndroidJavaObject>("addOnCompleteListener", new ReviewRequestListener(activity, manager)).Dispose();
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RateUs] Play in-app review unavailable -> store listing. " + e.Message);
                return false;
            }
        }

        // Play hands the ReviewInfo back asynchronously; launchReviewFlow must then run on the UI thread.
        class ReviewRequestListener : AndroidJavaProxy
        {
            readonly AndroidJavaObject activity, manager;
            public ReviewRequestListener(AndroidJavaObject a, AndroidJavaObject m)
                // review:2.x returns a Google Play Services Task, NOT the old play-core one — the proxy must implement
                // com.google.android.gms.tasks.OnCompleteListener or the JNI proxy never binds.
                : base("com.google.android.gms.tasks.OnCompleteListener") { activity = a; manager = m; }

            void onComplete(AndroidJavaObject task)
            {
                try
                {
                    if (!task.Call<bool>("isSuccessful")) return;
                    var info = task.Call<AndroidJavaObject>("getResult");
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try { manager.Call<AndroidJavaObject>("launchReviewFlow", activity, info); }
                        catch (Exception e) { Debug.LogWarning("[RateUs] launchReviewFlow failed. " + e.Message); }
                    }));
                }
                catch (Exception e) { Debug.LogWarning("[RateUs] review task failed. " + e.Message); }
            }
        }
#endif

        // ---- Tiny UI builders (local copies so this file stays self-contained) ----

        static Image Img(Transform parent, Sprite sprite, Color fallback)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.color = Color.white; } else img.color = fallback;
            return img;
        }

        void Title(string text)
        {
            var tile = Img(card, UIKit.TitleBarA(), new Color(0.25f, 0.55f, 0.90f));
            tile.color = new Color(0.25f, 0.55f, 0.90f); tile.raycastTarget = false;
            var rt = tile.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, 450); rt.sizeDelta = new Vector2(680, 150);
            // wrap:true so a long translation ("BUNU DUYDUĞUMUZA ÜZÜLDÜK") shrinks/folds inside the tile instead of
            // spilling past it. Card spans ±450 vertically: title 375..525, body 90..330, buttons -100..60,
            // LATER -250..-130, NEVER -410..-290 — nothing overlaps and nothing leaves the panel.
            Label(card, text, new Vector2(0, 450), new Vector2(640, 130), 50, Color.white, true);
        }

        void Body(string text) => Label(card, text, new Vector2(0, 210), new Vector2(740, 240), 38, Color.white, true);

        // wrap=false keeps single-line labels (titles, button captions) from ever being broken mid-word by a long
        // translation; wrap=true is used for the body copy, which MUST fold inside the card.
        Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color, bool wrap)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UIKit.Title(); t.text = text; t.fontSize = fontSize; t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = wrap;                 // long translations shrink instead of spilling off the card
            t.resizeTextMinSize = 22; t.resizeTextMaxSize = fontSize;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.4f); sh.effectDistance = new Vector2(2, -2);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return t;
        }

        void Btn(Vector2 pos, Vector2 size, Color tint, string caption, int fontSize, Action onClick)
        {
            var img = Img(card, UIKit.PriceBtnA(), tint);
            img.color = tint;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            Label(img.transform, caption, Vector2.zero, new Vector2(size.x - 40, size.y - 40), fontSize, Color.white, true);
        }

        void Close(Action onClose)
        {
            var img = Img(card, UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f));
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-40, -40); rt.sizeDelta = new Vector2(96, 96);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClose());
            img.transform.SetAsLastSibling();
        }
    }
}
