using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public class PlayerMovement : MonoBehaviour
{
    public float walkSpeed = 6f;

    float moveX = 0f;
    float moveZ = 0f;

    private Animator animator;

    private Rigidbody rb;

    public bool IsMoving
    {
        get
        {
            return moveX != 0f || moveZ != 0f;
        }
    }

    private void Start()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
    }
    public void MovePlayer(KeyCode code)
    {
        if(code == KeyCode.W)
        {
            moveZ = 1f;
        }
        if (code == KeyCode.S)
        {
            moveZ = -1f;
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
            moveZ = 0f;
        }
        if(data == "A")
        {
            moveX = 0f;
        }
    }



    public void SetMovePlayer()
    {
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;

        cameraForward.y = 0f;
        cameraRight.y = 0f;

        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDirection =
            cameraForward * moveZ +
            cameraRight * moveX;

        // 대각선 이동 시 더 빨라지는 것 방지
        if (moveDirection.sqrMagnitude > 1f)
            moveDirection.Normalize();

        Vector3 nextPosition = rb.position + moveDirection * walkSpeed * Time.fixedDeltaTime;

        rb.MovePosition(nextPosition);

        UpdateAnimation(moveDirection);
    }

    private void UpdateAnimation(Vector3 moveDirection)
    {
        //float movement = moveDirection.magnitude;
        //animator.SetFloat("Speed", movement);

        animator.SetBool("IsMoving", IsMoving);
    }

    public void PlayThrow()
    {
        animator.SetTrigger("Throw");
    }

    public void PlayHit()
    {
        animator.SetTrigger("Hit");
    }

    public void PlayDeath()
    {
        animator.SetTrigger("Death");
    }
}
