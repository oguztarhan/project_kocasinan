#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif
using System;
using System.Collections;
using System.Collections.Generic; // List<DateTime> in BuildSlots
using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Local (offline) re-engagement notifications. Self-spawns at launch into a DontDestroyOnLoad object — no scene or
    /// Inspector wiring (same pattern as MusicManager / FpsCounter). When the app goes to the BACKGROUND it schedules a
    /// ladder of reminders (1, 2, 3 … 30 days out) plus the free-chest-ready nudge; when the player RETURNS it cancels
    /// them ALL, so an active player never gets pinged. Text is localized to the device language via
    /// <see cref="NotificationContent"/>. Requires the "Mobile Notifications" package (com.unity.mobile.notifications).
    /// Delete this one file to remove the whole feature.
    /// </summary>
    public class NotificationService : MonoBehaviour
    {
        // First channel under the Ridebury name. Android locks a channel's sound and importance at CREATION and an app
        // can NEVER change them afterwards, so every such change REQUIRES a fresh id; the superseded channels are
        // deleted in Start (they keep their OLD literal ids there — that is what exists on already-shipped devices).
        const string AndroidChannel = "ridebury_reengage1";
        // res/raw/ridebury_chime.mp3, shipped by Plugins/Android/NotificationSound.androidlib. Android resource names are
        // [a-z0-9_] and may NOT start with a digit, which is why the original "352669__foolboymedia__up-chime-4" was renamed.
        const string SoundRes = "ridebury_chime";

        // ---- Reminder schedule shape ------------------------------------------
        const int FirstNudgeH = 2;  // day 0: first "come back and play" nudge at +2h — deliberately BELOW the chest floor
                                    // (EveryHours) so a generic come-back always lands same-day and the chest can't be the
                                    // only same-day notification. (Was +4h == chest floor -> the two collided and the guard
                                    // dropped the generic one, so a mid-afternoon quit saw ONLY the chest.)
        const int EveryHours  = 4;  // day 0: cadence between the same-day come-back nudges, and the chest's earliest time
        const int QuietStart  = 10; // earliest local hour a reminder may fire
        const int QuietEnd    = 21; // latest local hour a reminder may fire  -> nothing EVER fires 21:00..10:00
        const int DailyHour   = 18; // day 1+ reminders land here
        const int HorizonDays = 90; // how far ahead rails are laid — see BuildSlots
        const int MinGapMin   = 90; // two reminders may never land within this many minutes of each other

        // The message rotation, cycled for as long as the player stays away (12 entries; index 4 is NOT here — the
        // free-chest line is placed separately at the chest's real ready time by ScheduleAll so it can never lie).
        // Ordered so the first touches carry the concrete hooks (daily reward, encouragement) and the soft "it's been a
        // while" lines come later, which is also where they land naturally as the cadence tapers.
        static readonly int[] Cycle = { 0, 5, 10, 1, 6, 7, 9, 11, 2, 3, 8, 12 };

        // Every reminder time, in order. Day 0 = a "come back" nudge at +FirstNudgeH, then every EveryHours through the
        // rest of today (daytime only) — these are the same-day come-back reminders, generic (not the chest). Then one a
        // day at DailyHour, tapering: daily for week 1 -> every 3 days -> weekly, out to HorizonDays.
        //
        // Why a horizon and not a true infinite loop: Android holds a finite number of alarms, so "forever" has to be
        // approximated. It costs nothing in practice — ScheduleAll runs on EVERY quit, so an active player always has a
        // full 90-day rail laid ahead of them; only someone who never opens the app again can ever reach the end, and by
        // then they have had ~25 reminders over three months and are gone.
        static List<DateTime> BuildSlots(DateTime now)
        {
            var list = new List<DateTime>();
            for (DateTime t = now.AddHours(FirstNudgeH); t.Date == now.Date && t.Hour < QuietEnd; t = t.AddHours(EveryHours))
                if (t.Hour >= QuietStart) list.Add(t);                                                    // day 0, daytime only
            for (int d = 1;  d <= 7;           d += 1) list.Add(now.Date.AddDays(d).AddHours(DailyHour)); // week 1: daily
            for (int d = 10; d <= 30;          d += 3) list.Add(now.Date.AddDays(d).AddHours(DailyHour)); // then every 3 days
            for (int d = 37; d <= HorizonDays; d += 7) list.Add(now.Date.AddDays(d).AddHours(DailyHour)); // then weekly
            return list;
        }

        // The free-chest line rides the chest's REAL ready time so it is never a lie (8h cooldown by default, and an
        // absent player never claims it, so once ready it stays ready). Never earlier than the first day-0 touch, and
        // pulled into the quiet window like everything else. Always resolves to a FUTURE time.
        static DateTime ChestSlot(DateTime now)
        {
            long soonest = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + EveryHours * 3600;
            DateTime t = DateTimeOffset.FromUnixTimeSeconds(Math.Max(SaveSystem.FreeChestReadyAt, soonest)).LocalDateTime;
            if (t.Hour >= QuietEnd)   return t.Date.AddDays(1).AddHours(QuietStart);
            if (t.Hour <  QuietStart) return t.Date.AddHours(QuietStart);
            return t;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("~NotificationService");
            go.AddComponent<NotificationService>();
            DontDestroyOnLoad(go);
        }

        void Start()
        {
#if UNITY_ANDROID
            // Legacy ids, kept VERBATIM — these are the channels sitting on already-shipped devices, and deleting them
            // is what clears the old app name out of the system notification settings.
            AndroidNotificationCenter.DeleteNotificationChannel("busjam_reengage");  // retire the old SILENT (Default) channel
            AndroidNotificationCenter.DeleteNotificationChannel("busjam_reengage2"); // retire v2 (device default sound)
            AndroidNotificationCenter.DeleteNotificationChannel("busjam_reengage3"); // retire v3 (last channel under the old name)
            EnsureChannel();
            // Android 13+ (API 33) needs a runtime POST_NOTIFICATIONS grant or EVERY notification is silently dropped.
            // Request it HERE so local reminders work even when Firebase never initialises — do NOT rely on
            // FirebaseManager for it (that only asks on a SUCCESSFUL Firebase init, which fails on a non-Firebase build).
            const string perm = "android.permission.POST_NOTIFICATIONS";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
                UnityEngine.Android.Permission.RequestUserPermission(perm);
#elif UNITY_IOS
            StartCoroutine(RequestIosAuthorization());
#endif
            CancelAll(); // foreground now — clear anything left over from a previous session
#if UNITY_ANDROID
            // TEST ONLY (TestSeconds): prove the pipeline works WITHOUT needing to background the app — post one visible
            // ping a few seconds after the player grants permission. Auto-removed when TestSeconds is set false for release.
            if (TestSeconds) StartCoroutine(TestPing());
#endif
        }

#if UNITY_ANDROID
        // Creates the notification channel carrying the CUSTOM SOUND. Two-step on purpose:
        //   1. Native (JNI) create -> the only way to set a sound. Unity's AndroidNotificationChannel struct has NO sound
        //      field in com.unity.mobile.notifications 2.3.2 (it exposes SoundName on iOS only), so C# alone cannot do it.
        //   2. Unity's RegisterNotificationChannel -> keeps Unity's own bookkeeping in sync and covers pre-API-26 devices
        //      (no channels there at all). Running it AFTER the native create is safe: Android's createNotificationChannel
        //      never overwrites an existing channel's sound. Doing it in the other order would silently lose the sound.
        static void EnsureChannel()
        {
            TryNativeChannelWithSound();
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = AndroidChannel,
                Name = "Ridebury",
                Importance = Importance.High, // High = sound + heads-up banner; Default delivers SILENTLY (testers never noticed)
                Description = "Ridebury reminders",
                CanShowBadge = true,
                EnableVibration = true,
            });
        }

        // JNI: new NotificationChannel(id, name, IMPORTANCE_HIGH).setSound(android.resource://<pkg>/raw/ridebury_chime, attrs).
        // Only ever takes effect on the FIRST creation of this channel id on a device (Android locks the sound after that)
        // — that is why AndroidChannel was bumped to v3. Failure is never fatal: EnsureChannel's Unity register still
        // creates the channel, just with the device default sound.
        static void TryNativeChannelWithSound()
        {
            try
            {
                using (var ver = new AndroidJavaClass("android.os.Build$VERSION"))
                    if (ver.GetStatic<int>("SDK_INT") < 26) return; // pre-Oreo: no channels exist; Unity's path handles it
                using (var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var nm       = activity.Call<AndroidJavaObject>("getSystemService", "notification"))
                using (var ch       = new AndroidJavaObject("android.app.NotificationChannel", AndroidChannel, "Ridebury", 4)) // 4 = IMPORTANCE_HIGH
                using (var uriCls   = new AndroidJavaClass("android.net.Uri"))
                using (var attrB    = new AndroidJavaObject("android.media.AudioAttributes$Builder"))
                {
                    string pkg = activity.Call<string>("getPackageName");
                    ch.Call("setDescription", "Ridebury reminders");
                    ch.Call("enableVibration", true);
                    ch.Call("setShowBadge", true);
                    using (var uri = uriCls.CallStatic<AndroidJavaObject>("parse", "android.resource://" + pkg + "/raw/" + SoundRes))
                    {
                        // USAGE_NOTIFICATION(5) + CONTENT_TYPE_SONIFICATION(4): routes the clip to the NOTIFICATION volume
                        // stream. Without AudioAttributes some OEMs play it on the media stream or drop it entirely.
                        attrB.Call<AndroidJavaObject>("setUsage", 5).Dispose();
                        attrB.Call<AndroidJavaObject>("setContentType", 4).Dispose();
                        using (var attrs = attrB.Call<AndroidJavaObject>("build"))
                            ch.Call("setSound", uri, attrs);
                    }
                    nm.Call("createNotificationChannel", ch);
                    Debug.Log("[Notif] native channel '" + AndroidChannel + "' created with sound raw/" + SoundRes);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Notif] native channel w/ custom sound failed -> device default sound. " + e.Message);
            }
        }
