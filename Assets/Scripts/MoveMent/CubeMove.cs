using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CubeMove : MonoBehaviour
{
    public float Speed;

    // Update is called once per frame
    void Update()
    {
        transform.eulerAngles += Vector3.up * Time.deltaTime * Speed;
    }
}
