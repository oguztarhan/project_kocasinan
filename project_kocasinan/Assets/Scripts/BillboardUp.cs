using UnityEngine;

namespace Ridebury
{
    /// <summary>Keeps a world-space label screen-aligned (parallel to the camera plane) so it is
    /// always readable and never mirrored, regardless of how its parent vehicle is rotated.</summary>
    public class BillboardUp : MonoBehaviour
    {
        Camera cam;
        Vector3 lastCam, lastSelf;
        bool primed;

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            Vector3 cp = cam.transform.position, sp = transform.position;
            // PERF: only recompute the orientation when the camera OR this label has actually moved. Under the static
            // top-down gameplay camera a PARKED vehicle's label (the common case) never moves, so it costs nothing;
            // only the camera moving or a vehicle sliding triggers a LookRotation.
            if (primed && (cp - lastCam).sqrMagnitude < 1e-8f && (sp - lastSelf).sqrMagnitude < 1e-8f) return;
            lastCam = cp; lastSelf = sp; primed = true;
            // Front (+Z, the readable face) points AT the camera so text is upright and not mirrored.
            Vector3 toCam = cp - sp;
            if (toCam.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(toCam, cam.transform.up);
        }
    }
}
