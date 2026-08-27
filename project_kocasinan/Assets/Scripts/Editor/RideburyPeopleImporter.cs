using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ridebury
{
    /// <summary>Import rules for the Ridebury queue characters. They are static one-mesh figures (~2.1k tris)
    /// whose "Body" slot is repainted to the round colour at runtime, so they need no rig, no animation and
    /// no CPU-side mesh copy.
    ///
    /// Materials: Unity 6 REMOVED ModelImporterMaterialLocation.External ("no longer supported"), so material
    /// sharing has to go through the remap API instead — every model's material names are remapped onto the
    /// shared assets in Materials/, which is what makes all 20 models use ONE Face_EyeWhite, ONE Body, etc.
    /// Bump GetVersion() to force Unity to reimport every model this postprocessor owns.</summary>
    public class RideburyPeopleImporter : AssetPostprocessor
    {
        const string Folder    = "Assets/Characters/RideburyPeople/";
        const string MatFolder = "Assets/Characters/RideburyPeople/Materials";

        public override uint GetVersion() => 8;

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
            var mi = (ModelImporter)assetImporter;

            mi.globalScale       = 1f;                                 // 1 Blender unit = 1 Unity unit
            mi.animationType     = ModelImporterAnimationType.None;    // static figures - no rig, no Animator
            mi.importAnimation   = false;
            mi.importCameras     = false;
            mi.importLights      = false;
            mi.importBlendShapes = true;    // Blink / Laugh / Mad / Surprise expressions
            mi.importVisibility  = false;   // never let a Blender hide-flag disable the MeshRenderer
            mi.isReadable        = false;   // no CPU mesh copy
            mi.importNormals     = ModelImporterNormals.Import;        // keep Blender's smooth/sharp split
            mi.importTangents    = ModelImporterTangents.None;         // flat toon shading - no normal maps
            mi.meshCompression   = ModelImporterMeshCompression.Off;  // protects the small expression deltas
            mi.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            mi.generateSecondaryUV = false;

            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation   = ModelImporterMaterialLocation.InPrefab;   // the only supported mode in Unity 6
            RemapSharedMaterials(mi);
        }

        // Point every material slot at the shared asset of the same name in Materials/, so the 20 models
        // share one set instead of each embedding private copies. Remapping a name a given model does not
        // use is harmless, so this can run blind for all of them.
        static void RemapSharedMaterials(ModelImporter mi)
        {
            if (!AssetDatabase.IsValidFolder(MatFolder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatFolder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat != null)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), mat);
            }
        }
    }

    /// <summary>"Ridebury ▸ Diagnose People" — prints what the queue characters ACTUALLY resolve to
    /// (renderer, submeshes, materials, size) so a "characters not showing" report can be traced to
    /// the real cause instead of guessed at.</summary>
    public static class PeopleDiagnostics
    {
        [MenuItem("Ridebury/Diagnose People")]
        public static void Diagnose()
        {
            var cat = Resources.Load<PeopleCatalog>("PeopleCatalog");
            if (cat == null) { Debug.LogError("[Ridebury] No PeopleCatalog in Resources."); return; }
            Debug.Log($"[Ridebury] catalog: {(cat.prefabs?.Length ?? 0)} entries, HasModels={cat.HasModels}, " +
                      $"modelScale={cat.modelScale}, yaw={cat.yaw}, yOffset={cat.yOffset}, markerHeight={cat.markerHeight}");

            int bad = 0;
            for (int i = 0; i < (cat.prefabs?.Length ?? 0); i++)
            {
                var p = cat.prefabs[i];
                if (p == null) { Debug.LogError($"[Ridebury] entry {i} is a MISSING reference"); bad++; continue; }

                var mr = p.GetComponentInChildren<MeshRenderer>(true);
                var smr = p.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Renderer r = (Renderer)smr ?? mr;
                if (r == null) { Debug.LogError($"[Ridebury] {p.name}: NO renderer"); bad++; continue; }

                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);
                var mats = r.sharedMaterials;
                int nullMats = 0;
                var names = new List<string>();
                foreach (var m in mats) { if (m == null) nullMats++; else names.Add(m.name); }

                string size = mesh != null ? mesh.bounds.size.ToString("F2") : "no mesh";
                bool hasTint = false;
                foreach (var m in mats) if (m != null && m.name.ToLowerInvariant().Contains("tint")) hasTint = true;
                if (!hasTint) { Debug.LogError($"[Ridebury] {p.name}: no Tint slot — it will never take a queue colour"); bad++; }
                string msg = $"[Ridebury] {p.name}: {r.GetType().Name} enabled={r.enabled} tint={hasTint} " +
                             $"verts={(mesh != null ? mesh.vertexCount : 0)} submeshes={(mesh != null ? mesh.subMeshCount : 0)} " +
                             $"slots={mats.Length} nullMats={nullMats} size={size} mats=[{string.Join(", ", names)}]";
                if (nullMats > 0 || mesh == null || !r.enabled) { Debug.LogError(msg); bad++; }
                else Debug.Log(msg);
            }
            Debug.Log($"[Ridebury] Diagnose People finished — {bad} problem(s).");
        }
    }
}
