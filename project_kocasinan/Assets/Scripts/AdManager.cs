// =============================================================================
//  AdManager — the SINGLE home for all AdMob (Google Mobile Ads) logic.
//  BusJamGame / GameUI never touch GoogleMobileAds types; they call only the clean
//  public methods below (ShowBanner/HideBanner/ShowInterstitialIfEligible/
//  ShowRewarded/AddInterstitialWin/AddInterstitialLoss/ForceInterstitial).
//
//  COMPILE SAFETY: every GoogleMobileAds reference is wrapped in `#if GMA_ADS`.
//  Until you (1) import the Google Mobile Ads package and (2) add the scripting
//  define symbol GMA_ADS (Project Settings ▸ Player ▸ Other ▸ Scripting Define
//  Symbols), this file compiles as harmless STUBS — the game runs, ads no-op, and
//  every fallback path still fires so nothing soft-locks.
//
//  TEST AD UNIT IDs ARE HARDCODED — DO NOT SHIP. Search this file (and the project)
//  for "REPLACE WITH REAL ID" before release.
// =============================================================================
using UnityEngine;
#if GMA_ADS
using System.Collections.Generic;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
#endif
using BusJam;

public class AdManager : MonoBehaviour
{
    // ===================== editable cadence / gating constants =====================
    const bool  ADS_ENABLED                = true;   // master kill switch (also flip off once Remove-Ads is purchased)
    const int   INTERSTITIAL_EVERY_LOSSES  = 2;
    const int   INTERSTITIAL_EVERY_WINS    = 3;
    const bool  USE_LEVEL_GRACE            = true;
    const int   MIN_LEVEL_FOR_INTERSTITIAL = 5;      // no interstitial before this level
    const bool  USE_COOLDOWN               = true;
    const float INTERSTITIAL_COOLDOWN_SEC  = 75f;    // 60–90s global cooldown
    const bool  SKIP_INTERSTITIAL_ON_BONUS = true;   // IsBonus rounds
    const bool  NEVER_STACK_WITH_REWARDED  = true;   // suppress an interstitial right after a rewarded

    // ===== TEMP debug panel + hotkey (T6). Set false / delete before release. =====
    public const bool SHOW_AD_DEBUG = false;   // OFF: the top-left on-screen ad-debug panel is hidden

    // ===================== TEST ad unit IDs (Google's universal test ids — publisher ca-app-pub-3940256099942544) =====================
    // >>> TEST ADS ONLY — for CLOSED TESTING, so a tester tap can NEVER trigger an AdMob "invalid traffic" ban. <<<
    // AdMob ad unit ids are PER-PLATFORM: an Android unit id returns "no ad available" on iOS (and vice-versa) — that is
    // why iOS ads never opened. Each platform now uses ITS OWN Google test ids. The REAL publisher's ids were swapped OUT
    // (recoverable from git history / release notes). RESTORE them — here AND in Assets ▸ Google Mobile Ads ▸ Settings
    // (adMobAndroidAppId / adMobIOSAppId) — before the PRODUCTION release, or real ads will never serve / earn.
#if UNITY_IOS
    const string APP_ID          = "ca-app-pub-3940256099942544~1458002511"; // iOS Google TEST App ID (mirror of Google Mobile Ads Settings adMobIOSAppId)
    const string BANNER_ID       = "ca-app-pub-3940256099942544/2435281174"; // iOS Google TEST adaptive banner
    const string INTERSTITIAL_ID = "ca-app-pub-3940256099942544/4411468910"; // iOS Google TEST interstitial
    const string REWARDED_ID     = "ca-app-pub-3940256099942544/1712485313"; // iOS Google TEST rewarded
#else
    const string APP_ID          = "ca-app-pub-3940256099942544~3347511713"; // Android Google TEST App ID (mirror of Google Mobile Ads Settings adMobAndroidAppId)
    const string BANNER_ID       = "ca-app-pub-3940256099942544/9214589741"; // Android Google TEST adaptive banner
    const string INTERSTITIAL_ID = "ca-app-pub-3940256099942544/1033173712"; // Android Google TEST interstitial
    const string REWARDED_ID     = "ca-app-pub-3940256099942544/5224354917"; // Android Google TEST rewarded
#endif

