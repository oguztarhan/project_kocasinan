using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Opt-OUT marker for the auto-fit pass in <see cref="GlobalFontApplier"/>. Add it to a Text / TMP_Text
    /// that is MEANT to spill outside its rect (a deliberately oversized number, an effect label whose box is
    /// only a placement anchor) and the applier will scale its font but leave the overflow settings alone.
    ///
    /// World-space text (bus seat counts, floating "-3" penalties, the neon LEFT sign) is skipped automatically,
    /// so this is only needed for screen-space UI.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class NoTextFit : MonoBehaviour { }
}
