using UnityEngine;
using Firebase;
using Firebase.Extensions;
using Firebase.RemoteConfig;
using Firebase.Messaging;
using Firebase.Analytics;

namespace Ridebury
{
    /// <summary>
    /// Single entry point for Firebase: initialises the SDK on launch, pulls Remote Config into <see cref="GameConfig"/>,
    /// wires Cloud Messaging (push notifications) with a player on/off toggle + a remote kill-switch, and exposes a thin
    /// Analytics logger. Everything is guarded so the game still runs if Firebase / Play Services are unavailable.
    /// </summary>
    public class FirebaseManager : MonoBehaviour
    {
        public static FirebaseManager Instance { get; private set; }
        public static bool Ready { get; private set; }

        const string NotifTopic = "all";
        // PRODUCTION (2026-07-23): 1h cache to avoid Firebase fetch throttling. (Dev used 0 = always fetch so console
        // changes appeared on relaunch.)
        const ulong MinFetchIntervalMs = 3600000;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("FirebaseManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FirebaseManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.Result != DependencyStatus.Available)
                {
                    Debug.LogWarning("[Firebase] unavailable (" + (task.IsFaulted ? "faulted" : task.Result.ToString()) +
                                     ") — running on GameConfig defaults.");
                    return;
                }
                Ready = true;
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                InitRemoteConfig();
                InitMessaging();
            });
        }

        // ---------------- Remote Config ----------------
        void InitRemoteConfig()
        {
            var rc = FirebaseRemoteConfig.DefaultInstance;
            rc.SetConfigSettingsAsync(new ConfigSettings { MinimumFetchIntervalInMilliseconds = MinFetchIntervalMs })
              .ContinueWithOnMainThread(_ =>
              rc.SetDefaultsAsync(GameConfig.Defaults()).ContinueWithOnMainThread(__ =>
              rc.FetchAndActivateAsync().ContinueWithOnMainThread(___ =>
              {
                  GameConfig.ApplyRemote(k => rc.GetValue(k).LongValue, k => rc.GetValue(k).BooleanValue);
                  ApplyNotificationState(); // the remote kill-switch may have changed
                  Debug.Log("[Firebase] Remote Config applied.");
              })));
        }

        // ---------------- Cloud Messaging (push) ----------------
        void InitMessaging()
        {
            FirebaseMessaging.TokenReceived   += (s, e) => Debug.Log("[FCM] token received");
            FirebaseMessaging.MessageReceived += (s, e) => Debug.Log("[FCM] message: " + (e.Message?.Notification?.Title ?? "(data)"));
            RequestNotificationPermission();
            ApplyNotificationState();
        }

        static void RequestNotificationPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string perm = "android.permission.POST_NOTIFICATIONS"; // Android 13+
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
                UnityEngine.Android.Permission.RequestUserPermission(perm);
#endif
        }

        /// <summary>Subscribe / unsubscribe the device using BOTH the player's toggle AND the remote kill-switch. Called
        /// on init, after a Remote Config fetch, and whenever the player flips the Settings notifications toggle.</summary>
        public void ApplyNotificationState()
        {
            if (!Ready) return;
            bool on = SaveSystem.NotificationsEnabled && GameConfig.NotificationsEnabled;
            if (on) FirebaseMessaging.SubscribeAsync(NotifTopic);
            else    FirebaseMessaging.UnsubscribeAsync(NotifTopic);
        }

        // ---------------- Analytics ----------------
        public static void LogEvent(string name)                         { if (Ready) FirebaseAnalytics.LogEvent(name); }
        public static void LogEvent(string name, string p, long value)   { if (Ready) FirebaseAnalytics.LogEvent(name, p, value); }
        public static void LogEvent(string name, string p, string value) { if (Ready) FirebaseAnalytics.LogEvent(name, p, value); }
    }
}
