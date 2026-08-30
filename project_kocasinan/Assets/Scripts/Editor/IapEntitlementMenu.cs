using UnityEditor;
using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Testing helper: the three no-ads tiers are ONE-TIME non-consumables, so once a tier is bought its shop row
    /// locks itself for good — the lock survives Play-mode exits because the entitlement lives in PlayerPrefs (see
    /// <see cref="IAPManager.Owned"/>). Use this to hand the entitlements back so the offers can be bought again.
    ///
    /// EDITOR ONLY, and only the LOCAL flags: a real Google Play purchase is owned by the account, and clearing this
    /// does not (and must not) refund it — on a device the tier comes straight back on the next launch, because Google
    /// replays owned non-consumables through ProcessPurchase.
    /// </summary>
    public static class IapEntitlementMenu
    {
        [MenuItem("Ridebury/Reset IAP Entitlements (no-ads tiers)")]
        static void ResetEntitlements()
        {
            PlayerPrefs.DeleteKey("bj_ads_removed");        // remove_ads / remove_ads_plus -> all ads off
            PlayerPrefs.DeleteKey("bj_ads_removed_plus");   // remove_ads_plus specifically (locks the PLUS row)
            PlayerPrefs.DeleteKey("bj_banner_removed");     // remove_banner -> banner off
            PlayerPrefs.DeleteKey("bj_rap_bonus");          // the PLUS tier's one-time +200 gold / free Recolor joker
            PlayerPrefs.Save();
            Debug.Log("[Ridebury] IAP entitlements cleared — the three no-ads offers are buyable again and ads are back on. " +
                      "(Local flags only: a real Play purchase is restored on the next launch / Restore Purchases.)");
        }
    }
}
