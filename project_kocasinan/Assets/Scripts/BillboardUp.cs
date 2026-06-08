using UnityEngine;

namespace BusJam
{
    /// <summary>Keeps a world-space label screen-aligned (parallel to the camera plane) so it is
    /// always readable and never mirrored, regardless of how its parent vehicle is rotated.</summary>
    public class BillboardUp : MonoBehaviour
    {
        Camera cam;

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            // Front (+Z, the readable face) points AT the camera so text is upright and not mirrored.
            Vector3 toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(toCam, cam.transform.up);
        }
    }
}
