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
        const string MegaRoot    = "Assets/Low Poly Cars - Mega Pack/Prefabs/";
        const string ConnectGlb  = "Assets/Unity Technologies/othercars/connectt.glb";
        const string BusGlb      = "Assets/Unity Technologies/othercars/bus.glb";
        const string CatalogPath = "Assets/Resources/VehicleSetCatalog.asset";

        // 10 CLASS-THEMED sets: one sedan from each Mega Pack class. displayName = the class (the package's name shown
        // on the card, e.g. "Super Cars"). Set 0 (Stock / Royal) is free; prices ramp up. Minivan + Bus are shared.
        static readonly (string cls, string car, int price)[] Defs =
        {
            ("Stock Cars",     "Royal",      0),
            ("Super Cars",     "Maximus",    800),
            ("Muscle Cars",    "Colorado",   1100),
            ("GT Cars",        "Silhouette", 1400),
            ("Rally Cars",     "Safari",     1800),
            ("Retro Cars",     "Betty",      2200),
            ("Tuned Cars",     "Asphalt",    2700),
            ("Deluxe Cars",    "Julieta",    3300),
            ("Prototype Cars", "Savage",     4000),
            ("Electric Cars",  "Spiral",     5000),
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
                var car = Load(MegaRoot + d.cls + "/" + d.car + ".prefab", d.cls + " '" + d.car + "'");
                list.Add(new VehicleSetCatalog.VehicleSet
                {
                    id          = "set_" + d.car.ToLower().Replace("-", ""),
                    displayName = d.cls,   // the CLASS is the package name shown on the card (e.g. "Super Cars")
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
