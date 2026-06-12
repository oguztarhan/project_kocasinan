using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Fixes ONLY the legacy "Language" TextMeshPro label in the open scene: it uses TMP's
/// default LiberationSans (which clashes with the GROBOLD/Oswald game UI). TMP can't use a
/// raw .ttf, so this creates an GROBOLD TMP font asset (once) and assigns it to the
/// "Language" text, keeping the colour black. Nothing else on the panel is touched.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Fix Language Label Font
/// </summary>
public static class LanguageFontFixer
{
    const string TtfPath = "Assets/300Mind/2D Game UI Kit/Fonts/GROBOLD.ttf";
    const string AssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/GROBOLD SDF.asset";

    [MenuItem("Tools/300Mind UI/Fix Language Label Font")]
    static void Fix()
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (fa == null) fa = CreateFontAsset();
        if (fa == null) { Debug.LogError("[LanguageFontFixer] Could not get/create the GROBOLD TMP font asset."); return; }

        int fixedCount = 0;
        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.text == null) continue;
            if (!t.text.Trim().Equals("Language", System.StringComparison.OrdinalIgnoreCase)) continue;
            Undo.RecordObject(t, "Fix Language Font");
            t.font = fa;
            t.color = Color.black;
            EditorUtility.SetDirty(t);
            fixedCount++;
        }

        if (fixedCount == 0) { Debug.LogWarning("[LanguageFontFixer] No 'Language' TMP text found in the open scene."); return; }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[LanguageFontFixer] Fixed {fixedCount} 'Language' label(s) -> GROBOLD, black. SAVE the scene (Ctrl+S).");
    }

    static TMP_FontAsset CreateFontAsset()
    {
        var ttf = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (ttf == null) { Debug.LogError($"[LanguageFontFixer] TTF not found: {TtfPath}"); return null; }
        try
        {
            var fa = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                   AtlasPopulationMode.Dynamic, true);
            if (fa == null) return null;
            AssetDatabase.CreateAsset(fa, AssetPath);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
            {
                fa.atlasTextures[0].name = "GROBOLD Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            }
            if (fa.material != null)
            {
                fa.material.name = "GROBOLD Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LanguageFontFixer] Font asset creation failed: " + e.Message);
            return null;
        }
    }
}
