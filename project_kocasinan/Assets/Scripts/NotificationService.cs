#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif
using System;
using System.Collections;
using UnityEngine;

namespace BusJam
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
        const string AndroidChannel = "busjam_reengage";

        // Re-engagement ladder: (days from app close, local hour, NotificationContent index). Fired ONLY if the player
        // does not come back — every entry is cancelled the moment the app regains focus. Index 4 (free chest) is
        // scheduled separately at its own ready time, so the ladder uses the other 12 messages.
        static readonly (int day, int hour, int notif)[] Ladder =
        {
            (1,  18, 0),   // Otobüsler seni bekliyor
            (2,  18, 9),   // Yeni bölüm seni bekliyor
            (3,  18, 1),   // Seni özledik
            (4,  18, 10),  // Pes etme
            (5,  18, 7),   // Garajını büyüt
            (6,  18, 5),   // Günlük ödülün hazır
            (7,  18, 2),   // Uzun zaman oldu
            (9,  18, 11),  // Bonus bölüm zamanı
            (11, 18, 6),   // Seriyi bozma
            (14, 18, 3),   // Hâlâ buradayız
            (21, 18, 8),   // Koleksiyonu tamamla
            (30, 18, 12),  // Bir molaya ne dersin
        };

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
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = AndroidChannel,
                Name = "BusJam",
                Importance = Importance.Default,
                Description = "BusJam reminders",
            });
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

        // OnApplicationPause(true) = went to background (the reliable mobile signal); (false) = came back.
        void OnApplicationPause(bool paused)
        {
            if (paused) ScheduleAll();
            else CancelAll();
        }

        void OnApplicationQuit() => ScheduleAll();

        // TEST ONLY: fire the whole 13-message ladder within ~2 minutes (SECONDS, not days) so you can background the
        // app on a device and watch every one arrive — verifies wiring, icon, permission + the per-language text.
        // >>> SET TO false BEFORE RELEASE <<<  (false = the real day-based schedule below.)
        static readonly bool TestSeconds = true;

        void ScheduleAll()
        {
            CancelAll(); // clean slate so reminders never stack up across sessions
            if (!SaveSystem.NotificationsEnabled || !GameConfig.NotificationsEnabled) return; // player toggle / remote kill-switch off

            if (TestSeconds)
            {
                // Background the app and wait: free chest at +5s, then the 12 ladder messages every 8s (13s, 21s, 29s … ~1:41).
                DateTime t = DateTime.Now;
                Schedule(4, t.AddSeconds(5));
                int k = 0;
                foreach (var s in Ladder) Schedule(s.notif, t.AddSeconds(13 + (k++) * 8));
                return;
            }

            // Free chest: at its ready time (or +1h if it is already ready) — the "Bedava sandık" message (index 4).
            long readyEpoch = SaveSystem.FreeChestReadyAt;
            long soon = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
            DateTime freeWhen = DateTimeOffset.FromUnixTimeSeconds(Math.Max(readyEpoch, soon)).LocalDateTime;
            Schedule(4, freeWhen);

            // Re-engagement ladder (fires at ~18:00 local on each offset day).
            DateTime now = DateTime.Now;
            foreach (var s in Ladder)
            {
                DateTime when = now.Date.AddDays(s.day).AddHours(s.hour);
                if (when <= now.AddMinutes(1)) when = now.AddMinutes(1);
                Schedule(s.notif, when);
            }
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
                SmallIcon = "busjam_notify", // white bus silhouette in the status bar — must match the Mobile Notifications icon Id
                LargeIcon = "busjam_large",  // full-colour app icon shown inside the expanded notification (optional)
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
