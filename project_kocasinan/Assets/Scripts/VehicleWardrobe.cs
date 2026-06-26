using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Runtime access to the vehicle "dolap": resolves the model the player has EQUIPPED for each vehicle type
    /// (from its unlocked set) and unlocks a set with coins. The catalog (10 sets) is built by
    /// "BusJam ▸ Build Vehicle Sets" and loaded from Resources. If the catalog is missing, callers fall back to
    /// the plain VehicleCatalog default.
    /// </summary>
    public static class VehicleWardrobe
    {
        static VehicleSetCatalog _cat;
        static bool _loaded;

        public static VehicleSetCatalog Catalog
        {
            get
            {
                if (!_loaded) { _cat = Resources.Load<VehicleSetCatalog>("VehicleSetCatalog"); _loaded = true; }
                return _cat;
            }
        }

        public static bool HasCatalog => Catalog != null && Catalog.sets != null && Catalog.sets.Length > 0;

        /// <summary>The prefab the player has equipped for this type (from its equipped set), or null if there is
        /// no catalog yet (caller then uses the VehicleCatalog default).</summary>
        public static GameObject EquippedModel(VehicleType t)
        {
            if (!HasCatalog) return null;
            // the equipped item for this type (self-heals to the type's free default), then its prefab. NOT sets[0] —
            // that is a CAR, whose PrefabFor(Minivan/Bus) would be null.
            var set = Catalog.ById(SaveSystem.EquippedSet(t)) ?? Catalog.ById(SaveSystem.DefaultFor(t));
            return set != null ? set.PrefabFor(t) : null;
        }

        /// <summary>Buy/unlock a whole set with coins (its car + minivan + bus). Returns true on success.</summary>
        public static bool TryUnlock(VehicleSetCatalog.VehicleSet set)
        {
            if (set == null || SaveSystem.OwnsSet(set.id)) return false;
            if (!SaveSystem.TrySpend(set.price)) return false;
            SaveSystem.AddOwnedSet(set.id);
            return true;
        }
    }
}
