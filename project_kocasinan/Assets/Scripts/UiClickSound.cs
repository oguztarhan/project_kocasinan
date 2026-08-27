using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Ridebury
{
    /// <summary>
    /// Plays the catalog "UI Button Click" on press of ANY interactable uGUI Button — every button in
    /// every scene (menu, HUD, panels, shop, level-select), including buttons created at runtime — with
    /// zero per-button wiring. It just raycasts the UI under the pointer on press and, if a Button is
    /// there, asks the single Sfx voice to play the click.
    ///
    /// One persistent instance (DontDestroyOnLoad), auto-created at startup. Because Sfx is one shared
    /// AudioSource that stops before each play, the click never mixes with other SFX.
    /// </summary>
    public class UiClickSound : MonoBehaviour
    {
        static UiClickSound instance;
        readonly List<RaycastResult> hits = new List<RaycastResult>();

        // Auto-boot in every scene the moment the game starts — no prefab or manual placement needed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            Sfx.Ensure();   // the single SFX voice (also DontDestroyOnLoad)
            Ensure();
        }

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("UiClickSound");
            instance = go.AddComponent<UiClickSound>();
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            if (!PressedThisFrame(out Vector2 pos)) return;

            var es = EventSystem.current;
            if (es == null) return;

            var ped = new PointerEventData(es) { position = pos };
            hits.Clear();
            es.RaycastAll(ped, hits);

            for (int i = 0; i < hits.Count; i++)
            {
                var btn = hits[i].gameObject.GetComponentInParent<Button>();
                if (btn != null && btn.interactable && btn.isActiveAndEnabled)
                {
                    Sfx.Instance?.Click();
                    return; // one click per press
                }
            }
        }

        // Same pointer-down read the gameplay tap uses (new Input System): mouse OR primary touch.
        static bool PressedThisFrame(out Vector2 pos)
        {
            pos = default;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            { pos = Mouse.current.position.ReadValue(); return true; }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            { pos = Touchscreen.current.primaryTouch.position.ReadValue(); return true; }
            return false;
        }
    }
}
