using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Holds the imported city-people prefabs used for the queue crowd. Built/refreshed
    /// by the editor menu "BusJam ▸ Build People Catalog" and loaded at runtime from
    /// Resources. If empty/missing, BusJamGame falls back to the code-built person.
    /// </summary>
    [CreateAssetMenu(fileName = "PeopleCatalog", menuName = "BusJam/People Catalog")]
    public class PeopleCatalog : ScriptableObject
    {
        [Tooltip("Character prefabs; one is chosen at random per queue person.")]
        public GameObject[] prefabs;

        [Header("Fit (tune so they sit right in the queue band)")]
        public float modelScale = 0.55f;  // scale to fit the queue band
        public float yaw = 180f;          // rotate to face the vehicles
        public float yOffset = 0f;        // raise/lower if the model origin isn't at the feet
        public float markerHeight = 1.4f; // height of the mystery "?" / golden crown above the head

        public bool HasModels => prefabs != null && prefabs.Length > 0;

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
