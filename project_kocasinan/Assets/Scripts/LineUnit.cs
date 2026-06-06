using UnityEngine;

namespace BusJam
{
    /// <summary>A spot in the queue: either a single person (count 1) or a cabin
    /// that releases <c>count</c> same-color people one by one.</summary>
    public class LineUnit : MonoBehaviour
    {
        public PieceColor color;
        public int count;
        public bool golden;
        public bool mystery;
        public bool revealed;
        public bool isCabin;

        public Renderer body;
        public TextMesh numberLabel;
        public GameObject mysteryCover;

        public void SetCount(int n)
        {
            count = Mathf.Max(0, n);
            if (numberLabel != null) numberLabel.text = count.ToString();
        }

        public void Reveal(Material colorMat)
        {
            if (revealed) return;
            revealed = true;
            if (body != null && colorMat != null) body.sharedMaterial = colorMat;
            if (mysteryCover != null) Destroy(mysteryCover);
        }
    }
}
