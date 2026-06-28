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
        const string MinivanRoot = "Assets/Vehicles/Minivans/";
        const string BusRoot     = "Assets/Vehicles/Buses/";
        const string CatalogPath = "Assets/Resources/VehicleSetCatalog.asset";

        // Every vehicle TYPE is its own collectible item now, in 4 rarity tiers (0 Common, 1 Uncommon, 2 Epic,
        // 3 Legendary). All are WON FROM CHESTS; rarity drives the draw; Legendary only from the Legendary chest.

        // CARS (Mega Pack FBX): (class folder, prefab, rarity). Set 0 (Firenze) is the free starter car.
        static readonly (string cls, string car, int rarity)[] Cars =
        {
            ("GT Cars",        "Firenze",    0),
            ("Muscle Cars",    "Azura",      0),
            ("Other Vehicles", "Stampede",   0),
            ("Super Cars",     "Arrow",      1),
            ("Super Cars",     "Agata",      1),
            ("Tuned Cars",     "Slipstream", 2),
            ("Super Cars",     "Poisson",    2),
            ("Super Cars",     "Centaur",    2),
            ("Tuned Cars",     "Blacklist",  3),
            ("Tuned Cars",     "Skywalker",  3),
        };
        // MINIVANS (.glb in Assets/Vehicles/Minivans): (file, display name, rarity). "Classic" (Connect) added free.
        static readonly (string file, string name, int rarity)[] Minivans =
        {
            ("mv_minivan",     "City Van",    0),
            ("mv_transit",     "Transit",     0),
            ("mv_minibus",     "Minibus",     0),
            ("mv_transporter", "Transporter", 1),
            ("mv_sprinter",    "Sprinter",    2),
            ("mv_vclass",      "V-Class",     2),
            ("mv_amber",       "Amber",       3),
            ("mv_vintage",     "Vintage",     3),
        };
        // BUSES (.glb in Assets/Vehicles/Buses): (file, display name, rarity). "Classic" (Bus) added free.
        static readonly (string file, string name, int rarity)[] Buses =
        {
            ("bus_city",   "City Bus", 0),
            ("bus_modern", "Modern",   0),
            ("bus_fleet",  "Fleet",    0),
            ("bus_alpine", "Alpine",   1),
            ("bus_coach",  "Coach",    1),
            ("bus_voyage", "Voyage",   2),
            ("bus_azure",  "Azure",    3),
            ("bus_silver", "Silver",   3),
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

            var connect  = Load(ConnectGlb, "Connect (Classic minivan)");
            var busModel = Load(BusGlb, "Bus (Classic bus)");

            var list = new List<VehicleSetCatalog.VehicleSet>();

            // --- CARS (set 0 = Firenze, the free starter car) ---
            foreach (var d in Cars)
            {
                var car = Load(MegaRoot + d.cls + "/" + d.car + ".prefab", "Car '" + d.car + "'");
                list.Add(new VehicleSetCatalog.VehicleSet
                {
                    id = "set_" + d.car.ToLower().Replace("-", ""), displayName = d.car,
                    rarity = d.rarity, type = VehicleType.Car, carPrefab = car,
                });
            }

            // --- MINIVANS: Classic (Connect, free) + the imported .glb minivans ---
            list.Add(new VehicleSetCatalog.VehicleSet { id = "mv_classic", displayName = "Classic", rarity = 0, type = VehicleType.Minivan, minivanPrefab = connect });
            foreach (var d in Minivans)
            {
                var mv = Load(MinivanRoot + d.file + ".glb", "Minivan '" + d.file + "'");
                list.Add(new VehicleSetCatalog.VehicleSet { id = d.file, displayName = d.name, rarity = d.rarity, type = VehicleType.Minivan, minivanPrefab = mv });
            }

            // --- BUSES: Classic (Bus, free) + the imported .glb buses ---
            list.Add(new VehicleSetCatalog.VehicleSet { id = "bus_classic", displayName = "Classic", rarity = 0, type = VehicleType.Bus, busPrefab = busModel });
            foreach (var d in Buses)
            {
                var b = Load(BusRoot + d.file + ".glb", "Bus '" + d.file + "'");
                list.Add(new VehicleSetCatalog.VehicleSet { id = d.file, displayName = d.name, rarity = d.rarity, type = VehicleType.Bus, busPrefab = b });
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
