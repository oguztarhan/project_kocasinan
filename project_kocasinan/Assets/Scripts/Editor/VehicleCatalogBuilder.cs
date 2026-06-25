using UnityEditor;
using UnityEngine;

namespace BusJam.EditorTools
{
    /// <summary>
    /// "BusJam ▸ Build Vehicle Catalog" — points the VehicleCatalog at the chosen vehicle models.
    /// THREE types now: Car = "Royal" (Low Poly Cars Mega Pack, URP-Lit), Minivan = othercars "Connect" (.glb),
    /// Bus = othercars "Bus" (.glb). None expose _Color01, so the runtime tints them BODY-ONLY (ColorSkinModel) —
    /// no catalog flag needed. Overwrites the model slots (fit tuning fields are left untouched).
    /// RUN THIS ONCE after pulling these changes: the .glb minivan/bus can only be wired by AssetDatabase
    /// (their internal fileIDs aren't in the .meta), so a hand-edit of the .asset can't reference them.
    /// </summary>
    public static class VehicleCatalogBuilder
    {
        // Low Poly Cars Mega Pack (URP-Lit prefabs — tinted body-only via _BaseColor)
        const string MegaRoot  = "Assets/Low Poly Cars - Mega Pack/Prefabs";
        const string RoyalCar  = MegaRoot + "/Stock Cars/Royal.prefab";

        // othercars set (raw glTF .glb — auto-tinted body-only at runtime via baseColorFactor)
        const string OtherRoot  = "Assets/Unity Technologies/othercars";
        const string BusGlb     = OtherRoot + "/bus.glb";
        const string ConnectGlb = OtherRoot + "/connectt.glb";

        // LowPolyRoadVehicles pack (legacy FBX, _Color01) — kept for reference/fallback only.
        const string SedanPack = "Assets/YelScryptFireStudio/LowPolyRoadVehiclesFreePackage/Vehicles/Sedan_01/pref_Sedan_01.prefab";

        // ACTIVE selection — Car=Royal, Minivan=Connect, Bus=Bus.
        const string CarPath     = RoyalCar;
        const string MinivanPath = ConnectGlb;
        const string BusPath     = BusGlb;
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

            cat.carPrefab     = Load(CarPath, "Car");
            cat.minivanPrefab = Load(MinivanPath, "Minivan");
            cat.busPrefab     = Load(BusPath, "Bus");

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
