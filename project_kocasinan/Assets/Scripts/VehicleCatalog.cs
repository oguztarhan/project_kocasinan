using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Maps each vehicle type to an imported model prefab + fit tuning. Built/refreshed
    /// by "Ridebury ▸ Build Vehicle Catalog" and loaded at runtime from Resources. If a
    /// type's prefab is missing, RideburyGame falls back to the code-built vehicle.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleCatalog", menuName = "Ridebury/Vehicle Catalog")]
    public class VehicleCatalog : ScriptableObject
    {
        [Header("Model per type")]
        public GameObject carPrefab;     // 1 cell  (Sedan)
        public GameObject minivanPrefab; // 1 cell  (Connect)
        public GameObject busPrefab;     // 2 cells

        [Header("Fit (tune so they sit right in the grid)")]
        [Range(0.5f, 1f)] public float fitFactor = 0.9f; // model length as a fraction of its cell span
        public float yaw = 0f;     // CAR base rotation (FBX Mega Pack). Use a MULTIPLE OF 90 — the diagonal auto-face only stays aligned for 90-multiples.
        public float yOffset = 0f; // raise/lower if a model's pivot isn't at the wheels

        [Header("Per-type yaw (glTF .glb faces the opposite way to FBX, so its base yaw differs by 180)")]
        public float yawMinivan = 0f; // Connect (.glb) — defaults 0 (i.e. car yaw 180 flipped); tune in multiples of 90 if it faces wrong
        public float yawBus     = 0f; // Bus (.glb)     — defaults 0; tune in multiples of 90 if it drives backwards

        public float YawFor(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:     return yaw;
                case VehicleType.Minivan: return yawMinivan;
                default:                  return yawBus;
            }
        }

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
}