#endif

        // OnApplicationPause(true) = went to background (the reliable mobile signal); (false) = came back.
        void OnApplicationPause(bool paused)
        {
            if (paused) ScheduleAll();
            else CancelAll();
        }

        void OnApplicationQuit() => ScheduleAll();

        // TEST ONLY: fire the whole 14-message ladder within ~2 minutes (SECONDS, not days) so you can background the
        // app on a device and watch every one arrive — verifies wiring, icon, permission + the per-language text.
        // >>> SET TO false BEFORE RELEASE <<<  (false = the real day-based schedule below.)
        static readonly bool TestSeconds = false;

        void ScheduleAll()
        {
            CancelAll(); // clean slate so reminders never stack up across sessions
            if (!SaveSystem.NotificationsEnabled || !GameConfig.NotificationsEnabled) return; // player toggle / remote kill-switch off

            if (TestSeconds)
            {
                // Background the app and wait: free chest at +5s, the rate nudge at +9s, then the 12 rotation
                // messages every 8s (~1:41 total). Reopening the app after the rate nudge lands should pop the
                // "did you like the game?" prompt in the menu — MarkNotificationScheduled is what makes that follow-up
                // fire, exactly as it does on the real schedule.
                DateTime t = DateTime.Now;
                Schedule(4, t.AddSeconds(5));
                Schedule(13, t.AddSeconds(9));
                RateUs.MarkNotificationScheduled(t.AddSeconds(9));
                for (int i = 0; i < Cycle.Length; i++) Schedule(Cycle[i], t.AddSeconds(13 + i * 8));
                return;
            }

            DateTime now = DateTime.Now;
            var slots = BuildSlots(now);
            DateTime chestAt = ChestSlot(now);

            // NEVER two reminders at once. The chest is the only line whose time it does not get to choose (it rides the
            // chest cooldown), so it wins and any base slot landing within MinGapMin of it is dropped. This is what fixes
            // the double-notification: the old code ran the chest off its own clock and the ladder off another, with
            // nothing reconciling them, so the two could — and did — coincide. Everything else is >= 4h apart by
            // construction. (Android coalesces these inexact alarms by a few minutes, which is harmless at this spacing;
            // exact alarms are not an option — SCHEDULE_EXACT_ALARM is restricted to genuine alarm/timer apps.)
            slots.RemoveAll(t => Math.Abs((t - chestAt).TotalMinutes) < MinGapMin);
            Schedule(4, chestAt);

            // The "did you like the game?" rating nudge (message 13), only while the prompt is still pending and the
            // player has actually finished some levels. Same MinGapMin rule as the chest so it never doubles up with a
            // come-back reminder. RateUs records its fire time, so the next launch after it lands opens the popup.
            if (RateUs.WantsNotification)
            {
                DateTime rateAt = RateUs.NotificationSlot(now, DailyHour);
                if (rateAt > now && Math.Abs((rateAt - chestAt).TotalMinutes) >= MinGapMin)
                {
                    slots.RemoveAll(t => Math.Abs((t - rateAt).TotalMinutes) < MinGapMin);
                    Schedule(13, rateAt);
                    RateUs.MarkNotificationScheduled(rateAt);
                }
            }

            // The rotation repeats across the whole horizon, so a long-absent player keeps getting reminded rather than
            // falling off the end of a fixed 12-entry ladder.
            for (int i = 0; i < slots.Count; i++) Schedule(Cycle[i % Cycle.Length], slots[i]);
        }

        void Schedule(int notif, DateTime when)
        {
#if UNITY_ANDROID || UNITY_IOS
            var txt = NotificationContent.Get(notif);
#endif
#if UNITY_ANDROID
            AndroidNotificationCenter.SendNotification(new AndroidNotification
            {
                Title = txt.title,
                Text = txt.body,
                FireTime = when,
                SmallIcon = "ridebury_notify", // white bus silhouette in the status bar — must match the Mobile Notifications icon Id
                LargeIcon = "ridebury_large",  // full-colour app icon shown inside the expanded notification (optional)
            }, AndroidChannel);
#elif UNITY_IOS
            double secs = (when - DateTime.Now).TotalSeconds;
            if (secs < 1) secs = 1;
            iOSNotificationCenter.ScheduleNotification(new iOSNotification
            {
                Title = txt.title,
                Body = txt.body,
                ShowInForeground = false,
                Trigger = new iOSNotificationTimeIntervalTrigger { TimeInterval = TimeSpan.FromSeconds(secs), Repeats = false },
            });
#endif
        }

        void CancelAll()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllNotifications();
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
            iOSNotificationCenter.ApplicationBadge = 0;
