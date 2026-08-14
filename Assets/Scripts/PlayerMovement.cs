using Mono.Cecil.Cil;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 2f;

    float moveX = 0f;
    float moveY = 0f;
    float moveZ = 0f;

    public Transform Head;
    public Transform Body;
    public void MovePlayer(KeyCode code)
    {
        if(code == KeyCode.W)
        {
            moveY = 1f;
        }
        if (code == KeyCode.S)
        {
            moveY = -1f;
        }
        if (code == KeyCode.D)
        {
            moveX = 1f;
        }
        if (code == KeyCode.A)
        {
            moveX = -1f;
        }


    }

    public void MovePlayer(string data)
    {
        if(data == "W")
        {
            moveY = 0f;
        }
        if(data == "A")
        {
            moveX = 0f;
        }
    }

    private void Update()
    {
        if (Head != null)
        {
            Vector3 position = Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0));
            position -= new Vector3(transform.position.x, transform.position.y, position.z);
            Head.rotation = Quaternion.LookRotation(Vector3.forward, position);
        }
    }
    public void SetMovePlayer()
    {
        float xSpeed = moveX * moveSpeed * Time.deltaTime;
        float ySpeed = moveY * moveSpeed * Time.deltaTime;
        float zSpeed = moveZ * moveSpeed * Time.deltaTime;

        Vector3 moveDirection = new Vector3(xSpeed, ySpeed, zSpeed);

        transform.Translate(moveDirection, Space.World);

        Body.rotation = Quaternion.LookRotation(Vector3.forward,moveDirection);
    }
}
