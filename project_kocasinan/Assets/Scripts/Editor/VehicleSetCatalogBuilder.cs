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

        // 17 collectible CARS, grouped by rarity (0 = Common, 1 = Medium, 2 = Legendary). Cars are WON FROM CHESTS now
        // (not bought) — rarity drives the chest draw. Set 0 (Rhino) is the FREE starter (always owned). Minivan + Bus
        // are shared (Connect / Bus) on every set. Legendary cars only ever drop from the Legendary chest.
        static readonly (string cls, string car, int rarity)[] Defs =
        {
            // Common (4) — set 0 (Rhino) is the free default
            ("Super Cars",     "Rhino",      0),
            ("Super Cars",     "R9",         0),
            ("Super Cars",     "Storm",      0),
            ("Super Cars",     "Tetra",      0),
            // Medium (6)
            ("Tuned Cars",     "Fox",        1),
            ("Tuned Cars",     "Silver-C",   1),
            ("Tuned Cars",     "P-600",      1),
            ("Super Cars",     "Cassini",    1),
            ("Super Cars",     "Poisson",    1),
            ("Super Cars",     "Wave",       1),
            // Legendary (7) — Legendary-chest only
            ("Prototype Cars", "Omen",       2),
            ("Prototype Cars", "Marsella",   2),
            ("Super Cars",     "Agata",      2),
            ("Super Cars",     "Chimera",    2),
            ("Tuned Cars",     "Blacklist",  2),
            ("Tuned Cars",     "Slipstream", 2),
            ("Tuned Cars",     "Skywalker",  2),
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
                    displayName = d.car,    // the CAR name shown on the card (e.g. "Chimera")
                    rarity      = d.rarity,
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
