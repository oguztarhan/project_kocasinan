using UnityEditor;
using UnityEngine;

namespace BusJam.EditorTools
{
    /// <summary>
    /// "BusJam ▸ Build Vehicle Catalog" — points the VehicleCatalog at the chosen vehicle models.
    /// Bus is now the othercars "Bus" glb; Car is still the LowPolyRoadVehicles Sedan. glb models have no
    /// _Color01, so the runtime auto-tints them BODY-ONLY (ColorSkinModel) — no catalog flag needed.
    /// Overwrites the model slots (fit tuning fields are left untouched). To trial the rest of the othercars
    /// set later, repoint CarPath at SedanGlb and/or BusPath at ConnectGlb below — one line each.
    /// </summary>
    public static class VehicleCatalogBuilder
    {
        // LowPolyRoadVehicles pack (FBX prefabs — tinted per-slot via _Color01)
        const string PackRoot  = "Assets/YelScryptFireStudio/LowPolyRoadVehiclesFreePackage/Vehicles";
        const string SedanPack = PackRoot + "/Sedan_01/pref_Sedan_01.prefab";
        const string BusPack   = PackRoot + "/Bus_01/pref_Bus_01.prefab";

        // othercars set (raw glTF .glb — auto-tinted body-only at runtime)
        const string OtherRoot = "Assets/Unity Technologies/othercars";
        const string BusGlb     = OtherRoot + "/bus.glb";
        const string ConnectGlb = OtherRoot + "/connectt.glb";
        const string SedanGlb   = OtherRoot + "/sedan.glb";

        // ACTIVE selection — buses replaced with the othercars "Bus" glb; car unchanged for now.
        const string CarPath = SedanPack;
        const string BusPath = BusGlb;
        const string CatalogPath = "Assets/Resources/VehicleCatalog.asset";

        [MenuItem("BusJam/Build Vehicle Catalog")]
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

            cat.carPrefab = Load(CarPath, "Car");
            cat.busPrefab = Load(BusPath, "Bus");

            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VehicleCatalog] {(created ? "created" : "updated")} at {CatalogPath} — " +
                      $"Car={Name(cat.carPrefab)}, Bus={Name(cat.busPrefab)}");
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
