using UnityEditor;
using UnityEngine;

namespace BusJam.EditorTools
{
    /// <summary>
    /// "BusJam ▸ Build Environment Catalogs" — create-or-keep ONE EnvironmentCatalog per theme at
    /// Resources/Environments/Environment_&lt;name&gt;.asset, with themeName PRESET (so a typo can't silently fall
    /// back) and all prefab slots left UNTOUCHED. Re-running keeps your assigned prefabs — it only ensures the 11
    /// assets exist with the right themeName. Then drop your own prefabs into the slots in the Inspector.
    /// </summary>
    public static class EnvironmentCatalogBuilder
    {
        const string Dir = "Assets/Resources/Environments";

        [MenuItem("BusJam/Build Environment Catalogs")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/Resources", "Environments");

            int created = 0, kept = 0;
            foreach (var th in Themes.AllThemes())
            {
                string path = $"{Dir}/Environment_{th.name}.asset";
                var cat = AssetDatabase.LoadAssetAtPath<EnvironmentCatalog>(path);
                if (cat == null)
                {
                    cat = ScriptableObject.CreateInstance<EnvironmentCatalog>();
                    cat.themeName = th.name;
                    AssetDatabase.CreateAsset(cat, path);
                    created++;
                }
                else
                {
                    if (cat.themeName != th.name) { cat.themeName = th.name; EditorUtility.SetDirty(cat); } // fix the key, keep slots
                    kept++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[EnvironmentCatalog] {created} created, {kept} kept under {Dir}. " +
                      "Assign prefabs per theme in the Inspector — empty slots stay procedural.");
        }
    }
}
