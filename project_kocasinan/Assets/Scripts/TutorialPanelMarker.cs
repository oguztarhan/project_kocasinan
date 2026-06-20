using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Tag placed on the user-authored panel that sits BEHIND the tutorial text. When present in the scene,
    /// <see cref="TutorialCoach"/> adopts this object as its banner background (so you control the look in the
    /// Inspector) and only drives the text on top of it. Create one via Tools ▸ 300Mind UI ▸ Bake Tutorial
    /// Panel, or add this component to any UI Image yourself.
    ///
    /// The panel self-hides the instant play starts, so it can only ever appear while a tutorial is running —
    /// no matter whether you leave its active checkbox ticked in the scene. Awake does NOT run in edit mode,
    /// so the panel stays visible in the editor for you to style. The coach finds it (active or not), sets
    /// <see cref="adopted"/> so the self-hide stands down, and shows/hides it during the level 1 / 5 / 10
    /// coaching.
    /// </summary>
    public class TutorialPanelMarker : MonoBehaviour
    {
        // Set true by TutorialCoach once it owns this panel; suppresses the self-hide so the coach's
        // Show/Hide is the single source of truth (handles the panel being left active OR inactive in the scene).
        [System.NonSerialized] public bool adopted;

        void Awake() { if (!adopted) gameObject.SetActive(false); }
    }
}