    public static AdManager Instance { get; private set; }
    BusJamGame _game;                       // for eligibility (CurrentLevel / IsBonus); may be null in the menu scene
    bool _adsEnabled = ADS_ENABLED;         // gates BANNER + INTERSTITIAL. refined in Awake with SaveSystem.AdsRemoved (PlayerPrefs can't be read in a field initializer)
    bool _bannerEnabled = true;             // BANNER-ONLY gate (remove_banner IAP): off => no banner, but interstitial/rewarded still show; refined in Awake
    bool _rewardedEnabled = ADS_ENABLED;    // REWARDED (opt-in) gate — deliberately NOT reduced by Remove-Ads: a no-ads buyer can STILL choose to watch a rewarded ad for x2 coins / free coins / continue

    // ---- in-memory counters (reset per app session; deliberately NOT persisted) ----
    int   _wins, _losses;
    bool  _lastWasWin;
    float _lastInterstitial = -99999f;
    bool  _rewardedJustShown;
    bool  _showingFullScreen;               // guards against two full-screen ads overlapping
    bool  _bannerShown;

    static readonly System.Action Noop = () => { };

#if GMA_ADS
    BannerView     _banner;
    InterstitialAd _interstitial;
    RewardedAd     _rewarded;
    System.Action  _pendingInterstitialDone;
    System.Action  _rwReward, _rwNo;
    bool           _rwEarned;
    bool           _mobileAdsInited;
#endif

    // Create (once) from BusJamGame.Start(); persists across scene loads.
    public static AdManager Ensure(BusJamGame game = null)
    {
        if (Instance == null)
        {
            var go = new GameObject("AdManager");
            Instance = go.AddComponent<AdManager>(); // Awake assigns Instance + DontDestroyOnLoad
        }
        if (game != null) Instance._game = game; // don't clobber the gameplay ref when called from the menu (game == null)
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _adsEnabled = ADS_ENABLED && !SaveSystem.AdsRemoved; // off for good once the no-ads IAP is owned (PlayerPrefs is safe in Awake, not in a field initializer)
        _bannerEnabled = !SaveSystem.BannerRemoved;          // banner off for good once the remove_banner IAP is owned (interstitial/rewarded unaffected)
    }

    bool _booted;
    void Start()
    {
        if (_booted) return; _booted = true;
        Boot();
    }

    // ===================== T1: UMP consent + Initialize =====================
    void Boot()
    {
#if GMA_ADS
        MobileAds.RaiseAdEventsOnUnityMainThread = true; // marshal all SDK callbacks to the Unity main thread
        MobileAds.SetRequestConfiguration(new RequestConfiguration
        {
            // TODO: paste your device's test id (printed in logcat on first ad request) so YOUR device always gets test ads:
            TestDeviceIds = new List<string>(),
            // ===== ALL-AGES / FAMILY-SAFE ADS (required for an all-ages / Designed-for-Families game) =====
            // G = only General-audience ad creatives are ever served (never PG / T / MA).
            MaxAdContentRating = MaxAdContentRating.G,
            // COPPA: treat every request as child-directed -> non-personalized, family-safe ad networks only.
            // (If your app is NOT directed at children, change these to .Unspecified and use an age screen instead.)
            TagForChildDirectedTreatment = TagForChildDirectedTreatment.True,
            // GDPR (EEA): treat users as under the age of consent.
            TagForUnderAgeOfConsent = TagForUnderAgeOfConsent.True,
        });

        var p = new ConsentRequestParameters();
        // --- UMP testing in the EEA: uncomment, add your hashed device id, and call ConsentInformation.Reset() between runs ---
        // p.ConsentDebugSettings = new ConsentDebugSettings { DebugGeography = DebugGeography.EEA, TestDeviceHashedIds = new List<string> { "TEST-DEVICE-HASHED-ID" } };
        ConsentInformation.Update(p, (FormError err) =>
        {
            if (err != null) Debug.LogWarning("[AdManager] consent update error: " + err.Message);
            ConsentForm.LoadAndShowConsentFormIfRequired((FormError formErr) =>
            {
                if (formErr != null) Debug.LogWarning("[AdManager] consent form error: " + formErr.Message);
                if (ConsentInformation.CanRequestAds()) InitMobileAds();
            });
        });
        if (ConsentInformation.CanRequestAds()) InitMobileAds(); // already-consented fast path
#else
        Debug.Log("[AdManager] (stub) Google Mobile Ads not compiled in. Import the package, then add the scripting define symbol GMA_ADS to enable real ads.");
#endif
    }

