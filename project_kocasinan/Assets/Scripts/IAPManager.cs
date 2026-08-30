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

        /// <summary>ONE-TIME OFFERS: true when this non-consumable entitlement is already held, so both shops can lock
        /// the row and <see cref="Buy"/> can refuse. Checks the LOCAL saved entitlement OR the store receipt, so
        /// ownership is known even before IAP has finished initialising (and in the editor's fake store, whose receipts
        /// are wiped on every play). Not for the consumable coin packs — those are always re-buyable.</summary>
        public static bool Owned(string id)
        {
            switch (id)
            {
                // Ads are already gone if EITHER no-ads tier is owned -> the plain tier is no longer sellable.
                case RemoveAds:
                    return SaveSystem.AdsRemoved || HasReceipt(RemoveAds) || HasReceipt(RemoveAdsPlus);
                // "Plus" is its own SKU (extra gold + Recolor joker) -> only owning PLUS locks it.
                case RemoveAdsPlus:
                    return SaveSystem.AdsRemovedPlus || HasReceipt(RemoveAdsPlus);
                // Either no-ads tier already kills the banner -> the banner-only tier would be money for nothing.
                case RemoveBanner:
                    return SaveSystem.BannerRemoved || SaveSystem.AdsRemoved
                        || HasReceipt(RemoveBanner) || HasReceipt(RemoveAds) || HasReceipt(RemoveAdsPlus);
            }
            return false;
        }

        /// <summary>True if the store itself reports an owned purchase for this product (null-safe before init).</summary>
        static bool HasReceipt(string id)
        {
            var p = Instance?.controller?.products?.WithID(id);
            return p != null && p.hasReceipt;
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
            // Remember WHICH no-ads tier this was, so the shop locks the right row (AdsRemoved can't tell them apart).
            if (id == RemoveAdsPlus) SaveSystem.AdsRemovedPlus = true;
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
            // ONE-TIME OFFERS: never start a second purchase flow for an entitlement the player already holds. Google
            // would answer ITEM_ALREADY_OWNED anyway, but the player must not even reach the payment sheet.
            if (Owned(id)) { Debug.Log("[IAP] " + id + " already owned — purchase blocked"); OnChanged?.Invoke(); return; }
            if (controller == null) { Debug.LogWarning("[IAP] not ready — purchase ignored"); return; }
            controller.InitiatePurchase(id);
        }

        /// <summary>Localized store price (e.g. "₺49,99"), or null until IAP has initialised / for an unknown id.</summary>
        public string Price(string id) => id == null ? null : controller?.products?.WithID(id)?.metadata?.localizedPriceString;

        /// <summary>True if the store itself reports this non-consumable as owned. Prefer the static
        /// <see cref="Owned"/> for UI: it also honours the locally saved entitlement (works before init).</summary>
        public bool Owns(string id) => HasReceipt(id);

        /// <summary>Outcome of <see cref="Restore"/>, so the button can say what actually happened.</summary>
        public enum RestoreResult { Restored, NothingToRestore, NotReady }

        /// <summary>Re-assert every entitlement the store reports as owned. Returns true if ANYTHING is owned.
        /// The "plus" one-time bonus is deliberately NOT re-granted here — only a real purchase grants it.</summary>
        bool ApplyOwnedEntitlements()
        {
            bool any = false;
            if (HasReceipt(RemoveAds) || HasReceipt(RemoveAdsPlus))
            {
                SaveSystem.AdsRemoved = true;
                AdManager.Instance?.SetAdsEnabled(false);
                any = true;
            }
            if (HasReceipt(RemoveAdsPlus)) { SaveSystem.AdsRemovedPlus = true; any = true; }
            if (HasReceipt(RemoveBanner))
            {
                SaveSystem.BannerRemoved = true;
                AdManager.Instance?.SetBannerEnabled(false);
                any = true;
            }
            OnChanged?.Invoke();   // repaint prices + the OWNED lock on both shops
            return any;
        }

        /// <summary>Restore Purchases button. Google replays owned non-consumables through ProcessPurchase on init, so
        /// on Android this re-asserts the entitlement from the owned receipts (manual safety net + Play-policy
        /// requirement). Apple does NOT replay automatically after a reinstall — iOS must explicitly call
        /// RestoreTransactions, which re-runs ProcessPurchase for every owned product (App Store review REJECTS a
        /// Restore button that skips this). <paramref name="onDone"/> reports the outcome so the button can show
        /// "RESTORED" / "NOTHING TO RESTORE" / "STORE NOT READY" instead of always claiming success.</summary>
        public void Restore(System.Action<RestoreResult> onDone = null)
        {
            // Before IAP initialises there are no receipts to read — saying "RESTORED" here would be a lie.
            if (controller == null)
            {
                Debug.LogWarning("[IAP] restore requested before the store initialised");
                onDone?.Invoke(RestoreResult.NotReady);
                return;
            }

            // iOS only (runtime-gated so the exact same compiled code ships on Android and this branch is simply never
            // taken there). Restored products arrive asynchronously via ProcessPurchase -> Grant, so the result is
            // reported from Apple's callback once the replay has settled.
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                try
                {
                    var apple = extensions?.GetExtension<IAppleExtensions>();
                    if (apple != null)
                    {
                        apple.RestoreTransactions((ok, msg) =>
                        {
                            Debug.Log("[IAP] Apple restore " + (ok ? "finished" : ("failed: " + msg)));
                            bool any = ApplyOwnedEntitlements();
                            onDone?.Invoke(!ok ? RestoreResult.NotReady
                                               : (any ? RestoreResult.Restored : RestoreResult.NothingToRestore));
                        });
                        return;   // the callback reports the result
                    }
                }
                catch (System.Exception e) { Debug.LogWarning("[IAP] Apple restore unavailable: " + e.Message); }
            }

            bool restored = ApplyOwnedEntitlements();
            onDone?.Invoke(restored ? RestoreResult.Restored : RestoreResult.NothingToRestore);
        }

        /// <summary>Localized label for a restore outcome — one wording for both shops' Restore buttons.</summary>
        public static string RestoreLabel(RestoreResult r)
            => r == RestoreResult.Restored ? Loc.T("RESTORED")
             : r == RestoreResult.NothingToRestore ? Loc.T("NOTHING TO RESTORE")
             : Loc.T("STORE NOT READY");
    }
}
#pragma warning restore 612, 618
