using UnityEditor;
using UnityEngine;

namespace BusJam.EditorTools
{
    /// <summary>
    /// "BusJam ▸ Build Vehicle Catalog" — points the VehicleCatalog at the imported
    /// LowPolyRoadVehicles pack: Car→Sedan, Bus→Bus_01. Overwrites the model
    /// slots (fit tuning fields are left untouched).
    /// </summary>
    public static class VehicleCatalogBuilder
    {
        const string Root = "Assets/YelScryptFireStudio/LowPolyRoadVehiclesFreePackage/Vehicles";
        const string CarPath = Root + "/Sedan_01/pref_Sedan_01.prefab";
        const string BusPath = Root + "/Bus_01/pref_Bus_01.prefab";
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
