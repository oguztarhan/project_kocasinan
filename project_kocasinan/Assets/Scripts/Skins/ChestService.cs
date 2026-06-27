using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    public enum ChestTier { Bronze, Silver, Gold, Legendary }

    /// <summary>Result of opening one chest: the CAR won, whether it was a duplicate, and (if so) the shards the
    /// duplicate melted into.</summary>
    public struct ChestResult
    {
        public VehicleSetCatalog.VehicleSet car; // the car won (chests give cars now, not skins)
        public bool wasDupe;
        public int shardsGained;
        public bool keyDropped;   // a bonus key to a higher-tier chest dropped this open
        public ChestTier keyTier; // which tier the dropped key opens
    }

    /// <summary>
    /// Gold-only gacha. Pure economy logic over the catalog (ids + rarities) — completely independent of HOW a
    /// skin renders, so it is unaffected if the visual side later swaps to an art pack. Each tier has its own
    /// rarity odds, a pity counter (a guaranteed Rare-or-better every N opens), and duplicates melt into shards.
    /// Tunable: the cost / odds / pity / shard tables below.
    /// </summary>
    public static class ChestService
    {
        public static long FreeChestCooldownSec => GameConfig.FreeChestCooldownSec; // free Bronze cooldown (remote-tunable)

        public static int Cost(ChestTier t) =>
            t == ChestTier.Bronze ? GameConfig.ChestBronze : t == ChestTier.Silver ? GameConfig.ChestSilver : t == ChestTier.Gold ? GameConfig.ChestGold : -1; // Legendary: -1 = key-only

        // Rarity odds per tier, indexed [Common, Uncommon, Rare, Epic, Legendary].
        static float[] Odds(ChestTier t) =>
            t == ChestTier.Bronze ? new[] { 0.62f, 0.26f, 0.09f, 0.025f, 0.005f } :
            t == ChestTier.Silver ? new[] { 0.40f, 0.34f, 0.18f, 0.065f, 0.015f } :
            t == ChestTier.Gold   ? new[] { 0.15f, 0.30f, 0.34f, 0.16f,  0.05f  } :
                                    new[] { 0.00f, 0.05f, 0.45f, 0.35f,  0.15f  }; // Legendary (key-only, best odds)

        static int PityEvery(ChestTier t) =>
            t == ChestTier.Bronze ? 10 : t == ChestTier.Silver ? 8 : t == ChestTier.Gold ? 6 : 4;

        // Shards a DUPLICATE car melts into, by car tier (0 Common / 1 Medium / 2 Legendary).
        static int DupeShardsTier(int tier) => tier >= 3 ? 200 : tier == 2 ? 80 : tier == 1 ? 25 : 10;

        /// <summary>Spend the tier's gold then open. Returns null (and spends nothing) if the player can't afford it.</summary>
        public static ChestResult? BuyAndOpen(ChestTier t)
        {
            if (Cost(t) < 0) return null;                  // key-only tier — not buyable with gold
            if (!SaveSystem.TrySpend(Cost(t))) return null;
            return Open(t);
        }

        /// <summary>Open a chest with one of its KEYS (free). Returns null (spends nothing) if you have no key for this tier.</summary>
        public static ChestResult? OpenWithKey(ChestTier t)
        {
            if (!SaveSystem.TrySpendKey(t.ToString())) return null;
            return Open(t);
        }

        /// <summary>Roll + grant one chest of this tier (does NOT charge gold — see BuyAndOpen / OpenFree).</summary>
        public static ChestResult Open(ChestTier t)
        {
            string tierKey = t.ToString();
            int pity = SaveSystem.Pity(tierKey) + 1;
            bool forceRarePlus = pity >= PityEvery(t);

            SkinRarity rarity = RollRarity(Odds(t), forceRarePlus);
            SaveSystem.SetPity(tierKey, rarity >= SkinRarity.Rare ? 0 : pity); // reset the counter on any Rare+

            // Same rarity odds as before — only the REWARD is now a CAR. Map the rolled rarity (Common..Legendary) to a
            // car tier (0 Common / 1 Uncommon / 2 Epic / 3 Legendary), and gate the Legendary tier to the Legendary CHEST
            // (any lower chest that rolls Legendary gives an Epic car instead).
            int tier = rarity == SkinRarity.Legendary ? 3 : rarity >= SkinRarity.Rare ? 2 : (int)rarity;
            if (t != ChestTier.Legendary && tier == 3) tier = 2;

            var car = RollCar(tier);
            var res = new ChestResult { car = car };
            if (car == null) return res;

            if (SaveSystem.OwnsSet(car.id))
            {
                res.wasDupe = true;
                res.shardsGained = DupeShardsTier(car.rarity);
                SaveSystem.AddShards(res.shardsGained);
            }
            else SaveSystem.AddOwnedSet(car.id);

            // Bonus "chase": a small chance to ALSO drop a key to a higher-tier chest.
            var key = RollKeyDrop(t);
            if (key.HasValue) { SaveSystem.AddKeys(key.Value.ToString()); res.keyDropped = true; res.keyTier = key.Value; }
            return res;
        }

        // Key drop odds (Bronze->Silver, Silver->Gold, Gold->Legendary). The 0.05% Gold->Legendary is the rare grail.
        static ChestTier? RollKeyDrop(ChestTier t)
        {
            float roll = Random.value;
            switch (t)
            {
                case ChestTier.Bronze: if (roll < 0.03f)   return ChestTier.Silver;    break; // 3%
                case ChestTier.Silver: if (roll < 0.015f)  return ChestTier.Gold;      break; // 1.5%
                case ChestTier.Gold:   if (roll < 0.0005f) return ChestTier.Legendary; break; // 0.05%
            }
            return null;
        }

        // ---- free chest (cooldown + optional rewarded-ad path, wired by the UI) ----
        static long Now() => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static bool FreeChestReady() => Now() >= SaveSystem.FreeChestReadyAt;
        public static long FreeChestSecondsLeft() => System.Math.Max(0, SaveSystem.FreeChestReadyAt - Now());

        /// <summary>Open the timed free Bronze chest (arms the next cooldown). Returns null if it isn't ready yet.</summary>
        public static ChestResult? OpenFree()
        {
            if (!FreeChestReady()) return null;
            SaveSystem.FreeChestReadyAt = Now() + FreeChestCooldownSec;
            return Open(ChestTier.Bronze);
        }

        /// <summary>A free Bronze granted by watching a rewarded ad (no cooldown gate — the ad IS the gate).</summary>
        public static ChestResult OpenAdReward() => Open(ChestTier.Bronze);

        // ---- rolls ----
        static SkinRarity RollRarity(float[] odds, bool forceRarePlus)
        {
            int start = forceRarePlus ? (int)SkinRarity.Rare : 0;
            float total = 0f;
            for (int i = start; i < odds.Length; i++) total += odds[i];
            if (total <= 0f) return SkinRarity.Rare;

            float roll = Random.value * total, acc = 0f;
            for (int i = start; i < odds.Length; i++)
            {
                acc += odds[i];
                if (roll <= acc) return (SkinRarity)i;
            }
            return (SkinRarity)(odds.Length - 1);
        }

        // Pick a random car of `tier`; if that tier has none, fall to a lower one (safety only). Cars come from the
        // wardrobe catalog (built by "BusJam ▸ Build Vehicle Sets"). The free starter can be re-rolled -> a dupe.
        static VehicleSetCatalog.VehicleSet RollCar(int tier)
        {
            var cat = VehicleWardrobe.Catalog;
            if (cat == null || cat.sets == null) return null;
            for (int tr = tier; tr >= 0; tr--)
            {
                var pool = new List<VehicleSetCatalog.VehicleSet>();
                foreach (var s in cat.sets) if (s != null && s.rarity == tr) pool.Add(s);
                if (pool.Count > 0) return pool[Random.Range(0, pool.Count)];
            }
            return null;
        }
    }
}
