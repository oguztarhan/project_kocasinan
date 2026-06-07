using UnityEngine;

namespace BusJam
{
    /// <summary>A single person waiting in the queue.</summary>
    public class LineUnit : MonoBehaviour
    {
        public PieceColor color;
        public bool golden;
        public bool mystery;
        public bool revealed;

        public Renderer body;
        public GameObject mysteryCover;

        public void Reveal(Material colorMat)
        {
            if (revealed) return;
            revealed = true;
            if (body != null && colorMat != null) body.sharedMaterial = colorMat;
            if (mysteryCover != null) Destroy(mysteryCover);
        }
    }
}
