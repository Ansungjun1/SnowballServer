using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SnowballMovement : MonoBehaviour
{
    private Vector3 direction;
    private float speed;

    public void Initialize(
        Vector3 direction,
        float speed)
    {
        this.direction = direction.normalized;
        this.speed = speed;
    }

    private void Update()
    {
        transform.position +=
            direction * speed * Time.deltaTime;
    }
}