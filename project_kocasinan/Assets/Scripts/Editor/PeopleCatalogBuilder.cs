using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>Scans the City People pack for CHARACTER prefabs (excludes props/tools)
    /// and writes them into a Resources/PeopleCatalog.asset that the game loads at
    /// runtime. Re-run any time; you can also hand-edit the catalog afterwards.</summary>
    public static class PeopleCatalogBuilder
    {
        const string PackPrefabs = "Assets/FREE/Pack_FREE_PartyCharacters/Prefabs";
        const string CatalogPath = "Assets/Resources/PeopleCatalog.asset";

        [MenuItem("BusJam/Build People Catalog")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            if (!AssetDatabase.IsValidFolder(PackPrefabs))
            {
                Debug.LogWarning($"[BusJam] People pack not found at {PackPrefabs}. " +
                                 "Move/point this to your prefab folder, or assign prefabs on the catalog by hand.");
            }

            var list = new List<GameObject>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { PackPrefabs }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!IsCharacter(path)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) list.Add(go);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var cat = AssetDatabase.LoadAssetAtPath<PeopleCatalog>(CatalogPath);
            bool created = cat == null;
            if (created)
            {
                cat = ScriptableObject.CreateInstance<PeopleCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            cat.prefabs = list.ToArray();
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BusJam] People catalog {(created ? "created" : "updated")} with {list.Count} characters at {CatalogPath}.");
        }

        // Keep people; drop props/tools/decor that live in the same Prefabs tree.
        static bool IsCharacter(string path)
        {
            string p = path.Replace('\\', '/').ToLowerInvariant();
            return !(p.Contains("/props/") || p.Contains("/tools/") || p.Contains("christmas") ||
                     p.Contains("backdrop") || p.Contains("plane_floor") || p.Contains("building_block") ||
                     p.Contains("dprop_") || p.Contains("tool_"));
        }
    }
}
