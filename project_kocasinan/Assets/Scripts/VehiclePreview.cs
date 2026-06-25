using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusJam
{
    /// <summary>
    /// Renders a vehicle prefab to a RenderTexture for the garage/wardrobe cards: a small 3/4 "showroom" shot framed to
    /// the model's bounds. One hidden camera + key light on a dedicated layer, parked far above the gameplay; each
    /// distinct prefab is rendered ONCE (a one-shot render request, URP-safe) and cached. The model shows its NATIVE
    /// colours (no gameplay tint) so the shop reads like a catalogue. Call <see cref="Get"/> with a vehicle prefab.
    /// </summary>
    public static class VehiclePreview
    {
        const int Layer = 31;     // a spare user layer; the preview camera + light cull to it (and the rig sits far away)
        const int W = 280, H = 200; // RT size — matches the card tile's 210x150 aspect (1.4) so the shot isn't squashed

        static Camera cam;
        static Light keyLight;
        static Transform stage;
        static readonly Dictionary<GameObject, RenderTexture> cache = new Dictionary<GameObject, RenderTexture>();

        static void EnsureRig()
        {
            if (cam != null) return;
            var root = new GameObject("~VehiclePreviewRig") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(root);
            root.transform.position = new Vector3(0f, 5000f, 0f); // far above the scene so nothing else lands in frame
            stage = root.transform;

            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(stage, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent -> the card's own dark tile shows behind
            cam.cullingMask = 1 << Layer;
            cam.orthographic = false;
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 100f;
            cam.enabled = false; // never renders on its own — only the one-shot requests below

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(stage, false);
            lightGo.transform.localRotation = Quaternion.Euler(38f, 150f, 0f);
            keyLight = lightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.2f;
            keyLight.cullingMask = 1 << Layer; // don't spill onto the gameplay scene (and it's only on during the shot)
            keyLight.enabled = false;
        }

        /// <summary>Cached preview RT for this prefab (rendered on first request). Null if prefab is null.</summary>
        public static RenderTexture Get(GameObject prefab)
        {
            if (prefab == null) return null;
            if (cache.TryGetValue(prefab, out var rt) && rt != null) return rt;
            EnsureRig();

            var model = Object.Instantiate(prefab, stage);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 215f, 0f); // turn the FRONT to a 3/4 angle toward the camera ("bize doğru çapraz"); flip 180 if it shows the back
            model.transform.localScale = Vector3.one;
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var lod in model.GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0); // show the full-detail mesh, never an LOD-culled blank
            SetLayer(model.transform, Layer);

            // Frame it: aim at the bounds centre from a front-right-above 3/4 angle, backed off to fit the bounding sphere.
            Bounds b = WorldBounds(model, stage.position);
            float radius = Mathf.Max(b.extents.magnitude, 0.05f);
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            Vector3 dir = new Vector3(0.5f, 0.34f, -1f).normalized; // in front + slightly right + above -> a 3/4 hero shot facing the viewer
            cam.transform.position = b.center + dir * dist;
            cam.transform.LookAt(b.center);

            rt = new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32) { name = "veh_" + prefab.name, antiAliasing = 4 };
            cam.aspect = (float)W / H;
            cam.targetTexture = rt;
            keyLight.enabled = true;
            Render(rt);
            keyLight.enabled = false;
            cam.targetTexture = null;

            Object.Destroy(model);
            cache[prefab] = rt;
            return rt;
        }

        // One-shot render that works under URP/HDRP (RenderPipeline request) AND the built-in pipeline (Camera.Render).
        static void Render(RenderTexture rt)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        static Bounds WorldBounds(GameObject go, Vector3 fallback)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return new Bounds(fallback, Vector3.one);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