    // ===================== T1b: Privacy options (Google EU consent policy) =====================
    // When UMP reports the privacy-options requirement (EEA/UK users who saw the consent form), the app MUST offer
    // an entry point to revisit/change that choice. GameUI shows a "Privacy options" button in Settings ONLY while
    // this is true, wired to ShowPrivacyOptions().
    public bool PrivacyOptionsRequired
    {
        get
        {
#if GMA_ADS
            return ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;
#else
            return false;
#endif
        }
    }

    public void ShowPrivacyOptions()
    {
#if GMA_ADS
        ConsentForm.ShowPrivacyOptionsForm((FormError err) =>
        {
            if (err != null) Debug.LogWarning("[AdManager] privacy options form error: " + err.Message);
        });
#endif
    }

#if GMA_ADS
    void InitMobileAds()
    {
        if (_mobileAdsInited) return; _mobileAdsInited = true;
        MobileAds.Initialize(_ =>
        {
            Debug.Log("[AdManager] MobileAds initialized — preloading banner / interstitial / rewarded.");
            LoadInterstitial();
            LoadRewarded();
            LoadBanner();
        });
    }
    bool CanRequestAds() => ConsentInformation.CanRequestAds();
#else
    bool CanRequestAds() => false;
#endif

    // ===================== T3: Banner =====================
    public void ShowBanner()
    {
        _bannerShown = true;
        if (!_adsEnabled || !_bannerEnabled) return;
#if GMA_ADS
        // Re-create the banner if HideBanner destroyed it. BannerView.Show() after Hide() is unreliable
        // (editor placeholder + some devices), so we rebuild instead — this makes the toggle work both ways.
        if (_banner == null) LoadBanner();
        else _banner.Show();
#endif
    }

    public void HideBanner()
    {
        _bannerShown = false;
#if GMA_ADS
        if (_banner != null) { _banner.Destroy(); _banner = null; } // destroy so the next ShowBanner rebuilds a visible banner
#endif
    }

#if GMA_ADS
    void LoadBanner()
    {
        if (!_adsEnabled || !_bannerEnabled || !CanRequestAds()) return;
        if (_banner != null) { _banner.Destroy(); _banner = null; }
        AdSize size = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
        _banner = new BannerView(BANNER_ID, size, AdPosition.Bottom);
        if (!_bannerShown) _banner.Hide();   // start HIDDEN so a boot-time preload can't flash on the loading screen (the SDK auto-shows on load); ShowBanner() reveals it
        _banner.OnBannerAdLoaded += () => { Debug.Log("[AdManager] banner loaded"); if (!_bannerShown) _banner.Hide(); };
        _banner.OnBannerAdLoadFailed += (LoadAdError e) => Debug.LogWarning("[AdManager] banner load failed: " + e);
        _banner.LoadAd(new AdRequest());
    }
#endif

    // ===================== T4: Interstitial =====================
    public void AddInterstitialWin()  { _wins++;   _lastWasWin = true;  }
    public void AddInterstitialLoss() { _losses++; _lastWasWin = false; }

