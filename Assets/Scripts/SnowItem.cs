using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SnowItem : MonoBehaviour
{
    public int itemId;

    [SerializeField]
    private float rotateSpeed = 50f;

    private void Update()
    {
        transform.Rotate(
            Vector3.up,
            rotateSpeed * Time.deltaTime,
            Space.World
        );
    }
}