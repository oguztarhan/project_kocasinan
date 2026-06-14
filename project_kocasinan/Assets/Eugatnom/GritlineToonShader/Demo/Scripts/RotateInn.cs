using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Eugatnom.GritlineShader.Demo
{
    public class RotateInn : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            transform.Rotate(Vector3.forward * (Time.deltaTime * 17.0f));
        }
    }
}