#endif
        }

        // ---------------- On-device delivery TEST (diagnostic) ----------------
        // Fires ONE visible notification ~4s after it is called, DELIBERATELY bypassing the +1h/day-based re-engagement
        // schedule AND the player/remote enabled toggles — an explicit tap is an explicit request. Android posts it even
        // while the app is in the FOREGROUND (the channel is High importance), so a tester gets a definitive yes/no in
        // seconds without backgrounding the app or waiting an hour. If NOTHING appears after tapping, the fault is
        // permission / OEM battery-kill / a Remote Config kill-switch — NOT this code. Returns a short status string for
        // the caller to show on the button. Wired to the Settings "TEST NOTIFICATION" button (GameUI).
        // NOTE: stay in the app after tapping — backgrounding within ~4s triggers ScheduleAll()'s CancelAll() and wipes it.
        public static string SendTest()
        {
#if UNITY_ANDROID
            EnsureChannel(); // idempotent — in case Start() has not run yet on this instance
            // 1) Runtime permission (Android 13+). If the app has NO notification permission, ask and bail — grant it,
            //    then tap again. (If the whole app's notifications are OFF in system settings this also reads false.)
            const string perm = "android.permission.POST_NOTIFICATIONS";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
            {
                UnityEngine.Android.Permission.RequestUserPermission(perm); // async dialog — user must grant then tap again
                Debug.Log("[NotifTest] POST_NOTIFICATIONS not granted -> requested. Grant, then tap again.");
                return "İzin YOK — ver, tekrar dokun";
            }
            // 2) Post it for +5s. The High channel shows it as a heads-up EVEN in the foreground, so STAY in the app
            //    (backgrounding within ~5s runs ScheduleAll()'s CancelAll() and wipes it).
            var txt = NotificationContent.Get(0);
            AndroidNotificationCenter.SendNotification(new AndroidNotification
            {
                Title = txt.title,
                Text = txt.body,
                FireTime = DateTime.Now.AddSeconds(5),
                SmallIcon = "ridebury_notify",
                LargeIcon = "ridebury_large",
            }, AndroidChannel);
            // NOTE: deliberately DO NOT call AndroidNotificationCenter.GetNotificationChannel() to read back the channel
            // importance — on this Mobile Notifications native lib it throws a JNI NoSuchFieldError ("field 'id' in class
            // ...NotificationChannelWrapper"). The notification is already posted above; the readback was only a
            // diagnostic, so we skip it. (If a notification never appears, check the channel toggle in system settings.)
            Debug.Log("[NotifTest] permission OK. posted for +5s on '" + AndroidChannel + "'.");
            return "Gönderildi — 5 sn, uygulamada kal";
#elif UNITY_IOS
            var txt = NotificationContent.Get(0);
            iOSNotificationCenter.ScheduleNotification(new iOSNotification
            {
                Title = txt.title,
                Body = txt.body,
                ShowInForeground = true, // show even while the app is open, so the test is visible without backgrounding
                Trigger = new iOSNotificationTimeIntervalTrigger { TimeInterval = TimeSpan.FromSeconds(5), Repeats = false },
            });
            return "Gönderildi — 5 sn";
#else
            return "Sadece mobil";
#endif
        }

#if UNITY_ANDROID
        // Waits for the POST_NOTIFICATIONS grant, then posts ONE visible notification ~5s later — so a tester can confirm
        // the whole chain (permission -> channel -> icon -> delivery) from the FOREGROUND, with no backgrounding needed.
        IEnumerator TestPing()
        {
            const string perm = "android.permission.POST_NOTIFICATIONS";
            float waited = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm) && waited < 30f)
            {
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm)) yield break;     // never granted
            if (!SaveSystem.NotificationsEnabled || !GameConfig.NotificationsEnabled) yield break;  // toggled off
            yield return new WaitForSeconds(2f);
            Schedule(0, DateTime.Now.AddSeconds(5)); // "Otobüsler seni bekliyor" ~5s out; Android shows it even in foreground
        }
#endif

#if UNITY_IOS
        IEnumerator RequestIosAuthorization()
        {
            using (var req = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, true))
                while (!req.IsFinished) yield return null;
        }
#endif
    }
}
