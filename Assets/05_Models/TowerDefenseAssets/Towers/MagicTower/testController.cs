using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class testController : MonoBehaviour
{
    
    private Transform tr;

    void Start()
    {
        tr = GetComponent<Transform>();    
    }

    // Update is called once per frame
    void Update()
    {
        tr.Translate(Vector3.forward * 1.0f * Time.deltaTime);
    }
}
