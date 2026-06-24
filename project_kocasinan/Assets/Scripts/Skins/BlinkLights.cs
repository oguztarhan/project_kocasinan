using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Alternates two light objects (a = red, b = blue) on/off for a blinking police-light effect. Driven by
    /// unscaled time so it keeps blinking even if the game is paused, and stays in sync across every police vehicle.
    /// Added by BusJamGame.BuildSkinExtras when the equipped skin has PoliceLights.
    /// </summary>
    public class BlinkLights : MonoBehaviour
    {
        public GameObject a, b;     // red, blue
        public float rate = 3f;     // toggles per second

        void Update()
        {
            bool on = (Mathf.FloorToInt(Time.unscaledTime * rate) & 1) == 0;
            if (a) a.SetActive(on);
            if (b) b.SetActive(!on);
        }
    }
}