    public void ShowInterstitialIfEligible(System.Action onDone)
    {
        onDone = onDone ?? Noop;
        if (!Eligible()) { onDone(); return; }
        ShowInterstitialNow(onDone);
    }

    // Debug: show regardless of cadence/grace/cooldown.
    public void ForceInterstitial(System.Action onDone) { ShowInterstitialNow(onDone ?? Noop); }

    bool Eligible()
    {
        if (!_adsEnabled) return false;
        bool cadence = _lastWasWin
            ? (INTERSTITIAL_EVERY_WINS   > 0 && _wins   % INTERSTITIAL_EVERY_WINS   == 0)
            : (INTERSTITIAL_EVERY_LOSSES > 0 && _losses % INTERSTITIAL_EVERY_LOSSES == 0);
        if (!cadence) return false;
        if (USE_LEVEL_GRACE && _game != null && _game.CurrentLevel < MIN_LEVEL_FOR_INTERSTITIAL) return false;
        if (USE_COOLDOWN && Time.unscaledTime - _lastInterstitial < INTERSTITIAL_COOLDOWN_SEC) return false;
        if (SKIP_INTERSTITIAL_ON_BONUS && _game != null && _game.IsBonus) return false;
        if (NEVER_STACK_WITH_REWARDED && _rewardedJustShown) { _rewardedJustShown = false; return false; }
        return true;
    }

    void ShowInterstitialNow(System.Action onDone)
    {
        onDone = onDone ?? Noop;
#if GMA_ADS
        if (_adsEnabled && _interstitial != null && _interstitial.CanShowAd() && !_showingFullScreen)
        {
            _pendingInterstitialDone = onDone;
            _lastInterstitial = Time.unscaledTime;
            _showingFullScreen = true;
            _rewardedJustShown = false;
            _interstitial.Show();
            return;
        }
#endif
        onDone(); // no ad ready -> never stall, proceed immediately
    }

#if GMA_ADS
    void LoadInterstitial()
    {
        if (!_adsEnabled || !CanRequestAds()) return;
        if (_interstitial != null) { _interstitial.Destroy(); _interstitial = null; }
        InterstitialAd.Load(INTERSTITIAL_ID, new AdRequest(), (InterstitialAd ad, LoadAdError err) =>
        {
            if (err != null || ad == null) { Debug.LogWarning("[AdManager] interstitial load failed: " + err); return; }
            _interstitial = ad;
            Debug.Log("[AdManager] interstitial loaded");
            ad.OnAdFullScreenContentOpened += () => { AudioListener.pause = true; };
            ad.OnAdFullScreenContentClosed += () =>
            {
                AudioListener.pause = false;
                _showingFullScreen = false;
                var d = _pendingInterstitialDone; _pendingInterstitialDone = null;
                LoadInterstitial();         // preload the next one
                d?.Invoke();
            };
            ad.OnAdFullScreenContentFailed += (AdError e) =>
            {
                AudioListener.pause = false;
                _showingFullScreen = false;
                var d = _pendingInterstitialDone; _pendingInterstitialDone = null;
                LoadInterstitial();
                Debug.LogWarning("[AdManager] interstitial show failed: " + e);
                d?.Invoke();                // failed to present -> still proceed
            };
        });
    }
#endif

