using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Bakes every 300Mind UI-kit atlas sub-sprite (+ the external audio icons) into Resources/UIKitAtlas.asset so they
    /// survive a PLAYER BUILD. Without this, code-built UI (garage, runtime-added buttons, the code-fallback panels)
    /// renders with null sprites on device because UIKit's editor-only AssetDatabase loader is compiled out of builds.
    ///
    /// Run after first setup and whenever the atlas textures or their slicing change:  BusJam ▸ Bake UIKit Resources.
    /// </summary>
    public static class UIKitResourceBaker
    {
        const string A1 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_1.png";
        const string A2 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_2.png";
        static readonly string[] External =
        {
            "Assets/MenuManager/Icons/Icon_Sound.png",
            "Assets/MenuManager/Icons/Icon_Music.png",
        };

        [MenuItem("BusJam/Bake UIKit Resources")]
        public static void Bake()
        {
            var names = new List<string>();
            var sprites = new List<Sprite>();

            foreach (var path in new[] { A1, A2 })
            {
                int before = sprites.Count;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Sprite sp) { names.Add(sp.name); sprites.Add(sp); }
                if (sprites.Count == before)
                    Debug.LogWarning($"[UIKitBaker] no sub-sprites at {path} — is the texture missing or its Sprite Mode not 'Multiple'?");
            }

            foreach (var path in External)
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) { names.Add(sp.name); sprites.Add(sp); }
                else Debug.LogWarning($"[UIKitBaker] missing external icon {path}");
            }

            const string dir = "Assets/Resources";
            const string assetPath = dir + "/UIKitAtlas.asset";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Resources");

            var so = AssetDatabase.LoadAssetAtPath<UIKitAtlas>(assetPath);
            bool created = so == null;
            if (created) so = ScriptableObject.CreateInstance<UIKitAtlas>();
            so.names = names.ToArray();
            so.sprites = sprites.ToArray();
            if (created) AssetDatabase.CreateAsset(so, assetPath);
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[UIKitBaker] baked {sprites.Count} sprites -> {assetPath}  ({(created ? "created" : "updated")})");
        }
    }
}
