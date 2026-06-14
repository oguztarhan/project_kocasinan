using UnityEngine;

namespace Eugatnom.GritlineShader.Demo
{
    public class RotatePlanetDemo : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
        
        }

        // Update is called once per frame
        void Update()
        {
            transform.Rotate(transform.right * (Time.deltaTime * 17.0f));
        }
    }
}
