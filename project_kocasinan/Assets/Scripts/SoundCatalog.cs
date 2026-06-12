using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Drop-in sound overrides — assign your OWN AudioClips here. The asset lives at
    /// Resources/SoundCatalog.asset and is editable in the Inspector: import your audio
    /// files anywhere under Assets/, then drag each into the matching slot.
    ///
    /// Any field left EMPTY falls back to the built-in procedural sound, so nothing ever
    /// goes silent — you can replace them one at a time.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "BusJam/Sound Catalog")]
    public class SoundCatalog : ScriptableObject
    {
        [Header("Each clip OVERRIDES the built-in sound. Empty = keep the built-in.")]
        public AudioClip board;     // a passenger boards a bus
        public AudioClip coin;      // coins / reward
        public AudioClip error;     // invalid action (not enough coins, locked joker, …)
        public AudioClip win;       // level complete
        public AudioClip lose;      // level failed
        public AudioClip click;     // UI button click
        public AudioClip crash;     // tap a BLOCKED vehicle
        public AudioClip honk;      // vehicle ARRIVES at its stop
        public AudioClip screech;   // full bus DRIFTS away
        public AudioClip deploy;    // vehicle slides out of the jam (NO built-in — silent unless you add one)

        [Header("Master")]
        [Range(0f, 1f)] public float volume = 1f;
    }
}