    // ===================== T5: Rewarded =====================
    // Grants ONLY inside the real reward callback; closing/skipping/no-ad => onClosedNoReward.
    public void ShowRewarded(string placement, System.Action onReward, System.Action onClosedNoReward)
    {
        var reward   = onReward ?? Noop;
        var noReward = onClosedNoReward ?? Noop;
#if GMA_ADS
        if (_rewardedEnabled && _rewarded != null && _rewarded.CanShowAd() && !_showingFullScreen)
        {
            _rwReward = reward; _rwNo = noReward; _rwEarned = false;
            _showingFullScreen = true;
            _rewarded.Show((Reward r) => { _rwEarned = true; }); // grant decided on close (below)
            return;
        }
        Debug.Log($"[AdManager] rewarded '{placement}' not ready -> no reward");
        noReward();
#else
        Debug.Log($"[AdManager] (stub) rewarded '{placement}' -> no reward (define GMA_ADS for real ads)");
        noReward();
#endif
    }

#if GMA_ADS
    void LoadRewarded()
    {
        if (!_rewardedEnabled || !CanRequestAds()) return; // NOT _adsEnabled: rewarded opt-in ads survive a Remove-Ads purchase
        if (_rewarded != null) { _rewarded.Destroy(); _rewarded = null; }
        RewardedAd.Load(REWARDED_ID, new AdRequest(), (RewardedAd ad, LoadAdError err) =>
        {
            if (err != null || ad == null) { Debug.LogWarning("[AdManager] rewarded load failed: " + err); return; }
            _rewarded = ad;
            Debug.Log("[AdManager] rewarded loaded");
            ad.OnAdFullScreenContentOpened += () => { AudioListener.pause = true; };
            ad.OnAdFullScreenContentClosed += () =>
            {
                AudioListener.pause = false;
                _showingFullScreen = false;
                _rewardedJustShown = true;  // suppress an interstitial right after (NEVER_STACK_WITH_REWARDED)
                var rw = _rwReward; var no = _rwNo; bool earned = _rwEarned;
                _rwReward = null; _rwNo = null; _rwEarned = false;
                LoadRewarded();             // preload the next one
                if (earned) rw?.Invoke(); else no?.Invoke();
            };
            ad.OnAdFullScreenContentFailed += (AdError e) =>
            {
                AudioListener.pause = false;
                _showingFullScreen = false;
                var no = _rwNo; _rwReward = null; _rwNo = null; _rwEarned = false;
                LoadRewarded();
                Debug.LogWarning("[AdManager] rewarded show failed: " + e);
                no?.Invoke();               // failed to present -> no reward
            };
        });
    }
#endif

    // Future: Remove-Ads purchase calls this to suppress banner + interstitial everywhere.
    public void SetAdsEnabled(bool enabled) { _adsEnabled = enabled; if (!enabled) HideBanner(); }
    // BANNER-ONLY toggle (remove_banner IAP). Turns the banner off/on without touching interstitial or rewarded ads.
    public void SetBannerEnabled(bool enabled) { _bannerEnabled = enabled; if (!enabled) HideBanner(); }

    // ===================== T6: TEMP on-screen debug panel (REMOVE BEFORE RELEASE) =====================
    void OnGUI()
    {
        if (!SHOW_AD_DEBUG) return;
        GUILayout.BeginArea(new Rect(8, 8, 260, 300), GUI.skin.box);
        GUILayout.Label("=== AD DEBUG — REMOVE BEFORE RELEASE ===");
        float cd = Mathf.Max(0f, INTERSTITIAL_COOLDOWN_SEC - (Time.unscaledTime - _lastInterstitial));
        GUILayout.Label($"wins:{_wins}  losses:{_losses}  cooldown:{cd:0}s");
#if GMA_ADS
        GUILayout.Label("GMA_ADS: ON (real ads)");
#else
        GUILayout.Label("GMA_ADS: OFF (stub — define GMA_ADS)");
#endif
        if (GUILayout.Button("Show Interstitial (force)"))
            ForceInterstitial(null);
        if (GUILayout.Button("Show Rewarded"))
            ShowRewarded("debug", () => Debug.Log("[AdManager] DEBUG reward GRANTED"), () => Debug.Log("[AdManager] DEBUG closed — NO reward"));
        if (GUILayout.Button(_bannerShown ? "Hide Banner" : "Show Banner"))
        { if (_bannerShown) HideBanner(); else ShowBanner(); }
        if (GUILayout.Button("Reset counters + cooldown"))
        { _wins = 0; _losses = 0; _lastInterstitial = -99999f; _rewardedJustShown = false; }
        GUILayout.EndArea();
    }
}
