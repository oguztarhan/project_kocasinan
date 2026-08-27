using UnityEngine;

namespace Ridebury
{
    /// <summary>A single person waiting in the queue.</summary>
    public class LineUnit : MonoBehaviour
    {
        public PieceColor color;
        public bool golden;
        public bool mystery;
        public bool revealed;

        public Renderer body;
        public int bodyMaterialIndex = -1; // -1 = recolor sharedMaterial; else that slot (e.g. model body, keep face)
        public GameObject mysteryCover;
        public CharacterLife life;         // breathing/expression driver on the model (null for the code person)

        /// <summary>Play an expression on this person, if they are a model with blend shapes.</summary>
        public void Express(CharacterLife.Mood mood, float seconds = 1.2f)
        {
            if (life != null) life.Express(mood, seconds);
        }

        public void Reveal(Material colorMat)
        {
            if (revealed) return;
            revealed = true;
            if (body != null && colorMat != null)
            {
                if (bodyMaterialIndex >= 0)
                {
                    var mats = body.sharedMaterials;
                    if (bodyMaterialIndex < mats.Length) { mats[bodyMaterialIndex] = colorMat; body.sharedMaterials = mats; }
                }
                else body.sharedMaterial = colorMat;
            }
            if (mysteryCover != null) Destroy(mysteryCover);
        }
    }
}
