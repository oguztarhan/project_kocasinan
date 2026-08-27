using UnityEngine;

#pragma warning disable 612, 618 // Unity IAP V5 marks the classic IStoreController API obsolete; it is the stable, fully
                                 // working path, so we use it deliberately and suppress the "upgrade to V5" nag here.
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace Ridebury
{
    /// <summary>
    /// In-app purchases (Google Play). SIX consumable coin packs + TWO non-consumable no-ads tiers. Self-spawns at
    /// launch, initialises Unity IAP, VERIFIES each receipt was really signed by Google (anti-fraud, via GooglePlayKey),
    /// grants on purchase, and AUTO-RESTORES the no-ads entitlement every launch (Google replays owned non-consumables
    /// through ProcessPurchase). The "plus" tier's one-time bonus is flag-gated so a restore can never re-grant it.
    /// Localized store prices feed the shop UI. Uses the classic (obsolete-warned but working) IStoreController API.
    /// </summary>
    public class IAPManager : MonoBehaviour, IDetailedStoreListener
    {
        public static IAPManager Instance { get; private set; }
        public static bool Ready { get; private set; }
        public static System.Action OnChanged; // shop subscribes -> refresh counters + localized prices

        // Consumable coin packs: store product id -> coins granted. IDs must match the Google Play Console products.
        // Each card grants EXACTLY the amount it shows; the product id is coins_<amount>. BOTH shops now use the SAME
        // six packs — CREATE these six products in Play Console (Consumable).
        public static readonly (string id, int coins)[] CoinPacks =
        {
            ("coins_200", 200), ("coins_500", 500), ("coins_1300", 1300),
            ("coins_2500", 2500), ("coins_4000", 4000), ("coins_5500", 5500),
        };
        public const string RemoveAds     = "remove_ads";       // non-consumable: ads off
        public const string RemoveAdsPlus = "remove_ads_plus";  // non-consumable: ads off + one-time 200 gold + Recolor joker
        public const string RemoveBanner  = "remove_banner";    // non-consumable: BANNER ONLY off (interstitial + rewarded still show) — CREATE this product in Play Console

        /// <summary>Store product id for a coin pack of exactly <paramref name="coins"/> coins, else null. Lets the
        /// shop UI (which knows the coin amount) initiate the right purchase without hard-coding ids per button.</summary>
        public static string ProductForCoins(int coins)
        {
            foreach (var p in CoinPacks) if (p.coins == coins) return p.id;
            return null;
        }

        IStoreController controller;
        IExtensionProvider extensions;   // kept for iOS: Apple restore lives on IAppleExtensions
        CrossPlatformValidator validator;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("IAPManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<IAPManager>();
        }

        void Start()
        {
            if (controller != null || Instance != this) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { validator = new CrossPlatformValidator(GooglePlayKey.Data(), Application.identifier); }
            catch (System.Exception e) { Debug.LogWarning("[IAP] receipt validator unavailable: " + e.Message); }
#endif
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance()); // store from Resources/BillingMode.json (GooglePlay)
            foreach (var p in CoinPacks) builder.AddProduct(p.id, ProductType.Consumable);
            builder.AddProduct(RemoveAds,     ProductType.NonConsumable);
            builder.AddProduct(RemoveAdsPlus, ProductType.NonConsumable);
            builder.AddProduct(RemoveBanner,  ProductType.NonConsumable);
            UnityPurchasing.Initialize(this, builder);
        }

        // ---------------- IDetailedStoreListener ----------------
        public void OnInitialized(IStoreController c, IExtensionProvider e)
        {
            controller = c; extensions = e; Ready = true;
            OnChanged?.Invoke(); // localized prices are available now
        }

        public void OnInitializeFailed(InitializationFailureReason error) => OnInitializeFailed(error, null);
        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => Debug.LogWarning("[IAP] init failed: " + error + (string.IsNullOrEmpty(message) ? "" : " — " + message));

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            // Anti-fraud: confirm Google really signed this receipt. A faked one is logged and NOT granted, but still
            // returned Complete so the bogus receipt can't loop. Validation is skipped in the editor / fake store.
            if (!ReceiptValid(args.purchasedProduct.receipt))
            {
                Debug.LogWarning("[IAP] receipt failed validation — NOT granting " + args.purchasedProduct.definition.id);
                return PurchaseProcessingResult.Complete;
            }
            Grant(args.purchasedProduct.definition.id);
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
            => Debug.LogWarning("[IAP] purchase failed: " + product?.definition?.id + " — " + reason);
        public void OnPurchaseFailed(Product product, PurchaseFailureDescription desc)
            => Debug.LogWarning("[IAP] purchase failed: " + product?.definition?.id + " — " + desc?.reason + " " + desc?.message);

        bool ReceiptValid(string receipt)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (validator == null) return true; // validator unavailable -> don't block real purchases
            try { validator.Validate(receipt); return true; }
            catch (IAPSecurityException) { return false; }
#else
            return true; // editor / fake-store receipts aren't real Google Play receipts -> skip
#endif
        }

        // ---------------- grants ----------------
        void Grant(string id)
        {
            // Consumables: ProcessPurchase fires only on a real (or interrupted) purchase, never on a restore -> safe to add.
            foreach (var p in CoinPacks)
                if (p.id == id) { SaveSystem.AddCoins(p.coins); OnChanged?.Invoke(); return; }

            // Non-consumables: ProcessPurchase ALSO replays every launch to restore the entitlement -> setting it is idempotent.
            if (id == RemoveAds || id == RemoveAdsPlus)
            {
                SaveSystem.AdsRemoved = true;
                AdManager.Instance?.SetAdsEnabled(false);
            }
            // Banner-only entitlement: turns off ONLY the banner (interstitial + rewarded keep serving).
            if (id == RemoveBanner)
            {
                SaveSystem.BannerRemoved = true;
                AdManager.Instance?.SetBannerEnabled(false);
            }
            // The "plus" tier's one-time bonus must NOT re-grant on a restore -> gate it behind a one-time flag.
            if (id == RemoveAdsPlus && PlayerPrefs.GetInt("bj_rap_bonus", 0) == 0)
            {
                PlayerPrefs.SetInt("bj_rap_bonus", 1); PlayerPrefs.Save();
                SaveSystem.AddCoins(200);
                SaveSystem.AddFreeJoker(0, 1); // one free Recolor joker
            }
            OnChanged?.Invoke();
        }

        // ---------------- public API for the shop ----------------
        public void Buy(string id)
        {
            if (controller == null) { Debug.LogWarning("[IAP] not ready — purchase ignored"); return; }
            controller.InitiatePurchase(id);
        }

        /// <summary>Localized store price (e.g. "₺49,99"), or null until IAP has initialised / for an unknown id.</summary>
        public string Price(string id) => id == null ? null : controller?.products?.WithID(id)?.metadata?.localizedPriceString;

        /// <summary>True if a non-consumable is owned (drives "OWNED" state + hides the no-ads rows).</summary>
        public bool Owns(string id)
        {
            var p = controller?.products?.WithID(id);
            return p != null && p.hasReceipt;
        }

        /// <summary>Restore Purchases button. Google replays owned non-consumables through ProcessPurchase on init, so
        /// on Android this just re-asserts the entitlement (manual safety net + Play-policy requirement). Apple does
        /// NOT replay automatically after a reinstall — iOS must explicitly call RestoreTransactions, which re-runs
        /// ProcessPurchase for every owned product (App Store review REJECTS a Restore button that skips this).</summary>
        public void Restore()
        {
            // iOS only (runtime-gated so the exact same compiled code ships on Android and this branch is simply never
            // taken there). Restored products arrive asynchronously via ProcessPurchase -> Grant; the hasReceipt
            // re-assert below still runs first as the cheap synchronous path for already-known receipts.
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                try
                {
                    extensions?.GetExtension<IAppleExtensions>()?.RestoreTransactions((ok, msg) =>
                    {
                        Debug.Log("[IAP] Apple restore " + (ok ? "finished" : ("failed: " + msg)));
                        OnChanged?.Invoke(); // refresh OWNED states once Apple's replay settles
                    });
                }
                catch (System.Exception e) { Debug.LogWarning("[IAP] Apple restore unavailable: " + e.Message); }
            }
            if (Owns(RemoveAds) || Owns(RemoveAdsPlus))
            {
                SaveSystem.AdsRemoved = true;
                AdManager.Instance?.SetAdsEnabled(false);
            }
            if (Owns(RemoveBanner))
            {
                SaveSystem.BannerRemoved = true;
                AdManager.Instance?.SetBannerEnabled(false);
            }
            OnChanged?.Invoke();
        }
    }
}
#pragma warning restore 612, 618
