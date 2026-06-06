using UnityEngine;

namespace BusJam
{
    /// <summary>Cheap idle life: bobs along an axis, or pulses scale. Put it on a
    /// VISUAL CHILD (never on a transform that gameplay also moves).</summary>
    public class IdleBob : MonoBehaviour
    {
        public float amp = 0.05f;
        public float speed = 2f;
        public float phase = 0f;
        public float scaleAmp = 0f;
        public bool scalePulse = false;
        public Vector3 axis = Vector3.up;

        Vector3 basePos, baseScale;
        float t;

        void OnEnable()
        {
            basePos = transform.localPosition;
            baseScale = transform.localScale;
            t = phase;
        }

        void Update()
        {
            t += Time.deltaTime * speed;
            if (scalePulse)
                transform.localScale = baseScale * (1f + Mathf.Sin(t) * scaleAmp);
            else
                transform.localPosition = basePos + axis * (Mathf.Sin(t) * amp);
        }
    }
}
