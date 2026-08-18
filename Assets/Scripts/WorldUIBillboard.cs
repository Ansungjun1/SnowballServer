using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldUIBillboard : MonoBehaviour
{
    private Camera targetCamera;

    private void Start()
    {
        targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
            return;

        transform.rotation = targetCamera.transform.rotation;
    }
}
