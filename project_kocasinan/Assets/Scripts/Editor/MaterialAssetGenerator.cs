using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>One-click generator: writes the stable materials as editable .mat assets
    /// into Assets/Resources/Materials so they can be tweaked in the Inspector. Existing
    /// assets are kept (your edits are never overwritten). Re-run any time to add new ones.</summary>
    public static class MaterialAssetGenerator
    {
        [MenuItem("BusJam/Generate Material Assets")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(MaterialLibrary.AssetFolder))
                AssetDatabase.CreateFolder("Assets/Resources", "Materials");

            var specs = MaterialLibrary.AllSpecs();
            specs.AddRange(MaterialLibrary.ThemeSpecs()); // per-theme environment materials

            int created = 0, kept = 0;
            foreach (var s in specs)
            {
                string path = MaterialLibrary.AssetFolder + "/" + s.key + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { kept++; continue; }

                var m = MaterialLibrary.MakeRuntime(s.color, s.smoothness, s.emission);
                m.name = s.key;
                AssetDatabase.CreateAsset(m, path);
                created++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BusJam] Material assets ready in {MaterialLibrary.AssetFolder} — {created} created, {kept} kept.");
        }
    }
}
