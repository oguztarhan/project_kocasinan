using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Car crafting: spend shards (duplicate cars melt into them when a chest is opened — see ChestService) to forge a
    /// GUARANTEED NEW car of a chosen rarity tier — never a duplicate. Pure economy logic over the vehicle-set catalog
    /// (ids + rarities), completely independent of HOW a car renders. Costs are ~5× the shards a single duplicate of
    /// that tier melts into, so duplicates always feel like progress toward a craft.
    ///
    /// Tiers match the wardrobe / chest rarity scale: 0 Common, 1 Uncommon, 2 Epic, 3 Legendary.
    /// </summary>
    public static class CraftService
    {
        // Shard cost to craft a guaranteed new car of `tier`. ~5× the dupe shard value of that tier
        // (ChestService melts dupes into 10 / 25 / 80 / 200 by tier), so roughly 5 duplicates == 1 craft.
        public static int Cost(int tier) => tier >= 3 ? 1000 : tier == 2 ? 400 : tier == 1 ? 125 : 50;

        /// <summary>Cars of this tier the player does NOT yet own (the craft pool).</summary>
        public static List<VehicleSetCatalog.VehicleSet> Craftable(int tier)
        {
            var list = new List<VehicleSetCatalog.VehicleSet>();
            var cat = VehicleWardrobe.Catalog;
            if (cat == null || cat.sets == null) return list;
            foreach (var s in cat.sets)
                if (s != null && s.rarity == tier && !SaveSystem.OwnsSet(s.id)) list.Add(s);
            return list;
        }

        /// <summary>True when the player can afford the craft AND there's still an unowned car of that tier to win.</summary>
        public static bool CanCraft(int tier) => SaveSystem.Shards >= Cost(tier) && Craftable(tier).Count > 0;

        /// <summary>Spend shards and grant a random not-yet-owned car of `tier`. Returns the crafted car, or null if
        /// the tier is fully owned or the player is short on shards (nothing is spent in that case).</summary>
        public static VehicleSetCatalog.VehicleSet Craft(int tier)
        {
            var pool = Craftable(tier);
            if (pool.Count == 0) return null;
            if (!SaveSystem.TrySpendShards(Cost(tier))) return null;
            var car = pool[Random.Range(0, pool.Count)];
            SaveSystem.AddOwnedSet(car.id);
            return car;
        }
    }
}
