using UnityEditor;
using UnityEngine;

namespace Ridebury.EditorTools
{
    /// <summary>
    /// "Ridebury ▸ Make Car Textures Readable" — enables Read/Write on the Low Poly Cars Mega Pack colour atlases so
    /// the runtime can recolour ONLY the body-swatch band (keeping windows/bumpers). Run once after pulling.
    /// </summary>
    public static class MakeMegapackTexturesReadable
    {
        static readonly string[] Paths =
        {
            "Assets/Low Poly Cars - Mega Pack/Textures/Color_1_Tex.png",
            "Assets/Low Poly Cars - Mega Pack/Textures/Color_2_Tex.png",
            "Assets/Low Poly Cars - Mega Pack/Textures/Color_3_Tex.png",
            "Assets/Low Poly Cars - Mega Pack/Textures/Color_4_Tex.png",
        };

        [MenuItem("Ridebury/Make Car Textures Readable")]
        public static void Run()
        {
            int changed = 0, ok = 0;
            foreach (var p in Paths)
            {
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) { Debug.LogWarning($"[CarTex] not found: {p}"); continue; }
                ok++;
                if (!imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); changed++; }
            }
            Debug.Log($"[CarTex] Read/Write now enabled — {changed} changed, {ok} total found.");
        }
    }
}
