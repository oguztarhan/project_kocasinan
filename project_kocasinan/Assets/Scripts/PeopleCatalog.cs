using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Holds the character models used for the queue and the background crowd. Built/refreshed by the
    /// editor menu "Ridebury ▸ Build People Catalog" and loaded at runtime from Resources; RideburyGame picks
    /// one AT RANDOM per figure, so every round is a fresh mix of the whole set. If empty/missing,
    /// RideburyGame falls back to the code-built person.
    ///
    /// The Ridebury people are all exported exactly 2.30 model-units tall, so ONE modelScale fits all of
    /// them and markerHeight lands above every head.
    /// </summary>
    [CreateAssetMenu(fileName = "PeopleCatalog", menuName = "Ridebury/People Catalog")]
    public class PeopleCatalog : ScriptableObject
    {
        [Tooltip("Character prefabs; one is chosen at random per queue person.")]
        public GameObject[] prefabs;

        [Header("Fit (tune so they sit right in the queue band)")]
        public float modelScale = 0.5f;   // 2.30-tall model * 0.5 = 1.15 world, same as the code-built person
        public float yaw = 180f;          // rotate to face the vehicles
        public float yOffset = 0f;        // raise/lower if the model origin isn't at the feet
        public float markerHeight = 1.4f; // height of the mystery "?" / golden crown above the head

        // Counts only LIVE entries: a catalog left over from a removed pack has the right Length but every
        // slot is a dead reference, which must still count as "no models" so the fallback (and the editor
        // catalog rebuild) kicks in.
        public bool HasModels
        {
            get
            {
                if (prefabs == null) return false;
                for (int i = 0; i < prefabs.Length; i++) if (prefabs[i] != null) return true;
                return false;
            }
        }

        public GameObject RandomPrefab()
        {
            if (!HasModels) return null;
            int n = prefabs.Length;
            int start = Random.Range(0, n);
            for (int i = 0; i < n; i++)
            {
                var p = prefabs[(start + i) % n];
                if (p != null) return p;
            }
            return null;
        }
    }
}
