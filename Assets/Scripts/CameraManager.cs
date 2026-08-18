using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Transform target;

    [Header("Camera")]
    public float distance = 6f;
    public float height = 2f;

    [Header("Mouse")]
    public float mouseSensitivity = 3f;

    private float yaw;
    private float pitch = 20f;

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 마우스로 카메라 회전
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 너무 위/아래로 뒤집히지 않도록 제한
        pitch = Mathf.Clamp(pitch, -10f, 60f);

        Quaternion cameraRotation =
            Quaternion.Euler(pitch, yaw, 0f);

        Vector3 lookPosition =
            target.position + Vector3.up * height;

        Vector3 cameraPosition =
            lookPosition
            - cameraRotation * Vector3.forward * distance;

        transform.position = cameraPosition;
        transform.LookAt(lookPosition);

        RotateBody();
    }

    private void RotateBody()
    {
        // 카메라의 위/아래 각도는 제거
        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            return;

        target.rotation =
            Quaternion.LookRotation(forward.normalized);
    }

    public void SetTarget(Transform target)
    {
        this.target = target;

        yaw = target.eulerAngles.y;
    }
}