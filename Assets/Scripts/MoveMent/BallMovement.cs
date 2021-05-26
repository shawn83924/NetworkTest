using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallMovement : MonoBehaviour
{
    
    public float Speed;
    public float Distance;
    private float dis;
    private void Start()
    {
        dis = Distance;
    }

    // Update is called once per frame
    void Update()
    {
        transform.position += transform.forward * Time.deltaTime * Speed;
        dis -= Time.deltaTime * Mathf.Abs(Speed);
        if (dis <= 0)
        {
            dis = Distance;
            Speed = -Speed;
        }
    }
}
