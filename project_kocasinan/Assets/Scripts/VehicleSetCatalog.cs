using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// The unlockable vehicle "sets" (the wardrobe / "dolap"). Each set bundles ONE model per vehicle type
    /// (car + minivan + bus). The player UNLOCKS a whole set with coins, then EQUIPS per type independently
    /// (e.g. change the car but keep the same minivan/bus). Built/refreshed by "BusJam ▸ Build Vehicle Sets"
    /// and loaded at runtime from Resources. Set 0 is the free default (Royal + Connect + Bus); its models
    /// mirror the VehicleCatalog defaults so a fresh save looks identical until the player changes something.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleSetCatalog", menuName = "BusJam/Vehicle Set Catalog")]
    public class VehicleSetCatalog : ScriptableObject
    {
        [System.Serializable]
        public class VehicleSet
        {
            public string id;            // stable key, e.g. "set_rhino" (set 0's id MUST equal SaveSystem.DefaultSetId)
            public string displayName;   // the CAR name shown on the card, e.g. "Chimera"
            public int price;            // (legacy) unused — cars are won from CHESTS now, not bought with coins
            public int rarity;           // 0 Common, 1 Uncommon, 2 Epic, 3 Legendary (drives the chest draw + card badge)
            public VehicleType type;     // which garage section this item belongs to — only ITS type's prefab below is set
            public GameObject carPrefab;
            public GameObject minivanPrefab;
            public GameObject busPrefab;

            public GameObject PrefabFor(VehicleType t)
            {
                switch (t)
                {
                    case VehicleType.Car:     return carPrefab;
                    case VehicleType.Minivan: return minivanPrefab;
                    default:                  return busPrefab;
                }
            }
        }

        public VehicleSet[] sets;

        public VehicleSet ById(string id)
        {
            if (sets != null && !string.IsNullOrEmpty(id))
                foreach (var s in sets) if (s != null && s.id == id) return s;
            return null;
        }

        public string DefaultSetId => (sets != null && sets.Length > 0 && sets[0] != null) ? sets[0].id : null;
    }
}
