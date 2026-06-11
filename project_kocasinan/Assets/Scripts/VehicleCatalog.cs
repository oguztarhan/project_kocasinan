using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Maps each vehicle type to an imported model prefab + fit tuning. Built/refreshed
    /// by "BusJam ▸ Build Vehicle Catalog" and loaded at runtime from Resources. If a
    /// type's prefab is missing, BusJamGame falls back to the code-built vehicle.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleCatalog", menuName = "BusJam/Vehicle Catalog")]
    public class VehicleCatalog : ScriptableObject
    {
        [Header("Model per type")]
        public GameObject carPrefab;   // 1 cell  (Sedan)
        public GameObject busPrefab;   // 2 cells (Step 2)
        public GameObject limoPrefab;  // 3 cells (Step 2) — using a long truck

        [Header("Fit (tune so they sit right in the grid)")]
        [Range(0.5f, 1f)] public float fitFactor = 0.9f; // model length as a fraction of its cell span
        public float yaw = 0f;     // base rotation if a model faces the wrong way — use a MULTIPLE OF 90 (0/90/180/270): the diagonal auto-face only stays aligned for 90-multiples
        public float yOffset = 0f; // raise/lower if a model's pivot isn't at the wheels

        public GameObject PrefabFor(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:  return carPrefab;
                case VehicleType.Limo: return limoPrefab;
                default:               return busPrefab;
            }
        }
    }
}
