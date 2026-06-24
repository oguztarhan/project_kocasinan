using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Shard crafting: spend shards (duplicates melt into them via ChestService) to forge a guaranteed NEW skin of
    /// a chosen rarity — never a duplicate. Pure economy logic over the catalog, independent of how skins render.
    /// Costs are ~5× a single duplicate of that rarity, so duplicates always feel like progress toward a craft.
    /// </summary>
    public static class CraftService
    {
        public static int Cost(SkinRarity r)
        {
            switch (r)
            {
                case SkinRarity.Common:    return 25;
                case SkinRarity.Uncommon:  return 60;
                case SkinRarity.Rare:      return 150;
                case SkinRarity.Epic:      return 400;
                case SkinRarity.Legendary: return 1000;
                default:                   return 25;
            }
        }

        /// <summary>Skins of this rarity the player does NOT yet own (the craft pool).</summary>
        public static List<SkinDef> Craftable(SkinRarity r)
        {
            var list = new List<SkinDef>();
            foreach (var s in SkinCatalog.ByRarity(SkinCategory.Vehicle, r))
                if (!SaveSystem.OwnsSkin(s.id)) list.Add(s);
            return list;
        }

        /// <summary>True when the player can afford the craft AND there's still something of that rarity to win.</summary>
        public static bool CanCraft(SkinRarity r) => SaveSystem.Shards >= Cost(r) && Craftable(r).Count > 0;

        /// <summary>Spend shards and grant a random not-yet-owned skin of `r`. Returns the crafted skin, or null if
        /// the rarity is fully owned or the player is short on shards (nothing is spent in that case).</summary>
        public static SkinDef Craft(SkinRarity r)
        {
            var pool = Craftable(r);
            if (pool.Count == 0) return null;
            if (!SaveSystem.TrySpendShards(Cost(r))) return null;
            var skin = pool[Random.Range(0, pool.Count)];
            SaveSystem.AddOwnedSkin(skin.id);
            return skin;
        }
    }
}
