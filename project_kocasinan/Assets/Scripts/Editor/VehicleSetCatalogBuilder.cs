using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BusJam.EditorTools
{
    /// <summary>
    /// "BusJam ▸ Build Vehicle Sets" — defines the 10 unlockable vehicle SETS and writes them into
    /// Resources/VehicleSetCatalog.asset. Each set = 1 car (a Low Poly Cars Mega Pack "Stock" sedan) + the
    /// shared Connect (minivan) + the shared Bus. Set 0 (Royal) is free.
    ///
    /// RUN THIS ONCE after pulling these changes: the .glb minivan/bus can only be wired via AssetDatabase
    /// (their internal fileIDs aren't in the .meta), so the asset can't be hand-edited to reference them.
    /// Later, when you add distinct minivan/bus models per set, just give those sets different prefabs here.
    /// </summary>
    public static class VehicleSetCatalogBuilder
    {
        const string Mega        = "Assets/Low Poly Cars - Mega Pack/Prefabs/Stock Cars/";
        const string ConnectGlb  = "Assets/Unity Technologies/othercars/connectt.glb";
        const string BusGlb      = "Assets/Unity Technologies/othercars/bus.glb";
        const string CatalogPath = "Assets/Resources/VehicleSetCatalog.asset";

        // 10 sets: (car prefab name in Stock Cars, coin price). Set 0 is free; prices ramp up.
        // Minivan + Bus are shared (Connect / Bus) for every set for now.
        static readonly (string car, int price)[] Defs =
        {
            ("Royal",     0),
            ("Magnum",    400),
            ("Panther",   600),
            ("Phantom",   800),
            ("Spilner",   1000),
            ("Supreme-C", 1300),
            ("Trophy",    1600),
            ("Valley",    2000),
            ("Wagen",     2500),
            ("Xenon",     3000),
        };

        [MenuItem("BusJam/Build Vehicle Sets")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var cat = AssetDatabase.LoadAssetAtPath<VehicleSetCatalog>(CatalogPath);
            bool created = false;
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<VehicleSetCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
                created = true;
            }

            var connect = Load(ConnectGlb, "Connect (minivan)");
            var bus     = Load(BusGlb, "Bus");

            var list = new List<VehicleSetCatalog.VehicleSet>();
            foreach (var d in Defs)
            {
                var car = Load(Mega + d.car + ".prefab", "Car '" + d.car + "'");
                list.Add(new VehicleSetCatalog.VehicleSet
                {
                    id          = "set_" + d.car.ToLower().Replace("-", ""),
                    displayName = d.car,
                    price       = d.price,
                    carPrefab   = car,
                    minivanPrefab = connect,
                    busPrefab   = bus,
                });
            }
            cat.sets = list.ToArray();

            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VehicleSetCatalog] {(created ? "created" : "updated")} with {cat.sets.Length} sets at {CatalogPath} " +
                      $"(set 0 = {cat.DefaultSetId}).");
            Selection.activeObject = cat;
        }

        static GameObject Load(string path, string label)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning($"[VehicleSetCatalog] {label} not found at {path}");
            return go;
        }
    }
}
