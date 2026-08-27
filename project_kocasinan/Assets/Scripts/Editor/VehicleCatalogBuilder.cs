using UnityEditor;
using UnityEngine;

namespace Ridebury.EditorTools
{
    /// <summary>
    /// "Ridebury ▸ Build Vehicle Catalog" — points the VehicleCatalog at the chosen vehicle models.
    /// THREE types now: Car = "Firenze" (in-house .glb), Minivan = othercars "Connect" (.glb, KEPT),
    /// Bus = "bus_classic" (in-house .glb). None expose _Color01, so the runtime tints them BODY-ONLY (ColorSkinModel) —
    /// no catalog flag needed. Overwrites the model slots (fit tuning fields are left untouched).
    /// RUN THIS ONCE after pulling these changes: the .glb minivan/bus can only be wired by AssetDatabase
    /// (their internal fileIDs aren't in the .meta), so a hand-edit of the .asset can't reference them.
    /// </summary>
    public static class VehicleCatalogBuilder
    {
        // In-house vehicle set (raw glTF .glb — auto-tinted body-only at runtime via baseColorFactor)
        const string DefaultCar = "Assets/Vehicles/Cars/car_firenze.glb";
        const string DefaultBus = "Assets/Vehicles/Buses/bus_classic.glb";

        // othercars set — only the Connect survives here; it is deliberately kept.
        const string ConnectGlb = "Assets/Unity Technologies/othercars/connectt.glb";

        // LowPolyRoadVehicles pack (legacy FBX, _Color01) — kept for reference/fallback only.
        const string SedanPack = "Assets/YelScryptFireStudio/LowPolyRoadVehiclesFreePackage/Vehicles/Sedan_01/pref_Sedan_01.prefab";

        // ACTIVE selection — Car=Firenze, Minivan=Connect (unchanged), Bus=bus_classic.
        const string CarPath     = DefaultCar;
        const string MinivanPath = ConnectGlb;
        const string BusPath     = DefaultBus;
        const string CatalogPath = "Assets/Resources/VehicleCatalog.asset";

        [MenuItem("Ridebury/Build Vehicle Catalog")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var cat = AssetDatabase.LoadAssetAtPath<VehicleCatalog>(CatalogPath);
            bool created = false;
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<VehicleCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
                created = true;
            }

            cat.carPrefab     = Load(CarPath, "Car");
            cat.minivanPrefab = Load(MinivanPath, "Minivan");
            cat.busPrefab     = Load(BusPath, "Bus");

            // The car is a .glb now, exported nose-to -X exactly like the minivan/bus set, so it no longer
            // needs the 180 that the FBX Mega Pack required. Keep all three types on the same base yaw.
            cat.yaw = cat.yawBus;

            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VehicleCatalog] {(created ? "created" : "updated")} at {CatalogPath} — " +
                      $"Car={Name(cat.carPrefab)}, Minivan={Name(cat.minivanPrefab)}, Bus={Name(cat.busPrefab)}");
            Selection.activeObject = cat;
        }

        static GameObject Load(string path, string label)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning($"[VehicleCatalog] {label} prefab not found at {path}");
            return go;
        }

        static string Name(Object o) => o != null ? o.name : "<missing>";
    }
}
