using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DroppedSnowItem : MonoBehaviour
{
    public int itemId;
    public int itemCount;

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
