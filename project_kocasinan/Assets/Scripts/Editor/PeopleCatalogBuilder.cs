using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>Collects the BusJam queue characters (Assets/Characters/BusJamPeople/*.fbx) into
    /// Resources/PeopleCatalog.asset, which the game loads at runtime and draws from AT RANDOM for
    /// every queue person and background crowd figure — so each round is a fresh mix of the whole set.
    /// Re-run from "BusJam ▸ Build People Catalog" after adding/removing models; it also self-heals on
    /// editor load when the catalog is missing or its entries are all gone.</summary>
    public static class PeopleCatalogBuilder
    {
        const string PeopleFolder = "Assets/Characters/BusJamPeople";
        const string CatalogPath  = "Assets/Resources/PeopleCatalog.asset";

        [MenuItem("BusJam/Build People Catalog")]
        public static void Build() { Build(true); }

        // Self-heal: a fresh clone (or a catalog whose models were replaced) gets a populated list without
        // anyone having to remember the menu item. Only runs when there is nothing usable to lose, so a
        // hand-edited list is never overwritten behind your back.
        [InitializeOnLoadMethod]
        static void BuildIfEmpty()
        {
            EditorApplication.delayCall += () =>
            {
                var cat = AssetDatabase.LoadAssetAtPath<PeopleCatalog>(CatalogPath);
                if (cat != null && cat.HasModels) return;
                if (!AssetDatabase.IsValidFolder(PeopleFolder)) return;
                Build(false);
            };
        }

        static void Build(bool verbose)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            if (!AssetDatabase.IsValidFolder(PeopleFolder))
            {
                Debug.LogWarning($"[BusJam] People models not found at {PeopleFolder}. " +
                                 "Point this at your model folder, or assign prefabs on the catalog by hand.");
                return;
            }

            // Plain file scan: AssetDatabase's "t:Prefab" filter does NOT match imported .fbx models.
            var list = new List<GameObject>();
            foreach (string file in Directory.GetFiles(PeopleFolder))
            {
                string path = file.Replace('\\', '/');
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".fbx" && ext != ".prefab" && ext != ".obj") continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) list.Add(go);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var cat2 = AssetDatabase.LoadAssetAtPath<PeopleCatalog>(CatalogPath);
            bool created = cat2 == null;
            if (created)
            {
                cat2 = ScriptableObject.CreateInstance<PeopleCatalog>();
                AssetDatabase.CreateAsset(cat2, CatalogPath);
            }
            cat2.prefabs = list.ToArray();
            EditorUtility.SetDirty(cat2);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BusJam] People catalog {(created ? "created" : "updated")} with {list.Count} characters at {CatalogPath}.");
        }
    }
}
